using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IReactionDetailsPresentationService
{
    Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        string reaction,
        int limit,
        CancellationToken cancellationToken);
}

public sealed class ReactionDetailsPresentationService(
    IClientApiQueryService query,
    IExternalEntityIdService externalIds,
    IAuthenticatedActorContext actorContext) : IReactionDetailsPresentationService
{
    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        string reaction,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reaction);
        AuthenticatedActor? viewer = await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ClientReactionActorView> actors = await query.ReadPostReactionActorsAsync(
            postId,
            reaction,
            Math.Clamp(limit, 1, 100),
            viewer?.ActorIri,
            cancellationToken).ConfigureAwait(false);
        var users = new List<NoteAuthorViewModel>(actors.Count);
        foreach (ClientReactionActorView reactionActor in actors)
        {
            ClientAccountView? account = await query.FindAccountByIriAsync(
                reactionActor.ActorIri,
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
            var emojis = account.Emojis
                .Where(value => !string.IsNullOrWhiteSpace(value.Shortcode) && !string.IsNullOrWhiteSpace(value.Url))
                .GroupBy(value => value.Shortcode, StringComparer.Ordinal)
                .ToDictionary(value => value.Key, value => value.First().Url, StringComparer.Ordinal);
            users.Add(new NoteAuthorViewModel(
                id,
                account.Username,
                account.Acct,
                string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName,
                account.AvatarUrl,
                account.Bot,
                IsCat: false,
                AvatarBlurhash: null,
                OnlineStatus: "unknown",
                Emojis: emojis));
        }

        return users;
    }
}
