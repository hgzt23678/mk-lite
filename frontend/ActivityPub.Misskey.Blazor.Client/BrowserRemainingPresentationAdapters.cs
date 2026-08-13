using System.Text.Json;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed class BrowserAboutPresentationService(MisskeyBrowserApiClient api) : IAboutPresentationService
{
    public async Task<AboutStatisticsViewModel> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostAsync("/api/stats", new { }, cancellationToken).ConfigureAwait(false);
        return new(value.OptionalInt64("originalUsersCount"), value.OptionalInt64("originalNotesCount"));
    }

    public async Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
        AboutFederationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        JsonElement values = await api.PostAsync(
            "/api/federation/instances",
            new
            {
                host = query.Host,
                blocked = query.State == "blocked" ? true : (bool?)null,
                notResponding = query.State == "notResponding" ? true : (bool?)null,
                suspended = query.State == "suspended" ? true : (bool?)null,
                federating = query.State == "federating" ? true : (bool?)null,
                subscribing = query.State == "subscribing" ? true : (bool?)null,
                publishing = query.State == "publishing" ? true : (bool?)null,
                limit = Math.Clamp(query.Limit, 1, 100),
                offset = Math.Max(0, query.Offset),
                sort = query.Sort
            },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(BrowserPresentationMapper.MapFederationInstance).ToArray();
    }
}

