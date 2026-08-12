using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IAvatarsPresentationService
{
    Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken);
}

public sealed class AvatarsPresentationService(
    IClientApiQueryService query,
    IExternalEntityIdService externalIds,
    MisskeyFrontendRuntimeConfiguration runtime) : IAvatarsPresentationService
{
    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (runtime.PublicBaseUri is null)
        {
            throw new AvatarsPresentationException("AVATARS_PUBLIC_BASE_URI_MISSING");
        }

        if (userIds.Any(string.IsNullOrWhiteSpace) ||
            userIds.Distinct(StringComparer.Ordinal).Count() != userIds.Count)
        {
            throw new AvatarsPresentationException("AVATARS_USER_IDS_INVALID");
        }

        var result = new List<NoteAuthorViewModel>(userIds.Count);
        foreach (string userId in userIds)
        {
            Guid? internalId = await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                userId,
                cancellationToken).ConfigureAwait(false);
            if (internalId is null)
            {
                throw new AvatarsPresentationException("AVATARS_USER_NOT_FOUND");
            }

            ClientAccountView? account = await query.FindAccountByIdAsync(
                internalId.Value,
                runtime.PublicBaseUri.IdnHost,
                cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                throw new AvatarsPresentationException("AVATARS_USER_NOT_FOUND");
            }

            result.Add(new NoteAuthorViewModel(
                userId,
                account.Username,
                account.Acct,
                string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
                account.AvatarUrl,
                account.Bot));
        }

        return result;
    }
}

public sealed class AvatarsPresentationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
