using System.Net;
using System.Text.RegularExpressions;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface ITimelinePresentationService
{
    Task<TimelinePageViewModel> ReadAsync(
        TimelineKind kind,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken);


    Task<NoteViewModel> CreateAsync(
        NoteDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<NoteViewModel> RenoteAsync(
        string noteId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<NoteViewModel> ReactAsync(
        string noteId,
        string reaction,
        bool remove,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<NoteViewModel> VotePollAsync(
        string noteId,
        int choiceIndex,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<NoteViewModel?> FindForStreamAsync(
        Guid id,
        TimelineKind kind,
        CancellationToken cancellationToken);

    Task<string> MapNoteIdAsync(
        Guid id,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public interface INotePagePresentationService
{
    Task<NoteViewModel?> FindAsync(
        string noteId,
        CancellationToken cancellationToken);
}

public interface IUserPagePresentationService
{
    Task<UserPageViewModel> ReadAsync(
        string acct,
        string? untilId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record UserPageViewModel(
    UserPreviewViewModel User,
    TimelinePageViewModel Notes);

public sealed partial class TimelinePresentationService(
    IClientApiQueryService query,
    IClientApiCommandService commands,
    IExternalEntityIdService externalIds,
    IAuthenticatedActorContext actorContext,
    MisskeyFrontendRuntimeConfiguration? runtime = null) : ITimelinePresentationService, INotePagePresentationService
{
    public async Task<TimelinePageViewModel> ReadAsync(
        TimelineKind kind,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        int safeLimit = Math.Clamp(limit, 1, 40);
        Guid? before = await ResolveCursorAsync(beforeId, cancellationToken).ConfigureAwait(false);
        AuthenticatedActor? actor = kind is TimelineKind.Home or TimelineKind.Hybrid
            ? await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false)
            : await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);

        ClientPage<ClientPostView> page = kind switch
        {
            TimelineKind.Home => await query.ReadHomeTimelineAsync(
                actor!.ActorIri,
                before,
                safeLimit,
                cancellationToken).ConfigureAwait(false),
            TimelineKind.Local => await query.ReadPublicTimelineAsync(
                before,
                safeLimit,
                localOnly: true,
                cancellationToken).ConfigureAwait(false),
            TimelineKind.Global => await query.ReadPublicTimelineAsync(
                before,
                safeLimit,
                localOnly: false,
                cancellationToken).ConfigureAwait(false),
            TimelineKind.Hybrid => await ReadHybridAsync(
                actor!.ActorIri,
                before,
                safeLimit,
                cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var notes = new List<NoteViewModel>(page.Items.Count);
        foreach (ClientPostView item in page.Items)
        {
            notes.Add(await MapAsync(item, cancellationToken).ConfigureAwait(false));
        }

        string? next = page.Next is null
            ? null
            : await MapNoteIdAsync(page.Next.Id, page.Next.Timestamp, cancellationToken).ConfigureAwait(false);
        return new(notes, next);
    }


    public async Task<NoteViewModel> CreateAsync(
        NoteDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        string text = draft.Text.Trim();
        if (text.Length > 5_000 || text.Length == 0 && draft.MediaIds.Count == 0 && draft.Poll is null)
        {
            throw new ArgumentException("A note must contain text, media, or a poll and cannot exceed 5000 characters.", nameof(draft));
        }

        if ((draft.ContentWarning?.Length ?? 0) > 500)
        {
            throw new ArgumentException("A content warning cannot exceed 500 characters.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > 200 ||
            idempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));
        }

        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientPostView created = await commands.CreatePostAsync(
            actor.Username,
            idempotencyKey,
            new(
                text,
                "text/x.misskeymarkdown",
                draft.Visibility,
                draft.ContentWarning,
                Sensitive: draft.Sensitive || !string.IsNullOrWhiteSpace(draft.ContentWarning),
                draft.ReplyToId,
                draft.QuoteTargetId,
                draft.MediaIds,
                draft.Poll is null
                    ? null
                    : new ClientPollMutation(draft.Poll.Choices, draft.Poll.Multiple, draft.Poll.ExpiresAt)),
            cancellationToken).ConfigureAwait(false);
        return await MapAsync(created, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteViewModel> RenoteAsync(
        string noteId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Guid postId = await ResolveRequiredPostIdAsync(noteId, cancellationToken).ConfigureAwait(false);
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientPostView post = await commands.AnnounceAsync(
            actor.Username,
            postId,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);
        return await MapAsync(post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteViewModel> ReactAsync(
        string noteId,
        string reaction,
        bool remove,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Guid postId = await ResolveRequiredPostIdAsync(noteId, cancellationToken).ConfigureAwait(false);
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientPostView post = remove
            ? await commands.UndoReactionAsync(actor.Username, postId, idempotencyKey, cancellationToken).ConfigureAwait(false)
            : await commands.ReactAsync(actor.Username, postId, reaction, idempotencyKey, cancellationToken).ConfigureAwait(false);
        return await MapAsync(post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteViewModel> VotePollAsync(
        string noteId,
        int choiceIndex,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(choiceIndex);

        Guid postId = await ResolveRequiredPostIdAsync(noteId, cancellationToken).ConfigureAwait(false);
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientPostView post = await commands.VotePollAsync(
            actor.Username,
            postId,
            choiceIndex,
            idempotencyKey,
            cancellationToken).ConfigureAwait(false);
        return await MapAsync(post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteViewModel?> FindForStreamAsync(
        Guid id,
        TimelineKind kind,
        CancellationToken cancellationToken)
    {
        AuthenticatedActor? actor = kind is TimelineKind.Home or TimelineKind.Hybrid
            ? await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false)
            : await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        ClientPostView? post;
        if (kind == TimelineKind.Hybrid)
        {
            post = await query.FindStreamPostAsync(
                id,
                actor!.ActorIri,
                ClientStreamAudience.Home,
                localOnly: false,
                cancellationToken).ConfigureAwait(false);
            post ??= await query.FindStreamPostAsync(
                id,
                actor.ActorIri,
                ClientStreamAudience.Public,
                localOnly: true,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            post = await query.FindStreamPostAsync(
                id,
                actor?.ActorIri,
                kind is TimelineKind.Local or TimelineKind.Global
                    ? ClientStreamAudience.Public
                    : ClientStreamAudience.Home,
                localOnly: kind == TimelineKind.Local,
                cancellationToken).ConfigureAwait(false);
        }

        return post is null ? null : await MapAsync(post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteViewModel?> FindAsync(
        string noteId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(noteId) || noteId.Length > 256 || noteId.Any(char.IsControl))
        {
            return null;
        }

        Guid? resolved = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            noteId,
            cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return null;
        }

        AuthenticatedActor? viewer = await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        ClientPostView? post = await query.FindPostAsync(
            resolved.Value,
            viewer?.ActorIri,
            cancellationToken).ConfigureAwait(false);
        return post is null ? null : await MapAsync(post, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TimelinePageViewModel> ReadUserNotesAsync(
        UserPreviewViewModel user,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        Guid? cursor = string.IsNullOrWhiteSpace(untilId)
            ? null
            : await externalIds.ResolveAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Post,
                untilId,
                cancellationToken).ConfigureAwait(false);
        if (untilId is not null && cursor is null)
        {
            throw new TimelineCursorException("TIMELINE_CURSOR_INVALID");
        }

        Uri publicBaseUri = runtime?.PublicBaseUri
            ?? throw new InvalidOperationException("Misskey frontend PublicBaseUri is not configured.");
        ClientPage<ClientPostView> page = await query.ReadAccountPostsAsync(
            user.InternalId,
            publicBaseUri.IdnHost,
            cursor,
            Math.Clamp(limit, 1, 40),
            (await actorContext.FindAsync(cancellationToken).ConfigureAwait(false))?.ActorIri,
            cancellationToken).ConfigureAwait(false);
        var notes = new List<NoteViewModel>(page.Items.Count);
        foreach (ClientPostView item in page.Items)
        {
            notes.Add(await MapAsync(item, cancellationToken).ConfigureAwait(false));
        }

        string? next = page.Next is null
            ? null
            : await MapNoteIdAsync(page.Next.Id, page.Next.Timestamp, cancellationToken).ConfigureAwait(false);
        return new(notes, next);
    }

    public Task<string> MapNoteIdAsync(
        Guid id,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            id,
            occurredAt,
            cancellationToken);

    private async Task<Guid?> ResolveCursorAsync(string? beforeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(beforeId))
        {
            return null;
        }

        Guid? resolved = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            beforeId,
            cancellationToken).ConfigureAwait(false);
        return resolved ?? throw new TimelineCursorException("TIMELINE_CURSOR_INVALID");
    }

    private async Task<Guid> ResolveRequiredPostIdAsync(string noteId, CancellationToken cancellationToken)
    {
        Guid? resolved = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            noteId,
            cancellationToken).ConfigureAwait(false);
        return resolved ?? throw new InvalidOperationException("The requested note does not exist.");
    }

    private async Task<ClientPage<ClientPostView>> ReadHybridAsync(
        string actorIri,
        Guid? before,
        int limit,
        CancellationToken cancellationToken)
    {
        Task<ClientPage<ClientPostView>> homeTask = query.ReadHomeTimelineAsync(
            actorIri,
            before,
            limit,
            cancellationToken);
        Task<ClientPage<ClientPostView>> localTask = query.ReadPublicTimelineAsync(
            before,
            limit,
            localOnly: true,
            cancellationToken);
        await Task.WhenAll(homeTask, localTask).ConfigureAwait(false);
        ClientPostView[] combined = homeTask.Result.Items
            .Concat(localTask.Result.Items)
            .DistinctBy(item => item.Id)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .ToArray();
        bool hasMore = homeTask.Result.Next is not null || localTask.Result.Next is not null;
        ClientPageCursor? next = hasMore && combined.Length > 0
            ? new(combined[^1].Id, combined[^1].CreatedAt)
            : null;
        return new(combined, next, combined.FirstOrDefault() is { } first ? new(first.Id, first.CreatedAt) : null);
    }

    private async Task<NoteViewModel> MapAsync(
        ClientPostView post,
        CancellationToken cancellationToken,
        bool includeReply = true)
    {
        AuthenticatedActor? viewer = await actorContext.FindAsync(cancellationToken).ConfigureAwait(false);
        ClientReactionSummaryView reactionSummary = await query.ReadPostReactionsAsync(
            post.Id,
            viewer?.ActorIri,
            cancellationToken).ConfigureAwait(false);
        string noteId = await MapNoteIdAsync(post.Id, post.CreatedAt, cancellationToken).ConfigureAwait(false);
        string authorId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            post.Account.Id,
            post.Account.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> mediaIds = await externalIds.GetOrCreateManyAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Media,
            post.Attachments.Select(item => (item.Id, item.CreatedAt)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        var media = post.Attachments.Select(item => new NoteMediaViewModel(
            mediaIds[item.Id],
            item.MediaType,
            item.Url,
            item.PreviewUrl,
            item.Description,
            item.Blurhash,
            item.Width,
            item.Height,
            post.Sensitive,
            item.Size)).ToArray();
        NotePollViewModel? poll = null;
        if (post.Poll is not null)
        {
            string pollId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Poll,
                post.Poll.Id,
                post.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            poll = new(
                pollId,
                post.Poll.ExpiresAt,
                post.Poll.Expired,
                post.Poll.Multiple,
                post.Poll.VotedByViewer,
                post.Poll.OwnVotes,
                post.Poll.Options.Select(option => new NotePollOptionViewModel(option.Title, option.VotesCount)).ToArray());
        }

        NoteViewModel? renote = post.AnnouncedPost is null
            ? null
            : await MapAsync(post.AnnouncedPost, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> visibleRecipientIds = await externalIds.GetOrCreateManyAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            (post.VisibleRecipients ?? []).Select(account => (account.Id, account.CreatedAt)).ToArray(),
            cancellationToken).ConfigureAwait(false);
        string? replyId = post.InReplyToId is null
            ? null
            : await MapNoteIdAsync(post.InReplyToId.Value, post.CreatedAt, cancellationToken).ConfigureAwait(false);
        NoteViewModel? reply = null;
        if (includeReply && post.InReplyToId is Guid replyPostId)
        {
            ClientPostView? replyPost = await query.FindPostAsync(
                replyPostId,
                viewer?.ActorIri,
                cancellationToken).ConfigureAwait(false);
            if (replyPost is not null && replyPost.Id != post.Id)
            {
                reply = await MapAsync(replyPost, cancellationToken, includeReply: false).ConfigureAwait(false);
            }
        }
        return new(
            post.Id,
            noteId,
            post.CreatedAt,
            new(
                authorId,
                post.Account.Username,
                post.Account.Acct,
                string.IsNullOrWhiteSpace(post.Account.DisplayName) ? post.Account.Username : post.Account.DisplayName,
                post.Account.AvatarUrl,
                post.Account.Bot),
            post.SourceText ?? ConvertSanitizedHtmlToText(post.SanitizedHtml),
            string.IsNullOrWhiteSpace(post.ContentWarning) ? null : post.ContentWarning,
            post.Visibility,
            replyId,
            post.RepliesCount,
            post.AnnouncesCount,
            reactionSummary.Reactions.Values.Sum(),
            reactionSummary.ViewerReaction is not null,
            reactionSummary.Reactions,
            reactionSummary.ViewerReaction,
            media,
            post.Mentions.Select(item => item.Acct).ToArray(),
            post.Hashtags.Select(item => item.Name).ToArray(),
            post.Emojis.ToDictionary(item => item.Shortcode, item => item.Url, StringComparer.Ordinal)
                .Concat(reactionSummary.CustomEmojiUrls)
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal),
            poll,
            renote,
            post.LocalOnly,
            (post.VisibleRecipients ?? [])
                .Where(account => visibleRecipientIds.ContainsKey(account.Id))
                .Select(account => visibleRecipientIds[account.Id])
                .ToArray(),
            RenoteId: renote?.Id,
            Reply: reply,
            IsMuted: post.MutedForViewer,
            RemoteUrl: post.Account.Acct.Contains('@', StringComparison.Ordinal)
                ? FirstAbsoluteHttpUrl(post.Url, post.Iri)
                : null);
    }

    private static string? FirstAbsoluteHttpUrl(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)) &&
                string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri.AbsoluteUri;
            }
        }

        return null;
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

    [GeneratedRegex("<(?:br\\s*/?|/p|/div|/li)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakElementRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlElementRegex();
}
