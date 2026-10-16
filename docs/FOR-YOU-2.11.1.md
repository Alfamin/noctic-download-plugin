# Finder 2.11.1: visible actions, song dismissals and responsive artist requests

## Root cause and fix

The live installed 2.11.0 Finder was inspected using the Computer Use skill. Noctis applies a global ScrollViewer
style enabling horizontal scrolling. Finder had not explicitly disabled it on its outer results scroller, so rows
were measured with unbounded width. The right-hand action column and Refresh button could be outside the visible
window. Earlier tests found buttons in the visual tree and invoked their events; they did not establish visibility.

The outer results scroller now explicitly disables horizontal scrolling. Inner artwork shelves retain their own
horizontal scrolling. Long text wraps/trims within the remaining space; normal/narrow song actions remain reachable.
The cached For you path also restores the page's own status rather than retaining the previous search's counts.

The new regression uses the complete SearchWindow, installed Noctis theme and 40 synthetic suggestions. Before the
fix, ten viewport assertions failed. It now checks button bounds, Refresh, horizontal extent, hit testing and a real
headless pointer click at normal/narrow widths. A synthetic paused queue verifies exactly one chosen request.

## Not for me

- Only the dismissed song is excluded. There is no automatic artist exclusion after two dismissals.
- Legacy Artists/Counts fields are ignored; previously hidden song IDs are preserved.
- New dismissals also remember a normalized song-title/primary-artist key, so regional/clean copies from another
  catalog do not immediately bring the same song back. Named live/remix versions remain distinct.
- Undo is shown in the fixed header for eight seconds. It restores both the current row (if still on that page)
  and the persistent preference. Repeated dismissals replace the toast with Undo for the newest action.
- Expiry keeps the song hidden. Refresh/navigation never causes Undo to inject an old row into a different result.
- Dismissal/Undo never cancels a chosen download or changes playback/library files.
- Preferences still live in for-you-hidden.json. Atomic saves refuse redirected files/ancestors.

## Artist request latency

ArtistCatalog previously held a single semaphore across every metadata request, including response bodies.
An unrelated slow album or background request could make an uncached artist wait for its full timeout.

Cache-key locks now serialize identical requests only and are reclaimed after the last reader/waiter. Up to three
metadata responses can be in flight. Apple request starts still share the existing 3.1-second minimum interval;
provider cooldowns remain shared, synchronized and persistent. Artwork retains its separate existing limit.
Fresh memory/disk hits bypass network waits. Artist-search songs and recent releases begin independently, and
unchanged verified native artist choices avoid a redundant disk write. Identity checks, release consolidation,
proxy routes, cancellation and response-size limits are retained.

The tests cover a stalled unrelated request alongside a cold artist fetch, same-key single-flight, cancelled
waiters, bounded HTTP concurrency, shared Apple pacing, cache reuse, existing namesake regressions and native pages.
Synthetic HTTP timings are diagnostics, not claims about real Internet speed. First-time fetches still depend on
the catalog, connection/proxy and provider limits. No audio prefetching is introduced.

## Saved queue restart

The final full-suite run also reproduced a Windows sharing violation while reopening a saved download queue as a
previous worker completed an atomic checkpoint. Queue reads now open a stable handle with writer/delete sharing,
retaining the existing BOM/legacy text-encoding detection. A held-handle regression and UTF-16 Unicode fixture verify
that restoration keeps pending requests rather than treating a temporarily busy queue as empty.

## Validation boundary

Final offline/headless run: **1,038 checks passed, zero failures**. Release build: zero warnings/errors.
The public package's manifest, blank API-key default and DLL parity passed, and the private Last.fm credential was
absent from 78 publication files and all four uncompressed ZIP entries. Normal/narrow rendered previews were viewed.

Computer Use read the user's real Finder page and confirmed the off-screen layout. Windows rejected injected clicks
with SendInput error 87; the user opened For you, then its screenshot/accessibility tree were inspected. No audio
download, bot request, login change or live plugin installation was performed. The fixed build is checked in the
complete headless Finder window with native Noctis resources, including rendered previews and pointer actions.
