using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Noctis.Plugins;

namespace FreeMusicFinder;

/// <summary>
/// Searches a Telegram music bot through the user's own Telegram account and downloads tracks
/// into a folder.
/// API 1.1 has no hook for a page or sidebar entry, so the plugin opens its own window, from
/// the track menu or from the "Open the search window" switch in its settings.
/// </summary>
public sealed class FreeMusicPlugin : INoctisPlugin
{
    private const string OpenSearchKey = "openSearch";

    private readonly List<IDisposable> _registrations = new();
    private IPluginHost? _host;
    private TelegramAccount? _telegram;
    private SearchWindow? _window;

    public PluginInfo Info { get; } = new(
        Id: "dev.moshi.freemusicfinder",
        Name: "Free Music Finder",
        Version: "2.0.0",
        Author: "moshi",
        Description: "Search the Telegram music bot @MusicsHuntersbot through your own Telegram account and download tracks into your library.");

    public void Initialize(IPluginHost host)
    {
        _host = host;
        _telegram = new TelegramAccount(Path.Combine(host.DataDirectory, "telegram.dat"));

        _registrations.Add(host.RegisterTrackCommand(
            "Find free music…",
            // A 24×24 SVG path: a magnifying glass.
            "M15.5 14h-.79l-.28-.27A6.47 6.47 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19zm-6 0A4.5 4.5 0 1 1 14 9.5 4.5 4.5 0 0 1 9.5 14z",
            (Action<TrackInfo>)(track => ShowWindow(track.Artist))));

        host.Settings.Changed += OnSettingChanged;
    }

    public void Shutdown()
    {
        if (_host is not null) _host.Settings.Changed -= OnSettingChanged;
        foreach (var r in _registrations) r.Dispose();
        _registrations.Clear();

        _window?.Close();
        _window = null;
        _telegram?.Dispose();
        _telegram = null;
        _host = null;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key == OpenSearchKey) ShowWindow(null);
    }

    private void ShowWindow(string? query)
    {
        if (_host is null || _telegram is null) return;
        try
        {
            if (_window is null)
            {
                _window = new SearchWindow(_host, _telegram);
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
        }
        catch (Exception ex)
        {
            // An exception out of a callback marks the plugin Failed; a window that will not open is not worth that.
            _host.Log("could not open the search window: " + ex.Message);
        }
    }
}
