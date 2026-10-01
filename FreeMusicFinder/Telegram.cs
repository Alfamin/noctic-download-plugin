using System.Globalization;
using System.Text.RegularExpressions;
using TL;
using WTelegram;

namespace FreeMusicFinder;

/// <summary>The user's own Telegram account: the login steps and the connection the bot source talks through.</summary>
internal sealed class TelegramAccount : IDisposable
{
    private readonly TelegramStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Client? _client;

    // WTelegramClient logs every packet to the console unless told otherwise.
    static TelegramAccount() => Helpers.Log = (_, _) => { };

    public TelegramAccount(string storePath) => _store = new TelegramStore(storePath);

    public bool IsLoggedIn => _store.Account.Length > 0;

    /// <summary>Display name of the signed-in account; empty when logged out.</summary>
    public string SignedInAs => _store.Account;

    public int ApiId => _store.ApiId;
    public string ApiHash => _store.ApiHash;

    /// <summary>The connected client of the signed-in account.</summary>
    public async Task<Client> ConnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsLoggedIn) throw new InvalidOperationException("Not logged in to Telegram.");
            if (_client is { Disconnected: true })
            {
                _client.Dispose();
                _client = null;
            }
            if (_client is null)
            {
                var client = CreateClient();
                try { await client.ConnectAsync().ConfigureAwait(false); }
                catch
                {
                    client.Dispose();
                    throw;
                }
                _client = client;
            }
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sends the phone number. Returns what Telegram wants next ("verification_code",
    /// "password", …) for <see cref="ContinueLoginAsync"/>, or null when the login is complete.
    /// </summary>
    public async Task<string?> StartLoginAsync(int apiId, string apiHash, string phone)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _client?.Dispose();
            _client = null;
            _store.Begin(apiId, apiHash);
            _client = CreateClient();
            return await StepAsync(_client, phone).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ContinueLoginAsync(string answer)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is null) throw new InvalidOperationException("Start the login again.");
            return await StepAsync(_client, answer).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ends the session at Telegram and forgets it here; forgets it here even when Telegram cannot be reached.</summary>
    public async Task LogOutAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsLoggedIn)
            {
                try
                {
                    _client ??= CreateClient();
                    await _client.ConnectAsync().ConfigureAwait(false);
                    await _client.Auth_LogOut().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is RpcException or WTException or IOException or TimeoutException)
                {
                }
            }
            Forget();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Telegram no longer accepts the session (ended from another device): go back to logged out.</summary>
    public void SessionEnded()
    {
        _gate.Wait();
        try { Forget(); }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }

    private void Forget()
    {
        _client?.Dispose();
        _client = null;
        _store.ClearSession();
    }

    private async Task<string?> StepAsync(Client client, string answer)
    {
        var next = await client.Login(answer).ConfigureAwait(false);
        if (next is null) _store.SetAccount(client.User.MainUsername is { Length: > 0 } name ? "@" + name : client.User.first_name);
        return next;
    }

    private Client CreateClient() => new(Config, _store.OpenSession());

    private string? Config(string what) => what switch
    {
        "api_id" => _store.ApiId.ToString(CultureInfo.InvariantCulture),
        "api_hash" => _store.ApiHash,
        _ => null, // WTelegramClient's defaults
    };
}

/// <summary>One track the bot listed. <paramref name="Fetch"/> downloads it.</summary>
internal sealed record BotTrack(string Title, string Artist, TimeSpan? Duration, TrackFetch Fetch);

/// <summary>Writes a track's audio to <paramref name="output"/> and returns its file extension ("flac").</summary>
internal delegate Task<string> TrackFetch(Stream output, IProgress<double>? progress, CancellationToken ct);

/// <summary>
/// A Telegram music bot, driven through the user's own account: "/search" lists tracks with one
/// numbered button each, and pressing a button makes the bot send that track as a file.
/// </summary>
internal sealed partial class TelegramBotSource
{
    public const string Bot = "MusicsHuntersbot";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan FileWait = TimeSpan.FromMinutes(3);

    private readonly TelegramAccount _account;
    // The bot answers every request in the same chat, so a second download would pick up the first one's file.
    private readonly SemaphoreSlim _oneDownload = new(1, 1);
    private (Client Client, InputPeer Peer)? _bot;

    public TelegramBotSource(TelegramAccount account) => _account = account;

    // "3. Kevin MacLeod - Darkest Child (3:59)"
    [GeneratedRegex(@"^(\d+)\.\s+(.+?) - (.+?)(?:\s+\((\d+(?::\d\d){1,2})\))?\s*$", RegexOptions.Multiline)]
    private static partial Regex ResultLine();

