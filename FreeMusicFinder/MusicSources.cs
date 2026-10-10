using System.Text.Json;
using System.Text.RegularExpressions;

namespace FreeMusicFinder;

internal sealed record MusicRequest(string Title, string Artist, double? Seconds = null, string? Url = null,
    bool? Explicit = null, string? Playlist = null, int Position = 0, int? MessageId = null, string? CachedFrom = null, string? PreferredSource = null, string? OriginUri = null,
    BotSelection? Selection = null)
{
    public TimeSpan? Duration => Seconds is > 0 and <= 86400 ? TimeSpan.FromSeconds(Seconds.Value) : null;
    public string Identity => OriginUri is {Length:>0} ? OriginUri : Url is { Length: > 0 } ? Url : BotText.Key(Artist) + "|" + BotText.Key(Title) + "|" + Seconds;
}

internal interface IMusicSource
{
    string Id { get; }
    Task<IReadOnlyList<MusicRequest>> SearchAsync(string query, CancellationToken ct);
    Task<string> FetchAsync(MusicRequest request, Stream output, IProgress<double>? progress, CancellationToken ct);
}

internal sealed class SourceLimitException(string provider, string message, DateTimeOffset until, bool global = false)
    : Exception(message)
{
    public string Provider { get; } = provider;
    public DateTimeOffset Until { get; } = until;
    public bool Global { get; } = global;
}
internal sealed class SourcesWaitingException(string message, DateTimeOffset until) : Exception(message)
{
    public DateTimeOffset Until { get; } = until;
}
internal sealed class TrackUnavailableException(string message) : Exception(message);

/// <summary>Limits are observations from bot replies, never guessed allowances. Unknown resets get a conservative retry.</summary>
internal static partial class BotLimits
{
    [GeneratedRegex(@"(?:FLOOD_(?:PREMIUM_)?WAIT[_ :]*|(?:wait|retry|try again|in|after)\s+)(\d+)\s*(seconds?|secs?|s\b|minutes?|mins?|hours?|hrs?|days?)?", RegexOptions.IgnoreCase)]
    private static partial Regex Wait();
    public static SourceLimitException? Read(string provider, string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLowerInvariant();
        var global = lower.Contains("flood_wait") || lower.Contains("flood_premium_wait") || lower.Contains("flood wait");
        // Queue position, a donation advertisement and a playlist's total are not quotas.
        var match = Wait().Match(text);
        var limited = global || (lower.Contains("daily limit") || lower.Contains("daily quota")) && (lower.Contains("reached") || lower.Contains("exceeded") || lower.Contains("try again") || lower.Contains("used all")) || lower.Contains("limit reached")
            || lower.Contains("quota exceeded") || lower.Contains("too many requests") || lower.Contains("try again tomorrow")
            || lower.Contains("download limit") && lower.Contains("reached") || lower.Contains("you have reached") && lower.Contains("limit")
            || match.Success && (lower.Contains("please wait") || lower.Contains("retry after") || lower.Contains("try again in"));
        if (!limited) return null;
        var retry = now.AddHours(24);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var number))
        {
            var unit = match.Groups[2].Value.ToLowerInvariant();
            var factor = unit.StartsWith("day") ? 86400 : unit.StartsWith("hour") || unit.StartsWith("hr") ? 3600
                : unit.StartsWith("min") ? 60 : 1;
            retry = now.AddSeconds(Math.Clamp(number * (double)factor, 2, 7 * 86400) + 2);
        }
        return new(provider, global ? "Telegram requires a wait before either bot can be used." :
            provider + " reported a download limit. " + (match.Success ? "Retrying after its requested wait." : "No reset time was supplied; checking again in 24 hours."), retry, global);
    }
}

internal sealed record SourceCooldown(string Id, DateTimeOffset Until, string Reason);