public sealed class BrowserAdminPresentationService(MisskeyBrowserApiClient api) : IAdminPresentationService
{
    public async Task<AdminOverviewViewModel> ReadOverviewAsync(CancellationToken cancellationToken) =>
        new(await ListRelaysAsync(cancellationToken).ConfigureAwait(false),
            await ListAnnouncementsAsync(cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<AdminRelayViewModel>> ListRelaysAsync(CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync("/api/admin/relays/list", new { }, cancellationToken)
            .ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(BrowserPresentationMapper.MapRelay).ToArray();
    }

    public async Task<AdminRelayViewModel> AddRelayAsync(string inbox, CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostAsync("/api/admin/relays/add", new { inbox }, cancellationToken)
            .ConfigureAwait(false);
        return BrowserPresentationMapper.MapRelay(value);
    }

    public async Task RemoveRelayAsync(string inbox, CancellationToken cancellationToken) =>
        _ = await api.PostAsync("/api/admin/relays/remove", new { inbox }, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AdminAnnouncementViewModel>> ListAnnouncementsAsync(
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync(
            "/api/admin/announcements/list",
            new { limit = 100 },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(BrowserPresentationMapper.MapAdminAnnouncement).ToArray();
    }

    public async Task CreateAnnouncementAsync(
        string title,
        string text,
        string? imageUrl,
        CancellationToken cancellationToken) =>
        _ = await api.PostAsync(
            "/api/admin/announcements/create",
            new { title, text, imageUrl },
            cancellationToken).ConfigureAwait(false);

    public async Task DeleteAnnouncementAsync(string announcementId, CancellationToken cancellationToken) =>
        _ = await api.PostAsync(
            "/api/admin/announcements/delete",
            new { id = announcementId },
            cancellationToken).ConfigureAwait(false);

    public async Task<AdminInvitationViewModel> CreateInvitationAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostAsync("/api/admin/invite", new { }, cancellationToken).ConfigureAwait(false);
        return new(value.RequiredString("code"), value.RequiredDateTimeOffset("expiresAt"));
    }
}

public sealed class BrowserAnnouncementPagePresentationService(MisskeyBrowserApiClient api)
    : IAnnouncementPagePresentationService
{
    public async Task<IReadOnlyList<AnnouncementPageViewModel>> ReadAsync(
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync(
            "/api/announcements",
            new { untilId, limit = Math.Clamp(limit, 1, 100), withUnreads = false },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(value => new AnnouncementPageViewModel(
            value.RequiredString("id"),
            value.RequiredDateTimeOffset("createdAt"),
            value.RequiredString("title"),
            value.RequiredString("text"),
            value.OptionalString("imageUrl"),
            value.OptionalBoolean("isRead"))).ToArray();
    }

    public async Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken)
    {
        await api.PostAsync("/api/i/read-announcement", new { announcementId = id }, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}

public sealed class BrowserAutocompletePresentationService(
    MisskeyBrowserApiClient api,
    IEmojiCatalog emojiCatalog) : IAutocompletePresentationService
{
    private const int MaximumSuggestions = 30;
    private static readonly string[] MfmTags =
    [
        "tada", "confetti", "party", "ticker", "jelly", "spin", "pulse", "blink", "pop", "jump",
        "rainbow", "flip", "bounce", "shake", "rotate", "font", "fg", "bg", "border", "position",
        "small", "center", "blur", "italic", "url", "scale", "x2", "x3", "x4", "huge", "plain",
        "motion", "bordered", "sparkle"
    ];

    public async Task<IReadOnlyList<AutocompleteUserViewModel>> SearchUsersAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        JsonElement values = await api.PostAsync(
            "/api/users/search",
            new { query, limit = 10, detail = true },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(value => new AutocompleteUserViewModel(
            value.RequiredString("id"),
            value.RequiredString("username"),
            value.OptionalString("host"),
            value.OptionalString("name") ?? value.RequiredString("username"),
            value.OptionalString("avatarUrl") ?? "/static-assets/user-unknown.png")).ToArray();
    }

    public async Task<IReadOnlyList<string>> SearchHashtagsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        JsonElement values = await api.PostAsync(
            "/api/hashtags/search",
            new { query, limit = MaximumSuggestions, offset = 0 },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!).ToArray();
    }

    public IReadOnlyList<AutocompleteEmojiViewModel> SearchEmojis(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        string value = query.Trim();
        return emojiCatalog.Emojis
            .Where(emoji => emoji.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                            emoji.Keywords.Any(keyword => keyword.Contains(value, StringComparison.OrdinalIgnoreCase)))
            .Take(MaximumSuggestions)
            .Select(emoji => new AutocompleteEmojiViewModel(emoji.Value, emoji.Name, null, null, false))
            .ToArray();
    }

    public IReadOnlyList<string> SearchMfmTags(string? query) => string.IsNullOrWhiteSpace(query)
        ? MfmTags
        : MfmTags.Where(tag => tag.StartsWith(query, StringComparison.Ordinal)).ToArray();

    public void RememberEmoji(string emoji) => _ = emoji;
}

public sealed class BrowserAvatarsPresentationService(MisskeyBrowserApiClient api) : IAvatarsPresentationService
{
    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        JsonElement values = await api.PostAsync("/api/users/show", new { userIds }, cancellationToken)
            .ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(BrowserTimelinePresentationService.MapAuthor).ToArray();
    }
}

public sealed class BrowserVisibleUsersPresentationService(MisskeyBrowserApiClient api)
    : IVisibleUsersPresentationService
{
    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count > 10) throw new VisibleUsersPresentationException("VISIBILITY_USER_IDS_INVALID");
        JsonElement values = await api.PostAsync("/api/users/show", new { userIds }, cancellationToken)
            .ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(BrowserTimelinePresentationService.MapAuthor).ToArray();
    }
}

public sealed class BrowserComposerMediaService(
    MisskeyBrowserApiClient api,
    BrowserTimelinePresentationService timeline) : IComposerMediaService
{
    public async Task<ComposerMediaViewModel> UploadAsync(
        string fileName,
        string? declaredMediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostFileAsync(
            "/api/drive/files/create",
            fileName,
            declaredMediaType,
            content,
            cancellationToken).ConfigureAwait(false);
        string externalId = value.RequiredString("id");
        Guid internalId = BrowserPresentationMapper.ParseInternalGuid(externalId);
        timeline.RegisterMediaId(internalId, externalId);
        return new(
            internalId,
            value.OptionalString("name") ?? fileName,
            value.RequiredString("type"),
            value.RequiredString("url"),
            value.OptionalString("thumbnailUrl") ?? value.RequiredString("url"),
            value.OptionalBoolean("isSensitive"),
            value.OptionalString("comment"),
            value.OptionalObject("properties").OptionalInt32("width"),
            value.OptionalObject("properties").OptionalInt32("height"),
            value.OptionalInt64Value("size"));
    }
}

public sealed class BrowserHashtagTrendPresentationService(MisskeyBrowserApiClient api)
    : IHashtagTrendPresentationService
{
    public async Task<IReadOnlyList<HashtagTrendViewModel>> ReadAsync(CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync("/api/hashtags/trend", new { }, cancellationToken)
            .ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(value => new HashtagTrendViewModel(
            value.RequiredString("tag"),
            value.OptionalInt64("usersCount"),
            value.OptionalArray("chart").Where(x => x.TryGetInt64(out _)).Select(x => x.GetInt64()).ToArray()))
            .ToArray();
    }
}

public sealed class BrowserMiauthAuthorizationService(
    MisskeyBrowserApiClient api,
    ActivityPub.Misskey.Blazor.Identity.IAuthenticatedActorContext actorContext) : IMiauthAuthorizationService
{
    public async Task AuthorizeAsync(
        string username,
        string session,
        string name,
        Uri? iconUri,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        ActivityPub.Misskey.Blazor.Identity.AuthenticatedActor actor = await actorContext
            .RequireAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actor.Username, username, StringComparison.Ordinal))
        {
            throw new ActivityPub.Misskey.Blazor.Identity.FrontendAuthenticationException("AUTH_SUBJECT_MISMATCH");
        }
        _ = await api.PostAsync(
            "/api/miauth/gen-token",
            new
            {
                session,
                name,
                description = "Authorized by the Misskey v12 web client.",
                iconUrl = iconUri?.AbsoluteUri,
                permission = permissions
            },
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed class BrowserNoteDeletionPresentationService(MisskeyBrowserApiClient api)
    : INoteDeletionPresentationService
{
    public async Task DeleteAsync(
        NoteViewModel note,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(note);
        _ = await api.PostAsync(
            "/api/notes/delete",
            new { noteId = note.Id },
            cancellationToken,
            idempotencyKey).ConfigureAwait(false);
    }
}

public sealed class BrowserReactionDetailsPresentationService(
    MisskeyBrowserApiClient api,
    BrowserTimelinePresentationService timeline)
    : IReactionDetailsPresentationService
{
    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        string reaction,
        int limit,
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync(
            "/api/notes/reactions",
            new { noteId = timeline.ResolveNoteId(postId), type = reaction, limit = Math.Clamp(limit, 1, 100) },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray()
            .Select(value => value.TryGetProperty("user", out JsonElement user) ? user : value)
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(BrowserTimelinePresentationService.MapAuthor)
            .ToArray();
    }
}

public sealed class BrowserRenoteDetailsPresentationService(
    MisskeyBrowserApiClient api,
    BrowserTimelinePresentationService timeline)
    : IRenoteDetailsPresentationService
{
    public async Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        int limit,
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync(
            "/api/notes/renotes",
            new { noteId = timeline.ResolveNoteId(postId), limit = Math.Clamp(limit, 1, 100) },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray()
            .Select(value => value.TryGetProperty("user", out JsonElement user) ? user : value)
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(BrowserTimelinePresentationService.MapAuthor)
            .ToArray();
    }
}

public sealed class BrowserUserPreviewPresentationService(
    MisskeyBrowserApiClient api,
    ActivityPub.Misskey.Blazor.Identity.IAuthenticatedActorContext actorContext)
    : IUserPreviewPresentationService
{
    public async Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        object request = query.StartsWith('@')
            ? BrowserPresentationMapper.UserLookupRequest(query)
            : new { userId = query };
        JsonElement user = await api.PostAsync("/api/users/show", request, cancellationToken).ConfigureAwait(false);
        JsonElement? relationship = null;
        ActivityPub.Misskey.Blazor.Identity.AuthenticatedActor? viewer = await actorContext
            .FindAsync(cancellationToken).ConfigureAwait(false);
        if (viewer is not null)
        {
            relationship = await api.PostAsync(
                "/api/users/relation",
                new { userId = user.RequiredString("id") },
                cancellationToken).ConfigureAwait(false);
        }

        bool isSelf = viewer is not null && user.OptionalString("host") is null &&
            string.Equals(viewer.Username, user.RequiredString("username"), StringComparison.OrdinalIgnoreCase);
        return BrowserPresentationMapper.MapUserPreview(user, relationship, viewer is not null && !isSelf);
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
        JsonElement relationship = await api.PostAsync(
            follow ? "/api/following/create" : "/api/following/delete",
            new { userId = user.Id },
            cancellationToken,
            idempotencyKey).ConfigureAwait(false);
        return user with
        {
            IsFollowing = relationship.OptionalBoolean("isFollowing"),
            HasPendingFollowRequestFromYou = relationship.OptionalBoolean("hasPendingFollowRequestFromYou"),
            IsFollowed = relationship.OptionalBoolean("isFollowed")
        };
    }
}

public sealed class BrowserUserSearchPresentationService(
    MisskeyBrowserApiClient api,
    IUserPreviewPresentationService previews) : IUserSearchPresentationService
{
    public async Task<IReadOnlyList<UserPreviewViewModel>> SearchAsync(
        string query,
        string origin,
        int limit,
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync(
            "/api/users/search",
            new { query, limit = Math.Clamp(limit, 1, 100), detail = true },
            cancellationToken).ConfigureAwait(false);
        var result = new List<UserPreviewViewModel>();
        foreach (JsonElement user in values.EnumerateRequiredArray())
        {
            bool remote = user.OptionalString("host") is not null;
            if (origin == "local" && remote || origin == "remote" && !remote) continue;
            result.Add(await previews.ReadAsync(user.RequiredString("id"), cancellationToken).ConfigureAwait(false));
        }

        return result;
    }
}

public sealed class BrowserUserPagePresentationService(
    MisskeyBrowserApiClient api,
    BrowserTimelinePresentationService timeline,
    IUserPreviewPresentationService previews) : IUserPagePresentationService
{
    public async Task<UserPageViewModel> ReadAsync(
        string acct,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        UserPreviewViewModel user = await previews.ReadAsync(acct, cancellationToken).ConfigureAwait(false);
        int safeLimit = Math.Clamp(limit, 1, 100);
        JsonElement values = await api.PostAsync(
            "/api/users/notes",
            new { userId = user.Id, untilId, limit = safeLimit },
            cancellationToken).ConfigureAwait(false);
        NoteViewModel[] notes = values.EnumerateRequiredArray().Select(timeline.MapNote).ToArray();
        return new(user, new TimelinePageViewModel(notes, notes.Length == safeLimit ? notes[^1].Id : null));
    }
}

public sealed class BrowserUserFollowRelationsPresentationService(
    MisskeyBrowserApiClient api,
    IUserPreviewPresentationService previews) : IUserFollowRelationsPresentationService
{
    public async Task<UserFollowRelationsPageViewModel?> ReadAsync(
        string acct,
        bool followers,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        UserPreviewViewModel owner;
        try
        {
            owner = await previews.ReadAsync(acct, cancellationToken).ConfigureAwait(false);
        }
        catch (UserPreviewPresentationException exception) when (exception.ErrorCode == "USER_PREVIEW_NOT_FOUND")
        {
            return null;
        }

        JsonElement values = await api.PostAsync(
            followers ? "/api/users/followers" : "/api/users/following",
            new { userId = owner.Id, untilId, limit = Math.Clamp(limit, 1, 100) },
            cancellationToken).ConfigureAwait(false);
        var result = new List<UserFollowRelationListItem>();
        foreach (JsonElement relation in values.EnumerateRequiredArray())
        {
            JsonElement user = relation.GetProperty(followers ? "follower" : "followee");
            UserPreviewViewModel preview = await previews.ReadAsync(
                user.RequiredString("id"),
                cancellationToken).ConfigureAwait(false);
            result.Add(new(relation.RequiredString("id"), preview));
        }

        return new(result);
    }
}

public sealed class BrowserSettingsPresentationService(MisskeyBrowserApiClient api)
    : ISettingsPresentationService
{
    public async Task<SettingsProfileViewModel> ReadProfileAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostAsync("/api/i", new { }, cancellationToken).ConfigureAwait(false);
        return new(
            value.RequiredString("username"),
            value.OptionalString("name") ?? string.Empty,
            value.OptionalString("description") ?? string.Empty,
            value.OptionalBoolean("isLocked"),
            value.RequiredBoolean("isExplorable"),
            value.OptionalString("avatarUrl") ?? string.Empty,
            value.OptionalString("bannerUrl") ?? string.Empty);
    }

