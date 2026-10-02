using System.Threading.Channels;

namespace FreeMusicFinder;

internal enum DownloadState { Queued, Running, Saved, AlreadyThere, Failed, Cancelled }

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

    public bool IsActive => State is DownloadState.Queued or DownloadState.Running;
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

    // Since the queue was last empty.
    private int _done;
    private int _saved;
    private Download? _last;

    public Downloads(Action<string> log)
    {
        _log = log;
        _ = Task.Run(RunAsync);
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
        lock (_gate)
        {
            var running = _active.FirstOrDefault(d => Same(d, track.FileName, folder));
            if (running is not null) return running;

            var download = new Download(track, folder);
            if (_stop.IsCancellationRequested || !_queue.Writer.TryWrite(download))
            {
                download.State = DownloadState.Cancelled;
                return download;
            }
            _active.Add(download);
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
                await RunOneAsync(download).ConfigureAwait(false);

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
                }
                Raise(download);
                if (summary is not null && !_stop.IsCancellationRequested) RaiseFinished(summary);
            }
        }
        catch (OperationCanceledException)
        {
        }

        // Noctis is closing or the plugin was switched off: what is still waiting will not run.
        lock (_gate)
        {
            foreach (var download in _active) download.State = DownloadState.Cancelled;
            _active.Clear();
        }
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
            Raise(download);
            var progress = new Report(p =>
            {
                download.Progress = p;
                Raise(download);
            });
            var outcome = await Downloader.DownloadAsync(download.Track, download.Folder, progress, _stop.Token).ConfigureAwait(false);
            download.State = outcome == DownloadOutcome.Saved ? DownloadState.Saved : DownloadState.AlreadyThere;
        }
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
