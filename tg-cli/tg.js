// Command-line Telegram client that acts as your own account (not a bot).
//
//   node tg.js login                         log in once (asks for phone number and code)
//   node tg.js get <song name...>            search the music bot, pick a result, save the file
//   node tg.js send <chat> <text...>         send a message, then show the replies
//   node tg.js click <chat> <msgId> <n>      press button number n under a message
//   node tg.js read <chat> [count]           show the latest messages in a chat
//
// <chat> is a username such as @MusicsHuntersbot. Files in replies are saved to downloads/.
const fs = require("fs");
const path = require("path");
const readline = require("readline/promises");
const { TelegramClient } = require("telegram");
const { StringSession } = require("telegram/sessions");
const { Logger } = require("telegram/extensions");

const configPath = path.join(__dirname, "config.json");
const sessionPath = path.join(__dirname, "session.txt");
const downloadDir = path.join(__dirname, "downloads");

const MUSIC_BOT = "@MusicsHuntersbot";
const QUIET_MS = 8000; // stop waiting once the chat has been silent this long
const MAX_WAIT_MS = 90000;

function makeClient() {
  const { apiId, apiHash } = JSON.parse(fs.readFileSync(configPath, "utf8"));
  const saved = fs.existsSync(sessionPath) ? fs.readFileSync(sessionPath, "utf8").trim() : "";
  const client = new TelegramClient(new StringSession(saved), apiId, apiHash, {
    connectionRetries: 3,
    baseLogger: new Logger("error"),
  });
  return client;
}

async function login() {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  const client = makeClient();
  await client.start({
    phoneNumber: () => rl.question("Phone number (with country code, e.g. +49...): "),
    phoneCode: () => rl.question("Code Telegram sent you: "),
    password: () => rl.question("Two-step verification password: "),
    onError: (e) => console.error(e.message),
  });
  rl.close();
  fs.writeFileSync(sessionPath, client.session.save());
  const me = await client.getMe();
  console.log(`Logged in as ${me.username ? "@" + me.username : me.firstName}.`);
  await client.disconnect();
}

async function connect() {
  const client = makeClient();
  await client.connect();
  if (!(await client.checkAuthorization())) {
    console.error("Not logged in. Run: node tg.js login");
    process.exit(1);
  }
  return client;
}

function flatButtons(m) {
  return (m.buttons || []).flat();
}

function fileName(m) {
  if (m.photo) return `photo-${m.id}.jpg`;
  const attrs = m.document.attributes || [];
  const named = attrs.find((a) => a.className === "DocumentAttributeFilename");
  let name = named && named.fileName;
  if (!name) {
    const audio = attrs.find((a) => a.className === "DocumentAttributeAudio");
    const ext = (m.document.mimeType || "").split("/")[1] || "bin";
    const title = audio && [audio.performer, audio.title].filter(Boolean).join(" - ");
    name = `${title || "file-" + m.id}.${ext}`;
  }
  return name.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_");
}

function print(m) {
  console.log(`${m.out ? ">" : "<"} [${m.id}] ${m.message || ""}`.trimEnd());
  flatButtons(m).forEach((b, i) => console.log(`      button ${i + 1}: ${b.text}`));
}

async function download(client, m) {
  const target = path.join(downloadDir, fileName(m));
  fs.mkdirSync(downloadDir, { recursive: true });
  fs.writeFileSync(target, await client.downloadMedia(m));
  console.log(`      saved ${target}`);
}

// Prints incoming messages newer than afterId (and later edits to them) until the chat goes quiet.
async function watch(client, chat, afterId) {
  const seen = new Map();
  const saved = new Set();
  const start = Date.now();
  let lastChange = start;
  while (Date.now() - start < MAX_WAIT_MS) {
    const messages = await client.getMessages(chat, { minId: afterId, limit: 30 });
    for (const m of messages.reverse()) {
      if (m.out) continue;
      const hasFile = Boolean(m.document || m.photo);
      const state = JSON.stringify([m.message, hasFile, flatButtons(m).map((b) => b.text)]);
      if (seen.get(m.id) === state) continue;
      seen.set(m.id, state);
      lastChange = Date.now();
      print(m);
      if (hasFile && !saved.has(m.id)) {
        saved.add(m.id);
        await download(client, m);
        lastChange = Date.now();
      }
    }
    if (seen.size && Date.now() - lastChange > QUIET_MS) return;
    await new Promise((r) => setTimeout(r, 1500));
  }
  if (!seen.size) console.log("No reply.");
}

