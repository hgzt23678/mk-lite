#if MISSKEY_BLAZOR_SERVER
using ActivityPub.Application;
using ActivityPub.Misskey.Blazor.Identity;
#endif

namespace ActivityPub.Misskey.Blazor.Presentation;

#if !MISSKEY_BLAZOR_SERVER
public interface INoteDeletionPresentationService
{
    Task DeleteAsync(NoteViewModel note, string idempotencyKey, CancellationToken cancellationToken);
}

#endif

#if MISSKEY_BLAZOR_SERVER
public sealed class NoteDeletionPresentationService(
    IAuthenticatedActorContext actorContext,
    IClientApiCommandService commands) : INoteDeletionPresentationService
{
    public async Task DeleteAsync(
        NoteViewModel note,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 200 ||
            idempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));
        }

        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        await commands.DeletePostAsync(
            actor.Username,
            note.InternalId,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);
    }
}
#endif
