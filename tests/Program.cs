using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using FreeMusicFinder.Tests;

// Runs every test and prints one line each. The exit code is the number of wrong ones.
// With FMF_PICTURES set to a folder, the window tests also save pictures of the search window there.
// With FMF_LIVE_ROUTE set to public Spotify or Deezer links, only LiveRouteTest runs: it reads their
// metadata from the real services and says which way (proxy or direct) this computer reached them.
if (Environment.GetEnvironmentVariable("FMF_LIVE_ROUTE") is { Length: > 0 } links)
{
    await LiveRouteTest.RunAsync(links, Environment.GetEnvironmentVariable("FMF_LIVE_PROXY"));
    return 0;
}
try
{
    await LogicTests.RunAsync();
    await TransferTests.RunAsync();
    await QueueManagementTests.RunAsync();
    await BotSelectionTests.RunAsync();
    await SpotifyPlaylistTests.RunAsync();
    await LivePlaylistTest.RunAsync(Environment.GetEnvironmentVariable("FMF_SPOTIFY_PLAYLIST"), Environment.GetEnvironmentVariable("FMF_LIBRARY_SNAPSHOT"), Environment.GetEnvironmentVariable("FMF_LIVE_REPORT"));

    var pictures = Environment.GetEnvironmentVariable("FMF_PICTURES");
    var app = AppBuilder.Configure<TestApp>();
    if (string.IsNullOrEmpty(pictures)) app.UseHeadless(new AvaloniaHeadlessPlatformOptions());
    else app.UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    app.SetupWithoutStarting();
    WindowTests.Run(string.IsNullOrEmpty(pictures) ? null : pictures);
    PlaylistWindowTests.Run(pictures);
    NativePlaylistTests.Run(Environment.GetEnvironmentVariable("FMF_NOCTIS_APP"));
}
catch (Exception ex)
{
    Check.True(false, "the tests stopped: " + ex);
}
finally
{
    Check.RemoveFolders();
}

Console.WriteLine($"\n{Check.Passed} ok, {Check.Failed} wrong");
return Check.Failed;

internal sealed class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
