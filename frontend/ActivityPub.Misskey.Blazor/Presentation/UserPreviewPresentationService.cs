using System.Net;
using System.Text.RegularExpressions;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IUserPreviewPresentationService
{
    Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken);

    Task<UserPreviewViewModel> FollowAsync(
        UserPreviewViewModel user,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<UserPreviewViewModel> UnfollowAsync(
        UserPreviewViewModel user,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record UserPreviewViewModel(
    Guid InternalId,
    string Id,
    NoteAuthorViewModel User,
    string Description,
    string? BannerUrl,
    long NotesCount,
    long FollowingCount,
    long FollowersCount,
    bool IsLocked,
    bool CanFollow,
    bool IsFollowing,
    bool HasPendingFollowRequestFromYou,
    bool IsFollowed,
    bool IsSilenced = false,
    bool IsSuspended = false);

public sealed partial class UserPreviewPresentationService(
    IClientApiQueryService clientQuery,
    IClientApiCommandService commands,
    IExternalEntityIdService externalIds,
    IAuthenticatedActorContext actorContext,
    MisskeyFrontendRuntimeConfiguration runtime) : IUserPreviewPresentationService
{
    public async Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken)
    {
        string safeQuery = ValidateQuery(query);
        Uri publicBaseUri = runtime.PublicBaseUri
            ?? throw new UserPreviewPresentationException("USER_PREVIEW_PUBLIC_BASE_URI_MISSING");
        ClientAccountView? account;
        if (safeQuery.StartsWith('@'))
        {
            account = await clientQuery.FindAccountByLookupAsync(
                safeQuery,
                publicBaseUri.IdnHost,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Guid? internalId = await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                safeQuery,
                cancellationToken).ConfigureAwait(false);
            account = internalId is null
                ? null
                : await clientQuery.FindAccountByIdAsync(
                    internalId.Value,
                    publicBaseUri.IdnHost,
                    cancellationToken).ConfigureAwait(false);
        }

        if (account is null)
        {
            throw new UserPreviewPresentationException("USER_PREVIEW_NOT_FOUND");
        }

        string externalId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            account.Id,
            account.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        AuthenticatedActor? viewer = await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        ClientRelationshipView? relationship = viewer is null || string.Equals(viewer.ActorIri, account.Iri, StringComparison.Ordinal)
            ? null
            : await clientQuery.FindRelationshipAsync(
                viewer.ActorIri,
                account.Id,
                publicBaseUri.IdnHost,
                cancellationToken).ConfigureAwait(false);
        var emojis = account.Emojis
            .Where(value => !string.IsNullOrWhiteSpace(value.Shortcode) && !string.IsNullOrWhiteSpace(value.Url))
            .GroupBy(value => value.Shortcode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Url, StringComparer.Ordinal);
        var author = new NoteAuthorViewModel(
            externalId,
            account.Username,
            account.Acct,
            string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
            account.AvatarUrl,
            account.Bot,
            IsCat: false,
            AvatarBlurhash: null,
            OnlineStatus: "unknown",
            Emojis: emojis);
        return new(
            account.Id,
            externalId,
            author,
            ConvertSanitizedHtmlToText(account.SummaryHtml),
            account.HeaderUrl,
            account.PostsCount,
            account.FollowingCount,
            account.FollowersCount,
            account.Locked,
            CanFollow: viewer is not null && !string.Equals(viewer.ActorIri, account.Iri, StringComparison.Ordinal),
            IsFollowing: relationship?.Following == true,
            HasPendingFollowRequestFromYou: relationship?.Requested == true,
            IsFollowed: relationship?.FollowedBy == true);
    }

    public Task<UserPreviewViewModel> FollowAsync(
        UserPreviewViewModel user,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ChangeFollowAsync(user, idempotencyKey, follow: true, cancellationToken);

    public Task<UserPreviewViewModel> UnfollowAsync(
        UserPreviewViewModel user,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ChangeFollowAsync(user, idempotencyKey, follow: false, cancellationToken);

    private async Task<UserPreviewViewModel> ChangeFollowAsync(
        UserPreviewViewModel user,
        string idempotencyKey,
        bool follow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.CanFollow)
        {
            throw new UserPreviewPresentationException("USER_PREVIEW_SELF_FOLLOW_FORBIDDEN");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 200 ||
            idempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));
        }

        AuthenticatedActor viewer = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientRelationshipView relationship = follow
            ? await commands.FollowAsync(
                viewer.Username,
                user.InternalId,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false)
            : await commands.UnfollowAsync(
                viewer.Username,
                user.InternalId,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false);
        return user with
        {
            IsFollowing = relationship.Following,
            HasPendingFollowRequestFromYou = relationship.Requested,
            IsFollowed = relationship.FollowedBy
        };
    }

    private static string ValidateQuery(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string result = value.Trim();
        if (result.Length > 2_048 || result.Any(char.IsControl))
        {
            throw new UserPreviewPresentationException("USER_PREVIEW_QUERY_INVALID");
        }

        return result;
    }

    private static string ConvertSanitizedHtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        string withLineBreaks = BreakElementRegex().Replace(html, "\n");
        return WebUtility.HtmlDecode(HtmlElementRegex().Replace(withLineBreaks, string.Empty)).Trim();
    }

    [GeneratedRegex("<(?:br\\s*/?|/p|/div|/li)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex BreakElementRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex HtmlElementRegex();
}

public sealed class UserPreviewPresentationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
