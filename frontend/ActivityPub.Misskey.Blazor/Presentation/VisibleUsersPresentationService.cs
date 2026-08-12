using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IVisibleUsersPresentationService
{
    Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken);
}

public sealed class VisibleUsersPresentationService(
    IClientApiQueryService query,
    IExternalEntityIdService externalIds,
    MisskeyFrontendRuntimeConfiguration runtime) : IVisibleUsersPresentationService
{
    public const int MaximumUsers = 10;

    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (runtime.PublicBaseUri is null)
        {
            throw new VisibleUsersPresentationException("VISIBILITY_PUBLIC_BASE_URI_MISSING");
        }

        var result = new List<NoteAuthorViewModel>(Math.Min(userIds.Count, MaximumUsers));
        foreach (string userId in userIds.Take(MaximumUsers))
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new VisibleUsersPresentationException("VISIBILITY_USER_ID_INVALID");
            }

            Guid? internalId = await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                userId,
                cancellationToken).ConfigureAwait(false);
            if (internalId is null)
            {
                throw new VisibleUsersPresentationException("VISIBILITY_USER_NOT_FOUND");
            }

            ClientAccountView? account = await query.FindAccountByIdAsync(
                internalId.Value,
                runtime.PublicBaseUri.IdnHost,
                cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                throw new VisibleUsersPresentationException("VISIBILITY_USER_NOT_FOUND");
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

public sealed class VisibleUsersPresentationException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}