    public async Task<IReadOnlyList<SettingsApiTokenViewModel>> ReadApiTokensAsync(
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync("/api/i/apps", new { }, cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(value => new SettingsApiTokenViewModel(
            value.RequiredString("id"),
            value.RequiredString("name"),
            value.OptionalString("description"),
            value.OptionalString("iconUrl"),
            value.OptionalArray("permission").Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).ToArray(),
            value.RequiredDateTimeOffset("createdAt"),
            value.RequiredDateTimeOffset("expiresAt"),
            value.OptionalDateTimeOffset("lastUsedAt"))).ToArray();
    }

    public async Task<SettingsApiTokenIssuedViewModel> GenerateApiTokenAsync(
        string? name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostAsync(
            "/api/miauth/gen-token",
            new
            {
                session = (string?)null,
                name = string.IsNullOrWhiteSpace(name) ? "Misskey v12 web client" : name.Trim(),
                description = "Generated from the Misskey API settings page.",
                permission = permissions
            },
            cancellationToken).ConfigureAwait(false);
        return new(
            value.RequiredString("token"),
            value.RequiredString("id"),
            value.RequiredDateTimeOffset("expiresAt"));
    }

    public async Task<bool> RevokeApiTokenAsync(string externalId, CancellationToken cancellationToken)
    {
        await api.PostAsync("/api/i/revoke-token", new { tokenId = externalId }, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task UpdateProfileAsync(
        string? name,
        string? description,
        bool? isLocked,
        bool? discoverable,
        CancellationToken cancellationToken) =>
        _ = await api.PostAsync(
            "/api/i/update",
            new { name, description, isLocked, discoverable },
            cancellationToken).ConfigureAwait(false);
}

public sealed class BrowserNotificationPresentationService(
    MisskeyBrowserApiClient api,
    BrowserTimelinePresentationService timeline) : INotificationPresentationService
{
    private readonly Dictionary<Guid, string> notificationIds = [];

    public async Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
        NotificationPresentationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonElement values = await api.PostAsync(
            "/api/i/notifications",
            new
            {
                untilId = request.UntilId,
                limit = Math.Clamp(request.Limit, 1, 100),
                unreadOnly = request.UnreadOnly,
                markAsRead = false,
                includeTypes = request.IncludeTypes?.Select(BrowserPresentationMapper.NotificationTypeName).ToArray(),
                excludeTypes = request.ExcludeTypes?.Select(BrowserPresentationMapper.NotificationTypeName).ToArray()
            },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(Map).ToArray();
    }

    public async Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        if (notificationIds.TryGetValue(notificationId, out string? externalId))
        {
            IReadOnlyList<NotificationViewModel> values = await ReadAsync(
                new(null, 100),
                cancellationToken).ConfigureAwait(false);
            return values.FirstOrDefault(value => value.Id == externalId);
        }

        return null;
    }

    public async Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        if (!notificationIds.TryGetValue(notificationId, out string? externalId)) return false;
        try
        {
            await api.PostAsync(
                "/api/notifications/read",
                new { notificationId = externalId },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MisskeyBrowserApiException exception) when (exception.Code == "NO_SUCH_NOTIFICATION")
        {
            return false;
        }
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        int unread = 0;
        string? untilId = null;
        while (true)
        {
            IReadOnlyList<NotificationViewModel> page = await ReadAsync(
                new(untilId, 100, UnreadOnly: true),
                cancellationToken).ConfigureAwait(false);
            unread = checked(unread + page.Count);
            if (page.Count < 100) break;
            string next = page[^1].Id;
            if (string.Equals(next, untilId, StringComparison.Ordinal)) break;
            untilId = next;
        }
        await api.PostAsync("/api/notifications/mark-all-as-read", new { }, cancellationToken)
            .ConfigureAwait(false);
        return unread;
    }

    internal NotificationViewModel Map(JsonElement value)
    {
        string id = value.RequiredString("id");
        Guid internalId = BrowserTimelinePresentationService.StableGuid(id);
        notificationIds[internalId] = id;
        MisskeyNotificationType type = BrowserPresentationMapper.ParseNotificationType(
            value.RequiredString("type"));
        NoteAuthorViewModel? user = value.TryGetProperty("user", out JsonElement userElement) &&
                                        userElement.ValueKind == JsonValueKind.Object
            ? BrowserTimelinePresentationService.MapAuthor(userElement)
            : null;
        NoteViewModel? fullNote = value.TryGetProperty("note", out JsonElement noteElement) &&
                                  noteElement.ValueKind == JsonValueKind.Object
            ? timeline.MapNote(noteElement)
            : null;
        NotificationNoteViewModel? note = fullNote is null ? null : BrowserPresentationMapper.MapNotificationNote(fullNote);
        UserPreviewViewModel? followUser = user is null ? null : new UserPreviewViewModel(
            BrowserPresentationMapper.ParseInternalGuid(user.Id),
            user.Id,
            user,
            string.Empty,
            null,
            0,
            0,
            0,
            false,
            true,
            false,
            false,
            false);
        return new(
            internalId,
            id,
            value.RequiredDateTimeOffset("createdAt"),
            type,
            value.OptionalBoolean("isRead"),
            user,
            note,
            value.OptionalString("reaction"),
            followUser,
            value.OptionalString("header"),
            value.OptionalString("body"),
            value.OptionalString("iconUrl"),
            value.OptionalString("blockedReason"),
            fullNote);
    }
}

internal static class BrowserPresentationMapper
{
    public static FederationInstanceViewModel MapFederationInstance(JsonElement value) => new(
        value.RequiredString("id"),
        value.RequiredString("host"),
        value.OptionalString("iconUrl"),
        value.OptionalBoolean("isNotResponding"),
        value.OptionalBoolean("isBlocked"),
        value.OptionalBoolean("isSuspended"),
        value.OptionalString("softwareName"),
        value.OptionalString("softwareVersion"),
        value.OptionalString("name"),
        value.OptionalDateTimeOffset("caughtAt"),
        value.OptionalInt64("usersCount"),
        value.OptionalInt64("notesCount"),
        value.OptionalInt64("followingCount"),
        value.OptionalInt64("followersCount"),
        value.OptionalDateTimeOffset("latestRequestSentAt"),
        value.OptionalDateTimeOffset("lastCommunicatedAt"));

