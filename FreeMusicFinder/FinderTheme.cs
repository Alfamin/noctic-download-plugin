using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace FreeMusicFinder;

/// <summary>Shared window-scoped presentation; never alters Noctis's own controls.</summary>
internal static class FinderTheme
{
    public static void Apply(Window window)
    {
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = Palette("#10131A", "#181D27", "#2C3443", "#E9EDF5", "#A3AEC1", "#665BE4", "#202736");
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = Palette("#F4F6FA", "#FFFFFF", "#D9DFE9", "#182133", "#56637B", "#5363D7", "#E9EDF5");
        var surface = new Style(s => s.OfType<Window>());
        surface.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Resource("FinderBackground")));
        surface.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Resource("FinderText")));
        surface.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, 13d));
        window.Styles.Add(surface);
        var button = new Style(s => s.OfType<Button>());
        button.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(8)));
        button.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 8)));
        button.Setters.Add(new Setter(TemplatedControl.MinHeightProperty, 36d));
        button.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Resource("FinderButton")));
        button.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Resource("FinderText")));
        window.Styles.Add(button);
        var primary = new Style(s => s.OfType<Button>().Class("finder-primary"));
        primary.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Resource("FinderAccent")));
        primary.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Brushes.White));
        primary.Setters.Add(new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        window.Styles.Add(primary);
        var card = new Style(s => s.OfType<Border>().Class("finder-card"));
        card.Setters.Add(new Setter(Border.BackgroundProperty, Resource("FinderPanel")));
        card.Setters.Add(new Setter(Border.BorderBrushProperty, Resource("FinderLine")));
        card.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        card.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(12)));
        window.Styles.Add(card);
        var input = new Style(s => s.OfType<TextBox>());
        input.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(8)));
        input.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 10)));
        input.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Resource("FinderLine")));
        window.Styles.Add(input);
        var muted = new Style(s => s.OfType<TextBlock>().Class("finder-muted"));
        muted.Setters.Add(new Setter(TextBlock.ForegroundProperty, Resource("FinderMuted")));
        window.Styles.Add(muted);
    }
    private static ResourceDictionary Palette(string background, string panel, string line, string text, string muted, string accent, string button)
    {
        var resources = new ResourceDictionary();
        foreach (var (key, color) in new[] { ("FinderBackground", background), ("FinderPanel", panel), ("FinderLine", line), ("FinderText", text), ("FinderMuted", muted), ("FinderAccent", accent), ("FinderButton", button) })
            resources[key] = new SolidColorBrush(Color.Parse(color));
        return resources;
    }
    private static DynamicResourceExtension Resource(string key) => new(key);
    public static Border Card(Control content, double padding = 14)
    {
        var card = new Border { Child = content, Padding = new Thickness(padding) }; card.Classes.Add("finder-card"); return card;
    }
    public static void Primary(Button button) => button.Classes.Add("finder-primary");
    public static TextBlock Caption(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 }; block.Classes.Add("finder-muted"); return block;
    }
}
