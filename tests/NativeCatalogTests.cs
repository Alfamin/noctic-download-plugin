using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeMusicFinder;
using static FreeMusicFinder.NativeCatalogBridge;

namespace FreeMusicFinder.Tests;

internal static class NativeCatalogTests
{
    public static void Run(string? root, string? pictures)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        Check.Section("native catalog integration: existing artist grids and existing album page, isolated library");
        var app = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Noctis");
        var core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Noctis.Core");
        // Load the shipped theme resources into the isolated TEST application. No Noctis App
        // instance/services, settings, watchers, Telegram workers or real audio are started.
        Application.Current!.Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis.UI/Assets/Icons.axaml") });
        Application.Current.Resources["InterSemiBold"] = new Avalonia.Media.FontFamily("avares://Noctis.UI/Assets/Fonts/Inter-SemiBold.ttf#Inter SemiBold");
        Application.Current.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis.UI/Assets/Styles.axaml") });
        Application.Current.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Noctis/")) { Source = new Uri("avares://Noctis/Controls/EqVisualizer.axaml") });
        var folder = Check.NewFolder("native-catalog"); var trackType = core.GetType("Noctis.Models.Track", true)!; var albumType = core.GetType("Noctis.Models.Album", true)!;
        Check.True(AlbumNavigation(app.GetType("Noctis.ViewModels.MainWindowViewModel", true)!, albumType).IsPublic, "album navigation resolves the installed host's public method and optional arguments");
        var track = Activator.CreateInstance(trackType)!; var path = Path.Combine(folder, "Owned.flac"); File.WriteAllText(path, "Synthetic test marker");
        Set(track, "Id", Guid.NewGuid()); Set(track, "Title", "Owned"); Set(track, "Artist", "North Star"); Set(track, "AlbumArtist", "North Star"); Set(track, "Album", "Orbit"); Set(track, "FilePath", path); Set(track, "Duration", TimeSpan.FromSeconds(180)); Set(track, "TrackNumber", 1); Set(track, "TrackCount", 3);
        var localAlbum = Activator.CreateInstance(albumType)!; Set(localAlbum, "Id", Guid.NewGuid()); Set(localAlbum, "Name", "Orbit"); Set(localAlbum, "Artist", "North Star"); Set(localAlbum, "Year", 2026); Set(localAlbum, "TrackCount", 1); Set(localAlbum, "Tracks", List(trackType, [track])); Set(track, "AlbumId", Get(localAlbum, "Id"));
        var originalTracks = List(trackType, [track]); var originalAlbums = List(albumType, [localAlbum]);
        var libraryType = core.GetType("Noctis.Services.ILibraryService", true)!;
        var library = DispatchProxy.Create(libraryType, typeof(NativePlaylistTests.Stub));
        ((NativePlaylistTests.Stub)library).Custom = (m, args) => m.Name switch
        {
            "get_Tracks" or "get_AllTracks" => originalTracks,
            "get_Albums" => originalAlbums,
            "get_Artists" => List(core.GetType("Noctis.Models.Artist", true)!, []),
            "GetTrackById" => Equals(Get(track, "Id"), args![0]) ? track : null,
            _ => NativePlaylistTests.Stub.Unhandled,
        };
        var persistence = Activator.CreateInstance(core.GetType("Noctis.Services.PersistenceService", true)!, BindingFlags.Instance | BindingFlags.NonPublic, null, [folder], null)!;
        object Proxy(Type type) => DispatchProxy.Create(type, typeof(NativePlaylistTests.Stub));
        var playerType = app.GetType("Noctis.ViewModels.PlayerViewModel", true)!;
        var playerCtor = playerType.GetConstructors().Single();
        var player = playerCtor.Invoke(playerCtor.GetParameters().Select(p => p.Name switch { "library" => library, "persistence" => persistence, _ => p.HasDefaultValue ? p.DefaultValue : Proxy(p.ParameterType) }).ToArray());
        var artistType = app.GetType("Noctis.ViewModels.ArtistDetailViewModel", true)!;
        var artistVm = artistType.GetConstructors().Single().Invoke(new object?[] { "North Star", library, player, null, null, null, null, null, null, null });
        using var catalog = DiscoveryTests.Catalog(Path.Combine(folder, "catalog"), new DiscoveryTests.Http());
        var profile = Finish(catalog.ArtistAsync(DiscoveryTests.Star, false, default)).Value;
        var metadata = Finish(catalog.AlbumAsync(DiscoveryTests.Orbit, false, default)).Value;
        var tiles = ApplyArtist(artistVm, profile);
        Check.Equal(2, tiles.Count, "catalog albums and singles added to the native artist model");
        Check.Equal(1, (int)Get(artistVm, "AlbumCount")!, "native Albums section uses catalog release type");
        Check.Equal(1, (int)Get(artistVm, "SingleCount")!, "native Singles & EPs section is filled, including missing releases");
        Check.Equal(1, originalAlbums.Count, "global library album list is unchanged");
        Check.Equal(1, originalTracks.Count, "global track list contains no catalog-only placeholders");
        Check.True(ReferenceEquals(((IList)Get(localAlbum, "Tracks")!)[0], track), "original album and track objects retain their data");
        Check.True(Values(artistType.GetMethod("GetAllTracks", Private)!.Invoke(artistVm, null)).All(t => Get(t, "FilePath") is string file && File.Exists(file)), "artist header playback, queue and file commands enumerate real local tracks only");
        Set(artistVm, "SelectedTab", "singles");
        Check.Until(() => Values(Get(artistVm, "SingleReleases")).Count == 1, "native Singles tab navigation shows fetched release");
        var artistView = (UserControl)Activator.CreateInstance(app.GetType("Noctis.Views.ArtistDetailView", true)!)!; artistView.DataContext = artistVm;
        var artistWindow = new Window { Content = artistView, Width = 1280, Height = 950 }; artistWindow.Show(); Save(artistWindow, pictures, "30 native artist singles"); artistWindow.Content = null; artistWindow.Close();
        var featured = profile with { Releases = profile.Releases.Concat(new[] { profile.Releases[0] with { Id = "deezer:999", Artist = new CatalogArtist("deezer:9", "Another artist"), Title = "Featured release" } }).ToList() };
        ApplyArtist(artistVm, featured);
        Check.True(Values(Get(artistVm, "AppearsOn")).Any(a => (string?)Get(a, "Name") == "Featured release"), "featured release fills the existing Appears On section");
        RestoreArtist(artistVm);
        Check.Equal(1, Values(Get(artistVm, "Releases")).Count, "restoring artist returns to local releases only");

