using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.MastodonApi;

public sealed record MastodonStatusMutation(
    string Status,
    string Visibility,
    string? SpoilerText,
    bool Sensitive,
    Guid? InReplyToId,
    IReadOnlyList<Guid> MediaIds);

public sealed class MastodonCommandService(
    IClientApiCommandService commands,
    IClientApiQueryService clientQueries,
    MastodonQueryService query,
    IExternalEntityIdService externalIds)
{
    public Task<MastodonStatus> CreateStatusAsync(
        string username,
        string idempotencyKey,
        MastodonStatusMutation mutation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            username,
            commands.CreatePostAsync(
                username,
                idempotencyKey,
                new ClientPostMutation(
                    mutation.Status,
                    "text/plain",
                    ParseVisibility(mutation.Visibility),
                    mutation.SpoilerText,
                    mutation.Sensitive,
                    mutation.InReplyToId,
                    null,
                    mutation.MediaIds),
                cancellationToken),
            cancellationToken);

    public Task<MastodonStatus> DeleteStatusAsync(
        string username,
        Guid statusId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(username, commands.DeletePostAsync(username, statusId, idempotencyKey, cancellationToken), cancellationToken);

    public Task<MastodonStatus> FavouriteAsync(
        string username,
        Guid statusId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(username, commands.LikeAsync(username, statusId, idempotencyKey, cancellationToken), cancellationToken);

    public Task<MastodonStatus> UnfavouriteAsync(
        string username,
        Guid statusId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(username, commands.UndoLikeAsync(username, statusId, idempotencyKey, cancellationToken), cancellationToken);

    public Task<MastodonStatus> ReblogAsync(
        string username,
        Guid statusId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(username, commands.AnnounceAsync(username, statusId, idempotencyKey, cancellationToken), cancellationToken);

    public Task<MastodonStatus> UnreblogAsync(
        string username,
        Guid statusId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(username, commands.UndoAnnounceAsync(username, statusId, idempotencyKey, cancellationToken), cancellationToken);

    public async Task<MastodonRelationship> MuteAsync(
        string username,
        Guid accountId,
        bool hideNotifications,
        TimeSpan? duration,
        CancellationToken cancellationToken) =>
        await MapRelationshipAsync(
            await commands.MuteAsync(
                username,
                accountId,
                hideNotifications,
                duration,
                cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonRelationship> UnmuteAsync(
        string username,
        Guid accountId,
        CancellationToken cancellationToken) =>
        await MapRelationshipAsync(
            await commands.UnmuteAsync(username, accountId, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonRelationship> FollowAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await MapRelationshipAsync(
            await commands.FollowAsync(username, accountId, idempotencyKey, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonRelationship> UnfollowAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await MapRelationshipAsync(
            await commands.UnfollowAsync(username, accountId, idempotencyKey, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonRelationship> BlockAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await MapRelationshipAsync(
            await commands.BlockAsync(username, accountId, idempotencyKey, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonRelationship> UnblockAsync(
        string username,
        Guid accountId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await MapRelationshipAsync(
            await commands.UnblockAsync(username, accountId, idempotencyKey, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<MastodonRelationship?> FindRelationshipAsync(
        string username,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        string? owner = await query.FindLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            return null;
        }

        ClientRelationshipView? relationship = await clientQueries.FindRelationshipAsync(
            owner,
            accountId,
            new Uri(owner).IdnHost,
            cancellationToken).ConfigureAwait(false);
        return relationship is null ? null : await MapRelationshipAsync(relationship, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MastodonStatus> ExecuteAsync(
        string username,
        Task<ClientPostView> operation,
        CancellationToken cancellationToken)
    {
        ClientPostView result = await operation.ConfigureAwait(false);
        string? viewerActorIri = await query.FindLocalActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        return await query.FindStatusAsync(result.Id, viewerActorIri, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A committed client mutation has no readable projection.");
    }

    private async Task<MastodonRelationship> MapRelationshipAsync(
        ClientRelationshipView value,
        CancellationToken cancellationToken)
    {
        string id = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            value.AccountId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return new(
            id,
            value.Following,
            value.ShowingAnnounces,
            value.Notifying,
            value.FollowedBy,
            value.Blocking,
            value.BlockedBy,
            value.Muting,
            value.MutingNotifications,
            value.Requested,
            value.RequestedBy,
            value.DomainBlocking,
            value.Endorsed,
            value.Note);
    }

    private static Visibility ParseVisibility(string value) => value switch
    {
        "public" => Visibility.Public,
        "unlisted" => Visibility.Unlisted,
        "private" => Visibility.FollowersOnly,
        "direct" => Visibility.MentionedOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Unsupported Mastodon visibility.")
    };
}
