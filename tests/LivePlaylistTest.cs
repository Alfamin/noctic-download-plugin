using System.Globalization;
using System.Text.Json;
using FreeMusicFinder;

namespace FreeMusicFinder.Tests;

/// <summary>Optional metadata-only check against an explicitly supplied public playlist.
/// Uses a read-only library snapshot and a fake native playlist store in the test folder.</summary>
internal static class LivePlaylistTest
{
    public static async Task RunAsync(string? url, string? libraryFile, string? report)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (string.IsNullOrWhiteSpace(libraryFile)) throw new ArgumentException("A read-only library snapshot is required for the live test.");
        var folder = Check.NewFolder("live-playlist"); var library = new SpotifyPlaylistTests.Library();
        var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(libraryFile));
        using (var json = JsonDocument.Parse(File.ReadAllText(libraryFile)))
            foreach (var track in json.RootElement.EnumerateArray())
            {
                var path = track.GetProperty("filePath").GetString()!;
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) continue;
                if (track.TryGetProperty("sourceType", out var source) && source.GetString() is not ("Local" or null)) continue;
                library.Songs.Add(new(Guid.Parse(track.GetProperty("id").GetString()!), track.GetProperty("artist").GetString()!,
                    track.GetProperty("title").GetString()!, path, TimeSpan.Parse(track.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture),
                    track.TryGetProperty("isExplicit", out var flag) && flag.GetBoolean()));
            }
        var source1 = new TransferTests.Source("DeezLoad"); var source2 = new TransferTests.Source("Music Hunters");
        var sources = new MusicSources(source1, source2, _ => { }); using var downloads = new Downloads(_ => { });
        using var imports = new SpotifyPlaylists(Path.Combine(folder, "imports.json"), library, downloads, sources.Track);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        await imports.ImportAsync(url, timeout.Token);
        var job = imports.Jobs.Single();
        if (!job.Created) throw new InvalidOperationException("Live playlist check: " + job.Status);
        var missing = job.Manifest!.Entries.Where(e => !job.Matches.ContainsKey(e.Position)).ToArray();
        var queued = await imports.QueueMissingAsync(Path.Combine(folder, "isolated-staging"));
        var result = new
        {
            name = job.Manifest.Name, total = job.Total, localMatches = job.Found, missingEntries = missing.Length,
            unavailableMetadata = missing.Count(e => e.Request is null), uniqueMissingQueued = queued,
            validLocalFiles = library.Songs.Count, realAudioRequests = source1.Calls + source2.Calls,
            originalOrderRetained = library.Lists[job.LibraryId].SequenceEqual(job.Matches.OrderBy(m => m.Key).Select(m => m.Value)),
            missing = missing.Select(e => new { e.Position, e.Request?.Artist, e.Request?.Title, e.Issue }),
        };
        if (report is not null) AtomicJson.Write(report, result);
        Console.WriteLine($"LIVE PLAYLIST RESULT: {job.Manifest.Name} · {job.Total} total · {job.Found} local matches · {missing.Length} missing entries · {queued} unique staged requests · {source1.Calls + source2.Calls} audio requests.");
        Check.True(downloads.Active.All(d => d.Held), "live test stages metadata without audio");
        Check.True(before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(libraryFile))), "live test keeps user's library snapshot byte-identical");
        Check.True(result.originalOrderRetained, "live playlist matches preserve Spotify order");
    }
}
