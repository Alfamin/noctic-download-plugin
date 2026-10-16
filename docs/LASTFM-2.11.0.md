# Finder 2.11.0: Last.fm and discovery latency

## Behavior

- Optional user-owned `lastfmApiKey` setting. Empty/malformed values never generate a Last.fm request.
- `artist.getSimilar` is preferred. Empty/failed answers fall back to ListenBrainz/MusicBrainz, then Deezer where
  usable. Missing lyrics, Telegram sessions, playback and audio are outside this change.
- The request route is the same `PlaylistCatalog.MetadataHandler` used by catalog metadata: system proxy,
  explicit HTTP/SOCKS, or explicit direct. Telegram-only proxies continue to carry Telegram only.
- Seed relations are fetched with at most three concurrent operations, merged deterministically in seed order.
  Successful Last.fm requests do not first resolve seed IDs in Apple/Deezer.
- Last.fm requests have a shared start gate, at least 350 ms apart, and at most three in flight. Provider cooldowns
  override this local ceiling. It is not a claim that Last.fm guarantees a particular quota.
- Phase two retains album-based `IdentifyAsync` for owned artists. Related artists use one field-specific song
  search, exact artist-name matching, and the dominant catalog ID. Equal counts are treated as unresolved.
- Same-recording deduplication still prefers explicit variants. Existing library/queue checks and dismissals apply.
- A successful page is saved to `for-you-last.json`. It loads without rebuilding, including offline. Owned/queued
  state is rechecked for the displayed rows and again before a download.
- A metadata-only warmup runs about 20 seconds after plugin startup. Pages younger than 12 hours need no rebuild.
  Stale saved pages can be rebuilt when first shown. A single source serializes builds. Shutdown cancels warmup.
- A prepared result never replaces visible rows automatically. The user selects **New picks ready**. Manual refresh
  keeps old rows visible, and an empty failure cannot erase a useful saved page.
- Existing For you download/tab-return fixes remain. Active filters have explicit hover/pressed styles on both
  the Button and its presenter; the native theme's 60 ms brush transition is allowed to settle in tests.

## Storage and failure handling

- `artist-catalog/lastfm.json`: up to 256 artist relation entries and 4 MiB. Found entries default to 14 days;
  artist-not-found (error 6) to 7 days. Shorter Cache-Control/Expires lifetimes win; no-store/no-cache is not persisted.
- A derived saved page is valid for at most seven days and no longer than its Last.fm data's lifetime. Saved pages
  are bounded to 2 MiB. Expired, corrupt, future-dated or unsupported pages are ignored.
- No key or authenticated request URL is included in either cache. Keys are injected at runtime via `Func<string>`.
  Provider messages and network exception chains are not forwarded, because either could echo credentials.
- Invalid/suspended keys (10/26) stop further requests with that key and produce a settings notice. Rate limits
  (29/HTTP 429) respect Retry-After, with a one-minute default when absent. Failed responses are not cached as missing.
- Cache reads/writes refuse reparse points, including ancestor directories. Writes use unique temporary files and
  atomic replacement. Concurrent ListenBrainz cache updates publish in mutation order.
- No new runtime dependency, AI model or audio asset. No history upload, scrobble, user-auth session or username query.
  Last.fm receives artist names; fallback MusicBrainz can receive local song titles for identity verification.
- Last.fm attribution and artist links are exposed in the page and recommendation context menus.

## Native migration seam

`Taste`, `TasteSources`, `Discover`, `LastFmSimilar` and the cache remain plain classes. `IDiscoverCatalog` isolates
retrieval from ranking. The plugin entry point owns settings/proxy clients, and marshals native album evidence
onto Avalonia's UI thread. No Noctis Plus host code or live-profile migration is included in this release.

## Validation

The offline suite covers response parsing, key changes, secret redaction, negative/positive cache lifetimes,
no-store, provider cooldown/recovery, cancellation, keyless/error fallback, namesake ties and empty-provider fallback,
bounded seed concurrency, saved-page validation/dismissals, and background refresh without row replacement.
The existing headless UI checks exercise real download actions with synthetic file payloads, including leaving
and returning to For you. Native compatibility tests use installed Noctis 1.6.3 in an isolated test application.

2026-10-16 result: **994 checks passed, 0 failed**; Release build had zero warnings/errors. A live public-metadata
check using the read-only local taste snapshot and the current system proxy produced 40 suggestions in 63.1 seconds
from an empty cache. Rebuilding from the warmed metadata took 8 ms; reading the persisted page took 16.8 ms. The last
two timings measure the data path, not complete window startup. Connection/provider state and listening history
affect cold latency; these are observations, not guaranteed performance or a listening-quality score. No live audio
or Telegram transfer was performed. No live Noctis installation or Windows 10 machine test was performed.

The opt-in metadata check accepts `FMF_FORYOU_LIVE` (read-only Noctis data directory), `FMF_DISCOVERY_CACHE` (isolated
cache), and optional `FMF_LASTFM_KEY_FILE` (private JSON with `api_key`). It uses the system proxy and prints picks;
keep its log private. It never opens Telegram or downloads audio. Cold, warm and saved-page timings are reported
separately; do not confuse a disk read with full UI startup, or compare different result counts as equivalent latency.

## Primary API references

- [Last.fm artist.getSimilar](https://www.last.fm/api/show/artist.getSimilar)
- [Last.fm API terms, cache and attribution requirements](https://www.last.fm/api/tos)
- [Apple Search API parameters and approximate request ceiling](https://performance-partners.apple.com/search-api)

The optional Last.fm top-track ordering and progressive first-run rendering are deferred: this release removes
redundant catalog work and adds a saved-page path without extra provider calls or a second ranking implementation.