        var sidebar = Activator.CreateInstance(app.GetType("Noctis.ViewModels.SidebarViewModel", true)!, persistence, library)!;
        var albumVmType = app.GetType("Noctis.ViewModels.AlbumDetailViewModel", true)!;
        var ctor = albumVmType.GetConstructors().Single();
        var albumVm = ctor.Invoke(ctor.GetParameters().Select(p => p.Name switch { "album" => localAlbum, "player" => player, "persistence" => persistence, "library" => library, "sidebar" => sidebar, _ => p.HasDefaultValue ? p.DefaultValue : Proxy(p.ParameterType) }).ToArray());
        var albumView = (UserControl)Activator.CreateInstance(app.GetType("Noctis.Views.AlbumDetailView", true)!)!; albumView.DataContext = albumVm;
        var list = albumView.FindControl<ItemsControl>("DiscGroupList")!;
        var originalItems = list.ItemsSource; var originalTemplate = list.ItemTemplate;
        var isolated = new SpotifyPlaylistTests.Library(); isolated.Songs.Add(new((Guid)Get(track, "Id")!, "North Star", "Owned", path, TimeSpan.FromSeconds(180)));
        int fetched = 0; bool loggedIn = true;
        BotTrack Track(MusicRequest r) => new(r.Title, r.Artist, r.Duration, r.Title, async (output, _, ct) => { Interlocked.Increment(ref fetched); await output.WriteAsync(new byte[] { 1, 2 }, ct); return "mp3"; }) { Request = r };
        using var downloads = new Downloads(_ => { }); downloads.SetPaused(true);
        var actions = new DiscoveryActions(isolated, downloads, Track, () => Path.Combine(folder, "saved"), () => loggedIn);
        var shell = new Shell(library, player);
        var previous = CatalogAlbumLibrary.Attach(albumVm);
        Check.True(ReferenceEquals(previous, library), "album-only library overlay retains the original service");
        using var rows = new NativeAlbumRows(shell, albumVm, metadata, actions, list.ItemTemplate, _ => { });
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((_, _) => rows); list.ItemsSource = new object[] { metadata };
        Finish(rows.RefreshAsync());
        var albumWindow = new Window { Content = albumView, Width = 1280, Height = 1000 }; albumWindow.Show(); Dispatcher.UIThread.RunJobs();
        Check.True(rows.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Missing"), "missing song uses Noctis's own track template inside the existing album list");
        Check.Equal(1, Values(Get(albumVm, "Tracks")).Count, "native Play/Shuffle still receive only verified local tracks");
        Check.Equal(1, originalTracks.Count, "full album display does not fabricate global library tracks");
        Check.Equal(0, downloads.History.Count, "opening enriched native album downloads no audio");
        Check.True(!ReferenceEquals(Get(albumVm, "Album"), localAlbum) && (int)Get(localAlbum, "TrackCount")! == 1, "native album header gets a transient clone; original local metadata is unchanged");
        int back = 0; var backField = albumVmType.GetField("BackRequested", Private)!; backField.SetValue(albumVm, new EventHandler((_, _) => back++));
        albumVmType.GetMethod("RefreshFromLibrary", Private)!.Invoke(albumVm, null);
        Check.Equal(0, back, "a library update does not kick a catalog album out of its native page");
        Save(albumWindow, pictures, "31 native album owned and missing");
        Button Find(string title) => albumView.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, title));
        Check.Until(() => albumView.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Download all missing…")), "album-wide download action sits with Noctis's original header buttons");
        Check.Equal(0, rows.GetVisualDescendants().OfType<CheckBox>().Count(), "native album selection has no checkboxes");
        albumWindow.Width = 900; Dispatcher.UIThread.RunJobs();
        var headerDownload = Find("Download all missing…");
        var headerPoint = headerDownload.TranslatePoint(default, albumWindow)!.Value;
        Check.True(headerPoint.X >= 0 && headerPoint.X + headerDownload.Bounds.Width <= albumWindow.Bounds.Width, "native album download action wraps within a narrow page");
        Save(albumWindow, pictures, "32 native album narrow"); albumWindow.Width = 1280; Dispatcher.UIThread.RunJobs();
        Find("Download all missing…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check.Equal(0, downloads.Active.Count, "Download all asks for its album-wide confirmation before queueing");
        Find("Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void CtrlTap(string id)
        {
            Dispatcher.UIThread.RunJobs();
            var item = rows.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext is CatalogSong s && s.Id == id);
            var at = item.TranslatePoint(new Point(125, item.Bounds.Height / 2), albumWindow)!.Value;
            albumWindow.MouseDown(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Control);
            albumWindow.MouseUp(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
        }
        CtrlTap("deezer:102"); CtrlTap("deezer:103");
        Check.Equal(2, rows.GetVisualDescendants().OfType<ListBoxItem>().Count(c => c.Classes.Contains("ctrl-selected")), "real Ctrl+click selects missing songs with native row highlighting");
        Find("Queue 2 downloads").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check.Until(() => downloads.Active.Count == 2, "native album queues exactly the selected missing songs");
        Check.Equal(0, fetched, "batch selection preserves paused downloads");
        Find("Download now").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check.Until(() => downloads.History.Any(d => d.State == DownloadState.Saved), "one native row's Download now promotes only its song");
        Check.True(downloads.Paused && downloads.Active.Count == 1 && fetched == 1, "the rest of the album remains queued and paused");
        Check.Equal("Synthetic test marker", File.ReadAllText(path), "owned audio marker untouched by all native UI actions");
        albumWindow.Content = null; albumWindow.Close(); rows.Dispose();
        albumVmType.GetField("_library", Private)!.SetValue(albumVm, library); Set(albumVm, "Album", localAlbum);
        list.ItemsSource = originalItems; list.ItemTemplate = originalTemplate;

        Check.Section("native page lifecycle: Back navigation, click guards and disable restoration");
        bool enabled = true; var logs = new List<string>(); var originalSingleCount = (int)Get(artistVm, "SingleCount")!;
        shell.CurrentView = artistVm;
        var lifecycleWindow = new Window { Content = artistView, Width = 1280, Height = 950 }; lifecycleWindow.Show();
        using var pages = new NativeCatalogPages(() => catalog, actions, logs.Add, () => enabled, () => shell, window: () => lifecycleWindow);
        pages.Scan();
        Check.Until(() => artistView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("online releases") == true), "page adapter fills the actual native artist view");
        var catalogStatus = artistView.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.Contains("online releases") == true);
        void Guard(UserControl view)
        {
            var tile = Values(Get(artistVm, "SingleReleases")).Single();
            var button = new Button { DataContext = tile, Content = "Synthetic tile action" };
            int nativeHandler = 0; button.Click += (_, _) => nativeHandler++;
            var panel = view.FindControl<StackPanel>("PageContent")!; panel.Children.Add(button); Dispatcher.UIThread.RunJobs();
            var click = new RoutedEventArgs(Button.ClickEvent, button); button.RaiseEvent(click);
            Check.True(click.Handled, "catalog tile click is intercepted before native marker playback");
            Check.Equal(0, nativeHandler, "catalog guard runs before native tile code-behind handlers");
            panel.Children.Remove(button);
        }
        Guard(artistView);
        var catalogOrbit = Values(Get(artistVm, "Releases")).First(a => Equals(Get(a, "Name"), "Orbit"));
        var ownedTileButton = new Button { DataContext = catalogOrbit }; ownedTileButton.Classes.Add("album-tile");
        var dots = new Button { Content = "…" }; dots.Classes.Add("tile-more"); ownedTileButton.Content = dots;
        var artistPanel = artistView.FindControl<StackPanel>("PageContent")!; artistPanel.Children.Add(ownedTileButton); Dispatcher.UIThread.RunJobs();
        bool realMenuAlbum = false; dots.Click += (_, e) => { realMenuAlbum = ReferenceEquals(ownedTileButton.DataContext, localAlbum); e.Handled = true; };
        dots.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dots));
        Check.True(realMenuAlbum, "owned artist tile keeps native menu handling with its original real local album");
        Dispatcher.UIThread.RunJobs(); Check.True(ReferenceEquals(ownedTileButton.DataContext, catalogOrbit), "catalog tile display is restored after its native owned-album menu opens");
        artistPanel.Children.Remove(ownedTileButton);
        var backArtist = (UserControl)Activator.CreateInstance(artistView.GetType())!; backArtist.DataContext = artistVm; lifecycleWindow.Content = backArtist;
        Dispatcher.UIThread.RunJobs(); pages.Scan(); Guard(backArtist);
        Check.True(!artistView.GetVisualDescendants().Contains(catalogStatus) && backArtist.GetVisualDescendants().Contains(catalogStatus), "Back reattaches catalog status to the newly created native artist view");

        shell.CurrentView = albumVm; lifecycleWindow.Content = albumView; Dispatcher.UIThread.RunJobs(); pages.Scan();
        Check.Until(() => albumView.GetVisualDescendants().OfType<NativeAlbumRows>().Any(), "adapter enriches an already open local album");
        var backAlbum = (UserControl)Activator.CreateInstance(albumView.GetType())!; backAlbum.DataContext = albumVm; lifecycleWindow.Content = backAlbum;
        Dispatcher.UIThread.RunJobs(); pages.Scan();
        Check.Until(() => backAlbum.GetVisualDescendants().OfType<NativeAlbumRows>().Any(r => r.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Missing")), "Back recreates the full native album rows in the new view");
        enabled = false; pages.Scan(); Dispatcher.UIThread.RunJobs();
        Check.True(!backAlbum.GetVisualDescendants().OfType<NativeAlbumRows>().Any(), "disabling catalog restores the original native album template");
        Check.True(ReferenceEquals(albumVmType.GetField("_library", Private)!.GetValue(albumVm), library) && ReferenceEquals(Get(albumVm, "Album"), localAlbum), "disabling restores the original album and library service");
        Check.Equal(1, Values(Get(albumVm, "Tracks")).Count, "disabling retains the original local playback list");
        Check.Equal(originalSingleCount, (int)Get(artistVm, "SingleCount")!, "disabling returns artist sections to original local data");
        Check.True(logs.Count == 0, "native lifecycle has no unsupported-host or restoration errors");
        enabled = true; isolated.ReadError = new IOException("Synthetic library scan unavailable"); pages.Scan();
        Check.Until(() => backAlbum.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Synthetic library scan unavailable") == true), "library snapshot failure shows a precise retryable catalog error");
        Check.True(!backAlbum.GetVisualDescendants().OfType<NativeAlbumRows>().Any() && ReferenceEquals(Get(albumVm, "Album"), localAlbum), "failed enrichment rolls back the native template and original album");
        Check.True(ReferenceEquals(albumVmType.GetField("_library", Private)!.GetValue(albumVm), library) && Values(Get(albumVm, "Tracks")).Count == 1, "failed enrichment keeps native service and playback tracks intact");
        isolated.ReadError = null;
        backAlbum.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Retry catalog")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check.Until(() => backAlbum.GetVisualDescendants().OfType<NativeAlbumRows>().Any(), "Retry catalog recovers without leaving or recreating the native page");
        lifecycleWindow.Close(); pages.Dispose();
        ((IDisposable)albumVm).Dispose(); ((IDisposable)artistVm).Dispose();
        if (player is IDisposable disposable) disposable.Dispose();
    }
    private static T Finish<T>(Task<T> task) { Check.Until(() => task.IsCompleted, "native catalog operation completed"); return task.GetAwaiter().GetResult(); }
    private static void Finish(Task task) { Check.Until(() => task.IsCompleted, "native catalog operation completed"); task.GetAwaiter().GetResult(); }
    private sealed class Shell(object library, object player) { private readonly object _library = library; public object Player { get; } = player; public object? CurrentView { get; set; } }
    private static void Save(Window window, string? folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder)) return; Directory.CreateDirectory(folder); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var file = File.Create(Path.Combine(folder, name + ".png")); window.CaptureRenderedFrame()?.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
