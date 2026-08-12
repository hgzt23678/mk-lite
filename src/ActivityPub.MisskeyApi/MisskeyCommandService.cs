using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.MisskeyApi;

public sealed class MisskeyCommandService(
    IDbContextFactory<FederationDbContext> contextFactory,
    IClientApiCommandService commands,
    IClientApiQueryService clientQuery,
    IExternalEntityIdService externalIds,
    MisskeyQueryService query)
{
    public async Task<object> FollowAsync(
        string username,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken)
    {
        (Guid targetId, ClientRelationshipView relationship) = await RequireRelationshipAsync(
            username,
            userId,
            cancellationToken).ConfigureAwait(false);
        if (relationship.Following || relationship.Requested)
        {
            throw new MisskeyApiException(400, "You are already following that user.", "ALREADY_FOLLOWING", "35387507-38c7-4cb9-9197-300b93783fa0");
        }

        _ = await commands.FollowAsync(username, targetId, idempotencyKey, cancellationToken).ConfigureAwait(false);
        return await query.FindUserAsync(userId, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw NoSuchUser();
    }

    public async Task<object> UnfollowAsync(
        string username,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken)
    {
        (Guid targetId, ClientRelationshipView relationship) = await RequireRelationshipAsync(
            username,
            userId,
            cancellationToken).ConfigureAwait(false);
        if (!relationship.Following && !relationship.Requested)
        {
            throw new MisskeyApiException(400, "You are not following that user.", "NOT_FOLLOWING", "5dbf82f5-c92b-40b1-87d1-6c8c0741fd09");
        }

        _ = await commands.UnfollowAsync(username, targetId, idempotencyKey, cancellationToken).ConfigureAwait(false);
        return await query.FindUserAsync(userId, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw NoSuchUser();
    }

    public async Task MuteAsync(
        string username,
        string userId,
        long? expiresAtMilliseconds,
        CancellationToken cancellationToken)
    {
        (Guid targetId, ClientRelationshipView relationship) = await RequireRelationshipAsync(
            username,
            userId,
            cancellationToken).ConfigureAwait(false);
        if (relationship.Muting)
        {
            throw new MisskeyApiException(400, "You are already muting that user.", "ALREADY_MUTING", "7e7359cb-160c-4956-b08f-4d1c653cd007");
        }

        TimeSpan? duration = null;
        if (expiresAtMilliseconds is not null)
        {
            DateTimeOffset expiresAt;
            try
            {
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMilliseconds.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw Invalid("expiresAt is outside the supported timestamp range.");
            }

            duration = expiresAt - DateTimeOffset.UtcNow;
            if (duration <= TimeSpan.Zero)
            {
                throw Invalid("expiresAt must be in the future.");
            }
        }

        _ = await commands.MuteAsync(username, targetId, hideNotifications: true, duration, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnmuteAsync(string username, string userId, CancellationToken cancellationToken)
    {
        (Guid targetId, ClientRelationshipView relationship) = await RequireRelationshipAsync(
            username,
            userId,
            cancellationToken).ConfigureAwait(false);
        if (!relationship.Muting)
        {
            throw new MisskeyApiException(400, "You are not muting that user.", "NOT_MUTING", "5467d020-daa9-4553-81e1-135c0c35a96d");
        }

        _ = await commands.UnmuteAsync(username, targetId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<object> BlockAsync(
        string username,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken)
    {
        (Guid targetId, ClientRelationshipView relationship) = await RequireRelationshipAsync(
            username,
            userId,
            cancellationToken).ConfigureAwait(false);
        await EnsureNotSelfAsync(username, targetId, create: true, cancellationToken).ConfigureAwait(false);
        if (relationship.Blocking)
        {
            throw new MisskeyApiException(400, "You are already blocking that user.", "ALREADY_BLOCKING", "787fed64-acb9-464a-82eb-afbd745b9614");
        }

        _ = await commands.BlockAsync(username, targetId, idempotencyKey, cancellationToken).ConfigureAwait(false);
        return await query.FindUserAsync(userId, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw NoSuchUser();
    }

    public async Task<object> UnblockAsync(
        string username,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken)
    {
        (Guid targetId, ClientRelationshipView relationship) = await RequireRelationshipAsync(
            username,
            userId,
            cancellationToken).ConfigureAwait(false);
        await EnsureNotSelfAsync(username, targetId, create: false, cancellationToken).ConfigureAwait(false);
        if (!relationship.Blocking)
        {
            throw new MisskeyApiException(400, "You are not blocking that user.", "NOT_BLOCKING", "291b2efa-60c6-45c0-9f6a-045c8f9b02cd");
        }

        _ = await commands.UnblockAsync(username, targetId, idempotencyKey, cancellationToken).ConfigureAwait(false);
        return await query.FindUserAsync(userId, null, null, cancellationToken).ConfigureAwait(false)
            ?? throw NoSuchUser();
    }

    private async Task EnsureNotSelfAsync(
        string username,
        Guid targetId,
        bool create,
        CancellationToken cancellationToken)
    {
        string? owner = await query.FindViewerActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            throw NoSuchUser();
        }

        ClientAccountView? target = await clientQuery.FindAccountByIdAsync(
            targetId,
            new Uri(owner).IdnHost,
            cancellationToken).ConfigureAwait(false);
        if (target is not null && string.Equals(owner, target.Iri, StringComparison.Ordinal))
        {
            throw create
                ? new MisskeyApiException(400, "Blockee is yourself.", "BLOCKEE_IS_YOURSELF", "88b19138-f28d-42c0-8499-6a31bbd0fdc6")
                : new MisskeyApiException(400, "Blockee is yourself.", "BLOCKEE_IS_YOURSELF", "06f6fac6-524b-473c-a354-e97a40ae6eac");
        }
    }

    public async Task<object> CreateNoteAsync(
        string username,
        string idempotencyKey,
        MisskeyCreateNoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ChannelId))
        {
            throw Invalid("Channels are not part of the federated note model.");
        }

        if (request.LocalOnly)
        {
            throw Invalid("localOnly cannot be silently federated; this deployment does not enable local-only notes.");
        }

        string text = request.Text?.Trim() ?? string.Empty;
        Guid? renoteId = await ResolveOptionalAsync(
            ExternalEntityType.Post,
            request.RenoteId,
            "renoteId",
            cancellationToken).ConfigureAwait(false);
        Guid? replyId = await ResolveOptionalAsync(
            ExternalEntityType.Post,
            request.ReplyId,
            "replyId",
            cancellationToken).ConfigureAwait(false);
        Guid[] fileIds = await ResolveManyAsync(
            ExternalEntityType.Media,
            request.FileIds,
            "fileIds",
            cancellationToken).ConfigureAwait(false);
        if (text.Length == 0 && fileIds.Length == 0 && renoteId is null && request.Poll is null)
        {
            throw Invalid("A note requires text, media, a poll, or renoteId.");
        }

        if (text.Length > 5_000 || (request.Cw?.Length ?? 0) > 500)
        {
            throw Invalid("The note or content warning exceeds the configured length limit.");
        }

        if (renoteId is not null && text.Length == 0 && fileIds.Length == 0 && request.Cw is null)
        {
            ClientPostView target = await commands.AnnounceAsync(
                username,
                renoteId.Value,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false);
            return await query.CreateRenoteProjectionAsync(
                username,
                target,
                cancellationToken).ConfigureAwait(false);
        }

        if (renoteId is not null)
        {
            string? viewer = await query.FindViewerActorIriAsync(username, cancellationToken).ConfigureAwait(false);
            ClientPostView? target = await clientQuery.FindPostAsync(
                renoteId.Value,
                viewer,
                cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                throw Missing("Renote target was not found or is not visible.");
            }

        }

        text = await AppendSpecifiedRecipientsAsync(
            text,
            request.Visibility,
            request.VisibleUserIds,
            cancellationToken).ConfigureAwait(false);
        ClientPollMutation? poll = MapPollMutation(request.Poll);
        var mutation = new ClientPostMutation(
            SourceText: text,
            SourceFormat: "text/x.misskeymarkdown",
            Visibility: MapVisibility(request.Visibility),
            ContentWarning: request.Cw,
            Sensitive: !string.IsNullOrEmpty(request.Cw),
            InReplyToId: replyId,
            QuoteTargetId: renoteId,
            MediaIds: fileIds,
            Poll: poll);
        ClientPostView status = await commands.CreatePostAsync(
            username,
            idempotencyKey,
            mutation,
            cancellationToken).ConfigureAwait(false);
        return await query.FindNoteByInternalIdAsync(
            status.Id,
            await query.FindViewerActorIriAsync(username, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Created note was not readable after its transaction committed.");
    }

    public async Task VotePollAsync(
        string username,
        string idempotencyKey,
        MisskeyPollVoteRequest request,
        CancellationToken cancellationToken)
    {
        Guid? postId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            request.NoteId,
            cancellationToken).ConfigureAwait(false);
        if (postId is null)
        {
            throw new MisskeyApiException(404, "No such note.", "NO_SUCH_NOTE", "ecafbd2e-c283-4d6d-aecb-1a0a33b75396");
        }

        try
        {
            _ = await commands.VotePollAsync(
                username,
                postId.Value,
                request.Choice,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ClientPollVoteException exception)
        {
            throw exception.Error switch
            {
                ClientPollVoteError.NoPoll => new MisskeyApiException(400, "The note does not attach a poll.", "NO_POLL", "5f979967-52d9-4314-a911-1c673727f92f"),
                ClientPollVoteError.InvalidChoice => new MisskeyApiException(400, "Choice ID is invalid.", "INVALID_CHOICE", "e0cc9a04-f2e8-41e4-a5f1-4127293260cc"),
                ClientPollVoteError.AlreadyVoted => new MisskeyApiException(400, "You have already voted.", "ALREADY_VOTED", "0963fc77-efac-419b-9424-b391608dc6d8"),
                ClientPollVoteError.Expired => new MisskeyApiException(400, "The poll is already expired.", "ALREADY_EXPIRED", "1022a357-b085-4054-9083-8f8de358337e"),
                ClientPollVoteError.Blocked => new MisskeyApiException(400, "You cannot vote this poll because you have been blocked by this user.", "YOU_HAVE_BEEN_BLOCKED", "85a5377e-b1e9-4617-b0b9-5bea73331e49"),
                _ => new MisskeyApiException(404, "No such note.", "NO_SUCH_NOTE", "ecafbd2e-c283-4d6d-aecb-1a0a33b75396")
            };
        }
    }

    private static ClientPollMutation? MapPollMutation(MisskeyPollRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        if (request.ExpiresAt is not null && request.ExpiredAfter is not null)
        {
            throw Invalid("expiresAt and expiredAfter cannot both be specified.");
        }

        DateTimeOffset? expiresAt = null;
        try
        {
            if (request.ExpiresAt is { } absolute)
            {
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(absolute);
            }
            else if (request.ExpiredAfter is { } duration)
            {
                if (duration <= 0 || duration > TimeSpan.FromDays(3650).TotalMilliseconds)
                {
                    throw Invalid("expiredAfter is outside the supported range.");
                }

                expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(duration);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Invalid("The poll expiration is outside the supported timestamp range.");
        }

        string[] choices = request.Choices?.Select(value => value?.Trim() ?? string.Empty).ToArray() ?? [];
        if (choices.Length is < 2 or > 10 || choices.Any(value => value.Length is 0 or > 100))
        {
            throw Invalid("A poll requires between two and ten choices of at most 100 characters.");
        }

        return new(choices, request.Multiple, expiresAt);
    }

    public async Task DeleteNoteAsync(
        string username,
        string idempotencyKey,
        MisskeyDeleteNoteRequest request,
        CancellationToken cancellationToken)
    {
        Guid? id = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            request.NoteId,
            cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            throw Missing("Note was not found.");
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ActivityRecord? activity = await db.Activities.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id.Value && x.Type == "Announce", cancellationToken)
            .ConfigureAwait(false);
        if (activity is not null)
        {
            if (activity.ObjectIri is null)
            {
                throw Missing("Renote target was not found.");
            }

            Guid? objectId = await db.Objects.Where(x => x.Iri == activity.ObjectIri && !x.IsDeleted)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (objectId is null)
            {
                throw Missing("Renote target was not found.");
            }

            _ = await commands.UndoAnnounceAsync(username, objectId.Value, idempotencyKey, cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = await commands.DeletePostAsync(username, id.Value, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> AppendSpecifiedRecipientsAsync(
        string text,
        string? visibility,
        IReadOnlyList<string>? visibleUserIds,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(visibility, "specified", StringComparison.Ordinal))
        {
            return text;
        }

        Guid[] ids = await ResolveManyAsync(
            ExternalEntityType.Actor,
            visibleUserIds,
            "visibleUserIds",
            cancellationToken).ConfigureAwait(false);
        if (ids.Length == 0)
        {
            throw Invalid("specified visibility requires at least one visibleUserId.");
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mentions = new List<string>(ids.Length);
        foreach (Guid id in ids)
        {
            LocalActor? local = await db.LocalActors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
            if (local is not null)
            {
                mentions.Add("@" + local.Username);
                continue;
            }

            RemoteActor? remote = await db.RemoteActors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
            if (remote?.PreferredUsername is null)
            {
                throw Missing("A specified recipient was not found.");
            }

            mentions.Add($"@{remote.PreferredUsername}@{new Uri(remote.Iri).IdnHost}");
        }

        string prefix = string.Join(' ', mentions.Distinct(StringComparer.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(text) ? prefix : prefix + " " + text;
    }

    private static Visibility MapVisibility(string? value) => value switch
    {
        null or "public" => Visibility.Public,
        "home" => Visibility.Unlisted,
        "followers" => Visibility.FollowersOnly,
        "specified" => Visibility.MentionedOnly,
        _ => throw Invalid("visibility is invalid.")
    };

    private async Task<Guid?> ResolveOptionalAsync(
        ExternalEntityType entityType,
        string? value,
        string parameter,
        CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return null;
        }

        return await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            entityType,
            value,
            cancellationToken).ConfigureAwait(false)
            ?? throw Invalid(parameter + " is invalid.");
    }

    private async Task<(Guid TargetId, ClientRelationshipView Relationship)> RequireRelationshipAsync(
        string username,
        string userId,
        CancellationToken cancellationToken)
    {
        Guid? targetId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            userId,
            cancellationToken).ConfigureAwait(false);
        string? owner = await query.FindViewerActorIriAsync(username, cancellationToken).ConfigureAwait(false);
        if (targetId is null || owner is null)
        {
            throw NoSuchUser();
        }

        ClientRelationshipView? relationship = await clientQuery.FindRelationshipAsync(
            owner,
            targetId.Value,
            new Uri(owner).IdnHost,
            cancellationToken).ConfigureAwait(false);
        return relationship is null
            ? throw NoSuchUser()
            : (targetId.Value, relationship);
    }

    private async Task<Guid[]> ResolveManyAsync(
        ExternalEntityType entityType,
        IReadOnlyList<string>? values,
        string parameter,
        CancellationToken cancellationToken)
    {
        if (values is null)
        {
            return [];
        }

        var result = new List<Guid>(values.Count);
        foreach (string value in values.Distinct(StringComparer.Ordinal))
        {
            Guid? resolved = await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                entityType,
                value,
                cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                throw Invalid(parameter + " contains an invalid id.");
            }

            result.Add(resolved.Value);
        }

        return result.ToArray();
    }

    private static MisskeyApiException Invalid(string message) =>
        new(400, message, "INVALID_PARAM", "3d81ceae-475f-4600-b2a8-2bc116157532");

    private static MisskeyApiException Missing(string message) =>
        new(404, message, "NO_SUCH_NOTE", "27e0c8c2-9c4a-4f77-90e7-657c2d5b9814");

    private static MisskeyApiException NoSuchUser() =>
        new(404, "No such user.", "NO_SUCH_USER", "fcd2eef9-a9b2-4c4f-8624-038099e90aa5");
}
