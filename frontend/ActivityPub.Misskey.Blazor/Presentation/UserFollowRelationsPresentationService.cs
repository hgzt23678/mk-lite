using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;

namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record UserFollowRelationListItem(
    string RelationId,
    UserPreviewViewModel User);

public sealed record UserFollowRelationsPageViewModel(
    IReadOnlyList<UserFollowRelationListItem> Items);

public interface IUserFollowRelationsPresentationService
{
    Task<UserFollowRelationsPageViewModel?> ReadAsync(
        string acct,
        bool followers,
        string? untilId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed class UserFollowRelationsPresentationService(
    IClientApiQueryService query,
    IExternalEntityIdService externalIds,
    IUserPreviewPresentationService previews,
    MisskeyFrontendRuntimeConfiguration runtime) : IUserFollowRelationsPresentationService
{
    public async Task<UserFollowRelationsPageViewModel?> ReadAsync(
        string acct,
        bool followers,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(acct) || acct.Length > 2_048 || acct.Any(char.IsControl))
        {
            throw new ArgumentException("The account lookup is invalid.", nameof(acct));
        }

        Uri publicBaseUri = runtime.PublicBaseUri
            ?? throw new InvalidOperationException("The frontend public base URI is not configured.");
        ClientAccountView? account = await query.FindAccountByLookupAsync(
            acct,
            publicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        Guid? beforeId = await ResolveRelationIdAsync(untilId, cancellationToken).ConfigureAwait(false);
        ClientPage<ClientFollowRelationView> page = await query.ReadFollowRelationsAsync(
            account.Id,
            followers,
            beforeId,
            afterId: null,
            Math.Clamp(limit, 1, 100),
            cancellationToken).ConfigureAwait(false);
        var items = new List<UserFollowRelationListItem>(page.Items.Count);
        foreach (ClientFollowRelationView relation in page.Items)
        {
            ClientAccountView target = followers ? relation.Follower : relation.Followee;
            string targetId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                target.Id,
                target.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            UserPreviewViewModel preview = await previews.ReadAsync(
                targetId,
                cancellationToken).ConfigureAwait(false);
            string relationId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.FollowRelation,
                relation.Id,
                relation.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            items.Add(new(relationId, preview));
        }

        return new(items);
    }

    private Task<Guid?> ResolveRelationIdAsync(
        string? externalId,
        CancellationToken cancellationToken) => string.IsNullOrWhiteSpace(externalId)
        ? Task.FromResult<Guid?>(null)
        : externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.FollowRelation,
            externalId,
            cancellationToken);
}

public sealed class UserFollowRelationsPaginationSource(
    IUserFollowRelationsPresentationService service,
    string acct,
    bool followers) : IMisskeyPaginationSource<UserFollowRelationListItem>
{
    public MisskeyPaginationOptions Options { get; } = new(20);

    public async ValueTask<IReadOnlyList<UserFollowRelationListItem>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken)
    {
        UserFollowRelationsPageViewModel? page = await service.ReadAsync(
            acct,
            followers,
            request.UntilId,
            request.Limit,
            cancellationToken).ConfigureAwait(false);
        return page?.Items ?? [];
    }

    public string GetId(UserFollowRelationListItem item) => item.RelationId;
}