    public static AdminRelayViewModel MapRelay(JsonElement value) => new(
        value.RequiredString("id"),
        value.RequiredString("inbox"),
        value.RequiredString("status"));

    public static AdminAnnouncementViewModel MapAdminAnnouncement(JsonElement value) => new(
        value.RequiredString("id"),
        value.RequiredDateTimeOffset("createdAt"),
        value.RequiredString("title"),
        value.RequiredString("text"),
        value.OptionalString("imageUrl"),
        value.OptionalInt64("reads"));

    public static object UserLookupRequest(string acct)
    {
        string normalized = acct.Trim().TrimStart('@');
        int separator = normalized.IndexOf('@', StringComparison.Ordinal);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["username"] = separator < 0 ? normalized : normalized[..separator],
            ["host"] = separator < 0 ? null : normalized[(separator + 1)..]
        };
    }

    public static UserPreviewViewModel MapUserPreview(
        JsonElement user,
        JsonElement? relationship,
        bool canFollow)
    {
        NoteAuthorViewModel author = BrowserTimelinePresentationService.MapAuthor(user);
        JsonElement relation = relationship ?? default;
        return new(
            ParseInternalGuid(author.Id),
            author.Id,
            author,
            user.OptionalString("description") ?? string.Empty,
            user.OptionalString("bannerUrl"),
            user.OptionalInt64("notesCount"),
            user.OptionalInt64("followingCount"),
            user.OptionalInt64("followersCount"),
            user.OptionalBoolean("isLocked"),
            canFollow,
            relation.OptionalBoolean("isFollowing"),
            relation.OptionalBoolean("hasPendingFollowRequestFromYou"),
            relation.OptionalBoolean("isFollowed"),
            user.OptionalBoolean("isSilenced"),
            user.OptionalBoolean("isSuspended"));
    }

