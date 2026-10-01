using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Noctis.Plugins;

namespace FreeMusicFinder;

/// <summary>
/// The search window. Built in code (no XAML, no bindings) so it depends on nothing but the
/// Avalonia controls Noctis already ships. Every event handler catches its own exceptions:
/// one escaping into Noctis would mark the plugin Failed.
/// </summary>
internal sealed class SearchWindow : Window
{
    private const int MaxParallelDownloads = 3;

    private readonly IPluginHost _host;
    private readonly TelegramAccount _telegram;
    private readonly TelegramBotSource _bot;
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _downloadSlots = new(MaxParallelDownloads, MaxParallelDownloads);
    private readonly List<Row> _rows = new();
    private CancellationTokenSource? _search;
    private string? _defaultFolder;

    private readonly TextBox _query;
    private readonly Button _searchButton;
    private readonly Button _downloadAllButton;
    private readonly Button _telegramButton;
    private readonly StackPanel _results;
    private readonly TextBlock _status;

    public SearchWindow(IPluginHost host, TelegramAccount telegram)
    {
        _host = host;
        _telegram = telegram;
        _bot = new TelegramBotSource(telegram);

        Title = "Free Music Finder";
        Width = 760;
        Height = 560;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _query = new TextBox { PlaceholderText = "Artist and title" };
        _query.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            StartSearch();
        };

