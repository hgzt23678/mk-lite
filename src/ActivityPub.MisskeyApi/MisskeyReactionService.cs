using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.MisskeyApi;

public sealed class MisskeyReactionService(
    IClientApiCommandService commands,
    IExternalEntityIdService externalIds)
{
    private const string NoSuchNoteId = "6f1b0db1-9a7b-4f5f-8d1e-51c5f4b8a101";
    private const string AlreadyReactedId = "51c42bb4-931a-456b-bff7-e5a8a70dd298";
    private const string NotReactedId = "2a9f85d9-4f40-4d72-9d62-7adf74e8cb31";
    private const string InaccessibleId = "68e9d2d1-48bf-42c2-b90a-b20e09fd3d48";
    private const string InvalidReactionId = "9d6b1e8a-b43c-45ad-9869-89fae7f67b61";

    public async Task CreateAsync(
        string username,
        string idempotencyKey,
        MisskeyReactionRequest request,
        CancellationToken cancellationToken)
    {
        Guid noteId = await ResolveNoteIdAsync(request.NoteId, cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await commands.ReactAsync(
                username,
                noteId,
                request.Reaction ?? FederatedReaction.DefaultValue,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ClientReactionStateException exception) when (exception.Error == ClientReactionStateError.AlreadyReacted)
        {
            throw new MisskeyApiException(
                400,
                "You already reacted with this emoji.",
                "ALREADY_REACTED",
                AlreadyReactedId);
        }
        catch (DomainException exception)
        {
            throw new MisskeyApiException(400, exception.Message, "INVALID_REACTION", InvalidReactionId);
        }
        catch (UnauthorizedAccessException)
        {
            throw Inaccessible();
        }
        catch (KeyNotFoundException)
        {
            throw NoSuchNote();
        }
    }

    public async Task DeleteAsync(
        string username,
        string idempotencyKey,
        MisskeyReactionRequest request,
        CancellationToken cancellationToken)
    {
        Guid noteId = await ResolveNoteIdAsync(request.NoteId, cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await commands.UndoReactionAsync(
                username,
                noteId,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ClientReactionStateException exception) when (exception.Error == ClientReactionStateError.NotReacted)
        {
            throw new MisskeyApiException(
                400,
                "You have not reacted to this note.",
                "NOT_REACTED",
                NotReactedId);
        }
        catch (UnauthorizedAccessException)
        {
            throw Inaccessible();
        }
        catch (KeyNotFoundException)
        {
            throw NoSuchNote();
        }
    }

    private async Task<Guid> ResolveNoteIdAsync(string noteId, CancellationToken cancellationToken) =>
        await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            noteId,
            cancellationToken).ConfigureAwait(false)
        ?? throw NoSuchNote();

    private static MisskeyApiException NoSuchNote() => new(
        404,
        "No such note.",
        "NO_SUCH_NOTE",
        NoSuchNoteId);

    private static MisskeyApiException Inaccessible() => new(
        403,
        "Note is not accessible for you.",
        "NOT_VISIBLE_FOR_ME",
        InaccessibleId);
}