    public static Guid ParseInternalGuid(string value) => Guid.TryParse(value, out Guid result)
        ? result
        : BrowserTimelinePresentationService.StableGuid(value);

    public static string NotificationTypeName(MisskeyNotificationType value) => value switch
    {
        MisskeyNotificationType.Follow => "follow",
        MisskeyNotificationType.FollowRequestAccepted => "followRequestAccepted",
        MisskeyNotificationType.ReceiveFollowRequest => "receiveFollowRequest",
        MisskeyNotificationType.GroupInvited => "groupInvited",
        MisskeyNotificationType.Renote => "renote",
        MisskeyNotificationType.Reply => "reply",
        MisskeyNotificationType.Mention => "mention",
        MisskeyNotificationType.Quote => "quote",
        MisskeyNotificationType.PollVote => "pollVote",
        MisskeyNotificationType.PollEnded => "pollEnded",
        MisskeyNotificationType.Reaction => "reaction",
        MisskeyNotificationType.App => "app",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static MisskeyNotificationType ParseNotificationType(string value) => value switch
    {
        "follow" => MisskeyNotificationType.Follow,
        "followRequestAccepted" => MisskeyNotificationType.FollowRequestAccepted,
        "receiveFollowRequest" => MisskeyNotificationType.ReceiveFollowRequest,
        "groupInvited" => MisskeyNotificationType.GroupInvited,
        "renote" => MisskeyNotificationType.Renote,
        "reply" => MisskeyNotificationType.Reply,
        "mention" => MisskeyNotificationType.Mention,
        "quote" => MisskeyNotificationType.Quote,
        "pollVote" => MisskeyNotificationType.PollVote,
        "pollEnded" => MisskeyNotificationType.PollEnded,
        "reaction" => MisskeyNotificationType.Reaction,
        _ => MisskeyNotificationType.App
    };

    public static NotificationNoteViewModel MapNotificationNote(NoteViewModel value) => new(
        value.InternalId,
        value.Id,
        value.CreatedAt,
        value.Author,
        value.Text,
        value.ContentWarning,
        value.ReplyId is not null,
        value.Media.Count,
        value.Poll is not null,
        value.Emojis,
        value.Renote is null ? null : MapNotificationNote(value.Renote));
}
