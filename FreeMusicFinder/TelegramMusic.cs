using System.Text.RegularExpressions;
using TL;
using WTelegram;

namespace FreeMusicFinder;

internal sealed class HunterMusicSource : IMusicSource
{
    private readonly TelegramAccount? _account;
    private readonly IHunterBotSource _bot;
    private readonly Action<string> _log;
    public HunterMusicSource(TelegramAccount account,Action<string> log)
    {_account=account;_bot=new TelegramBotSource(account,log);_log=log;}
    internal HunterMusicSource(IHunterBotSource bot,Action<string>? log=null)
    {_bot=bot;_log=log??(_=>{});}
    public string Id => BotSelections.Hunters;
    public async Task<IReadOnlyList<MusicRequest>> SearchAsync(string query, CancellationToken ct)
        => (await _bot.SearchAsync(query, ct).ConfigureAwait(false)).Select(t =>
            (t.Request??new MusicRequest(t.Title,t.Artist,t.Duration?.TotalSeconds)) with {PreferredSource=Id}).ToArray();
    public async Task<string> FetchAsync(MusicRequest request, Stream output, IProgress<double>? progress, CancellationToken ct)
    {
        request=BotSelections.ForSource(request,Id);
        if(_account is not null && await TryCachedAsync(request,output,progress,ct).ConfigureAwait(false) is {} cached)return cached;
        if(request.Selection is not null)
        {
            try {return await _bot.FetchSelectedAsync(request,output,progress,ct).ConfigureAwait(false);}
            catch(StaleSelectionException ex){_log(ex.Message);}
        }
        var tracks = await _bot.SearchAsync(request.Artist + " " + request.Title, ct).ConfigureAwait(false);
        var match = tracks.FirstOrDefault(t => RecordingMatch.Fits(request,t.Artist,t.Title,t.Duration));
        if (match is null) throw new TrackUnavailableException("No matching recording in Music Hunters' results.");
        // A stale selection is refreshed once; no search loop and no human button press.
        return await match.Fetch(output,progress,ct).ConfigureAwait(false);
    }
    private async Task<string?> TryCachedAsync(MusicRequest request,Stream output,IProgress<double>? progress,CancellationToken ct)
    {
        var account=_account!;
        var cachedClient=await account.ConnectAsync(ct).ConfigureAwait(false);
        var cachedPeer=(await cachedClient.Contacts_ResolveUsername(TelegramBotSource.Bot)).User;
        if(await TelegramAudioCache.Find(cachedClient,cachedPeer,request,ct).ConfigureAwait(false) is {} existing)
        {
            await cachedClient.DownloadFileAsync(existing,output,(PhotoSizeBase?)null,(done,total)=>{ct.ThrowIfCancellationRequested();if(total>0)progress?.Report((double)done/total);}).WaitAsync(ct).ConfigureAwait(false);
            return "flac";
        }
        if (request.MessageId is {} id && request.CachedFrom == Id)
        {
            var client=await account.ConnectAsync(ct).ConfigureAwait(false);
            var bot=(await client.Contacts_ResolveUsername(TelegramBotSource.Bot)).User;
            var old=await client.Messages_GetHistory(bot,offset_id:id+1,limit:1).WaitAsync(ct).ConfigureAwait(false);
            if(old.Messages.OfType<Message>().FirstOrDefault(m=>m.id==id)?.media is MessageMediaDocument{document:Document doc} && DeezLoadSource.Fits(request,doc))
            {
                await client.DownloadFileAsync(doc,output,(PhotoSizeBase?)null,(done,total)=>{ct.ThrowIfCancellationRequested();if(total>0)progress?.Report((double)done/total);}).WaitAsync(ct).ConfigureAwait(false);
                return Path.GetExtension(doc.Filename??"").TrimStart('.').ToLowerInvariant();
            }
        }
        return null;
    }
}

