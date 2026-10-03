using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Noctis.Plugins;

namespace FreeMusicFinder;

/// <summary>
/// Searches a Telegram music bot through the user's own Telegram account and downloads tracks
/// into a folder.
/// API 1.1 has no hook for a page or sidebar entry, so the plugin opens its own window: with a
/// keyboard shortcut anywhere in Noctis, from the track menu, or from the "Open the search
/// window" switch in its settings. The downloads belong to the plugin: they go on when the
/// window is closed and stop with the plugin.
/// </summary>
public sealed class FreeMusicPlugin : INoctisPlugin
{
    private const string OpenSearchKey = "openSearch";
    private const string ProxyKey = "proxy";
    private const string ShortcutKey = "shortcut";
    internal const string DownloadFolderKey = "downloadFolder";

    private readonly List<IDisposable> _registrations = new();
    private IPluginHost? _host;
    private TelegramAccount? _telegram;
    private TelegramBotSource? _bot;
    private Downloads? _downloads;
    private SearchWindow? _window;
    private KeyGesture? _shortcut;
    // Read on the threads that connect to Telegram; settings themselves are only read on the UI thread.
    private volatile string _proxy = "";

    public PluginInfo Info { get; } = new(
        Id: "dev.moshi.freemusicfinder",
        Name: "Free Music Finder",
        // The version is written once, in plugin.json; the build gives it to the assembly.
        Version: typeof(FreeMusicPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        Author: "moshi",
        Description: "Search the Telegram music bot @MusicsHuntersbot through your own Telegram account and download tracks into your library.");

    public void Initialize(IPluginHost host)
    {
        _host = host;
        _proxy = host.Settings.GetString(ProxyKey) ?? "";
        _telegram = new TelegramAccount(Path.Combine(host.DataDirectory, "telegram.dat"), () => _proxy, Log);
        _bot = new TelegramBotSource(_telegram, Log);
        _downloads = new Downloads(Log);
        _downloads.Finished += Announce;

        _registrations.Add(host.RegisterTrackCommand(
            "Find more by this artist…",
            // A 24×24 SVG path: a magnifying glass.
            "M15.5 14h-.79l-.28-.27A6.47 6.47 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19zm-6 0A4.5 4.5 0 1 1 14 9.5 4.5 4.5 0 0 1 9.5 14z",
            (Action<TrackInfo>)(track => ShowWindow(track.Artist))));

        host.Settings.Changed += OnSettingChanged;

        // The plugin kit has no shortcuts, but the plugin runs inside Noctis: a class handler sees
        // the keys pressed in any of its windows. It only takes a key press nobody has handled,
        // so a shortcut Noctis itself uses keeps working.
        ApplyShortcut();
        _registrations.Add(InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown));

        // What a download left behind when Noctis was last closed or crashed in the middle of it.
        var configured = host.Settings.GetString(DownloadFolderKey);
        var data = host.DataDirectory;
        Task.Run(() =>
        {
            var folder = Downloader.Folder(configured, data);
            var removed = Downloader.CleanLeftovers(folder);
            if (removed > 0) Log($"removed {removed} unfinished download{(removed == 1 ? "" : "s")} left in {folder}");
        });
    }

    public void Shutdown()
    {
        if (_host is not null) _host.Settings.Changed -= OnSettingChanged;
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();

        _window?.Close();
        _window = null;
        if (_downloads is not null) _downloads.Finished -= Announce;
        _downloads?.Dispose();
        _downloads = null;
        _bot = null;
        _telegram?.Dispose();
        _telegram = null;
        _host = null;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key == OpenSearchKey) ShowWindow(null);
        else if (key == ProxyKey) _proxy = _host?.Settings.GetString(ProxyKey) ?? "";
        else if (key == ShortcutKey) ApplyShortcut();
    }

    private void ApplyShortcut()
    {
        if (_host is null) return;
        try
        {
            _shortcut = Shortcut.Parse(_host.Settings.GetString(ShortcutKey), OperatingSystem.IsMacOS());
        }
        catch (FormatException ex)
        {
            _shortcut = null;
            _host.Log("shortcut: " + ex.Message);
            Announce("Free Music Finder: " + ex.Message);
        }
    }

    private void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Handled || _shortcut is not { } shortcut || !shortcut.Matches(e)) return;
        e.Handled = true;
        ShowWindow(null);
    }

    private void ShowWindow(string? query)
    {
        if (_host is null || _telegram is null || _bot is null || _downloads is null) return;
        try
        {
            if (_window is null)
            {
                _window = new SearchWindow(_host, _telegram, _downloads, _bot.SearchAsync);
                _window.Closed += (_, _) => _window = null;

                var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner is not null) _window.Show(owner);
                else _window.Show();
            }
            else
            {
                if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
                _window.Activate();
            }

            if (!string.IsNullOrWhiteSpace(query)) _window.Search(query);
            else _window.FocusSearchBox();
        }
        catch (Exception ex)
        {
            // An exception out of a callback marks the plugin Failed; a window that will not open is not worth that.
            _host.Log("could not open the search window: " + ex.Message);
        }
    }

    /// <summary>Tells the user what was saved. Called from the download thread.</summary>
    private void Announce(string message)
        => Dispatcher.UIThread.Post(() =>
        {
            // Noctis shows at most 200 characters, and drops notices sent faster than one a second; that is fine here.
            try { _host?.Notify(message.Length <= 200 ? message : message[..199] + "…"); }
            catch (Exception ex) { _host?.Log("notify failed: " + ex.Message); }
        });

    /// <summary>Writes to the Noctis log from any thread.</summary>
    private void Log(string message)
        => Dispatcher.UIThread.Post(() =>
        {
            try { _host?.Log(message); }
            catch (Exception) { }
        });
}