    /// <summary>The first page of the bot's results for <paramref name="query"/>.</summary>
    public Task<IReadOnlyList<BotTrack>> SearchAsync(string query, CancellationToken ct)
        => RunAsync<IReadOnlyList<BotTrack>>(async (client, bot) =>
        {
            var sent = await client.SendMessageAsync(bot, "/search " + query).ConfigureAwait(false);
            // The answer always carries buttons; without results it has no numbered ones.
            var results = await WaitForAsync(client, bot, sent.id, m => Buttons(m).Any(), SearchWait, ct).ConfigureAwait(false);
            if (results is null) return Array.Empty<BotTrack>();

            var buttons = Buttons(results).Where(IsNumber).ToDictionary(b => b.Text, b => b.Data);
            var tracks = new List<BotTrack>();
            foreach (Match line in ResultLine().Matches(results.message))
            {
                if (!buttons.TryGetValue(line.Groups[1].Value, out var data)) continue;
                tracks.Add(new BotTrack(
                    Title: line.Groups[3].Value.Trim(),
                    Artist: line.Groups[2].Value.Trim(),
                    Duration: ParseDuration(line.Groups[4].Value),
                    Fetch: (output, progress, token) => FetchAsync(results.id, data, output, progress, token)));
            }
            return tracks;
        }, ct);

    /// <summary>"4:18" or "1:02:03".</summary>
    private static TimeSpan? ParseDuration(string text)
    {
        if (text.Length == 0) return null;
        var seconds = text.Split(':').Aggregate(0, (total, part) => total * 60 + int.Parse(part, CultureInfo.InvariantCulture));
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Presses the result's button, waits for the file the bot sends and copies it to <paramref name="output"/>.</summary>
    private async Task<string> FetchAsync(int resultsId, byte[] button, Stream output, IProgress<double>? progress, CancellationToken ct)
    {
        await _oneDownload.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RunAsync(async (client, bot) =>
            {
                var newest = (await client.Messages_GetHistory(bot, limit: 1).ConfigureAwait(false)).Messages.FirstOrDefault()?.ID ?? resultsId;
                string? notice = null;
                try { notice = (await client.Messages_GetBotCallbackAnswer(bot, resultsId, button).ConfigureAwait(false))?.message; }
                // The bot may acknowledge the press later than Telegram waits; the file still arrives.
                catch (RpcException ex) when (ex.Message.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase)) { }

                var file = await WaitForAsync(client, bot, newest, m => m.media is MessageMediaDocument { document: Document }, FileWait, ct).ConfigureAwait(false)
                    ?? throw new TimeoutException(string.IsNullOrWhiteSpace(notice) ? "The bot did not send the file." : "The bot answered: " + notice);
                var document = (Document)((MessageMediaDocument)file.media).document;

                var lastReport = 0.0;
                await client.DownloadFileAsync(document, output, (PhotoSizeBase?)null, (done, total) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (total > 0 && (double)done / total - lastReport >= 0.02) progress?.Report(lastReport = (double)done / total);
                }).ConfigureAwait(false);
                return Extension(document);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _oneDownload.Release();
        }
    }

    private async Task<T> RunAsync<T>(Func<Client, InputPeer, Task<T>> work, CancellationToken ct)
    {
        try
        {
            var client = await _account.ConnectAsync(ct).ConfigureAwait(false);
            if (_bot?.Client != client)
                _bot = (client, (await client.Contacts_ResolveUsername(Bot).ConfigureAwait(false)).User);
            return await work(client, _bot.Value.Peer).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code == 401)
        {
            _account.SessionEnded();
            throw new InvalidOperationException("Telegram ended the session. Log in again.", ex);
        }
    }

    /// <summary>The oldest message from the bot after <paramref name="afterId"/> that matches, or null when none came in time.</summary>
    private static async Task<Message?> WaitForAsync(Client client, InputPeer bot, int afterId, Func<Message, bool> matches,
        TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var history = await client.Messages_GetHistory(bot, min_id: afterId, limit: 30).ConfigureAwait(false);
            var found = history.Messages.OfType<Message>().Reverse()
                .FirstOrDefault(m => !m.flags.HasFlag(Message.Flags.out_) && matches(m));
            if (found is not null) return found;
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>The buttons under a message that send data back to the bot when pressed.</summary>
    private static IEnumerable<(string Text, byte[] Data)> Buttons(Message message)
    {
        if (message.reply_markup is not ReplyInlineMarkup markup) yield break;
        foreach (var button in markup.rows.SelectMany(r => r.buttons))
            if (button.type is InlineButtonTypeCallback callback) yield return (button.text, callback.data);
    }

    private static bool IsNumber((string Text, byte[] Data) button) => button.Text.Length > 0 && button.Text.All(char.IsAsciiDigit);

    private static string Extension(Document document)
    {
        var fromName = Path.GetExtension(document.Filename ?? "").TrimStart('.').ToLowerInvariant();
        if (fromName.Length is > 0 and <= 5) return fromName;
        return document.mime_type switch
        {
            "audio/flac" or "audio/x-flac" => "flac",
            "audio/mp4" or "audio/x-m4a" => "m4a",
            "audio/ogg" => "ogg",
            _ => "mp3",
        };
    }
}