/// <summary>Primary first on each track. Failed or limited requests use the backup; Telegram-wide waits stop both.</summary>
internal sealed class MusicSources
{
    private readonly IMusicSource[] _sources;
    private readonly Dictionary<string, SourceCooldown> _cooldowns = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _now;
    private readonly string? _state;
    private readonly Action<string> _notice;
    public MusicSources(IMusicSource primary, IMusicSource backup, Action<string> notice, string? state = null, Func<DateTimeOffset>? now = null)
    {
        _sources = [primary, backup]; _notice = notice; _state = state; _now = now ?? (() => DateTimeOffset.UtcNow);
        if (state is null || !File.Exists(state)) return;
        try
        {
            if (new FileInfo(state).Length > 64 * 1024) throw new InvalidDataException();
            foreach (var entry in JsonSerializer.Deserialize<SourceCooldown[]>(File.ReadAllText(state)) ?? [])
                if ((_sources.Any(s => s.Id == entry.Id) || entry.Id == "Telegram") && entry.Until <= _now().AddDays(8)) _cooldowns[entry.Id] = entry;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        { throw new InvalidDataException("SOURCE_STATE_UNREADABLE: saved bot limits could not be read. Keep the file for diagnosis; repair it before resuming.", ex); }
    }
    public BotTrack Track(MusicRequest request)
    {
        var name = Downloader.FileNames([new BotText.Line("1", request.Artist, request.Title, request.Duration)])[0];
        // A stable identifier prevents two recordings sharing a title from overwriting one another.
        if (request.Url is { Length: > 0 }) name += " [" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Url)))[..8].ToLowerInvariant() + "]";
        return new(request.Title, request.Artist, request.Duration, name, (output, progress, ct) => FetchAsync(request, output, progress, ct)) { Request = request };
    }
    public async Task<IReadOnlyList<BotTrack>> SearchAsync(string query, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CheckGlobal(); var errors = new List<string>();
            foreach (var source in _sources)
            {
                if (Cooling(source.Id)) continue;
                try
                {
                    var items = await source.SearchAsync(query, ct).ConfigureAwait(false);
                    // The result belongs to the bot that returned it, including a backup search.
                    if (items.Count > 0) return items.Select(r=>Track(r with {PreferredSource=source.Id})).ToArray();
                }
                catch (SourceLimitException ex) { Block(ex); if (ex.Global) throw Waiting(); errors.Add(ex.Message); }
                catch (TL.RpcException ex) when (BotLimits.Read(source.Id,ex.Message,_now()) is not null)
                {var limit=BotLimits.Read(source.Id,ex.Message,_now())!;Block(limit);if(limit.Global)throw Waiting();errors.Add(limit.Message);}
                catch(System.Net.Sockets.SocketException ex)
                {Block(new(source.Id,"TELEGRAM_CONNECTION_FAILED: "+ex.Message+" Check the VPN/proxy; queued work is preserved.",_now().AddMinutes(10),true));throw Waiting();}
                catch (Exception ex) when (ex is TimeoutException or BotAnswerException or TrackUnavailableException) { errors.Add(source.Id + ": " + ex.Message); }
            }
            if (_sources.Any(s => Cooling(s.Id))) throw Waiting();
            if (errors.Count > 0) throw new BotAnswerException(string.Join(" ", errors));
            return [];
        }
        finally { _gate.Release(); }
    }
    public async Task<string> FetchAsync(MusicRequest request, Stream output, IProgress<double>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CheckGlobal(); var errors = new List<string>();
            foreach (var source in _sources.OrderBy(s => request.CachedFrom == s.Id ? 0 : request.Selection?.Provider == s.Id ? 1 : request.PreferredSource == s.Id ? 2 : 3))
            {
                if (Cooling(source.Id) && request.CachedFrom != source.Id) continue;
                try
                {
                    if (output.CanSeek) { output.SetLength(0); output.Position = 0; }
                    // Callbacks and chat message IDs are scoped to one provider. Only recording
                    // metadata and public track URLs may cross to the fallback bot.
                    var ext = await source.FetchAsync(BotSelections.ForSource(request,source.Id), output, progress, ct).ConfigureAwait(false);
                    if (ext != "flac") throw new TrackUnavailableException("FLAC was requested, but this source returned " + ext + ".");
                    return ext;
                }
                catch (SourceLimitException ex) { Block(ex); if (ex.Global) throw Waiting(); errors.Add(ex.Message); }
                catch (TL.RpcException ex) when (BotLimits.Read(source.Id,ex.Message,_now()) is not null)
                {var limit=BotLimits.Read(source.Id,ex.Message,_now())!;Block(limit);if(limit.Global)throw Waiting();errors.Add(limit.Message);}
                catch(System.Net.Sockets.SocketException ex)
                {Block(new(source.Id,"TELEGRAM_CONNECTION_FAILED: "+ex.Message+" Check the VPN/proxy; queued work is preserved.",_now().AddMinutes(10),true));throw Waiting();}
                catch (Exception ex) when (ex is TimeoutException or BotAnswerException or TrackUnavailableException or StaleSelectionException)
                { errors.Add(source.Id + ": " + ex.Message); _notice(source.Id + " could not supply “" + request.Title + "”; trying the backup."); }
            }
            if (_sources.Any(s => Cooling(s.Id))) throw Waiting();
            throw new TrackUnavailableException("Neither bot supplied a matching FLAC recording. " + string.Join(" ", errors));
        }
        finally { _gate.Release(); }
    }
    private bool Cooling(string id) => _cooldowns.TryGetValue(id, out var value) && value.Until > _now();
    private void CheckGlobal() { if (Cooling("Telegram")) throw Waiting(); }
    private SourcesWaitingException Waiting()
    {
        var active = _cooldowns.Values.Where(c => c.Until > _now()).ToArray();
        return new("Downloads saved for later. " + string.Join(" ", active.Select(c => c.Reason)),
            active.Where(c => c.Id == "Telegram").Select(c => c.Until).DefaultIfEmpty(active.Min(c => c.Until)).Max());
    }
    private void Block(SourceLimitException error)
    {
        _cooldowns[error.Global ? "Telegram" : error.Provider] = new(error.Global ? "Telegram" : error.Provider, error.Until, error.Message);
        if (_state is not null) AtomicJson.Write(_state, _cooldowns.Values.ToArray());
        _notice(error.Message + (error.Global ? "" : " Switching to the backup."));
    }
}

internal static class AtomicJson
{
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing a redirected queue file.");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try {File.Delete(temp);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        }
    }
}
