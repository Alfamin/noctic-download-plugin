using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace FreeMusicFinder;

/// <summary>A small, optional portrait. Identity and navigation never depend on its availability.</summary>
internal sealed class ArtistPicture : UserControl, IDisposable
{
    private readonly Image _image;
    private Bitmap? _bitmap;
    private CancellationTokenSource? _stop;
    private bool _started, _disposed;
    public ArtistPicture(CatalogArtist artist, double size = 64)
    {
        Width = Height = size;
        var initials = string.Concat(artist.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => System.Globalization.StringInfo.GetNextTextElement(w))).ToUpperInvariant();
        var grid = new Grid();
        grid.Children.Add(new Border { CornerRadius = new(size / 2), Background = new SolidColorBrush(Color.Parse("#252A36")), Child = new TextBlock { Text = initials, Foreground = Brushes.White, FontSize = size * .3, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        _image = new Image { Width = size, Height = size, Stretch = Stretch.UniformToFill, Clip = new EllipseGeometry(new Rect(0, 0, size, size)), IsVisible = false };
        grid.Children.Add(_image); Content = grid;
    }
    public async void Load(ArtistCatalog catalog, CatalogArtist artist, CancellationToken ct)
    {
        if (_started || _disposed || ct.IsCancellationRequested) return; _started = true;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct); ct = _stop.Token;
        try
        {
            await catalog.PortraitAsync(artist, ct, async path => await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || ct.IsCancellationRequested) return;
                using var input = File.OpenRead(path); var next = Bitmap.DecodeToWidth(input, 640);
                var previous = _bitmap; _bitmap = next; _image.Source = next; _image.IsVisible = true; previous?.Dispose();
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TimeoutException or SourcesWaitingException or ArgumentException or OperationCanceledException or System.Text.RegularExpressions.RegexMatchTimeoutException) { }
    }
    public void Dispose() { _disposed = true; _stop?.Cancel(); _stop?.Dispose(); _stop = null; _image.Source = null; _bitmap?.Dispose(); _bitmap = null; }
}