        _searchButton = new Button { Content = "Search", Margin = new Thickness(8, 0, 0, 0), MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        _searchButton.Click += (_, _) => StartSearch();

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(_query);
        Grid.SetColumn(_searchButton, 1);
        top.Children.Add(_searchButton);

        _results = new StackPanel { Spacing = 2 };
        var scroller = new ScrollViewer { Content = _results, Margin = new Thickness(0, 12, 0, 12) };

        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.7,
        };
        _telegramButton = new Button { Margin = new Thickness(8, 0, 0, 0) };
        _telegramButton.Click += (_, _) => OpenTelegramLogin();
        _downloadAllButton = new Button { Content = "Download all", IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
        _downloadAllButton.Click += (_, _) => DownloadAll();
        var openFolder = new Button { Content = "Open folder", Margin = new Thickness(8, 0, 0, 0) };
        openFolder.Click += (_, _) => OpenFolder();

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        bottom.Children.Add(_status);
        Grid.SetColumn(_telegramButton, 1);
        bottom.Children.Add(_telegramButton);
        Grid.SetColumn(_downloadAllButton, 2);
        bottom.Children.Add(_downloadAllButton);
        Grid.SetColumn(openFolder, 3);
        bottom.Children.Add(openFolder);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16) };
        root.Children.Add(top);
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        Content = root;

        ShowTelegramState();
        _status.Text = _telegram.IsLoggedIn
            ? $"Searches the Telegram bot @{TelegramBotSource.Bot} through your Telegram account."
            : LoginFirst;

        Opened += (_, _) => _query.Focus();
        Closed += (_, _) =>
        {
            _closed.Cancel();
            _search?.Cancel();
        };
    }

    private const string LoginFirst = "Log in to Telegram to search.";

    /// <summary>The Telegram button's label says who is logged in.</summary>
    private void ShowTelegramState()
        => _telegramButton.Content = _telegram.IsLoggedIn ? "Telegram: " + _telegram.SignedInAs : "Log in to Telegram…";

    private async void OpenTelegramLogin()
    {
        try
        {
            await new TelegramLoginWindow(_telegram, _host).ShowDialog(this);
            ShowTelegramState();
            if (_telegram.IsLoggedIn && _status.Text == LoginFirst) _status.Text = "";
        }
        catch (Exception ex)
        {
            _host.Log("could not open the Telegram login: " + ex.Message);
        }
    }

    private string DownloadFolder
    {
        get
        {
            var configured = _host.Settings.GetString("downloadFolder")?.Trim();
            return string.IsNullOrEmpty(configured)
                ? _defaultFolder ??= Downloader.DefaultFolder(_host.DataDirectory)
                : Environment.ExpandEnvironmentVariables(configured);
        }
    }

    /// <summary>Fills the search box and runs the search.</summary>
    public void Search(string query)
    {
        _query.Text = query;
        StartSearch();
    }

    private async void StartSearch()
    {
        try
        {
            var query = _query.Text?.Trim() ?? "";
            if (query.Length == 0) return;

            _defaultFolder = null; // looked up again per search: the library folders may have changed
            _search?.Cancel();
            var search = _search = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
            search.CancelAfter(TimeSpan.FromSeconds(25));
            var ct = search.Token;

            _rows.Clear();
            _results.Children.Clear();
            _downloadAllButton.IsEnabled = false;
            if (!_telegram.IsLoggedIn)
            {
                _status.Text = LoginFirst;
                return;
            }

            _searchButton.IsEnabled = false;
            _status.Text = "Searching…";
            try
            {
                // Task.Run keeps the waiting on the bot off the UI thread.
                var tracks = await Task.Run(() => _bot.SearchAsync(query, ct), ct);
                if (search != _search) return; // a newer search took over
                foreach (var track in tracks) AddRow(track);
                _status.Text = _rows.Count == 0 ? "Nothing found." : $"{_rows.Count} track{(_rows.Count == 1 ? "" : "s")} found.";
            }
            catch (Exception ex)
            {
                if (search != _search || _closed.IsCancellationRequested) return;
                _host.Log("search failed: " + ex.Message);
                _status.Text = ex is OperationCanceledException ? "The bot did not answer." : "Search failed: " + ex.Message;
            }

            _searchButton.IsEnabled = true;
            _downloadAllButton.IsEnabled = _rows.Count > 0;
            ShowTelegramState(); // a search can find out that Telegram ended the session
        }
        catch (Exception ex)
        {
            _host.Log("search failed: " + ex.Message);
            _searchButton.IsEnabled = true;
            _status.Text = "Search failed: " + ex.Message;
        }
    }

    private void AddRow(BotTrack track)
    {
        var title = new TextBlock { Text = track.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var details = track.Artist;
        if (track.Duration is { } d) details += " · " + (d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss") : d.ToString(@"m\:ss"));
        var subtitle = new TextBlock { Text = details, Opacity = 0.7, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(subtitle);

        var button = new Button { Content = "Download", MinWidth = 110, Margin = new Thickness(12, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var row = new Row(track, button);
        if (Downloader.FindExisting(DownloadFolder, track) is not null) row.MarkDone("Downloaded");
        button.Click += (_, _) => Download(row, announce: true);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 6) };
        grid.Children.Add(text);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);

        _rows.Add(row);
        _results.Children.Add(grid);
    }

    private async void DownloadAll()
    {
        try
        {
            var todo = _rows.Where(r => r.CanStart).ToList();
            if (todo.Count == 0) return;
            _downloadAllButton.IsEnabled = false;
            var results = await Task.WhenAll(todo.Select(r => DownloadCoreAsync(r)));
            var saved = results.Count(ok => ok);
            if (_closed.IsCancellationRequested) return;

            _downloadAllButton.IsEnabled = _rows.Any(r => r.CanStart);
            var message = $"Saved {saved} of {todo.Count} tracks to {DownloadFolder}";
            _status.Text = message;
            Announce(message);
        }
        catch (Exception ex)
        {
            _host.Log("download all failed: " + ex.Message);
        }
    }

    private async void Download(Row row, bool announce)
    {
        try
        {
            if (!row.CanStart) return;
            var ok = await DownloadCoreAsync(row);
            if (!ok || _closed.IsCancellationRequested) return;
            var message = $"Saved \"{row.Track.Title}\" to {DownloadFolder}";
            _status.Text = message;
            if (announce) Announce(message);
        }
        catch (Exception ex)
        {
            _host.Log("download failed: " + ex.Message);
        }
    }

    /// <summary>True when the file is on disk afterwards. Never throws.</summary>
    private async Task<bool> DownloadCoreAsync(Row row)
    {
        var ct = _closed.Token;
        row.MarkBusy("Queued");
        try
        {
            await _downloadSlots.WaitAsync(ct);
            try
            {
                row.MarkBusy("Starting…");
                // Created on the UI thread, so reports are posted back to it.
                var progress = new Progress<double>(p => row.MarkBusy($"{p:P0}"));
                var folder = DownloadFolder;
                var outcome = await Task.Run(() => Downloader.DownloadAsync(row.Track, folder, progress, ct), ct);
                // Progress reports are posted; let the queued ones land before the final label.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                row.MarkDone(outcome == DownloadOutcome.Saved ? "Downloaded" : "Already saved");
                return true;
            }
            finally
            {
                _downloadSlots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _host.Log($"download of \"{row.Track.Title}\" failed: {ex.Message}");
            row.MarkFailed();
            ToolTip.SetTip(row.Button, ex.Message);
            if (!_closed.IsCancellationRequested) _status.Text = $"Could not download \"{row.Track.Title}\": {ex.Message}";
            return false;
        }
    }

    private void Announce(string message)
    {
        // Noctis drops notices sent faster than one a second; that is fine here.
        try { _host.Notify(message); }
        catch (Exception ex) { _host.Log("notify failed: " + ex.Message); }
    }

    private void OpenFolder()
    {
        try
        {
            var folder = DownloadFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = "Could not open the folder: " + ex.Message;
        }
    }

    /// <summary>A result line: the track and the button that doubles as its status.</summary>
    private sealed class Row
    {
        private bool _busy;
        private bool _done;

        public Row(BotTrack track, Button button)
        {
            Track = track;
            Button = button;
        }

        public BotTrack Track { get; }
        public Button Button { get; }
        public bool CanStart => !_busy && !_done;

        public void MarkBusy(string label)
        {
            if (_done) return;
            _busy = true;
            Button.IsEnabled = false;
            Button.Content = label;
        }

        public void MarkDone(string label)
        {
            _busy = false;
            _done = true;
            Button.IsEnabled = false;
            Button.Content = label;
        }

        public void MarkFailed()
        {
            _busy = false;
            Button.IsEnabled = true;
            Button.Content = "Retry";
        }
    }
}
