using System.Net.Sockets;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FreeMusicFinder;
using Noctis.Plugins;

namespace FreeMusicFinder.Tests;

/// <summary>
/// The search window and the plugin's entry point, with a stand-in for Noctis and a stand-in
/// for the bot. Runs on the UI thread of Avalonia's headless platform: no window is shown.
/// </summary>
internal static class WindowTests
{
    private static string? _pictures;

    /// <param name="pictures">A folder to save pictures of the window into, or null for none.</param>
    public static void Run(string? pictures)
    {
        _pictures = pictures;
        if (pictures is not null) Directory.CreateDirectory(pictures);
        var music = Check.NewFolder("window/music");
        var folder = Path.Combine(music, "Noctis Free Music");
        var host = new FakeHost(Check.NewFolder("window/noctis"), music);
        host.Tracks.Add(new TrackInfo("1", "In Library", "Artist", "Album", "Artist", TimeSpan.FromSeconds(201), 2020, "", 1, @"X:\elsewhere\song.flac", false, 0, 0));

        var bot = new FakeBot();
        var downloads = new Downloads(host.Log);
        try
        {
            NotLoggedIn(host, downloads, bot);

            var account = LoggedIn(host);
            ResultsAndDownloads(host, account, downloads, bot, folder);
            ReopenedWindow(host, account, downloads, bot, folder);
            FailureAndRetry(host, account, downloads, bot);
            BotTrouble(host, account, downloads, bot);
            NoLibraryPermission(host, account, downloads, bot);
        }
        finally
        {
            downloads.Dispose();
        }

        Plugin(music, folder);
    }

    private static void NotLoggedIn(FakeHost host, Downloads downloads, FakeBot bot)
    {
        Check.Section("the search window, not logged in");
        var window = new SearchWindow(host, new TelegramAccount(Path.Combine(host.DataDirectory, "nobody.dat"), () => ""), downloads, bot.SearchAsync);
        window.Show();
        window.Search("artist");
        Check.Until(() => Status(window) == "Log in to Telegram to search.", "asks to log in");
        Check.Equal(0, bot.Searches, "the bot is not asked");
        Check.Equal("Log in to Telegram…", (string?)TelegramButton(window).Content, "the Telegram button offers the login");
        window.Close();
    }

    private static TelegramAccount LoggedIn(FakeHost host)
    {
        var path = Path.Combine(host.DataDirectory, "telegram-test.dat");
        var store = new TelegramStore(path);
        store.Begin(12345, "not a real hash");
        store.SetAccount("@tester");
        return new TelegramAccount(path, () => "");
    }

    private static void ResultsAndDownloads(FakeHost host, TelegramAccount account, Downloads downloads, FakeBot bot, string folder)
    {
        Check.Section("the search window: results and downloads");
        var gate = new TaskCompletionSource();
        bot.Answers["artist"] = () => new[] { LogicTests.Track("First", before: () => gate.Task), LogicTests.Track("Second"), LogicTests.Track("In Library") };

        var window = new SearchWindow(host, account, downloads, bot.SearchAsync);
        window.Show();
        Check.Equal("Telegram: @tester", (string?)TelegramButton(window).Content, "the Telegram button says who is logged in");
        window.Search("artist");
        Check.Until(() => Rows(window).Count == 3, "three results are listed");
        var rows = Rows(window);
        Check.Equal("3 tracks found, 1 already in your library.", Status(window), "the status counts them");
        Check.Equal("Artist · 3:20 | Artist · 3:20 | Artist · 3:20 · in your library", string.Join(" | ", rows.Select(r => r.Details)), "the one the library has is marked");
        Check.Equal("Download+ Download+ Download+", Buttons(rows), "all three can be downloaded");
        Check.True(DownloadAll(window).IsEnabled, "\"Download all\" is on");

        Click(rows[0].Button);
        Check.Until(() => Buttons(rows) == "Starting…- Download+ Download+", "the first one starts");
        Click(DownloadAll(window));
        Check.Until(() => Buttons(rows) == "Starting…- Queued- Download+", "\"Download all\" queues the second and leaves out the one in the library");
        Check.False(DownloadAll(window).IsEnabled, "\"Download all\" is off: nothing is left for it");
        Picture(window, "1 results, one running, one queued, one in the library");

        window.Close();
        Dispatcher.UIThread.RunJobs();
        Check.Equal(2, downloads.Active.Count, "closing the window does not stop the downloads");
        gate.SetResult();
        Check.Until(() => downloads.Active.Count == 0, "they finish with the window closed");
        Check.Equal("Artist - First.mp3,Artist - Second.mp3", string.Join(",", Directory.GetFiles(folder).Select(Path.GetFileName).Order()), "both songs are in the download folder");
    }

