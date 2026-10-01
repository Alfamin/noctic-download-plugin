# Free Music Finder — quick start

A Noctis plugin that searches the Telegram bot `@MusicsHuntersbot` through your own Telegram
account and saves the songs into your Noctis library.

You need: Noctis 1.5.3 or newer, a Telegram account, and `FreeMusicFinder-2.0.0.zip`.

## 1. Install the zip

1. In Noctis open **Settings → Plugins** and turn on **Community plugins**.
2. Press **Install from file…** and pick `FreeMusicFinder-2.0.0.zip` (do not unzip it).
3. Enable the plugin and approve its permissions (track menu entry, notices, internet).

## 2. Get a Telegram API id and hash (once)

1. Open <https://my.telegram.org> and sign in with your phone number and the code Telegram
   sends to your Telegram app.
2. Click **API development tools**.
3. Fill in any app title and a short name (letters and digits, no spaces), choose **Desktop**,
   and press **Create application**.
4. Note the **App api_id** (a number) and **App api_hash** (32 characters). Keep the hash private.

## 3. Log in

1. Open the search window: right-click any track → **Find free music…**, or flip
   **Open the search window** in the plugin's settings.
2. Press **Log in to Telegram…**.
3. Enter the API id, the API hash and your phone number with country code (`+49…`).
4. Enter the login code Telegram sends you, then your two-step password if you have one.

The button now shows your account name. You only log in once.

## 4. Search and download

1. Type an artist and title and press Enter.
2. Press **Download** on a result, or **Download all**.

Songs are saved as `Artist - Title.ext` in a `Noctis Free Music` folder inside your first Noctis
library folder and appear in the library a moment later. **Open folder** shows where they are.

## Good to know

- **Songs do not appear in the library:** check that **Watch Folders** is on in Noctis's
  settings and that Noctis has at least one library folder. If you set your own download
  folder in the plugin's settings, it must be inside a library folder.
- **Only seven results:** the plugin shows the bot's first page. Make the search more specific.
- **Downloads are slow or queued:** the bot sends one file at a time.
- **Log out:** press the Telegram button in the search window, then **Log out**. This ends the
  session at Telegram and removes it from this computer.
- **What is stored:** only the API id and hash, your account name and the Telegram session, in
  `telegram.dat` in the plugin's data folder. On Windows it is encrypted for your Windows user;
  on macOS and Linux it is not encrypted. Your phone number, code and password are not stored.
- **Your responsibility:** the bot is a third party and is not limited to freely licensed
  music, so whether you may download a track depends on the law where you live. Telegram can
  also limit or ban accounts that are used in an automated way.
