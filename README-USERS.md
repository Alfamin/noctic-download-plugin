# Free Music Finder — quick start

A Noctis plugin that uses Music Hunters and DeezLoad through your own Telegram account and
saves songs into your Noctis library. Music Hunters is first for quick searches; playlist
requests prefer DeezLoad and automatically use Music Hunters when DeezLoad is busy or fails.

You need: Noctis 1.5.3 or newer, a Telegram account, and the file
[`FreeMusicFinder.zip`](dist/FreeMusicFinder.zip).

## 1. Install the zip

**Fixed in 2.9.1:** download status shows the current song's progress and actual waiting requests.
Previously saved songs no longer appear as part of a new download queue.

**New in 2.9:** one **Search** tab for artists, songs and albums. Type a name or `song by artist`
and press Enter. **All** shows the top match and related results; **Artists**, **Songs** and
**Albums** narrow them. Artist searches include recent releases. Artist/album buttons open the
normal Noctis pages. Songs show **Download now**, **Queue download**, or **In your library**.
The filters keep context: searching a song and choosing **Artists** puts that song's credited
performer first, ahead of unrelated literal-name matches. Hover for the song relationship;
the tiles themselves show names, pictures and genres without redundant match labels.
Browsing needs no Telegram login and never downloads audio. For unreleased songs or missing
catalog entries, choose **Download bots** to use the original Telegram search. Catalog ranking
comes from Deezer/Apple metadata and your local album evidence; it can differ from Spotify.

**New in 2.8 (Noctis 1.6.3):** open any artist's normal Noctis page to see online albums and
singles alongside your local releases. Click an album for its ordered track list. Owned songs
play normally; missing songs show **Download now** and **Queue download**. Ctrl+click several
missing rows, then **Queue N downloads**, or use **Download all missing…** and confirm.
There are no selection checkboxes. Browsing never starts an audio download.

**2.8.1 fixes:** equivalent clean/explicit and international copies share one tile, preferring
the explicit version. Deluxe and other distinct editions stay separate. Existing international
album tags open the full catalog listing, and album header covers now load and survive rescans.

**2.8.2:** saved releases appear immediately while updates load in the background. Artist pages
react to navigation immediately, reuse cached metadata/covers and show a small loading bar.
First visits still need your connection; switching artists cancels the previous page's request.

**2.8.3:** artist search shows profile pictures and genres, without raw Apple/Deezer IDs.
Same-name artists stay separate. Your local albums help choose the right catalog, and old
selections are checked again. Pictures load through your proxy, are cached, and show initials
if unavailable. Opening another namesake from search won't change your local artist's choice.

**2.8.4:** covers appear quickly, then sharpen in the background (up to 1200px on Apple,
1000px on Deezer). Artist photos also upgrade. Sharp images are cached; slow/failed upgrades
keep the preview. Existing local/custom covers stay intact. No cache clearing is required.

For an artist outside your library: Finder → **Search** → search → **Open in Noctis**.
If your local albums cannot distinguish namesakes, choose by picture and genre. Download progress
is in Finder → **Queue**. Noctis must scan downloaded songs before they become playable.
The native **Add to Queue** button still adds owned songs to the playback queue.

Metadata and covers use your existing Finder/system proxy settings. Saved catalogs remain
usable offline; **Refresh catalog** checks again. Provider catalogs can omit regional or
unreleased material. Popular/All Songs lists still show local music. You can turn off
**Fill artist and album pages from online catalogs** in the plugin settings.

**New in 2.6:** the **Playlists** tab brings your Spotify playlists into Noctis using the music
you already have. Paste several playlist links, one per line → **Create playlists** → review
**Missing songs** → **Queue all missing songs** → **Start queued downloads**. For one playlist
alone, click that playlist and then **Queue these songs**. Shared songs use
one local file, in every playlist's original order. You can create playlists before logging
in to Telegram. Downloads need Telegram and your normal download folder to be in Noctis's
library (or add it/rescan). **Check library again** fills local gaps; Spotify refetch is deferred.


