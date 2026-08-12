namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record MisskeyPaginationOptions(
    int Limit,
    bool NoPaging = false,
    bool Reversed = false,
    bool OffsetMode = false)
{
    public int EffectiveLimit => Limit > 0 ? Limit : 10;
}

public sealed record MisskeyPaginationRequest(
    int Limit,
    string? UntilId = null,
    string? SinceId = null,
    int? Offset = null);

public interface IMisskeyPaginationSource<TItem>
{
    MisskeyPaginationOptions Options { get; }

    ValueTask<IReadOnlyList<TItem>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken);

    string GetId(TItem item);

    TItem MarkAdvertisement(TItem item) => item;
}
