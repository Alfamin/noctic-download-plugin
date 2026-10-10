using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using FreeMusicFinder.Tests;

// Runs every test and prints one line each. The exit code is the number of wrong ones.
// With FMF_PICTURES set to a folder, the window tests also save pictures of the search window there.
try
{
    await LogicTests.RunAsync();
    await TransferTests.RunAsync();
    await QueueManagementTests.RunAsync();

    var pictures = Environment.GetEnvironmentVariable("FMF_PICTURES");
    var app = AppBuilder.Configure<TestApp>();
    if (string.IsNullOrEmpty(pictures)) app.UseHeadless(new AvaloniaHeadlessPlatformOptions());
    else app.UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    app.SetupWithoutStarting();
    WindowTests.Run(string.IsNullOrEmpty(pictures) ? null : pictures);
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