1. In Noctis open **Settings → Plugins** and turn on **Community plugins**.
2. Press **Install from file…** and pick `FreeMusicFinder.zip` (do not unzip it).
3. Enable the plugin and approve its permissions (track menu entry, notices, internet, reading
   the library).

Updating from an older version works the same way: install the new zip over it. Your Telegram
login is kept. Coming from 2.0, Noctis asks you to approve once more, because the plugin now
also reads the library (to mark the songs you already have).

## 2. Where Telegram is blocked: VPN or proxy

The plugin needs to reach Telegram. If Telegram does not open where you are without a VPN,
turn your VPN on. Nothing has to be set in the plugin: with its **Proxy** setting left empty it
does what the computer does. When a proxy is switched on in the computer's own settings (the
"system proxy" mode of a VPN app does that), Telegram is reached through it; a VPN that covers
the whole computer works as well. Switching the VPN on or off later needs nothing either.

Spotify and Deezer playlists are read the same way, through the same VPN or proxy. If a playlist
cannot be read, its status says which way was tried (for example "through the computer's proxy
127.0.0.1:10808", or "directly"): check that the VPN is connected and press **Create playlists**
again. A Telegram proxy link carries Telegram only; with one of those, playlists still follow the
computer's own proxy or VPN.

Only if neither is what you use, put a proxy into the **Proxy** setting (Settings → Plugins →
Free Music Finder) before you log in:

- just its address, the way VPN apps show it: `127.0.0.1:10808`
- a SOCKS5 proxy: `socks5://host:port`, or an HTTP proxy: `http://host:port`
- a Telegram proxy link, the kind you tap in Telegram: `https://t.me/proxy?server=…&port=…&secret=…`
- `direct` to never use a proxy, whatever the computer is set to

## 3. Telegram app settings (only if none were supplied)

**Private friend setup:** if your installer supplied app settings, skip this section. Login asks for
your phone number first. You can change the API id/hash under **Advanced: Telegram API settings**.
The public plugin does not contain shared credentials.

1. Open <https://my.telegram.org> and sign in with your phone number and the code Telegram
   sends to your Telegram app.
2. Click **API development tools**.
3. Fill in any app title and a short name (letters and digits, no spaces), choose **Desktop**,
   and press **Create application**.
4. Note the **App api_id** (a number) and **App api_hash** (32 characters). Keep the hash private.

## 4. Log in

1. Open the search window: press **Ctrl+Shift+F** anywhere in Noctis (Cmd+Shift+F on a Mac).
   Also possible: right-click any track → **Find more by this artist…**, or flip
   **Open the search window** in the plugin's settings.
2. Press **Log in to Telegram…**.
3. Enter your phone number with country code (`+98…`). If app settings were not supplied, fill in
   your API id/hash under **Advanced: Telegram API settings** first.
4. Enter the login code Telegram sends you, then your two-step password if you have one.

The button now shows your account name. You only log in once.

`API_CREDENTIALS_REJECTED` means Telegram rejected the app defaults: open Advanced and supply a
valid pair. `TELEGRAM_CONNECTION_FAILED` means Telegram could not be reached: check your VPN/proxy.
Your phone number, login code and two-step password are never filled in by someone else's setup.

## 5. Search and download

1. Type an artist and title and press Enter.
2. Press **Download now** to put that song ahead of the backlog, or **Queue download** to download it later. **Queue all** adds the search results to the batch.

**Download now** works while the batch is paused. A file already transferring finishes first;
bot limits still apply. Search results and saved requests have separate **Search** and **Queue** tabs.
In **Queue**, find a song by artist/title, press **Remove** to cancel that request, or
**Download now** to prioritise it. **Clear queue…** asks for confirmation, stops the current
transfer and playlist imports, and erases the saved requests/history. Downloaded files stay on disk.
The queue has pages, so requests beyond the first 100 remain accessible. Removed songs stay removed
after restarting Noctis. You can deliberately add them again later.

Selecting a Music Hunters result automatically presses its matching bot button. You do not need
to open Telegram or choose the option there. If the button expired, the plugin searches once for
the same artist/title/version and selects the matching result. Each result remembers its source;
DeezLoad uses track links, while Music Hunters uses its own numbered options.

Songs are saved as `Artist - Title.ext` in a `Noctis Free Music` folder inside your first Noctis
library folder and appear in the library a moment later. **Open folder** shows where they are.
You can close the window while songs are downloading: they go on, and Noctis shows a notice
when they are done.

## Good to know

Paste a Spotify or Deezer playlist/album link in the search box to import it. The complete
ordered track list is saved before either bot is asked for audio. Existing tracks are skipped;
remaining recordings are requested individually, so the backup receives only unfinished songs.

- The progress bar counts saved downloads; metadata collection has its own visible status.
- **Pause queue** pauses batch work after the current request; **Download now** still works. **Resume queue** continues the batch.
  Songs queued from the Playlists tab wait until you start them: while any are waiting, the same button reads
  **Start queued downloads**, and one press starts them and lifts a pause.
- Closing Noctis saves unfinished work and resumes it when Noctis runs again.
- Bot limits and retry times are tracked separately. When both are unavailable, work waits.
- Telegram-wide waits affect both bots. Source switching does not avoid that wait.
- No daily allowance is hard-coded. Without a supplied reset time, the next check is in 24 hours.
- **Retry failed** retries failed songs and incomplete metadata; completed files stay intact.
- Songs are asked for in FLAC from both bots first. When neither has a song in FLAC (often the case
  for leaks and unreleased songs), the MP3 a bot sent is saved instead; switch off **Accept MP3 when
  there is no FLAC** in the plugin's settings for FLAC only. Wrong artists, clear version differences,
  large duration differences and named clean versions of explicit tracks are rejected.
- Searching works while a song downloads; it no longer waits for the transfer to finish.
- A bot you never opened in Telegram is started automatically ("/start") before the first request.
- Public playlists need no Spotify login. Private/removed/region-blocked metadata is reported;
  make the link accessible and retry. Truncated previews are refused.
- Local-file metadata is retained when unambiguous. Ambiguous entries and podcasts stay visible.

The saved queue supports 10,000 requests and retains playlist order/IDs. Catalog filenames have
a short stable identifier to prevent recording collisions. No ZIP subscription or new Spotify
playlists are needed. The plugin never makes payments or joins channels automatically.

- **"in your library" under a result:** Noctis already has that song. **Queue all** skips
  it; its own **Download now** button still works.
- **Songs do not appear in the library:** check that **Watch Folders** is on in Noctis's
  settings and that Noctis has at least one library folder. If you set your own download
  folder in the plugin's settings, it must be inside a library folder.
- **Only seven results:** the plugin shows the bot's first page. Make the search more specific.
- **Downloads are slow or queued:** the plugin tries the other bot, explains waiting and saves
  unfinished work for later. Bots can have their own queues.
- **"Retry" on a button:** that download failed; point at the button to see why. "The bot sent
  another file" means the bot answered with something that is not the song you asked for; it
  was not saved.
- **"Telegram could not be reached":** see step 2.
- **"Its list could not be read":** the bot changed how it writes its results; the plugin needs
  an update.
- **Log out:** press the Telegram button in the search window, then **Log out**. This ends the
  session at Telegram and removes it from this computer.
- **What is stored:** only the API id and hash, your account name and the Telegram session, in
  `telegram.dat` in the plugin's data folder. On Windows it is encrypted for your Windows user;
  on macOS and Linux it is not encrypted. Your phone number, code and password are not stored.
  The Proxy setting is kept by Noctis as plain text.
- **Your responsibility:** the bot is a third party and is not limited to freely licensed
  music, so whether you may download a track depends on the law where you live. Telegram can
  also limit or ban accounts that are used in an automated way.