internal static class RecordingMatch
{
    public static bool Fits(MusicRequest wanted, string artist, string title, TimeSpan? duration)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title)) return false;
        if (wanted.Explicit == true && Regex.IsMatch(title, @"\b(clean|censored|radio edit)\b",RegexOptions.IgnoreCase)) return false;
        static string Versions(string name)=>Regex.Replace(name,@"\(with instrumental by[^)]*\)","",RegexOptions.IgnoreCase);
        foreach (var version in new[]{"live","remix","instrumental","acoustic","cover","sped up","slowed"})
            if (Regex.IsMatch(Versions(wanted.Title), @"\b"+version+@"\b",RegexOptions.IgnoreCase) != Regex.IsMatch(Versions(title),@"\b"+version+@"\b",RegexOptions.IgnoreCase)) return false;
        if (wanted.Duration is {} length && duration is {} other && Math.Abs((length-other).TotalSeconds)>3) return false;
        static IEnumerable<string> ArtistKeys(string value)=>Regex.Split(value,@"\s*(?:,|&|\bfeat\.?|\bft\.?|\bfeaturing\b)\s*",RegexOptions.IgnoreCase).Select(BotText.Key).Where(k=>k.Length>0);
        var a=BotText.Key(wanted.Artist); var b=BotText.Key(artist);
        if(a.Length==0 || b.Length==0 || !(a==b || ArtistKeys(wanted.Artist).Intersect(ArtistKeys(artist)).Any())) return false;
        return BotText.Key(wanted.Title)==BotText.Key(title) || wanted.Duration is not null && duration is not null && BotText.Key(BotText.Plain(wanted.Title))==BotText.Key(BotText.Plain(title));
    }
}

