namespace FreeMusicFinder;

internal sealed partial class ArtistCatalog
{
    /// <summary>
    /// The artists Deezer's listeners pair with this one, in Deezer's order. Only Deezer artists have such a list; an
    /// Apple artist gives none. Public metadata, cached for a week like other slow-changing pages.
    /// </summary>
    public Task<CatalogResult<List<CatalogArtist>>> RelatedAsync(CatalogArtist artist, CancellationToken ct)
    {
        var (provider, id) = Split(artist.Id);
        if (provider != "deezer") return Task.FromResult(new CatalogResult<List<CatalogArtist>>([], _now(), false));
        return Cached("related:" + artist.Id, TimeSpan.FromDays(7), false, async () =>
        {
            using var data = await Get("deezer", "/artist/" + id + "/related?limit=25", ct);
            return Data(data.RootElement).Select(DeezerArtist).Where(a => a is not null).Cast<CatalogArtist>()
                .Where(a => a.Id != artist.Id).DistinctBy(a => a.Id).ToList();
        }, ct);
    }
}
