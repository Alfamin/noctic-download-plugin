using System.Net;
using System.Text.Json;
using FreeMusicFinder;

namespace FreeMusicFinder.Tests;

internal static class DiscoveryTests
{
    internal sealed class Http : HttpMessageHandler
    {
        public readonly List<Uri> Calls = new();
        public bool Offline, AppleOnly, UnsafeNext, Refreshed, Partial, BrokenJson, Limited;
        public int AppleAlbumCount;
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Override;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Calls) Calls.Add(request.RequestUri!); ct.ThrowIfCancellationRequested();
            if (Override is {} replace) return replace(request, ct);
            if (Offline) throw new HttpRequestException("Synthetic offline connection");
            var uri = request.RequestUri!; var path = uri.AbsolutePath; var query = uri.Query;
            if (Limited || AppleOnly && uri.Host == "api.deezer.com")
            {
                var response = new HttpResponseMessage(Limited ? (HttpStatusCode)429 : HttpStatusCode.Forbidden);
                response.Headers.RetryAfter = new(TimeSpan.FromMinutes(9)); return Task.FromResult(response);
            }
            if (BrokenJson) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>blocked</html>") });
            object body = uri.Host == "itunes.apple.com" ? Apple(path, query) : path switch
            {
                "/search/artist" => new { data = new[] { Artist(), Artist(43, "Guest") } },
                "/artist/42" => Artist(),
                "/artist/43" => Artist(43, "Guest"),
                "/artist/42/top" or "/artist/43/top" => new { data = Songs() },
                "/artist/42/albums" or "/artist/43/albums" => Albums(path, query),
                "/album/111" => Album(),
                "/album/112" => Album(112, "Orbit - Single", "single", 1),
                "/album/111/tracks" => new { data = Songs(), total = Partial ? 4 : 3 },
                "/album/112/tracks" => new { data = new[] { Song(102, "Missing") }, total = 1 },
                _ => new { error = new { message = "Synthetic unknown request" } },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) });
        }
        private object Albums(string path, string query) => UnsafeNext ? new { data = new[] { Album() }, next = "https://untrusted.invalid/artist/42/albums?index=1", total = 2 } : query.Contains("index=1")
            ? new { data = new[] { Album(112, "Orbit - Single", "single", 1) }, total = 2 }
            : Refreshed ? new { data = new[] { Album(113, "New release", "album", 3) }, total = 1 }
            : (object)new { data = new[] { Album() }, total = 2, next = "https://api.deezer.com" + path + "?index=1&limit=100" };
        internal static object Artist(long id = 42, string name = "North Star") => new { id, name };
        internal static object Album(int id = 111, string title = "Orbit", string record_type = "album", int nb_tracks = 3) => new { id, title, artist = Artist(), release_date = "2026-10-01", record_type, nb_tracks, explicit_lyrics = true };
        internal static object Song(int id, string title) => new { id, title, artist = Artist(), contributors = new[] { Artist(), Artist(43, "Guest") }, duration = 180, explicit_lyrics = true, track_position = id - 100, disk_number = 1 };
        private static object[] Songs() => [Song(101, "Owned"), Song(102, "Missing"), Song(103, "Night (feat. Guest)")];
        private object Apple(string path, string query)
        {
            var artist = new { wrapperType = "artist", artistId = 75, artistName = "North Star", primaryGenreName = "Alternative" };
            var release = new { wrapperType = "collection", collectionType = "Album", collectionId = 211, collectionName = "Orbit", artistId = 75, artistName = "North Star", releaseDate = "2026-10-01T07:00:00Z", trackCount = Partial ? 3 : 2, collectionExplicitness = "explicit" };
            object Song(int id, int disc, int number, string explicitness) => new { wrapperType = "track", kind = "song", trackId = id, trackName = "Apple song " + id, collectionId = 211, artistId = 75, artistName = "North Star", trackTimeMillis = 180000, trackNumber = number, discNumber = disc, trackExplicitness = explicitness };
            if (path == "/search") return new { results = new object[] { artist } };
            if (query.Contains("entity=album")) return new { results = new[] { (object)artist }.Concat(Enumerable.Range(0, Math.Max(1, AppleAlbumCount)).Select(_ => (object)release)).ToArray() };
            if (query.Contains("id=211")) return new { results = new object[] { release, Song(203, 2, 1, "cleaned"), Song(202, 1, 2, "explicit") } };
            return new { results = new object[] { artist, Song(202, 1, 2, "explicit") } };
        }
    }
    internal static ArtistCatalog Catalog(string folder, Http handler, Func<DateTimeOffset>? now = null) => new(folder, handler, now, TimeSpan.Zero);
    internal static readonly CatalogArtist Star = new("deezer:42", "North Star");
    internal static readonly CatalogRelease Orbit = new("deezer:111", "Orbit", Star, "2026-10-01", "album", 3);
    internal static CatalogSong Song(string id, string title, bool? explicitFlag = true) => new(id, title, [Star], 180, explicitFlag);

    public static async Task RunAsync()
    {
        Check.Section("artist discovery: catalogs, provider isolation, cache, rate limits and network failure");
        var http = new Http(); var folder = Check.NewFolder("discovery-cache");
        var now = DateTimeOffset.UtcNow;
        using var catalog = Catalog(folder, http, () => now);
        Check.Equal(0, http.Calls.Count, "constructing catalog performs no network requests");
        var search = await catalog.SearchAsync("North & Star فارسی", false, default);
        Check.True(http.Calls.Single().Query.Contains("%26") && Uri.UnescapeDataString(http.Calls.Single().Query).Contains("فارسی"), "artist query is encoded and preserves non-Latin names");
        Check.Equal(2, search.Value.Count, "same-name/alternate artists remain selectable, with stable IDs");
        var calls = http.Calls.Count; await catalog.SearchAsync("North & Star فارسی", false, default);
        Check.Equal(calls, http.Calls.Count, "repeat search uses disk cache without another network request");
        var profile = await catalog.ArtistAsync(Star, false, default);
        Check.Equal(2, profile.Value.Releases.Count, "all discography pages read before caching");
        Check.True(profile.Value.Releases.Any(a => a.Kind == "single"), "single classification retained");
        var album = await catalog.AlbumAsync(Orbit, false, default);
        Check.Equal(3, album.Value.Songs.Count, "ordered complete album fetched");
        Check.Equal("Guest", album.Value.Songs[0].Artists[1].Name, "contributor identity supports featured-artist navigation");
        Check.Equal("https://www.deezer.com/track/101", album.Value.Songs[0].Request.Url, "Deezer recording URL is constructed from validated identity");
        Check.True(album.Value.Songs[0].Request.Explicit == true, "explicit metadata preserved through download requests");
        calls = http.Calls.Count; http.Offline = true;
        var cached = await catalog.AlbumAsync(Orbit, false, default);
        Check.True(cached.Cached && http.Calls.Count == calls, "fresh saved album is browsable entirely offline");
        var stale = await catalog.AlbumAsync(Orbit, true, default);
        Check.True(stale.Cached && stale.Warning.Contains("saved metadata"), "failed explicit refresh retains saved album with visible warning");
        now = now.AddDays(10); var expired = await catalog.ArtistAsync(Star, false, default);
        Check.True(expired.Cached && expired.Value.Releases.Count == 2, "expired metadata stays available after connection loss");
        http.Offline = false; http.Refreshed = true;
        var refreshed = await catalog.ArtistAsync(Star, true, default);
        Check.Equal(3, refreshed.Value.Releases.Count, "catalog refresh adds new releases and keeps previously seen releases");
        calls = http.Calls.Count;
        await Check.ThrowsAsync<InvalidDataException>(() => catalog.ArtistAsync(new("deezer:42/../../secret", "Bad"), false, default), "unsafe catalog IDs rejected before network access");
        Check.Equal(calls, http.Calls.Count, "invalid identities send no requests");
        await Check.ThrowsAsync<ArgumentException>(() => catalog.SearchAsync("bad\nname", false, default), "control characters rejected in search input");

        var unsafeHttp = new Http { UnsafeNext = true };
        using (var unsafeCatalog = Catalog(Check.NewFolder("discovery-unsafe"), unsafeHttp))
        {
            await Check.ThrowsAsync<InvalidDataException>(() => unsafeCatalog.ArtistAsync(Star, false, default), "foreign pagination URL rejected");
            Check.True(unsafeHttp.Calls.All(u => u.Host == "api.deezer.com"), "unsafe next-page host is never contacted");
        }
        var partialHttp = new Http { Partial = true };
        using (var partial = Catalog(Check.NewFolder("discovery-partial"), partialHttp))
            await Check.ThrowsAsync<InvalidDataException>(() => partial.AlbumAsync(Orbit, false, default), "incomplete paginated album is not cached as a full album");
        var appleHttp = new Http { AppleOnly = true }; var appleFolder = Check.NewFolder("discovery-apple");
        using (var apple = Catalog(appleFolder, appleHttp))
        {
            var artists = await apple.SearchAsync("North Star", false, default);
            Check.Equal("apple:75", artists.Value.Single().Id, "blocked Deezer automatically uses Apple catalog, without mixing IDs");
            var appleProfile = await apple.ArtistAsync(artists.Value.Single(), false, default);
            Check.True(appleProfile.Value.Notice.Contains("not a guaranteed complete"), "fallback's regional/completeness limitation is visible");
            var appleAlbum = await apple.AlbumAsync(appleProfile.Value.Releases.Single(), false, default);
            Check.Equal("apple:202", appleAlbum.Value.Songs[0].Id, "multi-disc songs use disc then track order");
            Check.True(appleAlbum.Value.Songs[0].Explicit == true && appleAlbum.Value.Songs[1].Explicit == false, "Apple explicit and cleaned versions remain distinct");
            Check.True(appleAlbum.Value.Songs.All(s => s.Request.Url is null), "Apple store URLs are never sent to bots as Deezer/Spotify download links");
            appleHttp.Partial = true;
            Check.True((await apple.AlbumAsync(appleProfile.Value.Releases.Single(), true, default)).Value.Notice.Contains("Download all is unavailable"), "incomplete fallback album permits explicit individual choices, not Download all");
            appleHttp.AppleAlbumCount = 200;
            Check.True((await apple.ArtistAsync(artists.Value.Single(), true, default)).Value.Notice.Contains("200-release limit"), "catalog result cap is reported, not silently claimed complete");
        }
        using (var restarted = Catalog(appleFolder, new Http { AppleOnly = true }))
        {
            var answer = await restarted.SearchAsync("Another query", true, default);
            Check.Equal("apple:75", answer.Value.Single().Id, "provider access-denied cooldown survives restart and fallback remains usable");
        }
        var limited = new Http { Limited = true };
        using (var limits = Catalog(Check.NewFolder("discovery-limits"), limited))
        {
            await Check.ThrowsAsync<SourcesWaitingException>(() => limits.SearchAsync("First", false, default), "both rate-limited providers report Retry-After instead of tight retries");
            calls = limited.Calls.Count;
            await Check.ThrowsAsync<SourcesWaitingException>(() => limits.SearchAsync("Second", false, default), "new query still respects catalog cooldowns");
            Check.Equal(calls, limited.Calls.Count, "no rate-limit bypass via another query");
        }
        var broken = new Http { BrokenJson = true };
        using (var blocked = Catalog(Check.NewFolder("discovery-json"), broken))
            await Check.ThrowsAsync<InvalidDataException>(() => blocked.SearchAsync("Artist", false, default), "blocked HTML is a descriptive catalog error");
        var cancelHttp = new Http { Override = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); } };
        using (var cancel = Catalog(Check.NewFolder("discovery-cancel"), cancelHttp))
        using (var stop = new CancellationTokenSource(30))
        {
            await Check.ThrowsAsync<OperationCanceledException>(() => cancel.SearchAsync("Artist", false, stop.Token), "cancellation does not trigger fallback requests");
            Check.Equal(1, cancelHttp.Calls.Count, "cancelled browsing stops its network work");
        }
        await Artwork();
        await Actions();
    }

    private static async Task Artwork()
    {
        Check.Section("catalog artwork: bounded cache and trusted provider URLs");
        var http = new Http { Override = (_, _) =>
        {
            var content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1sAAAAASUVORK5CYII="));
            content.Headers.ContentType = new("image/png"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        } };
        using var catalog = Catalog(Check.NewFolder("catalog-art"), http);
        const string url = "https://is1-ssl.mzstatic.com/synthetic.png";
        var path = await catalog.ArtworkAsync(url, default);
        Check.True(path is not null && File.Exists(path), "provider artwork is saved inside the metadata cache");
        Check.Equal(path, await catalog.ArtworkAsync(url, default), "reopening art reuses its cached file");
        Check.Equal(1, http.Calls.Count, "cached art makes no additional network request");
        foreach (var unsafeUrl in new[] { "http://is1-ssl.mzstatic.com/a", "https://is1-ssl.mzstatic.com.evil.invalid/a", "https://user@is1-ssl.mzstatic.com/a", "https://is1-ssl.mzstatic.com:444/a", "file:///C:/music/a", "https://127.0.0.1/a" })
            Check.True(await catalog.ArtworkAsync(unsafeUrl, default) is null, "untrusted artwork URL is rejected before network access");
        Check.Equal(1, http.Calls.Count, "untrusted art never reaches the network handler");
        http.Override = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>blocked</html>") });
        Check.True(await catalog.ArtworkAsync(url + "?html", default) is null, "blocked HTML is not cached as cover art");
        http.Override = (_, _) => { var content = new ByteArrayContent(new byte[512 * 1024 + 1]); content.Headers.ContentType = new("image/png"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }); };
        Check.True(await catalog.ArtworkAsync(url + "?large", default) is null, "oversized artwork is rejected");
    }

    private static async Task Actions()
    {
        Check.Section("discovery downloads: explicit choices, multiple roots, deduplication and preserved files");
        var folder = Check.NewFolder("discovery-actions"); var library = new SpotifyPlaylistTests.Library();
        var owned = library.Add(Path.Combine(folder, "root-a/sub"), "Owned", "North Star");
        var second = library.Add(Path.Combine(folder, "root-b"), "Second root", "North Star");
        library.Add(folder, "Wrong length", "North Star", 400);
        var original = File.ReadAllBytes(owned.Path); int fetched = 0; bool login = false;
        BotTrack Track(MusicRequest r) => new(r.Title, r.Artist, r.Duration, r.Title + "-" + r.Identity.GetHashCode(), async (output, progress, ct) =>
        { Interlocked.Increment(ref fetched); await output.WriteAsync(new byte[] { 1, 2, 3 }, ct); return "mp3"; }) { Request = r };
        using var downloads = new Downloads(_ => { }); downloads.SetPaused(true);
        var actions = new DiscoveryActions(library, downloads, Track, () => Path.Combine(folder, "download"), () => login);
        var rows = new[] { Song("deezer:101", "Owned"), Song("deezer:102", "Missing"), Song("deezer:103", "Second root"), Song("deezer:104", "Wrong length") };
        var matches = await actions.MatchAsync(rows, default);
        Check.True(matches["deezer:101"].Local is not null && matches["deezer:103"].Local is not null, "all library roots and nested folders matched");
        Check.True(matches["deezer:104"].Local is null, "wrong-length recording remains unmatched");
        Check.Equal(0, downloads.History.Count, "browsing and library matching queue no audio");
        await Check.ThrowsAsync<InvalidOperationException>(() => actions.QueueAsync([rows[1]], false, default), "logged-out download explains login without affecting browsing");
        Check.Equal(0, downloads.Active.Count, "no requests staged behind an unsuccessful login action");
        login = true;
        var queued = await actions.QueueAsync([rows[0], rows[1], rows[2], rows[1] with { Id = "apple:900" }], false, default);
        Check.Equal(1, queued.Added, "explicit selection queues unique missing recording once, across provider IDs");
        Check.Equal(2, queued.Owned, "owned files skipped at action time");
        Check.Equal(0, fetched, "paused queue remains paused for batch selections");
        Check.Equal(1, (await actions.QueueAsync([rows[1]], false, default)).Existing, "repeated action does not duplicate an existing queue request");
        await actions.QueueAsync([rows[1]], true, default);
        await Check.UntilAsync(() => downloads.History.Any(d => d.State == DownloadState.Saved), "Download now promotes chosen request and works while batch is paused");
        Check.Equal(1, fetched, "priority action fetched exactly the one selected recording");
        var saved = (await actions.MatchAsync([rows[1]], default))[rows[1].Id];
        Check.True(saved.Download is not null && saved.Detail.Contains("waiting for Noctis"), "saved file pending Noctis scan is not downloaded again");
        Check.Equal(1, (await actions.QueueAsync([rows[1]], true, default)).Existing, "saved but unscanned file still prevents duplicates");
        Check.True(File.ReadAllBytes(owned.Path).SequenceEqual(original) && File.Exists(second.Path), "original files and second-root music unchanged");
        File.Delete(owned.Path); // synthetic marker created by this test
        Check.True((await actions.MatchAsync([rows[0]], default))[rows[0].Id].Local is null, "disconnected/deleted synthetic file is not claimed owned");
        var hidden = second with { Visible = false }; library.Songs.Remove(second); library.Songs.Add(hidden);
        Check.True((await actions.MatchAsync([rows[2]], default))[rows[2].Id].Detail.Contains("hidden folder"), "hidden music is reported and not needlessly fetched");
        Check.True(!SpotifyPlaylists.SameRecording(rows[1].Request, (rows[1] with { Explicit = false }).Request), "known explicit and clean recordings never deduplicate together");
    }
}