    private static void ReopenedWindow(FakeHost host, TelegramAccount account, Downloads downloads, FakeBot bot, string folder)
    {
        Check.Section("the search window, opened again while a download runs");
        var gate = new TaskCompletionSource();
        var third = downloads.Start(LogicTests.Track("Third", before: () => gate.Task), folder);
        Check.Until(() => third.State == DownloadState.Running, "a download is running");

        var window = new SearchWindow(host, account, downloads, bot.SearchAsync);
        window.Show();
        Check.Equal("Third: Starting…-", string.Join(",", Rows(window).Select(r => r.Title + ": " + Buttons(new[] { r }))), "the window shows it");
        Check.Equal("1 download is still going.", Status(window), "and says so");

        window.Search("artist");
        Check.Until(() => Rows(window).Count == 4, "a search lists its results and keeps the running download in view");
        var rows = Rows(window);
        Check.Equal("First,Second,In Library,Third", string.Join(",", rows.Select(r => r.Title)), "results first");
        Check.Equal("Downloaded- Downloaded- Download+ Starting…-", Buttons(rows), "what is saved already says so");
        Picture(window, "2 opened again, saved ones marked, a download still running");

        gate.SetResult();
        Check.Until(() => Buttons(rows).EndsWith("Downloaded-"), "the running one finishes in view");
        Check.Until(() => Status(window) == $"Saved \"Third\" to {folder}", "the status says what was saved");
        window.Close();
    }

    private static void FailureAndRetry(FakeHost host, TelegramAccount account, Downloads downloads, FakeBot bot)
    {
        Check.Section("the search window: a download that fails");
        var tries = 0;
        var broken = new BotTrack("Broken", "Artist", null, "Artist - Broken", async (output, _, ct) =>
        {
            if (++tries == 1) throw new TimeoutException("The bot sent another file (Somebody - Something) and not this track.");
            await output.WriteAsync(new byte[100], ct);
            return "flac";
        });
        bot.Answers["broken"] = () => new[] { broken };

        var window = new SearchWindow(host, account, downloads, bot.SearchAsync);
        window.Show();
        window.Search("broken");
        Check.Until(() => Rows(window).Count == 1, "one result");
        var row = Rows(window)[0];
        Click(row.Button);
        Check.Until(() => Buttons(new[] { row }) == "Retry+", "the button turns into \"Retry\"");
        Check.Equal("Could not download \"Broken\": The bot sent another file (Somebody - Something) and not this track.", Status(window), "the status says why");
        Check.Equal("The bot sent another file (Somebody - Something) and not this track.", ToolTip.GetTip(row.Button) as string, "so does the button's tip");
        Picture(window, "3 a download that failed");
        Click(row.Button);
        Check.Until(() => Buttons(new[] { row }) == "Downloaded-", "the second try saves it");
        Check.Equal(null, ToolTip.GetTip(row.Button), "the tip is gone");
        window.Close();
    }

