using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static FreeMusicFinder.NativeCatalogBridge;

namespace FreeMusicFinder;

/// <summary>Enriches the existing artist release grids and native album track-list surface.
/// Local library/persistence/playback objects are not populated with catalog-only tracks.</summary>
internal sealed class NativeCatalogPages : IDisposable
{
    private readonly Func<ArtistCatalog> _catalog;
    private readonly DiscoveryActions _actions;
    private readonly Func<object?> _main;
    private readonly Func<Window?> _window;
    private readonly Func<bool> _enabled;
    private readonly Action<string> _log;
    private readonly DispatcherTimer _timer;
    private readonly IDisposable _clickGuard, _contextGuard, _loadedGuard;
    private INotifyPropertyChanged? _shellNotify, _viewNotify;
    private object? _observedView;
    private int _scanQueued;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<object, ArtistState> _artists = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, AlbumState> _albums = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, CatalogArtist> _chosen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<object, (CatalogAlbum Metadata, object Tile)> _opened = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, CatalogRelease> _tiles = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _pending = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _albumLoading = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _artAttempted = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (UserControl Page, WrapPanel Notice)> _albumNotices = new(ReferenceEqualityComparer.Instance);
    private readonly string? _choicesPath;
    private CancellationTokenSource? _openStop;
    private bool _disposed, _reported;
    private sealed class ArtistState(object vm, UserControl page, StackPanel panel)
    {
        public object Vm = vm; public UserControl Page = page; public StackPanel Panel = panel;
        public readonly WrapPanel Strip = new() { Margin = new Thickness(32, 8, 32, 8) };
        public readonly TextBlock Status = new() { Opacity = .65, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        public CatalogProfile? Profile; public Dictionary<object, CatalogRelease> CurrentTiles = new(ReferenceEqualityComparer.Instance);
        public readonly ProgressBar Loading = new() { IsIndeterminate = true, Width = 80, Height = 3, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        public CancellationTokenSource? LoadStop;
        public bool Interrupted;
        public IReadOnlyList<object> Displayed = []; public string ViewKey = "";
        public bool Busy;
        public EventHandler<RoutedEventArgs>? Click;
        public EventHandler<ContextRequestedEventArgs>? Context;
    }
    private sealed class AlbumState(object vm, UserControl page, ItemsControl list, object originalLibrary, object originalAlbum, CatalogAlbum metadata)
    {
        public object Vm = vm, OriginalLibrary = originalLibrary, OriginalAlbum = originalAlbum;
        public UserControl Page = page; public ItemsControl List = list;
        public object? OriginalItems = list.ItemsSource; public IDataTemplate? OriginalTemplate = list.ItemTemplate;
        public IReadOnlyList<object> OriginalTracks = Values(Get(vm, "Tracks"));
        public string? OriginalHeaderArt = Get(vm, "HeaderArtPath") as string, CoverPath;
        public bool LoadingCover;
        public DateTimeOffset CoverRetry;
        public CatalogAlbum Metadata = metadata; public NativeAlbumRows? Rows;
    }
    public NativeCatalogPages(Func<ArtistCatalog> catalog, DiscoveryActions actions, Action<string> log, Func<bool> enabled, Func<object?>? main = null, string? choicesPath = null, Func<Window?>? window = null)
    {
        _catalog = catalog; _actions = actions; _log = log; _enabled = enabled; _choicesPath = choicesPath;
        try
        {
            if (choicesPath is not null && File.Exists(choicesPath) && new FileInfo(choicesPath).Length < 256 * 1024 && (File.GetAttributes(choicesPath) & FileAttributes.ReparsePoint) == 0)
                foreach (var (name, artist) in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, CatalogArtist>>(File.ReadAllText(choicesPath)) ?? [])
                    if (name.Length <= 200 && artist is { Name.Length: > 0 and <= 1000, Id: not null } && System.Text.RegularExpressions.Regex.IsMatch(artist.Id, @"^(apple|deezer):[1-9][0-9]{0,17}$")) _chosen[name] = artist;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { _log("CATALOG_CHOICES_UNREADABLE: saved artist choices could not be read; you can choose them again."); }
        _main = main ?? (() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext);
        _window = window ?? (() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        // Class handlers run before the target's code-behind handlers as well as commands.
        // This also guards the native hover-dots action, which opens its menu in code-behind.
        _clickGuard = Button.ClickEvent.AddClassHandler<Button>((button, e) =>
        {
            if (_disposed || e.Handled || !ReferenceEquals(e.Source, button)) return;
            var page = button.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(p => p.GetType().FullName == "Noctis.Views.ArtistDetailView");
            if (page?.DataContext is {} vm && _artists.TryGetValue(vm, out var state))
            {
                if (button.Classes.Contains("tile-more") && TileAt(button) is {} tile && UseOwnedContext(button, tile)) return;
                ArtistClick(state, e);
            }
        });
        _contextGuard = Control.ContextRequestedEvent.AddClassHandler<Control>((control, e) =>
        {
            if (!_disposed && TileAt(control) is {} tile && !UseOwnedContext(control, tile)) e.Handled = true;
        });
        _loadedGuard = Control.LoadedEvent.AddClassHandler<UserControl>((page, _) =>
        {
            if (page.GetType().FullName is "Noctis.Views.ArtistDetailView" or "Noctis.Views.AlbumDetailView") QueueScan();
        });
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _timer.Tick += (_, _) => Scan(); _timer.Start();
        QueueScan();
    }
    private void QueueScan()
    {
        if (_disposed || Interlocked.Exchange(ref _scanQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _scanQueued, 0); if (!_disposed) Scan(); }, DispatcherPriority.Loaded);
    }
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is null or "" or "CurrentView") QueueScan(); }
    private void OnViewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "SelectedTab" or "GridColumns" or "AlbumCount" or "SingleCount" or "ReleaseSortMode") QueueScan();
    }
    private void Observe(object main, object? current)
    {
        if (!ReferenceEquals(_shellNotify, main as INotifyPropertyChanged))
        {
            if (_shellNotify is not null) _shellNotify.PropertyChanged -= OnShellChanged;
            _shellNotify = main as INotifyPropertyChanged; if (_shellNotify is not null) _shellNotify.PropertyChanged += OnShellChanged;
        }
        if (ReferenceEquals(_observedView, current)) return;
        _observedView = current;
        foreach (var state in _artists.Values.Where(s => s.Busy && !ReferenceEquals(s.Vm, current))) { state.Interrupted = true; state.LoadStop?.Cancel(); }
        if (_viewNotify is not null) _viewNotify.PropertyChanged -= OnViewChanged;
        _viewNotify = current as INotifyPropertyChanged; if (_viewNotify is not null) _viewNotify.PropertyChanged += OnViewChanged;
    }
    internal void Scan()
    {
        if (_disposed) return;
        if (!_enabled()) { RestorePages(); return; }
        try
        {
            var window = _window();
            if (window is null || _main() is not {} main) return;
            var current = Get(main, "CurrentView");
            Observe(main, current);
            foreach (var page in window.GetVisualDescendants().OfType<UserControl>().Where(p => ReferenceEquals(p.DataContext, current) && p.GetType().FullName is "Noctis.Views.ArtistDetailView" or "Noctis.Views.AlbumDetailView").Take(1).ToArray())
            {
                if (page.DataContext is not {} vm || !ReferenceEquals(vm, current)) continue;
                if (page.GetType().FullName == "Noctis.Views.ArtistDetailView")
                {
                    if (!_artists.ContainsKey(vm) && _pending.Add(vm)) _ = LoadArtist(page, vm);
                    else if (_artists.TryGetValue(vm, out var state))
                    {
                        if (!ReferenceEquals(state.Page, page)) ReattachArtist(state, page);
                        if (state.Interrupted && !state.Busy) _ = LoadArtist(page, vm);
                        if (state.Profile is not null && (!Values(Get(vm, "Releases")).SequenceEqual(state.Displayed) || ViewKey(vm) != state.ViewKey)) Apply(state);
                    }
                }
                else if (page.GetType().FullName == "Noctis.Views.AlbumDetailView")
                {
                    if (_albums.TryGetValue(vm, out var album)) { if (!ReferenceEquals(album.Page, page)) ReattachAlbum(album, page); EnsureAlbumArtwork(album); }
                    else if (!_albumNotices.ContainsKey(vm) && _pending.Add(vm)) _ = LoadAlbum(page, vm);
                    else if (_albumNotices.TryGetValue(vm, out var notice) && !ReferenceEquals(notice.Page, page))
                    {
                        if (notice.Notice.Parent is Panel old) old.Children.Remove(notice.Notice);
                        if (page.FindControl<ItemsControl>("DiscGroupList")?.Parent is StackPanel panel) panel.Children.Insert(0, notice.Notice);
                        _albumNotices[vm] = (page, notice.Notice);
                    }
                }
            }
            if (current is null || !_artists.TryGetValue(current, out var shown)) return;
            foreach (var button in shown.Page.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.DataContext is {} tile && _tiles.ContainsKey(tile)))
            {
                var at = button.TranslatePoint(default, window);
                if (at is not {} point || point.Y + button.Bounds.Height < 0 || point.Y > window.Bounds.Height) continue;
                var tile = button.DataContext!;
                if (string.IsNullOrEmpty(Get(tile, "ArtworkPath") as string) && _tiles[tile].Artwork is { Length: > 0 } url && _artAttempted.Add(tile)) _ = LoadArtwork(tile, url);
            }
        }
        catch (Exception ex) { if (!_reported) { _reported = true; _log("CATALOG_HOST_UNSUPPORTED: " + ex.Message); } }
    }
    public void OpenArtist(CatalogArtist artist)
    {
        SaveChoice(artist.Name, artist);
        var main = _main() ?? throw Unsupported("main window"); NativeCatalogBridge.OpenArtist(main, artist.Name);
        _window()?.Activate();
    }
    private async Task<CatalogArtist?> ResolveArtist(string name, WrapPanel? choices, CancellationToken ct, Action<CatalogArtist>? choose = null)
    {
        if (_chosen.TryGetValue(name, out var saved)) return saved;
        var result = await _catalog().SearchAsync(name, false, ct);
        var exact = result.Value.Where(a => BotText.Key(a.Name) == BotText.Key(name)).ToArray();
        if (exact.Length == 1) { SaveChoice(name, exact[0]); return exact[0]; }
        if (choices is null) throw new InvalidOperationException("CATALOG_ARTIST_AMBIGUOUS: Open the artist page and select the correct catalog identity first. Local music was kept.");
        foreach (var artist in exact.Length > 0 ? exact : result.Value.ToArray())
        {
            var button = NativeButton(artist.Name + (artist.Genre.Length > 0 ? " · " + artist.Genre : "") + " (" + artist.Id + ")");
            button.Click += (_, _) => { SaveChoice(name, artist); choose?.Invoke(artist); }; choices.Children.Add(button);
        }
        return null;
    }
    private async Task LoadArtist(UserControl page, object vm, bool refresh = false)
    {
        if (_artists.TryGetValue(vm, out var active) && active.Busy) return;
        ArtistState? state = null;
        CancellationTokenSource? loading = null;
        try
        {
            var panel = page.FindControl<StackPanel>("PageContent") ?? throw Unsupported("artist sections");
            if (!_artists.TryGetValue(vm, out state))
            {
                state = new(vm, page, panel); state.Strip.Children.Add(state.Status); panel.Children.Insert(Math.Min(1, panel.Children.Count), state.Strip); _artists.Add(vm, state);
                var captured = state;
                // Button.Click is a bubble event. The native command executes only after
                // RaiseEvent returns, so marking it handled here prevents marker playback.
                state.Click = (_, e) => ArtistClick(captured, e); page.AddHandler(Button.ClickEvent, state.Click, RoutingStrategies.Bubble);
                state.Context = (_, e) => { if (TileAt(e.Source as Control) is not null) e.Handled = true; }; page.AddHandler(Control.ContextRequestedEvent, state.Context, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            }
            state.Busy = true; state.Interrupted = false; state.LoadStop = loading = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            state.Loading.IsVisible = true; state.Strip.Children.Clear(); state.Strip.Children.Add(state.Status); state.Strip.Children.Add(state.Loading); state.Status.Text = "Loading releases… Your local music stays available.";
            var name = Get(vm, "ArtistName") as string ?? "";
            var identity = await ResolveArtist(name, state.Strip, loading.Token, chosen => { _ = LoadArtist(state.Page, vm); });
            if (identity is null) { state.Status.Text = "Choose the artist's catalog once (several artists share this name):"; return; }
            var saved = await _catalog().SavedArtistAsync(identity, loading.Token); loading.Token.ThrowIfCancellationRequested();
            if (_disposed || !_enabled() || !_artists.ContainsKey(vm)) return;
            if (saved is not null)
            {
                if (!ReferenceEquals(state.Profile, saved.Value)) { state.Profile = saved.Value; Apply(state); }
                state.Status.Text = $"{saved.Value.Releases.Count} online releases · cached catalog · checking for updates…";
            }
            var result = await _catalog().ArtistAsync(identity, refresh, loading.Token); if (_disposed || !_enabled() || !_artists.ContainsKey(vm)) return;
            if (!ReferenceEquals(state.Profile, result.Value)) { state.Profile = result.Value; Apply(state); }
            state.Status.Text = $"{result.Value.Releases.Count} online releases · {(identity.Id.StartsWith("apple:") ? "Apple Music" : "Deezer")}" + (result.Cached ? " · cached catalog" : "");
            ToolTip.SetTip(state.Status, result.Value.Notice + " " + result.Warning);
            var retry = NativeButton("Refresh catalog"); retry.Click += (_, _) => { _ = LoadArtist(page, vm, true); }; state.Strip.Children.Add(retry);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested || loading?.IsCancellationRequested == true) { }
        catch (Exception ex)
        {
            if (state is not null && !_disposed)
            {
                state.Status.Text = ArtistCatalog.Explain(ex) + " Your local releases were kept.";
                var retry = NativeButton("Retry catalog"); retry.Click += (_, _) => { _ = LoadArtist(page, vm, refresh); }; state.Strip.Children.Add(retry);
            }
            else _log("CATALOG_HOST_UNSUPPORTED: " + ex.Message);
        }
        finally
        {
            if (state is not null) { state.Busy = false; state.Loading.IsVisible = false; state.LoadStop = null; if (state.Interrupted) QueueScan(); }
            loading?.Dispose(); _pending.Remove(vm);
        }
    }
    private void Apply(ArtistState state)
    {
        // Keep aliases until the artist state is restored: outgoing controls can receive input
        // before the next layout turn after a rescan. They still need the metadata-only guard.
        var added = ApplyArtist(state.Vm, state.Profile!, state.CurrentTiles, consolidated: true); state.CurrentTiles = added; foreach (var pair in added) _tiles[pair.Key] = pair.Value;
        state.Displayed = Values(Get(state.Vm, "Releases")); state.ViewKey = ViewKey(state.Vm);
    }
    private static string ViewKey(object vm) => string.Join("|", Get(vm, "SelectedTab"), Get(vm, "ReleaseSortMode"), Get(vm, "GridColumns"), vm.GetType().GetField("_query", Private)?.GetValue(vm));
    private void SaveChoice(string name, CatalogArtist artist)
    {
        _chosen[name] = artist;
        if (_choicesPath is not null) try { AtomicJson.Write(_choicesPath, _chosen); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log("CATALOG_CHOICE_SAVE_FAILED: artist choice works for this session but could not be saved."); }
    }
    private void ReattachArtist(ArtistState state, UserControl page)
    {
        var panel = page.FindControl<StackPanel>("PageContent") ?? throw Unsupported("artist sections");
        if (state.Click is not null) state.Page.RemoveHandler(Button.ClickEvent, state.Click);
        if (state.Context is not null) state.Page.RemoveHandler(Control.ContextRequestedEvent, state.Context);
        state.Panel.Children.Remove(state.Strip); state.Page = page; state.Panel = panel; panel.Children.Insert(Math.Min(1, panel.Children.Count), state.Strip);
        page.AddHandler(Button.ClickEvent, state.Click!, RoutingStrategies.Bubble); page.AddHandler(Control.ContextRequestedEvent, state.Context!, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }
    private void ReattachAlbum(AlbumState state, UserControl page)
    {
        var list = page.FindControl<ItemsControl>("DiscGroupList") ?? throw Unsupported("album track list");
        state.Rows?.Dispose(); state.List.ItemsSource = state.OriginalItems as IEnumerable; state.List.ItemTemplate = state.OriginalTemplate;
        state.Page = page; state.List = list; state.OriginalItems = list.ItemsSource; state.OriginalTemplate = list.ItemTemplate;
        var rows = new NativeAlbumRows(_main()!, state.Vm, state.Metadata, _actions, list.ItemTemplate, OpenArtist); state.Rows = rows;
        list.ItemTemplate = new FuncDataTemplate<object>((_, _) => rows); list.ItemsSource = new object[] { state.Metadata }; _ = rows.RefreshAsync();
    }
    private object? TileAt(Control? source)
    {
        for (Control? at = source; at is not null; at = at.GetVisualParent() as Control)
        {
            if (at.DataContext is {} data && _tiles.ContainsKey(data)) return data;
            if (at is Button { CommandParameter: {} parameter } && _tiles.ContainsKey(parameter)) return parameter;
        }
        return null;
    }
    private bool UseOwnedContext(Control source, object tile)
    {
        if (_main() is not {} main || !_tiles.TryGetValue(tile, out var release)) return false;
        var library = main.GetType().GetField("_library", Private)?.GetValue(main);
        var owned = library is null ? null : Values(Get(library, "Albums")).FirstOrDefault(a => CatalogEditions.Matches(release, Get(a, "Name") as string ?? "", Get(a, "Artist") as string ?? ""));
        if (owned is null) return false;
        var button = source.GetVisualAncestors().Prepend(source).OfType<Button>().FirstOrDefault(b => b.Classes.Contains("album-tile") && ReferenceEquals(b.DataContext, tile));
        if (button is null) return false;
        // Let Noctis build its original menu using the real local album and real tracks.
        // The catalog tile returns after this synchronous routed event has completed.
        button.DataContext = owned;
        Dispatcher.UIThread.Post(() => { if (ReferenceEquals(button.DataContext, owned)) button.DataContext = tile; });
        return true;
    }
    private void ArtistClick(ArtistState state, RoutedEventArgs e)
    {
        var button = e.Source as Button; if (button is null) return;
        var tile = TileAt(button);
        if (tile is null && ReferenceEquals(button.Command, Get(state.Vm, "PlayLatestReleaseCommand"))) tile = Get(state.Vm, "LatestRelease");
        if (tile is null || !_tiles.TryGetValue(tile, out var release)) return;
        e.Handled = true; _ = OpenRelease(tile, release, state.Status);
    }
    private async Task OpenRelease(object tile, CatalogRelease release, TextBlock status)
    {
        _openStop?.Cancel(); var stop = _openStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var origin = _main() is {} shell ? Get(shell, "CurrentView") : null;
        try
        {
            status.Text = "Opening " + release.Title + "… Reading metadata only.";
            var result = await _catalog().AlbumAsync(release, false, stop.Token); var matches = await _actions.MatchAsync(result.Value.Songs, stop.Token);
            if (_disposed || !_enabled() || stop.IsCancellationRequested || _main() is not {} main || !ReferenceEquals(Get(main, "CurrentView"), origin)) return;
            var vm = NativeCatalogBridge.OpenAlbum(main, tile, result.Value, matches); _opened[vm] = (result.Value, tile); Scan();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) status.Text = ArtistCatalog.Explain(ex); }
        finally { if (ReferenceEquals(_openStop, stop)) _openStop = null; stop.Dispose(); }
    }
    private async Task LoadAlbum(UserControl page, object vm)
    {
        if (!_albumLoading.Add(vm)) return;
        WrapPanel? notice = null;
        TextBlock? status = null;
        AlbumState? mounting = null;
        try
        {
            var list = page.FindControl<ItemsControl>("DiscGroupList") ?? throw Unsupported("native album track list");
            if (_albumNotices.TryGetValue(vm, out var previous)) { notice = previous.Notice; notice.Children.Clear(); }
            else
            {
                notice = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
                if (list.Parent is not StackPanel panel) throw Unsupported("native track list container");
                panel.Children.Insert(0, notice); _albumNotices[vm] = (page, notice);
            }
            status = new TextBlock { Text = "Reading online album metadata…", Opacity = .65, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; notice.Children.Add(status);
            CatalogAlbum metadata;
            if (_opened.TryGetValue(vm, out var opened)) metadata = opened.Metadata;
            else
            {
                var album = Get(vm, "Album") ?? throw Unsupported("album");
                var identity = await ResolveArtist(Get(album, "Artist") as string ?? "", notice, _stop.Token, chosen => { _ = LoadAlbum(page, vm); });
                if (identity is null) { status.Text = "Choose the correct catalog artist once:"; return; }
                var profile = await _catalog().ArtistAsync(identity!, false, _stop.Token);
                var release = profile.Value.Releases.FirstOrDefault(r => CatalogEditions.Matches(r, Get(album, "Name") as string ?? "", Get(album, "Artist") as string ?? ""));
                if (release is null) { status.Text = "This release was not found in the online catalog. Your local tracks are shown."; return; }
                metadata = (await _catalog().AlbumAsync(release, false, _stop.Token)).Value;
            }
            if (_disposed || !_enabled() || _main() is not {} main || _albums.ContainsKey(vm)) return;
            if (metadata.Songs.Count == 0) throw new InvalidDataException("The catalog returned no songs. Local tracks were kept.");
            // Validate the native row template before changing any native page state.
            var rows = new NativeAlbumRows(main, vm, metadata, _actions, list.ItemTemplate, OpenArtist);
            var originalAlbum = Get(vm, "Album")!; object originalLibrary;
            try { originalLibrary = CatalogAlbumLibrary.Attach(vm); } catch { rows.Dispose(); throw; }
            var state = mounting = new AlbumState(vm, page, list, originalLibrary, originalAlbum, metadata) { Rows = rows }; _albums.Add(vm, state);
            list.ItemTemplate = new FuncDataTemplate<object>((_, _) => rows); list.ItemsSource = new object[] { metadata };
            await rows.RefreshAsync(requireSuccess: true);
            if (_disposed || !_enabled() || !_albums.ContainsKey(vm)) return;
            EnsureAlbumArtwork(state);
            _opened.Remove(vm); _albumNotices.Remove(vm); if (notice.Parent is Panel parent) parent.Children.Remove(notice);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (mounting is not null && _albums.Remove(vm)) RestoreAlbum(mounting);
            if (!_disposed && notice is not null && status is not null)
            {
                status.Text = ArtistCatalog.Explain(ex) + " Your local tracks were kept.";
                var retry = NativeButton("Retry catalog"); retry.Click += (_, _) => { _ = LoadAlbum(page, vm); }; notice.Children.Add(retry);
            }
            else _log("CATALOG_ALBUM_UNAVAILABLE: " + ex.Message);
        }
        finally { _pending.Remove(vm); _albumLoading.Remove(vm); }
    }
    private async Task LoadArtwork(object tile, string url)
    {
        try
        {
            var path = await _catalog().ArtworkAsync(url, _stop.Token);
            if (path is null || _disposed || !_tiles.ContainsKey(tile)) return;
            Set(tile, "ArtworkPath", path);
            var notify = tile.GetType().GetMethod("OnPropertyChanged", Private, [typeof(string)]);
            notify?.Invoke(tile, ["ArtworkPath"]);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Optional cover art keeps the native placeholder on a network/cache error. */ }
    }
    private void EnsureAlbumArtwork(AlbumState state)
    {
        // A native library refresh resets HeaderArtPath to its persistence folder. The
        // catalog cover remains in our cache, so restore that view binding without refetching.
        var native = Get(state.Vm, "HeaderArtPath") as string;
        var path = !string.IsNullOrWhiteSpace(native) && File.Exists(native) ? native : state.CoverPath ?? state.OriginalHeaderArt ?? Get(Get(state.Vm, "Album")!, "ArtworkPath") as string;
        if (AlbumArtwork(state.Vm, path)) { state.CoverPath = path; return; }
        if (state.LoadingCover || DateTimeOffset.UtcNow < state.CoverRetry || !ArtistCatalog.ArtworkUrl(state.Metadata.Release.Artwork)) return;
        state.LoadingCover = true; _ = LoadAlbumArtwork(state);
    }
    private async Task LoadAlbumArtwork(AlbumState state)
    {
        try
        {
            var path = await _catalog().ArtworkAsync(state.Metadata.Release.Artwork, _stop.Token);
            if (_disposed || !_enabled() || !_albums.TryGetValue(state.Vm, out var active) || !ReferenceEquals(active, state)) return;
            if (AlbumArtwork(state.Vm, path)) state.CoverPath = path;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* A failed optional cover never replaces usable local artwork or blocks songs. */ }
        finally { state.LoadingCover = false; state.CoverRetry = DateTimeOffset.UtcNow.AddMinutes(1); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _stop.Cancel(); _openStop?.Cancel(); _timer.Stop(); _clickGuard.Dispose(); _contextGuard.Dispose(); _loadedGuard.Dispose();
        if (_shellNotify is not null) _shellNotify.PropertyChanged -= OnShellChanged;
        if (_viewNotify is not null) _viewNotify.PropertyChanged -= OnViewChanged;
        RestorePages();
    }
    private void RestorePages()
    {
        foreach (var state in _albums.Values) RestoreAlbum(state);
        foreach (var state in _artists.Values)
        {
            state.LoadStop?.Cancel();
            if (state.Click is not null) state.Page.RemoveHandler(Button.ClickEvent, state.Click);
            if (state.Context is not null) state.Page.RemoveHandler(Control.ContextRequestedEvent, state.Context);
            state.Panel.Children.Remove(state.Strip); try { RestoreArtist(state.Vm); } catch { }
        }
        _albums.Clear(); _artists.Clear(); _tiles.Clear(); _artAttempted.Clear();
        foreach (var notice in _albumNotices.Values) if (notice.Notice.Parent is Panel panel) panel.Children.Remove(notice.Notice);
        _albumNotices.Clear(); _opened.Clear();
    }
    private void RestoreAlbum(AlbumState state)
    {
        state.Rows?.Dispose(); state.List.ItemsSource = state.OriginalItems as IEnumerable; state.List.ItemTemplate = state.OriginalTemplate;
        try
        {
            state.Vm.GetType().GetField("_library", Private)!.SetValue(state.Vm, state.OriginalLibrary); Set(state.Vm, "Album", state.OriginalAlbum);
            Set(state.Vm, "HeaderArtPath", state.OriginalHeaderArt);
            var tracks = Get(state.Vm, "Tracks")!; var type = state.OriginalAlbum.GetType().GetProperty("Tracks")!.PropertyType.GenericTypeArguments.Single();
            tracks.GetType().GetMethod("ReplaceAll")!.Invoke(tracks, [List(type, state.OriginalTracks)]);
            state.Vm.GetType().GetMethod("BuildDiscGroups", Private)!.Invoke(state.Vm, null);
        }
        catch (Exception ex) { _log("CATALOG_RESTORE_FAILED: " + ex.Message); }
    }
    private static Button NativeButton(string title)
    {
        var button = new Button { Content = title, Margin = new Thickness(8, 0, 0, 4), Padding = new Thickness(12, 5), CornerRadius = new CornerRadius(999), FontWeight = FontWeight.Bold };
        button.Classes.Add("accent-btn"); return button;
    }
}