internal sealed class DeezLoadSource : IMusicSource
{
    public const string Bot = "deezload2bot";
    public string Id => "DeezLoad";
    private readonly TelegramAccount _account;
    private readonly Action<string> _notice;
    private readonly SemaphoreSlim _conversation = new(1,1);
    public DeezLoadSource(TelegramAccount account, Action<string> notice) { _account=account; _notice=notice; }
    public async Task<IReadOnlyList<MusicRequest>> SearchAsync(string query,CancellationToken ct)
    {
        var client=await _account.ConnectAsync(ct).ConfigureAwait(false);
        var bot=(await client.Contacts_ResolveUsername(Bot)).User;
        try
        {
            var results=await client.Messages_GetInlineBotResults(bot,bot,query,"").WaitAsync(ct).ConfigureAwait(false);
            var found=new List<MusicRequest>();
            foreach(var result in results.results)
            {
                if(result is BotInlineMediaResult media && media.document is Document doc)
                {
                    var track=Request(doc,null);
                    if(track is not null) found.Add(track);
                }
                else if(result is BotInlineResult plain && plain.send_message is BotInlineMessageText text)
                {
                    if(InlineTrack(plain.title??"",plain.description??"",text.message) is {} track)found.Add(track);
                }
            }
            return found;
        }
        catch(RpcException ex) { throw Translate(ex); }
    }
    internal static MusicRequest? InlineTrack(string title,string description,string message)
    {
        var match=Regex.Match(message,@"https://(?:www\.)?deezer\.com/(?:[a-z]{2}/)?track/\d+");
        var artistMatch=Regex.Match(description,@"^Artists?\s*:\s*(.+)$",RegexOptions.Multiline|RegexOptions.IgnoreCase);
        var artist=artistMatch.Success?artistMatch.Groups[1].Value.Trim():description.Split('\n')[0].Trim();
        title=title.Trim();
        if(!match.Success || !MusicLink.TryParse(match.Value,out var link) || title.Length==0 || artist.Length==0 || title.EndsWith('…'))return null;
        return new(title,artist,Url:link!.Url);
    }
    public async Task<string> FetchAsync(MusicRequest request,Stream output,IProgress<double>? progress,CancellationToken ct)
    {
        var client=await _account.ConnectAsync(ct).ConfigureAwait(false);
        var bot=(await client.Contacts_ResolveUsername(Bot)).User;
        if(await TelegramAudioCache.Find(client,bot,request,ct).ConfigureAwait(false) is {} existing)
            return await Download(client,existing,output,progress,ct).ConfigureAwait(false);
        if(request.MessageId is {} id && request.CachedFrom == Id)
        {
            // The message is already in this bot's chat; reading its file does not request another bot download.
            var old=await client.Messages_GetHistory(bot,offset_id:id+1,limit:1).WaitAsync(ct).ConfigureAwait(false);
            var message=old.Messages.OfType<Message>().FirstOrDefault(m=>m.id==id);
            if(message?.media is MessageMediaDocument {document:Document cached} && Fits(request,cached))
                return await Download(client,cached,output,progress,ct).ConfigureAwait(false);
        }
        await _conversation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var url=request.Url;
            if(url is null)
            {
                var matches=await client.Messages_GetInlineBotResults(bot,bot,request.Artist+" "+request.Title,"").WaitAsync(ct).ConfigureAwait(false);
                foreach(var item in matches.results.OfType<BotInlineMediaResult>())
                    if(item.document is Document doc && Fits(request,doc)) return await Download(client,doc,output,progress,ct).ConfigureAwait(false);
                url=MatchingInlineLink(request,matches.results.OfType<BotInlineResult>());
            }
            if(!MusicLink.TryParse(url,out var link) || link!.Kind!="track") throw new TrackUnavailableException("DeezLoad did not return a usable track link.");
            var sent=await client.SendMessageAsync(bot,link.Url).WaitAsync(ct).ConfigureAwait(false);
            var end=DateTimeOffset.UtcNow.AddMinutes(15); var cursor=sent.id; var lastNotice="";
            while(DateTimeOffset.UtcNow<end)
            {
                ct.ThrowIfCancellationRequested();
                var replies=await ReadNew(client,bot,sent.id,ct).ConfigureAwait(false);
                foreach(var m in replies)
                {
                    if(BotLimits.Read(Id,m.message??"",DateTimeOffset.UtcNow) is {} limited) throw limited;
                    if(m.media is MessageMediaDocument {document:Document doc} && Fits(request,doc)) return await Download(client,doc,output,progress,ct).ConfigureAwait(false);
                    if(m.message is {Length:>0} status && status!=lastNotice && (status.Contains("position in the queue") || status.Contains("Downloading")))
                    {
                        _notice("DeezLoad: "+(lastNotice=status).ReplaceLineEndings(" "));
                        if (status.Contains("position in the queue")) throw new SourceLimitException(Id,"DeezLoad has a queue delay; trying the other bot.",DateTimeOffset.UtcNow.AddMinutes(10));
                    }
                    if(Regex.IsMatch(m.message??"",@"cannot download|not available|not found",RegexOptions.IgnoreCase)) throw new TrackUnavailableException("DeezLoad could not supply this recording.");
                }
                await Task.Delay(TimeSpan.FromSeconds(4),ct).ConfigureAwait(false);
            }
            throw new TimeoutException("DeezLoad did not deliver the track within 15 minutes. The backup will be tried.");
        }
        catch(RpcException ex) { throw Translate(ex); }
        finally { _conversation.Release(); }
    }
    internal Exception Translate(RpcException ex)
        => BotLimits.Read(Id,ex.Message,DateTimeOffset.UtcNow) ?? (Exception)new BotAnswerException("DeezLoad: "+ex.Message);
    internal static string? MatchingInlineLink(MusicRequest wanted,IEnumerable<BotInlineResult> results)
        => results.Where(r=>r.send_message is BotInlineMessageText)
            .Select(r=>InlineTrack(r.title??"",r.description??"",((BotInlineMessageText)r.send_message).message))
            .FirstOrDefault(r=>r is not null && RecordingMatch.Fits(wanted,r.Artist,r.Title,r.Duration))?.Url;
    internal static MusicRequest? Request(Document doc,int? messageId)
    {
        var audio=doc.attributes?.OfType<DocumentAttributeAudio>().FirstOrDefault();
        if(audio is null || string.IsNullOrWhiteSpace(audio.title) || string.IsNullOrWhiteSpace(audio.performer)) return null;
        return new(audio.title,audio.performer,audio.duration>0?audio.duration:null,MessageId:messageId);
    }
    internal static bool Fits(MusicRequest wanted,Document doc)
    {
        var sent=Request(doc,null);
        return sent is not null && RecordingMatch.Fits(wanted,sent.Artist,sent.Title,sent.Duration);
    }
    private static async Task<string> Download(Client client,Document doc,Stream output,IProgress<double>? progress,CancellationToken ct)
    {
        var ext=Path.GetExtension(doc.Filename??"").TrimStart('.').ToLowerInvariant();
        if(ext!="flac" && doc.mime_type is not "audio/flac" and not "audio/x-flac") throw new TrackUnavailableException("The bot returned a file in a different quality; FLAC is required.");
        await client.DownloadFileAsync(doc,output,(PhotoSizeBase?)null,(done,total)=>{ ct.ThrowIfCancellationRequested(); if(total>0) progress?.Report((double)done/total); }).WaitAsync(ct).ConfigureAwait(false);
        return "flac";
    }
    internal static async Task<IReadOnlyList<Message>> ReadNew(Client client,InputPeer bot,int after,CancellationToken ct)
    {
        var all=new List<Message>(); var offset=0;
        for(var page=0;page<100;page++)
        {
            var history=await client.Messages_GetHistory(bot,offset_id:offset,min_id:after,limit:100).WaitAsync(ct).ConfigureAwait(false);
            if(history.Messages.Length==0) break;
            all.AddRange(history.Messages.OfType<Message>().Where(m=>!m.flags.HasFlag(Message.Flags.out_)));
            var next=history.Messages.Min(m=>m.ID);
            if(next<=after || next==offset) break;
            offset=next;
            if(history.Messages.Length<100) break;
            await Task.Delay(600,ct).ConfigureAwait(false);
        }
        return all.OrderBy(m=>m.id).ToArray();
    }
}