    private static void BotTrouble(FakeHost host, TelegramAccount account, Downloads downloads, FakeBot bot)
    {
        Check.Section("the search window: the bot or Telegram misbehaves");
        const string unreadable = "@MusicsHuntersbot answered, but its list could not be read. The bot may have changed how it writes its results.";
        bot.Answers["unreadable"] = () => throw new BotAnswerException(unreadable);
        bot.Answers["silent"] = () => throw new TimeoutException("The bot did not answer.");
        bot.Answers["offline"] = () => throw new SocketException(10060);
        bot.Answers["proxy"] = () => throw new FormatException("The proxy setting is not understood. " + ProxyChoice.Help);
        bot.Answers["nothing"] = () => Array.Empty<BotTrack>();

        var window = new SearchWindow(host, account, downloads, bot.SearchAsync);
        window.Show();
        foreach (var (query, expected) in new[]
        {
            ("unreadable", unreadable),
            ("silent", "The bot did not answer."),
            ("offline", "Telegram could not be reached. Where it is blocked, turn on a VPN or set a proxy in the plugin's settings."),
            ("proxy", "The proxy setting is not understood. " + ProxyChoice.Help),
            ("nothing", "Nothing found."),
        })
        {
            window.Search(query);
            Check.Until(() => Status(window) == expected, $"\"{query}\": {expected}");
            Check.True(SearchButton(window).IsEnabled && Rows(window).Count == 0, "  and the window is ready for the next search");
        }
        window.Close();
    }

    private static void NoLibraryPermission(FakeHost host, TelegramAccount account, Downloads downloads, FakeBot bot)
    {
        Check.Section("the search window, without the library permission");
        host.LibraryGranted = false;
        host.Logs.Clear();
        bot.Answers["two"] = () => new[] { LogicTests.Track("In Library"), LogicTests.Track("Also In Library") };
        var window = new SearchWindow(host, account, downloads, bot.SearchAsync);
        window.Show();
        window.Search("two");
        Check.Until(() => Rows(window).Count == 2, "the results are listed all the same");
        Check.Equal("2 tracks found.", Status(window), "none is marked");
        Check.Equal(1, host.Logs.Count(l => l.StartsWith("results are not checked against the library")), "the log says so once");
        host.LibraryGranted = true;
        window.Close();
    }

    private static void Plugin(string music, string folder)
    {
        Check.Section("the plugin in Noctis");
        var host = new FakeHost(Check.NewFolder("plugin/noctis"), music);
        host.Values["shortcut"] = Shortcut.Default; // what Noctis gives from plugin.json
        var dead = Path.Combine(folder, "Artist - Dead.fmf.part");
        File.WriteAllText(dead, "x");
        File.SetLastWriteTimeUtc(dead, DateTime.UtcNow.AddHours(-1));

        var plugin = new FreeMusicPlugin();
        Check.Equal("2.2.0", plugin.Info.Version, "the version is the one in plugin.json");
        plugin.Initialize(host);
        Check.Equal("Find more by this artist…", string.Join(",", host.Commands.Select(c => c.Label)), "one track menu entry");
        Check.Until(() => !File.Exists(dead), "what an interrupted download left behind is removed at the start");
        Check.Until(() => host.Logs.Any(l => l.StartsWith("removed 1 unfinished download left in")), "and the log says so");

        SearchWindow? Window() => (SearchWindow?)typeof(FreeMusicPlugin).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plugin);
        host.Values["openSearch"] = "true";
        host.Raise("openSearch");
        var window = Window();
        Check.True(window is { IsVisible: true }, "flipping the switch opens the window");

        host.Commands[0].Handler(new TrackInfo("7", "Some Song", "Somebody", "", "", TimeSpan.Zero, 0, "", 0, "", false, 0, 0));
        Check.True(ReferenceEquals(window, Window()), "the menu entry uses the open window");
        Check.Equal("Somebody", window is null ? null : Query(window).Text, "and searches for the track's artist");
        Check.Until(() => window is not null && Status(window) == "Log in to Telegram to search.", "which needs the login first");

        // A download that finishes while the window is closed is announced by the plugin itself.
        window?.Close();
        var downloads = (Downloads)typeof(FreeMusicPlugin).GetField("_downloads", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plugin)!;
        downloads.Start(LogicTests.Track("Announced"), folder);
        Check.Until(() => host.Notices.Count == 1, "a finished download gives one notice in Noctis");
        Check.Equal($"Saved \"Announced\" to {folder}", host.Notices.FirstOrDefault(), "which says what was saved and where");
        host.Raise("openSearch");
        window = Window();
        Check.True(window is { IsVisible: true }, "the window opens again after it was closed");

