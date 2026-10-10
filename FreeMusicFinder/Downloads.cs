using System.Threading.Channels;

namespace FreeMusicFinder;

internal enum DownloadState { Queued, Running, Saved, AlreadyThere, Failed, Cancelled, Waiting }

/// <summary>One track on its way into the download folder.</summary>
internal sealed class Download
{
    public Download(BotTrack track, string folder)
    {
        Track = track;
        Folder = folder;
    }

    public BotTrack Track { get; }
    public string Folder { get; }
    public DownloadState State { get; internal set; }

    /// <summary>0 to 1 while <see cref="DownloadState.Running"/>.</summary>
    public double Progress { get; internal set; }

    /// <summary>Why it failed; empty otherwise.</summary>
    public string Error { get; internal set; } = "";
    public DateTimeOffset? RetryAt { get; internal set; }

    public bool IsActive => State is DownloadState.Queued or DownloadState.Running or DownloadState.Waiting;
}

/// <summary>
/// The downloads of the plugin, one after the other in the order they were asked for: the bot
/// answers every request in the same chat and sends one file at a time. They belong to the
/// plugin, not to the search window, so closing the window does not stop them.
/// </summary>
internal sealed class Downloads : IDisposable
{
    private readonly Channel<Download> _queue = Channel.CreateUnbounded<Download>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<Download> _active = new();
    private readonly HashSet<string> _tidied = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string> _log;
    private readonly string? _store;
    private readonly List<Download> _known = new();
    private bool _paused;
    public bool Paused { get { lock (_gate) return _paused; } }
    public IReadOnlyList<Download> History { get { lock (_gate) return _known.ToArray(); } }

    // Since the queue was last empty.
    private int _done;
    private int _saved;
    private Download? _last;

