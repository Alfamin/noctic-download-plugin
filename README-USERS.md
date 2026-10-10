# Free Music Finder — quick start

A Noctis plugin that searches the Telegram bot `@MusicsHuntersbot` through your own Telegram
account and saves the songs into your Noctis library.

You need: Noctis 1.5.3 or newer, a Telegram account, and the file
[`FreeMusicFinder.zip`](dist/FreeMusicFinder.zip).

## 1. Install the zip

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
2. Press **Download** on a result, or **Download all**.

Songs are saved as `Artist - Title.ext` in a `Noctis Free Music` folder inside your first Noctis
library folder and appear in the library a moment later. **Open folder** shows where they are.
You can close the window while songs are downloading: they go on, and Noctis shows a notice
when they are done.

## Good to know

- **"in your library" under a result:** Noctis already has that song. **Download all** skips
  it; its own **Download** button still works.
- **Songs do not appear in the library:** check that **Watch Folders** is on in Noctis's
  settings and that Noctis has at least one library folder. If you set your own download
  folder in the plugin's settings, it must be inside a library folder.
- **Only seven results:** the plugin shows the bot's first page. Make the search more specific.
- **Downloads are slow or queued:** the bot sends one file at a time.
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