        // The shortcut, pressed in another Noctis window.
        window?.Close();
        var noctis = new Window { Width = 400, Height = 300 };
        noctis.Show();
        Check.False(Press(noctis, Key.F, KeyModifiers.Control), "Ctrl+F (Noctis's own search) is left to Noctis");
        Check.True(Window() is null, "and opens nothing");
        Press(noctis, Key.F, KeyModifiers.Control | KeyModifiers.Shift, handledAlready: true);
        Check.True(Window() is null, "Ctrl+Shift+F that Noctis has already handled is left to Noctis");
        Check.True(Press(noctis, Key.F, KeyModifiers.Control | KeyModifiers.Shift), "Ctrl+Shift+F is taken");
        window = Window();
        Check.True(window is { IsVisible: true }, "and opens the search window");
        if (window is not null) Query(window).Text = "typed before";
        Check.True(window is not null && Press(window, Key.F, KeyModifiers.Control | KeyModifiers.Shift), "pressed again in the search window");
        Check.True(ReferenceEquals(window, Window()) && window is not null && Query(window).SelectedText == "typed before",
            "it stays one window, with the search box selected for a new search");

        window?.Close();
        host.Values["shortcut"] = "Ctrl+Alt+M";
        host.Raise("shortcut");
        Press(noctis, Key.F, KeyModifiers.Control | KeyModifiers.Shift);
        Check.True(Window() is null, "a changed shortcut replaces the old one");
        Press(noctis, Key.M, KeyModifiers.Control | KeyModifiers.Alt);
        Check.True(Window() is { IsVisible: true }, "the new one works at once");
        Window()?.Close();
        host.Values["shortcut"] = "Shift+F";
        host.Raise("shortcut");
        Check.Until(() => host.Notices.Any(n => n.Contains("would get in the way of typing")), "a shortcut that would get in the way is refused with a notice");
        Press(noctis, Key.F, KeyModifiers.Shift);
        Check.True(Window() is null, "and does nothing");
        host.Values["shortcut"] = "";
        host.Raise("shortcut");
        Press(noctis, Key.F, KeyModifiers.Control | KeyModifiers.Shift);
        Check.True(Window() is null, "an empty setting turns the shortcut off");
        host.Values["shortcut"] = Shortcut.Default;
        host.Raise("shortcut");
        Press(noctis, Key.F, KeyModifiers.Control | KeyModifiers.Shift);
        window = Window();

