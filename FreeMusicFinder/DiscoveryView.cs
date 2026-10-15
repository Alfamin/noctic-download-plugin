using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace FreeMusicFinder;

/// <summary>Artist search only. Results open Noctis's own artist page, never a second catalog UI.</summary>
internal sealed class DiscoveryView : UserControl, IDisposable
{
    private readonly Func<ArtistCatalog> _catalog;
    private readonly Action<CatalogArtist> _open;
    private readonly TextBox _query = new() { PlaceholderText = "Find an artist, even outside your local library" };
    private readonly TextBlock _status = FinderTheme.Caption("Choose an artist to open their existing Noctis page with online releases.");
    private readonly StackPanel _results = new() { Spacing = 8 };
    private CancellationTokenSource? _search;
    private bool _disposed;
    public DiscoveryView(Func<ArtistCatalog> catalog, Action<CatalogArtist> open)
    {
        _catalog = catalog; _open = open;
        var search = new Button { Content = "Find artists", Margin = new Thickness(8, 0, 0, 0) }; FinderTheme.Primary(search);
        search.Click += (_, _) => Search(_query.Text ?? "");
        _query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Search(_query.Text ?? ""); } };
        var bar = new Grid { ColumnDefinitions = new("*,Auto") }; bar.Children.Add(_query); Grid.SetColumn(search, 1); bar.Children.Add(search);
        var header = new StackPanel { Spacing = 10 }; header.Children.Add(bar); header.Children.Add(_status);
        var root = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 }; root.Children.Add(header); var scroll = new ScrollViewer { Content = _results }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
    }
    public async void Search(string name, bool openExact = false)
    {
        name = name.Trim(); if (_disposed || name.Length == 0) return; _query.Text = name;
        _search?.Cancel(); _search?.Dispose(); var source = _search = new CancellationTokenSource();
        _status.Text = "Finding catalog artists… No audio is requested.";
        try
        {
            var result = await _catalog().SearchAsync(name, false, source.Token); if (_disposed || source.IsCancellationRequested) return;
            _results.Children.Clear();
            foreach (var artist in result.Value)
            {
                var text = new StackPanel { Spacing = 6 }; text.Children.Add(new TextBlock { Text = artist.Name, FontWeight = FontWeight.SemiBold, FontSize = 18 });
                text.Children.Add(FinderTheme.Caption(artist.Genre + " · " + (artist.Id.StartsWith("apple:") ? "Apple Music" : "Deezer") + " · " + artist.Id));
                var open = new Button { Content = "Open in Noctis →", HorizontalAlignment = HorizontalAlignment.Left };
                open.Click += (_, _) => { try { _open(artist); } catch (Exception ex) { _status.Text = ex.Message; } }; text.Children.Add(open); _results.Children.Add(FinderTheme.Card(text));
            }
            _status.Text = result.Value.Count == 0 ? "No artist found. Try another spelling; unreleased songs can still be found in Search." : "Choose the artist. Albums and songs open in Noctis's existing pages. " + result.Warning;
            var exact = result.Value.Where(a => BotText.Key(a.Name) == BotText.Key(name)).ToArray(); if (openExact && exact.Length == 1) _open(exact[0]);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) _status.Text = ArtistCatalog.Explain(ex); }
    }
    public void Dispose() { _disposed = true; _search?.Cancel(); _search?.Dispose(); }
}