internal static class TelegramAudioCache
{
    /// <summary>Already received files can be copied directly, without another bot request or a new provider queue.</summary>
    public static async Task<Document?> Find(Client client,InputPeer peer,MusicRequest wanted,CancellationToken ct)
    {
        var history=await client.Messages_GetHistory(peer,limit:100).WaitAsync(ct).ConfigureAwait(false);
        return history.Messages.OfType<Message>().Select(m=>(m.media as MessageMediaDocument)?.document).OfType<Document>()
            .FirstOrDefault(doc=>doc.mime_type is "audio/flac" or "audio/x-flac" && DeezLoadSource.Fits(wanted,doc));
    }
}

internal sealed record MusicLink(string Service,string Kind,string Id,string Url)
{
    public static bool TryParse(string? text,out MusicLink? link)
    {
        link=null;
        if(string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(),UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length>0 || !uri.IsDefaultPort) return false;
        var pieces=uri.AbsolutePath.Trim('/').Split('/');
        if(uri.Host.Equals("open.spotify.com",StringComparison.OrdinalIgnoreCase))
        {
            if(pieces.Length==3 && pieces[0].StartsWith("intl-")) pieces=pieces[1..];
            if(pieces.Length!=2 || pieces[0] is not ("track" or "album" or "playlist") || !Regex.IsMatch(pieces[1],@"^[A-Za-z0-9]{22}$")) return false;
            link=new("spotify",pieces[0],pieces[1],"https://open.spotify.com/"+string.Join('/',pieces));return true;
        }
        if(uri.Host is "deezer.com" or "www.deezer.com")
        {
            if(pieces.Length==3 && pieces[0].Length==2) pieces=pieces[1..];
            if(pieces.Length!=2 || pieces[0] is not ("track" or "album" or "playlist") || !Regex.IsMatch(pieces[1],@"^\d{1,20}$")) return false;
            link=new("deezer",pieces[0],pieces[1],"https://www.deezer.com/"+string.Join('/',pieces));return true;
        }
        return false;
    }
}