        host.Values["proxy"] = "nonsense";
        host.Raise("proxy");
        plugin.Shutdown();
        Check.True(window is { IsVisible: false } && Window() is null, "switching the plugin off closes the window");
        Check.Equal(1, host.CommandsRemoved, "and takes the menu entry away");
        Check.False(Press(noctis, Key.F, KeyModifiers.Control | KeyModifiers.Shift), "and the shortcut");
        Check.True(Window() is null, "which opens nothing any more");
        noctis.Close();
        Check.Equal(0, host.Logs.Count(l => l.Contains("could not")), "nothing went wrong on the way");
    }

    // ── looking at the window ──

    private sealed record RowView(string Title, string Details, Button Button);

    private static Grid Root(SearchWindow window) => (Grid)window.Content!;

    private static List<RowView> Rows(SearchWindow window)
    {
        var panel = (StackPanel)((ScrollViewer)Root(window).Children[1]).Content!;
        return panel.Children.Cast<Grid>().Select(row =>
        {
            var text = (StackPanel)row.Children[0];
            return new RowView(((TextBlock)text.Children[0]).Text ?? "", ((TextBlock)text.Children[1]).Text ?? "", (Button)row.Children[1]);
        }).ToList();
    }

    /// <summary>Each row's button: its label, then "+" when it can be pressed and "-" when not.</summary>
    private static string Buttons(IEnumerable<RowView> rows) => string.Join(" ", rows.Select(r => r.Button.Content + (r.Button.IsEnabled ? "+" : "-")));

    private static string Status(SearchWindow window) => ((Grid)Root(window).Children[2]).Children.OfType<TextBlock>().First().Text ?? "";

    private static Button DownloadAll(SearchWindow window) => ((Grid)Root(window).Children[2]).Children.OfType<Button>().First(b => "Download all".Equals(b.Content));

    private static Button TelegramButton(SearchWindow window) => ((Grid)Root(window).Children[2]).Children.OfType<Button>().First();

    private static Button SearchButton(SearchWindow window) => ((Grid)Root(window).Children[0]).Children.OfType<Button>().First();

    private static TextBox Query(SearchWindow window) => ((Grid)Root(window).Children[0]).Children.OfType<TextBox>().First();

    private static void Picture(SearchWindow window, string name)
    {
        if (_pictures is null) return;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var file = File.Create(Path.Combine(_pictures, name + ".png"));
        window.CaptureRenderedFrame()?.Save(file);
    }

    /// <summary>A key press in a window; returns whether someone took it.</summary>
    private static bool Press(Window window, Key key, KeyModifiers modifiers, bool handledAlready = false)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Handled = handledAlready };
        window.RaiseEvent(e);
        Dispatcher.UIThread.RunJobs();
        return e.Handled && !handledAlready;
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    // ── stand-ins ──

    /// <summary>The bot: answers a search text with a prepared list, or the way it was told to fail.</summary>
    private sealed class FakeBot
    {
        public Dictionary<string, Func<IReadOnlyList<BotTrack>>> Answers { get; } = new();
        public int Searches;

        public Task<IReadOnlyList<BotTrack>> SearchAsync(string query, CancellationToken ct)
        {
            Interlocked.Increment(ref Searches);
            return Task.FromResult(Answers[query]());
        }
    }

    /// <summary>Noctis, as far as the plugin uses it.</summary>
    private sealed class FakeHost : IPluginHost, IPluginSettings, ILibraryReader
    {
        public FakeHost(string noctisData, string musicFolder)
        {
            File.WriteAllText(Path.Combine(noctisData, "settings.json"), "{ \"musicFolders\": [ " + System.Text.Json.JsonSerializer.Serialize(musicFolder) + " ] }");
            DataDirectory = Path.Combine(noctisData, "plugin-data", "dev.moshi.freemusicfinder");
            Directory.CreateDirectory(DataDirectory);
        }

        public List<string> Logs { get; } = new();
        public List<string> Notices { get; } = new();
        public List<(string Label, Action<TrackInfo> Handler)> Commands { get; } = new();
        public int CommandsRemoved;
        public Dictionary<string, string> Values { get; } = new();
        public List<TrackInfo> Tracks { get; } = new();
        public bool LibraryGranted = true;

        public void Raise(string key)
        {
            Changed?.Invoke(this, key);
            Dispatcher.UIThread.RunJobs();
        }

        // IPluginHost
        public string AppVersion => "1.5.8";
        public string DataDirectory { get; }
        public INowPlaying NowPlaying => throw new NotSupportedException();
        public IBeatSource Beat => throw new NotSupportedException();
        public ISpectrumSource Spectrum => throw new NotSupportedException();
        public void Log(string message) { lock (Logs) Logs.Add(message); }
        public void RegisterVisualLayer(IVisualLayerProvider provider) { }
        public IPluginSettings Settings => this;
        public ILibraryReader Library => LibraryGranted ? this : throw new PluginPermissionException(PluginPermissions.LibraryRead);
        public void Notify(string message) => Notices.Add(message);

        public IDisposable RegisterTrackCommand(string label, string? icon, Action<TrackInfo> handler)
        {
            Commands.Add((label, handler));
            return new Removal(() => CommandsRemoved++);
        }

        // IPluginSettings
        public event EventHandler<string>? Changed;
        public string? GetString(string key) => Values.GetValueOrDefault(key, "");
        public bool GetBool(string key, bool fallback = false) => Values.TryGetValue(key, out var value) ? value == "true" : fallback;
        public double GetNumber(string key, double fallback = 0) => fallback;

        // ILibraryReader, the way Noctis searches: every word of the query is in the title, artist or album.
        public int TrackCount => Tracks.Count;

        public IReadOnlyList<TrackInfo> Search(string query, int limit = 50)
        {
            var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return Tracks.Where(t => words.All(w => (t.Title + " " + t.Artist + " " + t.Album).ToLowerInvariant().Contains(w))).Take(limit).ToList();
        }

        private sealed class Removal : IDisposable
        {
            private readonly Action _removed;

            public Removal(Action removed) => _removed = removed;

            public void Dispose() => _removed();
        }
    }
}
