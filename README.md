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

1. Build (below) or take `FreeMusicFinder/bin/Release/FreeMusicFinder-2.0.0.zip`.
2. In Noctis: Settings → Plugins → turn on Community plugins → **Install from file…** → pick the zip.
3. Enable the plugin and approve its permissions (track menu entry, notices, internet).

Needs Noctis 1.5.3 or newer (plugin API 1.1).

## Use

Plugin API 1.1 cannot add a page or sidebar entry, so the plugin opens its own window:

- right-click any track → **Find free music…** (searches for that track's artist), or
- Settings → Plugins → Free Music Finder → flip **Open the search window**.

The first time, press **Log in to Telegram…** (see below). Then type an artist and title, press
Enter, and press **Download** on a row or **Download all**. A search sends `/search <your text>`
to the bot from your account and lists its first page of results; a download makes the bot send
the file. Downloads run one at a time.

Files are saved as `Artist - Title.ext` in a `Noctis Free Music` folder inside your first Noctis
library folder (change it in the plugin's settings). Plugins cannot add tracks to the library
themselves, but Noctis watches its library folders, so with **Watch Folders** on (the default) a
download appears in the library a moment after it finishes. If Noctis has no library folder yet,
files go to `<Music>/Noctis Free Music`; add that folder to your library folders to see them.

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

`Noctis.Plugins.Abstractions` is not on NuGet yet, so the script clones the Noctis repo into
`extern/Noctis` and builds against it. To use an existing checkout:

```powershell
./build.ps1 -NoctisRepo C:\path\to\Noctis
```

## Layout

- `FreeMusicFinder/FreeMusicPlugin.cs` — entry point: track menu command, settings switch, window lifetime
- `FreeMusicFinder/SearchWindow.cs` — the search window (Avalonia, built in code)
- `FreeMusicFinder/Downloader.cs` — saves files into the download folder
- `FreeMusicFinder/Telegram.cs` — the Telegram account (login steps, connection) and the bot: search and file download
- `FreeMusicFinder/TelegramStore.cs` — the protected file the Telegram session is kept in
- `FreeMusicFinder/TelegramLoginWindow.cs` — the login window
- `FreeMusicFinder/plugin.json` — manifest and declared settings
