using System.Text.Json;
using FreeMusicFinder;

namespace FreeMusicFinder.Tests;

/// <summary>Explicit opt-in public metadata probe. Never opens Telegram or reads music files.</summary>
internal static class LiveDiscoveryTest
{
    public static async Task RunAsync(string names)
    {
        var folder = Environment.GetEnvironmentVariable("FMF_DISCOVERY_CACHE") ?? throw new ArgumentException("Set an isolated FMF_DISCOVERY_CACHE directory.");
        var proxy = "";
        if (Environment.GetEnvironmentVariable("FMF_DISCOVERY_SETTINGS") is { Length: > 0 } path)
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(path));
            if (Find(settings.RootElement, "pluginSettingValues") is {} plugins && Find(plugins, "dev.moshi.freemusicfinder") is {} values && Find(values, "proxy") is {} setting && setting.ValueKind == JsonValueKind.String) proxy = setting.GetString() ?? "";
        }
        var ways = new List<string>(); var route = PlaylistCatalog.MetadataHandler(proxy, way => { ways.Add(way); Console.WriteLine("  " + way); });
        using var catalog = new ArtistCatalog(folder, route);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        foreach (var input in names.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = input.Split('=', 2); var name = pieces[0];
            var search = await catalog.SearchAsync(name, false, stop.Token);
            var exact = search.Value.Where(a => BotText.Key(a.Name) == BotText.Key(name) && (pieces.Length == 1 || a.Id == pieces[1])).ToArray();
            Check.Equal(1, exact.Length, "live metadata: one exact artist identity for " + name);
            if (exact.Length != 1) continue;
            var artist = await catalog.ArtistAsync(exact[0], false, stop.Token);
            Check.True(artist.Value.Releases.Count > 0 && artist.Value.Popular.Count > 0, "live metadata: releases and popular songs for " + name);
            var release = artist.Value.Releases.First(a => a.Count > 0);
            var album = await catalog.AlbumAsync(release, false, stop.Token);
            Check.True(album.Value.Songs.Count > 0 && album.Value.Songs.All(s => !string.IsNullOrWhiteSpace(s.Request.Artist) && !string.IsNullOrWhiteSpace(s.Request.Title)), "live metadata: album songs produce usable recording requests");
            Console.WriteLine($"  {artist.Value.Artist.Name}: {artist.Value.Artist.Id.Split(':')[0]}, {artist.Value.Releases.Count} releases, {artist.Value.Popular.Count} popular songs; {album.Value.Release.Title}: {album.Value.Songs.Count}/{album.Value.Release.Count} songs. {artist.Value.Notice} {album.Value.Notice}");
            var again = await catalog.AlbumAsync(release, false, stop.Token);
            Check.True(again.Cached, "live metadata: opening release again reuses saved catalog");
        }
        Console.WriteLine($"{Check.Passed} ok, {Check.Failed} wrong. Public metadata only; zero Telegram requests, zero audio downloads, zero library writes.");
        if (Check.Failed > 0) throw new InvalidOperationException("Live metadata checks failed.");
    }
    private static JsonElement? Find(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault() : null;
}