    public Downloads(Action<string> log, string? store = null, Func<MusicRequest, BotTrack>? restore = null)
    {
        _log = log;
        _store = store;
        if (store is not null && File.Exists(store))
        {
            if (new FileInfo(store).Length > 16 * 1024 * 1024 || (File.GetAttributes(store) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("QUEUE_UNREADABLE: saved queue is too large or redirected.");
            QueueFile file;
            try { file = System.Text.Json.JsonSerializer.Deserialize<QueueFile>(File.ReadAllText(store)) ?? throw new InvalidDataException(); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
            { throw new InvalidDataException("QUEUE_UNREADABLE: keep the saved queue for diagnosis; it was not replaced.", ex); }
            if (file.Version != 1 || file.Items is null || file.Items.Length > 10000 || restore is null) throw new InvalidDataException("QUEUE_UNREADABLE: unsupported saved queue.");
            _paused = file.Paused;
            foreach (var item in file.Items)
            {
                if (!Path.IsPathFullyQualified(item.Folder) || item.Request is null || string.IsNullOrWhiteSpace(item.Request.Title) || string.IsNullOrWhiteSpace(item.Request.Artist) || item.Request.Title.Length > 1000 || item.Request.Artist.Length > 1000 || !Enum.IsDefined(item.State) || item.RetryAt>DateTimeOffset.UtcNow.AddDays(8)) throw new InvalidDataException("QUEUE_UNREADABLE: invalid track or folder.");
                var download = new Download(restore(item.Request),item.Folder) { State=item.State,Error=item.Error,RetryAt=item.RetryAt };
                _known.Add(download);
                if (item.State is DownloadState.Saved or DownloadState.AlreadyThere or DownloadState.Failed) continue;
                download.State = DownloadState.Queued; _active.Add(download); _queue.Writer.TryWrite(download);
            }
        }
        _ = Task.Run(RunAsync);
    }

    private sealed record QueueItem(MusicRequest Request,string Folder,DownloadState State,string Error,DateTimeOffset? RetryAt);
    private sealed record QueueFile(int Version,bool Paused,QueueItem[] Items);
    private void Save()
    {
        if (_store is not null) AtomicJson.Write(_store,new QueueFile(1,_paused,_known.Where(d=>d.Track.Request is not null)
            .Select(d=>new QueueItem(d.Track.Request!,d.Folder,d.State,d.Error,d.RetryAt)).ToArray()));
    }
    public void SetPaused(bool paused) { lock (_gate) { _paused=paused; Save(); } }
    public void RetryFailed()
    {
        lock (_gate)
        {
            foreach (var item in _known.Where(d=>d.State==DownloadState.Failed))
            { item.State=DownloadState.Queued; item.Error=""; _active.Add(item); _queue.Writer.TryWrite(item); }
            Save();
        }
    }

    /// <summary>A download changed its state or progress. Raised on a worker thread.</summary>
    public event Action<Download>? Changed;

    /// <summary>The queue ran empty; the text says what was saved. Raised on a worker thread.</summary>
    public event Action<string>? Finished;

    /// <summary>The downloads that are waiting or running, oldest first.</summary>
    public IReadOnlyList<Download> Active
    {
        get { lock (_gate) return _active.ToArray(); }
    }

    /// <summary>The waiting or running download that saves under this name into this folder, if there is one.</summary>
    public Download? Find(string fileName, string folder)
    {
        lock (_gate) return _active.FirstOrDefault(d => Same(d, fileName, folder));
    }

    /// <summary>Queues a track. Asking again for one that is already waiting or running returns that download.</summary>
    public Download Start(BotTrack track, string folder)
    {
        folder=Path.GetFullPath(folder);
        lock (_gate)
        {
            var running = _active.FirstOrDefault(d => Same(d, track.FileName, folder));
            if (running is not null) return running;

            var download = new Download(track, folder);
            if (_known.Count >= 10000) throw new InvalidOperationException("QUEUE_FULL: finish the existing queue before adding more than 10,000 tracks.");
            if (_stop.IsCancellationRequested)
            {
                download.State = DownloadState.Cancelled;
                return download;
            }
            _active.Add(download);
            _known.Add(download);
            try {Save();} catch {_active.Remove(download);_known.Remove(download);throw;}
            if(!_queue.Writer.TryWrite(download)) {download.State=DownloadState.Cancelled;_active.Remove(download);Save();}
            return download;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _queue.Writer.TryComplete();
    }

    private static bool Same(Download download, string fileName, string folder)
        => string.Equals(download.Track.FileName, fileName, StringComparison.OrdinalIgnoreCase)
           && string.Equals(download.Folder, folder, StringComparison.OrdinalIgnoreCase);

    private async Task RunAsync()
    {
        try
        {
            await foreach (var download in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                while (Paused) await Task.Delay(500,_stop.Token).ConfigureAwait(false);
                if (download.RetryAt is {} later && later > DateTimeOffset.UtcNow)
                { Schedule(download,later); continue; }
                await RunOneAsync(download).ConfigureAwait(false);
                if (download.State == DownloadState.Waiting) { Schedule(download,download.RetryAt!.Value); continue; }

                string? summary = null;
                lock (_gate)
                {
                    _active.Remove(download);
                    _done++;
                    if (download.State == DownloadState.Saved) _saved++;
                    if (download.State != DownloadState.Cancelled) _last = download;
                    if (_active.Count == 0)
                    {
                        summary = Summary();
                        _done = _saved = 0;
                        _last = null;
                    }
                    Save();
                }
                Raise(download);
                if (summary is not null && !_stop.IsCancellationRequested) RaiseFinished(summary);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch(Exception ex)
        { _log("QUEUE_SAVE_FAILED: "+ex.Message+" Pending requests remain in the last saved queue."); }

        // Noctis is closing or the plugin was switched off: what is still waiting will not run.
        lock (_gate)
        {
            foreach (var download in _active) download.State = DownloadState.Cancelled;
            _active.Clear();
            try {Save();}catch(Exception ex){_log("QUEUE_SAVE_FAILED: "+ex.Message);}
        }
    }

    private void Schedule(Download download,DateTimeOffset until)
    {
        download.State=DownloadState.Waiting; lock(_gate) Save(); Raise(download);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(until > DateTimeOffset.UtcNow ? until-DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(10),_stop.Token).ConfigureAwait(false);
                _queue.Writer.TryWrite(download);
            }
            catch(OperationCanceledException) { }
        });
    }

    private async Task RunOneAsync(Download download)
    {
        try
        {
            _stop.Token.ThrowIfCancellationRequested();
            bool tidy;
            lock (_gate) tidy = _tidied.Add(download.Folder);
            if (tidy && Downloader.CleanLeftovers(download.Folder) is > 0 and var removed)
                _log($"removed {removed} unfinished download{(removed == 1 ? "" : "s")} left in {download.Folder}");

            download.State = DownloadState.Running;
            download.Error=""; download.RetryAt=null; lock(_gate) Save();
            Raise(download);
            var progress = new Report(p =>
            {
                download.Progress = p;
                Raise(download);
            });
            var outcome = await Downloader.DownloadAsync(download.Track, download.Folder, progress, _stop.Token).ConfigureAwait(false);
            download.State = outcome == DownloadOutcome.Saved ? DownloadState.Saved : DownloadState.AlreadyThere;
        }
        catch(SourcesWaitingException ex)
        { download.State=DownloadState.Waiting;download.Error=ex.Message;download.RetryAt=ex.Until; }
        catch(IOException ex)
        { download.State=DownloadState.Waiting;download.Error="Connection or storage interrupted: "+ex.Message;download.RetryAt=DateTimeOffset.UtcNow.AddMinutes(10); }
        catch(System.Net.Sockets.SocketException ex)
        { download.State=DownloadState.Waiting;download.Error="TELEGRAM_CONNECTION_FAILED: "+ex.Message;download.RetryAt=DateTimeOffset.UtcNow.AddMinutes(10); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            download.State = DownloadState.Cancelled;
        }
        catch (Exception ex)
        {
            download.Error = ex is OperationCanceledException ? "The download was interrupted." : ex.Message;
            download.State = DownloadState.Failed;
            _log($"download of \"{download.Track.Title}\" failed: {download.Error}");
        }
    }

    // Under _gate, when the queue has just run empty. Null when there is nothing worth saying.
    private string? Summary()
    {
        if (_last is null) return null;
        if (_done > 1) return $"Saved {_saved} of {_done} tracks to {_last.Folder}";
        return _last.State switch
        {
            DownloadState.Saved => $"Saved \"{_last.Track.Title}\" to {_last.Folder}",
            DownloadState.Failed => $"Could not download \"{_last.Track.Title}\": {_last.Error}",
            _ => null,
        };
    }

    // A listener that throws must not stop the queue.
    private void Raise(Download download)
    {
        try { Changed?.Invoke(download); }
        catch (Exception ex) { _log("download listener failed: " + ex.Message); }
    }

    private void RaiseFinished(string summary)
    {
        try { Finished?.Invoke(summary); }
        catch (Exception ex) { _log("download listener failed: " + ex.Message); }
    }

    // Progress<T> would post to the thread it was made on; this one reports where the download runs.
    private sealed class Report : IProgress<double>
    {
        private readonly Action<double> _report;

        public Report(Action<double> report) => _report = report;

        void IProgress<double>.Report(double value) => _report(value);
    }
}
