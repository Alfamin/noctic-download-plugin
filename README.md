# Free Music Finder — a Noctis plugin

Searches the Telegram music bot `@MusicsHuntersbot` through your own Telegram account and
downloads tracks into your library, from inside [Noctis](https://github.com/heartached/Noctis).

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

Needs Noctis 1.5.3 or newer (plugin API 1.1). Step-by-step instructions for users are in
[README-USERS.md](README-USERS.md).

## Use

Plugin API 1.1 cannot add a page or sidebar entry, so the plugin opens its own window:

- press **Ctrl+Shift+F** anywhere in Noctis (Cmd+Shift+F on a Mac; change it or turn it off in
  the plugin's **Shortcut** setting),
- right-click any track → **Find more by this artist…**, or
- Settings → Plugins → Free Music Finder → flip **Open the search window**.

The shortcut is not a plugin API feature: the plugin listens to key presses in Noctis's windows
and takes only ones Noctis has not handled, so if Noctis itself uses the same keys, Noctis wins.

The first time, press **Log in to Telegram…** (see below). Then type an artist and title, press
Enter, and press **Download** on a row or **Download all**. A search sends `/search <your text>`
to the bot from your account and lists its first page of results; a download makes the bot send
the file.

- Results your Noctis library already has (in any of its folders) are marked "in your library".
  They can still be downloaded one by one; **Download all** leaves them out.
- Downloads run one at a time, in the order you asked for them: the bot sends one file at a
  time. They belong to the plugin, not to the window: closing the window does not stop them, and
  opening it again shows the ones still going. They stop when the plugin is switched off or
  Noctis is closed.
- Only the file that was asked for is saved. The bot answers everything in one chat, so a file
  can arrive that belongs to an earlier request or is something else altogether; it is told
  apart by its name and length and left alone. Files that are not songs are never saved.

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

## Where Telegram is blocked

The plugin talks to Telegram itself, so it needs a connection that reaches Telegram: either a
VPN that covers the whole computer, or the **Proxy** setting of the plugin:

- a Telegram (MTProto) proxy link: `https://t.me/proxy?server=…&port=…&secret=…` (or `tg://proxy?…`),
- a SOCKS5 proxy: `socks5://host:port`, or `socks5://name:password@host:port`
  (Telegram's own `https://t.me/socks?…` links work too).

It is used from the next search or login on. Noctis keeps plugin settings as plain text.

## Telegram login

The bot is used through your own Telegram account, so the login asks for:

1. an API id and API hash, which you create once at <https://my.telegram.org> → API development tools,
2. your phone number, then the login code Telegram sends you,
3. your two-step verification password, if the account has one.

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
was asked for" rule, the proxy setting (with a SOCKS5 proxy on the same computer), saving and
tidying up, the download queue, and the search window on Avalonia's headless platform with a
stand-in for the bot. With `FMF_PICTURES` set to a folder they also save pictures of the window.
Not covered by them: everything that needs the real bot and a real Telegram login.

## Layout

- `FreeMusicFinder/FreeMusicPlugin.cs` — entry point: track menu command, settings switch, shortcut, window lifetime, notices
- `FreeMusicFinder/Shortcut.cs` — reads the shortcut setting
- `FreeMusicFinder/SearchWindow.cs` — the search window (Avalonia, built in code)
- `FreeMusicFinder/Downloads.cs` — the download queue: one at a time, independent of the window
- `FreeMusicFinder/Downloader.cs` — file names, the download folder, saving a file, tidying up
- `FreeMusicFinder/BotText.cs` — reads the bot's result list; tells whether two descriptions mean the same song
- `FreeMusicFinder/Telegram.cs` — the Telegram account (login steps, connection) and the bot: search and file download
- `FreeMusicFinder/Proxy.cs` — the proxy setting and the SOCKS5 connection
- `FreeMusicFinder/TelegramStore.cs` — the protected file the Telegram session is kept in
- `FreeMusicFinder/TelegramLoginWindow.cs` — the login window
- `FreeMusicFinder/plugin.json` — manifest and declared settings
- `Plugin.props` — reads the version and the Noctis release to build against from `plugin.json`
- `tests/` — the tests
- `dist/FreeMusicFinder.zip` — the built plugin
