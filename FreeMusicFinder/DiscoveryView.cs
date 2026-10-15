using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace FreeMusicFinder;

/// <summary>Artist search only. Results open Noctis's own artist page, never a second catalog UI.</summary>
internal sealed class DiscoveryView : UserControl, IDisposable
{
    private readonly Func<ArtistCatalog> _catalog;
    private readonly Action<CatalogArtist> _open;
    private readonly Func<string, IReadOnlyList<string>>? _localAlbums;
    private readonly TextBox _query = new() { PlaceholderText = "Find an artist, even outside your local library" };
    private readonly TextBlock _status = FinderTheme.Caption("Choose an artist to open their existing Noctis page with online releases.");
    private readonly StackPanel _results = new() { Spacing = 8 };
    private readonly ScrollViewer _scroll = new();
    private readonly List<(CatalogArtist Artist, ArtistPicture Picture)> _pictures = [];
    private readonly Dictionary<string, (Border Card, TextBlock Badge)> _cards = new();
    private CancellationTokenSource? _search;
    private bool _disposed;
    public DiscoveryView(Func<ArtistCatalog> catalog, Action<CatalogArtist> open, Func<string, IReadOnlyList<string>>? localAlbums = null)
    {
        _catalog = catalog; _open = open; _localAlbums = localAlbums;
        var search = new Button { Content = "Find artists", Margin = new Thickness(8, 0, 0, 0) }; FinderTheme.Primary(search);
        search.Click += (_, _) => Search(_query.Text ?? "");
        _query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Search(_query.Text ?? ""); } };
        var bar = new Grid { ColumnDefinitions = new("*,Auto") }; bar.Children.Add(_query); Grid.SetColumn(search, 1); bar.Children.Add(search);
        var header = new StackPanel { Spacing = 10 }; header.Children.Add(bar); header.Children.Add(_status);
        _scroll.Content = _results; _scroll.ScrollChanged += (_, _) => LoadPictures(); _scroll.SizeChanged += (_, _) => LoadPictures();
        var root = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 }; root.Children.Add(header); Grid.SetRow(_scroll, 1); root.Children.Add(_scroll); Content = root;
    }
    public async void Search(string name, bool openExact = false)
    {
        name = name.Trim(); if (_disposed || name.Length == 0) return; _query.Text = name;
        _search?.Cancel(); _search?.Dispose(); var source = _search = new CancellationTokenSource();
        ClearPictures(); _cards.Clear(); _results.Children.Clear();
        _status.Text = "Finding catalog artists…";
        try
        {
            var result = await _catalog().SearchAsync(name, false, source.Token); if (_disposed || source.IsCancellationRequested) return;
            Render(result.Value, null);
            var local = _localAlbums?.Invoke(name) ?? [];
            ArtistIdentity? identity = null;
            if (local.Count > 0) identity = await _catalog().IdentifyAsync(name, local, null, source.Token, result.Value);
            if (_disposed || source.IsCancellationRequested) return;
            if (identity?.LibraryMatch == true) Render(result.Value, identity);
            _status.Text = result.Value.Count == 0 ? "No artist found. Try another spelling; unreleased songs can still be found in Search." : "Choose an artist to open their Noctis page. " + result.Warning;
            var exact = result.Value.Where(a => BotText.Key(a.Name) == BotText.Key(name)).ToArray();
            if (openExact && identity?.LibraryMatch == true) _open(identity.Artist!);
            else if (openExact && exact.Length == 1) _open(exact[0]);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) _status.Text = ArtistCatalog.Explain(ex); }
    }
    private void Render(IReadOnlyList<CatalogArtist> artists, ArtistIdentity? identity)
    {
        if (artists.Count != _cards.Count || artists.Any(a => !_cards.ContainsKey(a.Id))) { ClearPictures(); _cards.Clear(); }
        _results.Children.Clear();
        var exactKey = BotText.Key(_query.Text);
        var ordered = artists.OrderByDescending(a => a.Id == identity?.Artist?.Id).ThenByDescending(a => BotText.Key(a.Name) == exactKey).ToArray();
        foreach (var artist in ordered)
        {
            var label = "";
            if (identity?.LibraryMatch == true && artist.Id == identity.Artist?.Id) label = "Matches your library · " + string.Join(", ", identity.Albums.Take(3));
            else if (BotText.Key(artist.Name) == exactKey && ordered.Any(a => a.Id != artist.Id && BotText.Key(a.Name) == exactKey)) label = ReferenceEquals(artist, ordered.First()) ? "Top search match" : "Different artist with the same name";
            if (_cards.TryGetValue(artist.Id, out var existing)) { existing.Badge.Text = label; existing.Badge.IsVisible = label.Length > 0; _results.Children.Add(existing.Card); continue; }
            var text = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(new TextBlock { Text = artist.Name, FontWeight = FontWeight.SemiBold, FontSize = 18 });
            var badge = FinderTheme.Caption(label); badge.IsVisible = label.Length > 0; text.Children.Add(badge);
            text.Children.Add(FinderTheme.Caption(string.Join(" · ", new[] { artist.Genre, artist.Id.StartsWith("apple:") ? "Apple Music" : "Deezer" }.Where(s => !string.IsNullOrEmpty(s)))));
            var picture = new ArtistPicture(artist); _pictures.Add((artist, picture));
            var row = new Grid { ColumnDefinitions = new("64,*,Auto"), ColumnSpacing = 16 }; row.Children.Add(picture); Grid.SetColumn(text, 1); row.Children.Add(text);
            var open = new Button { Content = "Open in Noctis →", VerticalAlignment = VerticalAlignment.Center };
            open.Click += (_, _) => { try { _open(artist); } catch (Exception ex) { _status.Text = ex.Message; } }; Grid.SetColumn(open, 2); row.Children.Add(open);
            var card = FinderTheme.Card(row); _cards[artist.Id] = (card, badge); _results.Children.Add(card);
        }
        Dispatcher.UIThread.Post(LoadPictures, DispatcherPriority.Loaded);
    }
    private void LoadPictures()
    {
        if (_disposed || _search is null || _scroll.Bounds.Height <= 0) return;
        foreach (var (artist, picture) in _pictures)
            if (picture.TranslatePoint(default, _scroll) is {} p && p.Y + picture.Height >= 0 && p.Y <= _scroll.Bounds.Height)
                picture.Load(_catalog(), artist, _search.Token);
    }
    private void ClearPictures() { foreach (var (_, picture) in _pictures) picture.Dispose(); _pictures.Clear(); }
    public void Dispose() { _disposed = true; _search?.Cancel(); _search?.Dispose(); ClearPictures(); _cards.Clear(); }
}
