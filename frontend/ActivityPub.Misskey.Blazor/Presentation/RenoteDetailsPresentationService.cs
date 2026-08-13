#if MISSKEY_BLAZOR_SERVER
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
#endif

namespace ActivityPub.Misskey.Blazor.Presentation;

#if !MISSKEY_BLAZOR_SERVER
public interface IRenoteDetailsPresentationService
{
    Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        int limit,
        CancellationToken cancellationToken);
}

#endif

#if MISSKEY_BLAZOR_SERVER
public sealed class RenoteDetailsPresentationService(
    IClientApiQueryService query,
    IExternalEntityIdService externalIds,
    IAuthenticatedActorContext actorContext) : IRenoteDetailsPresentationService
{
    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        int limit,
        CancellationToken cancellationToken)
    {
        AuthenticatedActor? viewer = await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ClientAnnounceActorView> actors = await query.ReadPostAnnounceActorsAsync(
            postId,
            Math.Clamp(limit, 1, 100),
            viewer?.ActorIri,
            cancellationToken).ConfigureAwait(false);
        var users = new List<NoteAuthorViewModel>(actors.Count);
        foreach (ClientAnnounceActorView announceActor in actors)
        {
            ClientAccountView? account = await query.FindAccountByIriAsync(
                announceActor.ActorIri,
                cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                continue;
            }

            string id = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                account.Id,
                account.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            users.Add(new NoteAuthorViewModel(
                id,
                account.Username,
                account.Acct,
                string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
                account.AvatarUrl,
                account.Bot,
                Emojis: account.Emojis
                    .Where(value => !string.IsNullOrWhiteSpace(value.Shortcode) && !string.IsNullOrWhiteSpace(value.Url))
                    .GroupBy(value => value.Shortcode, StringComparer.Ordinal)
                    .ToDictionary(value => value.Key, value => value.First().Url, StringComparer.Ordinal)));
        }

        return users;
    }
}
#endif
