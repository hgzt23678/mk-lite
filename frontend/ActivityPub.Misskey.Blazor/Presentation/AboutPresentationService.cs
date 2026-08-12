using ActivityPub.Application;
using ActivityPub.MisskeyApi;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IAboutPresentationService
{
    Task<AboutStatisticsViewModel> GetStatisticsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
        AboutFederationQuery query,
        CancellationToken cancellationToken);
}

public sealed class AboutPresentationService(
    IFederationQueryStore federation,
    MisskeyQueryService misskey) : IAboutPresentationService
{
    private static readonly HashSet<string> States = new(StringComparer.Ordinal)
    {
        "all", "federating", "subscribing", "publishing", "suspended", "blocked", "notResponding"
    };

    private static readonly HashSet<string> Sorts = new(StringComparer.Ordinal)
    {
        "+pubSub", "-pubSub", "+notes", "-notes", "+users", "-users", "+following", "-following",
        "+followers", "-followers", "+caughtAt", "-caughtAt", "+lastCommunicatedAt", "-lastCommunicatedAt"
    };

    public async Task<AboutStatisticsViewModel> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        NodeInfoCounts counts = await federation.GetNodeInfoCountsAsync(cancellationToken).ConfigureAwait(false);
        return new AboutStatisticsViewModel(counts.LocalUsers, counts.LocalPosts);
    }

    public async Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
        AboutFederationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!States.Contains(query.State) ||
            !Sorts.Contains(query.Sort) ||
            query.Limit is < 1 or > 100 ||
            query.Offset < 0 ||
            query.Host is { Length: > 253 } ||
            query.Host?.Any(char.IsControl) == true)
        {
            throw new AboutPresentationException("ABOUT_FEDERATION_QUERY_INVALID");
        }

        string? host = string.IsNullOrWhiteSpace(query.Host) ? null : query.Host;
        IReadOnlyList<MisskeyFederationInstance> values = await misskey.ReadFederationInstancesAsync(
            new MisskeyFederationInstancesRequest(
                host,
                Blocked: query.State == "blocked" ? true : null,
                NotResponding: query.State == "notResponding" ? true : null,
                Suspended: query.State == "suspended" ? true : null,
                Federating: query.State == "federating" ? true : null,
                Subscribing: query.State == "subscribing" ? true : null,
                Publishing: query.State == "publishing" ? true : null,
                query.Limit,
                query.Offset,
                query.Sort),
            cancellationToken).ConfigureAwait(false);

        return values.Select(Map).ToArray();
    }

    private static FederationInstanceViewModel Map(MisskeyFederationInstance value) => new(
        value.Id,
        value.Host,
        value.IconUrl,
        value.IsNotResponding,
        value.IsBlocked,
        value.IsSuspended,
        value.SoftwareName,
        value.SoftwareVersion,
        value.Name,
        value.CaughtAt,
        value.UsersCount,
        value.NotesCount,
        value.FollowingCount,
        value.FollowersCount,
        value.LatestRequestSentAt,
        value.LastCommunicatedAt);
}

public sealed record AboutStatisticsViewModel(long OriginalUsersCount, long OriginalNotesCount);

public sealed record AboutFederationQuery(
    string? Host,
    string State,
    string Sort,
    int Limit,
    int Offset);

public sealed class AboutFederationPaginationSource(
    IAboutPresentationService service,
    string? host,
    string state,
    string sort) : IMisskeyPaginationSource<FederationInstanceViewModel>
{
    public MisskeyPaginationOptions Options { get; } = new(10, OffsetMode: true);

    public async ValueTask<IReadOnlyList<FederationInstanceViewModel>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.UntilId is not null || request.SinceId is not null || request.Offset is < 0)
        {
            throw new AboutPresentationException("ABOUT_FEDERATION_PAGINATION_INVALID");
        }

        return await service.ReadFederationInstancesAsync(
            new AboutFederationQuery(
                string.IsNullOrEmpty(host) ? null : host,
                state,
                sort,
                request.Limit,
                request.Offset ?? 0),
            cancellationToken).ConfigureAwait(false);
    }

    public string GetId(FederationInstanceViewModel item) => item.Id;
}

public sealed class AboutPresentationException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