async function send(chat, text) {
  const client = await connect();
  const sent = await client.sendMessage(chat, { message: text });
  print(sent);
  await watch(client, chat, sent.id);
  await client.disconnect();
}

async function click(chat, messageId, number) {
  const client = await connect();
  const [m] = await client.getMessages(chat, { ids: messageId });
  const button = m && flatButtons(m)[number - 1];
  if (!button) {
    console.error(`Message ${messageId} has no button ${number}.`);
    process.exit(1);
  }
  console.log(`Pressing "${button.text}"`);
  await press(m, number - 1);
  await watch(client, chat, messageId - 1);
  await client.disconnect();
}

// The bot may take longer to acknowledge the press than Telegram waits; its reply still arrives.
function press(m, index) {
  return m.click({ i: index }).catch((e) => {
    if (!/BOT_RESPONSE_TIMEOUT/.test(e.message)) throw e;
  });
}

// Returns the first incoming message newer than afterId that matches, or null on timeout.
async function waitFor(client, chat, afterId, matches) {
  const start = Date.now();
  while (Date.now() - start < MAX_WAIT_MS) {
    const messages = await client.getMessages(chat, { minId: afterId, limit: 30 });
    const found = messages.reverse().find((m) => !m.out && matches(m));
    if (found) return found;
    await new Promise((r) => setTimeout(r, 1500));
  }
  return null;
}

// Searches the music bot, asks which result to take, and saves the file it sends.
async function get(query) {
  const client = await connect();
  const sent = await client.sendMessage(MUSIC_BOT, { message: `/search ${query}` });
  const results = await waitFor(client, MUSIC_BOT, sent.id, (m) =>
    flatButtons(m).some((b) => /^\d+$/.test(b.text))
  );
  if (!results) {
    console.error("The bot returned no results.");
    process.exit(1);
  }
  console.log(results.message.split("\n").filter((line) => /^\d+\. /.test(line)).join("\n"));

  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  const choice = (await rl.question("Number to download (Enter to cancel): ")).trim();
  rl.close();
  if (!choice) return;
  const index = flatButtons(results).findIndex((b) => b.text === choice);
  if (index < 0) {
    console.error(`There is no result ${choice}.`);
    process.exit(1);
  }

  await press(results, index);
  console.log("Downloading...");
  const file = await waitFor(client, MUSIC_BOT, results.id, (m) => m.document);
  if (!file) {
    console.error("The bot did not send a file.");
    process.exit(1);
  }
  await download(client, file);
  await client.disconnect();
}

async function read(chat, count) {
  const client = await connect();
  const messages = await client.getMessages(chat, { limit: count });
  messages.reverse().forEach(print);
  await client.disconnect();
}

async function main() {
  const [command, chat, ...rest] = process.argv.slice(2);
  if (command === "login") return login();
  if (command === "get" && chat) return get([chat, ...rest].join(" "));
  if (command === "send" && chat && rest.length) return send(chat, rest.join(" "));
  if (command === "click" && rest.length === 2) return click(chat, Number(rest[0]), Number(rest[1]));
  if (command === "read" && chat) return read(chat, Number(rest[0]) || 10);
  console.log(
    [
      "Usage:",
      "  node tg.js login",
      "  node tg.js get <song name...>",
      "  node tg.js send <chat> <text...>",
      "  node tg.js click <chat> <msgId> <n>",
      "  node tg.js read <chat> [count]",
    ].join("\n")
  );
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error(e.message || e);
    process.exit(1);
  }
);
