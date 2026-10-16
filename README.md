# Free Music Finder — a Noctis plugin

Finds songs and imports Spotify/Deezer playlists through Music Hunters and DeezLoad using
your own Telegram account, from inside [Noctis](https://github.com/heartached/Noctis).

Version **2.11.0** makes **For you** faster and keeps your last successful picks between sessions.
Optionally paste your own **Last.fm API key** in Finder settings for better similar artists. Last.fm is tried first;
ListenBrainz/MusicBrainz and Deezer remain backups. Seed relations run three at a time; related artists' songs need
one catalog request instead of two. Your own artists still use local album evidence to distinguish namesakes.
Saved picks appear immediately; stale picks refresh in the background. **New picks ready** lets you choose when to
show the replacement, so rows never jump under your pointer. **Refresh** keeps the previous list usable.
Metadata follows Finder's existing proxy setting; no audio downloads automatically. Only artist names go to Last.fm;
the other catalog fallbacks may also receive song titles. The key stays in your Noctis setting (plain text), never
in the public DLL/ZIP, caches, or diagnostic messages. Blank key preserves the existing account-free fallback.
See [technical notes and validation](docs/LASTFM-2.11.0.md).

Version **2.10.1** fixes For you's **Download now** and **Queue download**, which did nothing after you had visited another tab, and the chosen filter (All, Artists…) now keeps its colour while the pointer is still on it.

Version **2.10.0** adds **For you**. Before you search, the Search tab shows songs you don't have yet, picked from the
artists you play. It uses what you finish and what you skip early, from Noctis's history and True Shuffle's listening
log, and how many songs of an artist you own. Each song says why it is there ("More from J. Cole", "Because you play
Drake"). **Download now**, **Queue download** and **Not for me** work as usual; two "not for me" from one artist leave
that artist out. Nothing downloads by itself, and songs you have or have queued are never suggested. Similar artists
come from ListenBrainz, the open listening database (no account needed), with artists matched in MusicBrainz through
your own songs by them, and from Deezer when Deezer can be reached. Only artist names and song titles are sent. The
first load takes a few minutes; after that it is cached. **Refresh** picks again.

Version **2.9.1** shows only current work in the download status. Finished downloads stay in
history for duplicate checks, but do not inflate queue counts or progress. A single download
shows its own transfer progress; queued, staged and provider-waiting requests are counted separately.

Version **2.9.0** combines song and artist search into one **Search** tab. Search an artist,
album, or `song by artist`; **All**, **Artists**, **Songs** and **Albums** filter the same
results. A relevant top result leads the page; artist queries include recent releases.
Artists and albums open Noctis's existing native pages. Song results show owned/queued state
and explicit **Download now** / **Queue download** actions. No Telegram login is required to
browse. **Download bots** keeps the original Music Hunters/DeezLoad search available for
unreleased material. Catalog results come from Deezer/Apple, so their ranking and coverage
can differ from Spotify. Sections load progressively, reuse saved metadata, respect provider
limits and use the existing proxy route. No AI service, Spotify account or new dependency.
The window uses a larger top-result panel, artwork shelves, round artist tiles, compact song
and download rows, and light/dark styling. Narrow layouts wrap actions; idle completed queues
do not cover search results. Public catalog searches preserve provider relevance rather than
sorting every album by its release date, and exact artist-ID lookups fill artist song results.
Result filters retain context: **Artists** for a song/album includes its credited performers,
ranked ahead of incidental name matches using the same search results. Small artists remain
selectable. Relationships and local matching evidence stay in hover tips; the visible cards
use artwork, name and genre rather than redundant “top match” explanations.

Version **2.8** fills Noctis's existing artist release sections and album pages with online
catalog metadata. The native grids, tabs and track-row templates stay in use. Missing songs
have **Download now** and **Queue download** actions; Ctrl+click selects several for download.
Browsing requests metadata and cover art only. No audio is downloaded until you choose it.

Version **2.8.1** groups clean/explicit and regional duplicates into one explicit-preferred
tile per edition; deluxe, live, remastered and anniversary editions remain separate. Local
international-version tags match that tile instead of adding a partial local duplicate.
Album header covers load from the catalog cache and survive native library refreshes.

Version **2.8.2** starts catalog loading as the native page opens, shows cached releases before
background updates, and reuses parsed metadata and existing tiles. The release page no longer
waits for an unused popular-song lookup. A subtle native loading indicator explains fresh
network loads; navigating away cancels that artist's request. Memory caches are bounded,
disk reads run off the UI thread, and provider pacing/proxy rules remain in effect.

Version **2.8.3** separates same-name artists using catalog IDs and albums in your library.
Old name-only selections are checked again; opening another namesake from search affects
that visit, without replacing the identity used by your local artist page. Artist search
shows round profile pictures and genres instead of raw provider IDs. Pictures load only for
visible results, use the existing proxy route and cache, and fall back to initials.

Version **2.8.4** shows quick artwork previews, then upgrades visible catalog covers to
1200px on Apple / 1000px on Deezer and artist search photos to 640px on Apple. Sharp cached
images appear immediately on later visits. Failed upgrades retain the preview; existing
custom/local art is preserved. Downloads share the same proxy route, are limited to two
at a time, and large files/dimensions are bounded. Old thumbnail caches upgrade automatically.
Leaving an artist also cancels unfinished album navigation and clears stale Opening text.

Version 2.6 adds a **Playlists** tab: paste several Spotify playlists, match songs across
all local library folders, and create ordered Noctis playlists. Unique missing recordings wait
in the shared queue until you start them; new downloads fill their original positions.
Search, Queue and Playlists now share a refined layout for narrow windows and light/dark themes.
Version 2.5.2 preserves exact Music Hunters search buttons across downloads and queue restarts,
binds each result to the bot that returned it, and checks DeezLoad inline artist/title links.
Version 2.5.1 added editable queues and priority downloads. Version 2.5 added full metadata manifests, individual source fallback, durable queues across
restarts, independent bot cooldowns and a visible progress/pause panel. Music Hunters handles
quick searches first; playlist requests prefer DeezLoad when available. See
[user instructions](README-USERS.md#good-to-know).

Know what this is before you use it:

- The bot is a third party. It serves tracks from commercial streaming services, not only
  freely licensed music, and the plugin shows no licence for its results. Whether you may
  download a given track is up to the law where you live and is your responsibility.
- Telegram's terms restrict automated use of user accounts; an account that is used this way
  can be limited or banned.

## Install

1. Download [`dist/FreeMusicFinder.zip`](dist/FreeMusicFinder.zip) (do not unzip it), or build it yourself (below).
2. In Noctis: Settings → Plugins → turn on Community plugins → **Install from file…** → pick the zip.
3. Enable the plugin and approve its permissions (track menu entry, notices, internet, reading the library).

Core search/download needs Noctis 1.5.3 or newer (plugin API 1.1). Native catalog integration
is tested against **Noctis 1.6.3** and checks its private view contracts before using them.
Step-by-step instructions for users are in
[README-USERS.md](README-USERS.md).

## Use

Open the Finder window for search, downloads, playlists or artist search:

- press **Ctrl+Shift+F** anywhere in Noctis (Cmd+Shift+F on a Mac; change it or turn it off in
  the plugin's **Shortcut** setting),
- right-click any track → **Find more by this artist…**, or
- Settings → Plugins → Free Music Finder → flip **Open the search window**.

The shortcut is not a plugin API feature: the plugin listens to key presses in Noctis's windows
and takes only ones Noctis has not handled, so if Noctis itself uses the same keys, Noctis wins.

The first time, press **Log in to Telegram…** (see below). Then type an artist and title, press
Enter, and press **Download now** for priority, **Queue download** for batch work, or **Queue all**. A search sends `/search <your text>`
to the bot from your account and lists its first page of results; a download makes the bot send
the file.

- Results your Noctis library already has (in any of its folders) are marked "in your library".
  They can still be downloaded one by one; **Queue all** leaves them out.
- Downloads run one at a time, with manual priority requests before batch work: the bot sends one file at a
  time. They belong to the plugin, not to the window: closing the window does not stop them, and
  opening the **Queue** tab shows the ones still going. Unfinished work is saved when the plugin or
  Noctis closes, and resumes when it is running again.
- The **Queue** tab supports artist/title filtering, pages, **Remove**, and **Download now**.
  **Clear queue…** requires confirmation, cancels local work and metadata imports, and erases
  requests/history without deleting downloaded audio. Removed requests never resume on restart.
  Priority works while batch work is paused, respects provider cooldowns, and does not interrupt
  the current transfer. Clearing/removing locally does not retract a message already sent to a bot.
- Only the file that was asked for is saved. The bot answers everything in one chat, so a file
  can arrive that belongs to an earlier request or is something else altogether; it is told
  apart by its name and length and left alone. Files that are not songs are never saved.
- Music Hunters results preserve their numbered callback and originating message: selecting
  a result presses that exact button instead of repeating the search. Deleted/expired buttons
  trigger one fresh search and a matching selection, then normal source fallback. DeezLoad uses
  its own inline search/track-link protocol. Callback bytes and chat message IDs never cross bots.

Files are saved as `Artist - Title.ext` in a `Noctis Free Music` folder inside your first Noctis
library folder (change it in the plugin's settings). Where the result list has the same artist
and title in different lengths (an album and a live version), each is saved with its length,
`Artist - Title (4.18).ext`. Plugins cannot add tracks to the library themselves, but Noctis
watches its library folders, so with **Watch Folders** on (the default) a download appears in
the library a moment after it finishes. If Noctis has no library folder yet, files go to
`<Music>/Noctis Free Music`; add that folder to your library folders to see them.

While a file is being downloaded it is called `<name>.fmf.part`, so that neither Noctis nor
anything else that watches the folder (the [WordLyrics](https://github.com/Alfamin/WordLyrics)
plugin, for one) takes a half-written file for a song. What a download left behind when Noctis
was closed in the middle of it is removed at the next start.

## Browse artist catalogs in native pages

Open a normal Noctis artist page. **Albums**, **Singles & EPs**, and **Appears On** keep their
native layout and gain releases returned by the catalog. Local-only releases remain. To find
an artist you do not own, open Finder → **Search**, search, and choose **Open in Noctis**.
If several artists share a name, known local albums identify the matching catalog when possible.
Otherwise, choose by picture and genre; that explicit local-page choice is saved. Finder search
visits remain separate. Small artists are never rejected just for having fewer listeners.

Open a release to see its ordered tracks on the native album page. Owned tracks keep native
playback; missing tracks are slightly muted and offer **Download now** or **Queue download**.
Ctrl+click missing rows, then **Queue N downloads**, or choose **Download all missing…** and
confirm the count. Native **Add to Queue** still means the playback queue. Download progress,
pause, retry and removal are in Finder → **Queue**. Telegram login is required for downloads,
but not for browsing. Saved songs become playable after Noctis scans the download folder.

Catalog requests and cover images follow Finder's **Proxy** setting and the same system
proxy/VPN route used by playlist metadata. A failed proxy does not trigger a direct bypass.
Deezer is tried first; Apple iTunes Search is a metadata fallback when search is unavailable.
Search/artist metadata is cached for a day, albums for seven days, covers are reused, and a
saved catalog remains available during network failures. **Refresh catalog** explicitly checks
for releases again. Rate-limit cooldowns survive restarts. The fallback uses the US storefront
and has a 200-release cap; regional availability, featured credits and unreleased material
mean this is not a guaranteed complete discography. Incomplete album results disable the
album-wide download action. Metadata cannot prove an untagged local recording is explicit.
API reference: [Apple Search API](https://performance-partners.apple.com/search-api).

The integration changes transient page models, not library files. Catalog-only tracks are
never inserted into the local library or passed to playback. Turning off **Fill artist and
album pages from online catalogs** restores local pages. Noctis has no public artist-page
extension API: future layouts may require an adapter update; unsupported layouts report an
error and retain the local view. Popular/All Songs lists continue to represent local music.

## Bring Spotify playlists into Noctis

1. Open **Playlists**, paste full Spotify playlist links (one per line), and choose **Create playlists**.
2. Review the matched/total counts and **Missing songs**. Matching reads your scanned local
   library across subfolders and multiple roots; no Telegram login is needed.
3. Choose **Queue all missing songs** for every playlist, or click one playlist (anywhere on it,
   or its **Missing songs** button) and then **Queue these songs** for that playlist alone (the
   playlist listed is the one with the coloured border). Then **Start queued downloads**, here or
   on the queue panel of the other tabs: while playlist songs wait to be started, its pause/resume
   button reads **Start queued downloads** and one press starts them and lifts a pause. Log in to
   Telegram if needed.

These are normal Noctis playlists named after Spotify, using shared file references. Audio is
not copied or moved. Original order and duplicate entries are retained. Different playlists
with the same name remain separate; re-pasting a link resumes its saved import. Recording
checks use artist, title, version and duration. Known clean versions are not substituted for
explicit songs; metadata alone cannot prove an untagged recording is explicit.

Spotify and the files' tags write the same recording differently, and the check reads both the
same way: an addition after a dash or in brackets (`Song - Remix`, `Song (Remix)`), a guest or
producer named in the title (`(with Guest)`, `FEAT. GUEST`, `(prod. X)`), artists joined by `,`
`&` `;` or `/`, and letters such as `ł` written plainly. A song that Spotify lists under another
name than its artist (a leak under its uploader) is found when that name is the file's album
artist and the length agrees. Lengths still have to be within three seconds, a remix or live
version is never taken for the original, and two different additions (`Pt. 1`, `Pt. 2`) stay two
recordings. An entry whose title and length are in the library under an unrelated artist stays
missing, and its line says so. Saved imports are checked again with these rules when Noctis starts.

Unidentified local Spotify files, podcasts, blocked metadata, unavailable recordings and bot
limits stay listed while other tracks continue. Shared missing recordings are queued once.
New requests wait for Start; existing download work is unaffected. Remove/retry/clear requests
in Queue. Clearing it keeps Noctis playlists and does not automatically refill downloads.
Hidden Noctis folders are reported; their files are not downloaded twice.

Noctis must scan completed downloads. If folder watching is off or the destination is outside
your library, add the folder/rescan, then **Check library again**. Saved manifests fill gaps
across restarts. This checks local files; automatic Spotify refetch/synchronization is deferred.
Private playlists must be accessible to the public metadata reader; there is no Spotify login.

Manual song/order edits and deleted playlists stop automatic filling, preserving your edits.
**Remove import** stops tracking but keeps the native playlist, audio and existing requests.
The desktop adapter validates Noctis's contract and uses its native save/refresh methods.
An unsupported future version shows an error before changes. Saved source records are in
the plugin data folder as `spotify-playlists.json`, without Spotify access tokens.

## Where Telegram is blocked

The plugin talks to Telegram itself, so it needs a connection that reaches Telegram. With the
**Proxy** setting left empty it does what the computer does: it asks for the computer's own
proxy at every connection (and at every reconnection, so switching a VPN on or off needs
nothing) and goes through it, or connects directly when none is switched on.

- Windows: the proxy under Settings → Network → Proxy, which a VPN app's "system proxy" mode
  switches on, with its list of exceptions; read from the registry each time. A setup script
  (PAC) is asked through .NET.
- macOS and Linux: what .NET takes for the system proxy (the network settings; `HTTPS_PROXY`,
  `ALL_PROXY`).

An HTTP proxy is asked for a tunnel (`CONNECT`), a SOCKS proxy is spoken to as SOCKS5.

The setting can also name a proxy:

- `host:port`, when it is not known what kind it is: HTTP is tried, then SOCKS5,
- a SOCKS5 proxy: `socks5://host:port`, or `socks5://name:password@host:port`
  (Telegram's own `https://t.me/socks?…` links work too),
- an HTTP proxy: `http://host:port`, or `http://name:password@host:port`,
- a Telegram (MTProto) proxy link: `https://t.me/proxy?server=…&port=…&secret=…` (or `tg://proxy?…`),
- `direct`: never a proxy, whatever the computer is set to.

Spotify and Deezer playlists are read the same way (`MetadataRoute` in `Proxy.cs`): through the
proxy the setting names, or with the setting empty through the computer's own, asked at every
connection. The name of the service is handed to the proxy and looked up at its end. A Telegram
(MTProto) proxy carries Telegram only, so with one set playlists are read the computer's way. A
read that fails says which way was tried, for example `METADATA_UNREACHABLE: Spotify could not
be reached through the computer's proxy 127.0.0.1:10808`, and the Noctis log notes the way each
time it changes.

A changed setting is used from the next search, login or playlist import on. One that is not
understood is said when it is changed and again by the search or import that needs it. Noctis
keeps plugin settings as plain text.

## Telegram login

The bot is used through your own Telegram account, so the login asks for:

1. an API id and API hash, which you create once at <https://my.telegram.org> → API development tools,
2. your phone number, then the login code Telegram sends you,
3. your two-step verification password, if the account has one.

Private installations may supply `telegram-defaults.private.json` in
`<Noctis data>/plugin-data/dev.moshi.freemusicfinder/` with an `api_id` number and `api_hash` string.
The public DLL and ZIP contain no shared credentials. With valid defaults, login asks for your phone
number first and collapses API fields under **Advanced: Telegram API settings**. A saved account's
custom API settings take precedence. Missing or invalid defaults require your own settings;
`API_CREDENTIALS_REJECTED` explains how to override rejected defaults. The hash field is masked,
private defaults are bounded/validated, and values are never included in login error logs.
Share files containing actual defaults privately, never in this public repository.

What is kept: the API id and hash, your account's display name and the session Telegram issued,
in `telegram.dat` in the plugin's data folder (`<Noctis data>/plugin-data/dev.moshi.freemusicfinder/`).
On Windows that file is encrypted with DPAPI, so only your Windows user on that computer can
read it; on macOS and Linux it is not encrypted, only readable by your user. The phone number,
login code and password are never written to disk. **Log out** in the login window ends the
session at Telegram and deletes it from the file. The session gives full access to your
Telegram account, so treat `telegram.dat` like a password.

## Build

Needs the .NET 10 SDK and git.

```powershell
./build.ps1
```

The script runs the tests, builds the plugin and copies the zip to `dist/FreeMusicFinder.zip`.

`Noctis.Plugins.Abstractions` is not on NuGet yet, so the script clones the Noctis repo and
builds against it: the release named by `minAppVersion` in `plugin.json`, into
`extern/Noctis-<that version>`. It has to be that release and not the newest source: .NET only
loads a plugin whose references are not newer than what the app has, so a plugin built against
today's Noctis is refused by every older one, while one built against the oldest supported
release runs in all of them. To use an existing checkout:

```powershell
./build.ps1 -NoctisRepo C:\path\to\Noctis
```

The version is written in one place, `plugin.json`; the DLL and the zip take it from there.
Noctis only updates an installed plugin to a higher version.

## Tests

```powershell
dotnet run --project tests
```

They need neither Telegram nor a screen: the result list, file names, the "is this the file that
was asked for" rule, the proxy setting and the computer's own (with a SOCKS5 and an HTTP proxy
on the same computer), the way playlists are read through them, saving and
tidying up, the download queue, and the search window on Avalonia's headless platform with a
stand-in for the bot. With `FMF_PICTURES` set to a folder they also save pictures of the window.
Not covered by them: everything that needs the real bot and a real Telegram login.

To see how this computer reaches Spotify and Deezer right now, set `FMF_LIVE_ROUTE` to one or
more public links (and `FMF_LIVE_PROXY` to a Proxy setting to try; empty is the computer's own).
Then only that runs: it reads the links' metadata from the real services, no audio, and prints
the way each went (through which proxy, or directly) and how many entries came back.

## Layout

- `FreeMusicFinder/FreeMusicPlugin.cs` — entry point: track menu command, settings switch, shortcut, window lifetime, notices
- `FreeMusicFinder/Shortcut.cs` — reads the shortcut setting
- `FreeMusicFinder/SearchWindow.cs` — the search window (Avalonia, built in code)
- `FreeMusicFinder/Downloads.cs` — the download queue: one at a time, independent of the window
- `FreeMusicFinder/QueueView.cs` — queue filtering, pages, individual actions and confirmed clearing
- `FreeMusicFinder/Downloader.cs` — file names, the download folder, saving a file, tidying up
- `FreeMusicFinder/BotText.cs` — reads the bot's result list; tells whether two descriptions mean the same song
- `FreeMusicFinder/Telegram.cs` — the Telegram account (login steps, connection) and the bot: search and file download
- `FreeMusicFinder/Proxy.cs` — the proxy setting, the computer's own proxy, the SOCKS5 and HTTP connections, and the way playlists are read through them
- `FreeMusicFinder/TelegramStore.cs` — the protected file the Telegram session is kept in
- `FreeMusicFinder/TelegramLoginWindow.cs` — the login window
- `FreeMusicFinder/plugin.json` — manifest and declared settings
- `Plugin.props` — reads the version and the Noctis release to build against from `plugin.json`
- `tests/` — the tests
- `dist/FreeMusicFinder.zip` — the built plugin
