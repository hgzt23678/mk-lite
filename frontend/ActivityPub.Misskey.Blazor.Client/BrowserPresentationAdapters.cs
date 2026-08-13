using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Client;

public sealed class MisskeyBrowserApiClient(HttpClient httpClient)
{
    private const int MaximumResponseBytes = 8_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64
    };

    public async Task<JsonElement> PostAsync(
        string path,
        object body,
        CancellationToken cancellationToken,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string code = await ReadSafeErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ActivityPub.Misskey.Blazor.Identity.FrontendAuthenticationException(code);
            }

            throw new MisskeyBrowserApiException(code, (int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new MisskeyBrowserApiException("RESPONSE_TOO_LARGE", 0);
        }

        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            new JsonDocumentOptions { MaxDepth = 64 },
            cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> PostFileAsync(
        string path,
        string fileName,
        string? mediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        using var file = new StreamContent(content);
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(mediaType, out var parsedMediaType))
        {
            file.Headers.ContentType = parsedMediaType;
        }

        using var multipart = new MultipartFormDataContent();
        multipart.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = multipart };
        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string code = await ReadSafeErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ActivityPub.Misskey.Blazor.Identity.FrontendAuthenticationException(code);
            }

            throw new MisskeyBrowserApiException(code, (int)response.StatusCode);
        }

        await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            new JsonDocumentOptions { MaxDepth = 64 },
            cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    private static async Task<string> ReadSafeErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 65_536)
        {
            return "MISSKEY_API_ERROR";
        }

        try
        {
            await response.Content.LoadIntoBufferAsync(65_536, cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                new JsonDocumentOptions { MaxDepth = 8 },
                cancellationToken).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                error.TryGetProperty("code", out JsonElement code) &&
                code.ValueKind == JsonValueKind.String &&
                code.GetString() is { Length: > 0 and <= 128 } value &&
                !value.Any(char.IsControl))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }

        return "MISSKEY_API_ERROR";
    }
}

public sealed class MisskeyBrowserApiException(string code, int statusCode) : Exception(code)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}

public sealed class BrowserInstancePresentationService(MisskeyBrowserApiClient api) : IInstancePresentationService
{
    public async Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostAsync("/api/meta", new { }, cancellationToken).ConfigureAwait(false);
        return new InstanceSummaryViewModel(
            value.RequiredString("name"),
            value.OptionalString("description") ?? string.Empty,
            value.RequiredString("version"),
            value.RequiredString("iconUrl"),
            value.OptionalString("backgroundImageUrl"),
            value.OptionalString("logoImageUrl"),
            value.OptionalBoolean("disableRegistration"),
            value.OptionalBoolean("emailRequiredForSignup"),
            value.OptionalBoolean("enableEmail"),
            value.OptionalString("tosUrl"),
            value.OptionalBoolean("enableHcaptcha"),
            value.OptionalString("hcaptchaSiteKey"),
            value.OptionalBoolean("enableRecaptcha"),
            value.OptionalString("recaptchaSiteKey"),
            value.OptionalBoolean("enableTurnstile"),
            value.OptionalString("turnstileSiteKey"),
            value.OptionalString("turnstileAction"),
            value.OptionalString("turnstileCdata"),
            value.OptionalString("maintainerName"),
            value.OptionalString("maintainerEmail"),
            value.OptionalBoolean("requireSetup"));
    }

    public async Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync(
            "/api/federation/instances",
            new { limit = 20, offset = 0, sort = "+pubSub" },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(value => new FederationInstanceViewModel(
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
            value.OptionalDateTimeOffset("lastCommunicatedAt"))).ToArray();
    }
}

public sealed class BrowserAnnouncementPresentationService(MisskeyBrowserApiClient api) : IAnnouncementPresentationService
{
    public async Task<IReadOnlyList<VisitorAnnouncementViewModel>> ReadPublicAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        JsonElement values = await api.PostAsync(
            "/api/announcements",
            new { limit = Math.Clamp(limit, 1, 100), withUnreads = false },
            cancellationToken).ConfigureAwait(false);
        return values.EnumerateRequiredArray().Select(value => new VisitorAnnouncementViewModel(
            value.RequiredString("id"),
            value.RequiredString("title"),
            value.RequiredString("text"),
            value.OptionalString("imageUrl"))).ToArray();
    }
}

public sealed class BrowserCurrentAccountPresentationService(MisskeyBrowserApiClient api)
    : ICurrentAccountPresentationService
{
    public async Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken)
    {
        JsonElement value = await GetDocumentAsync(cancellationToken).ConfigureAwait(false);
        return BrowserTimelinePresentationService.MapAuthor(value);
    }

    internal Task<JsonElement> GetDocumentAsync(CancellationToken cancellationToken) =>
        api.PostAsync("/api/i", new { }, cancellationToken);
}

public sealed class BrowserTimelinePresentationService(MisskeyBrowserApiClient api)
    : ITimelinePresentationService, INotePagePresentationService
{
    private readonly Dictionary<Guid, string> noteIds = [];
    private readonly Dictionary<Guid, string> mediaIds = [];

    public async Task<TimelinePageViewModel> ReadAsync(
        TimelineKind kind,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        string endpoint = kind switch
        {
            TimelineKind.Home => "/api/notes/timeline",
            TimelineKind.Local => "/api/notes/local-timeline",
            TimelineKind.Global => "/api/notes/global-timeline",
            TimelineKind.Hybrid => "/api/notes/hybrid-timeline",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        int safeLimit = Math.Clamp(limit, 1, 40);
        JsonElement values = await api.PostAsync(
            endpoint,
            new { untilId = beforeId, limit = safeLimit },
            cancellationToken).ConfigureAwait(false);
        NoteViewModel[] notes = values.EnumerateRequiredArray().Select(MapNote).ToArray();
        return new TimelinePageViewModel(
            notes,
            notes.Length == safeLimit ? notes[^1].Id : null);
    }

    public async Task<NoteViewModel> CreateAsync(
        NoteDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        JsonElement value = await api.PostAsync(
            "/api/notes/create",
            new
            {
                text = draft.Text,
                visibility = ToWireVisibility(draft.Visibility),
                visibleUserIds = Array.Empty<string>(),
                cw = draft.ContentWarning,
                localOnly = false,
                fileIds = draft.MediaIds.Select(ResolveMediaId).ToArray(),
                replyId = ResolveInternalId(draft.ReplyToId),
                renoteId = ResolveInternalId(draft.QuoteTargetId),
                channelId = (string?)null,
                poll = draft.Poll is null ? null : new
                {
                    choices = draft.Poll.Choices,
                    multiple = draft.Poll.Multiple,
                    expiresAt = draft.Poll.ExpiresAt?.ToUnixTimeMilliseconds(),
                    expiredAfter = (long?)null
                }
            },
            cancellationToken,
            idempotencyKey).ConfigureAwait(false);
        return MapNote(value.GetProperty("createdNote"));
    }

    public async Task<NoteViewModel> RenoteAsync(
        string noteId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        JsonElement value = await api.PostAsync(
            "/api/notes/create",
            new { text = (string?)null, visibility = "public", renoteId = noteId, localOnly = false },
            cancellationToken,
            idempotencyKey).ConfigureAwait(false);
        return MapNote(value.GetProperty("createdNote"));
    }

    public async Task<NoteViewModel> ReactAsync(
        string noteId,
        string reaction,
        bool remove,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await api.PostAsync(
            remove ? "/api/notes/reactions/delete" : "/api/notes/reactions/create",
            new { noteId, reaction = remove ? null : reaction },
            cancellationToken,
            idempotencyKey).ConfigureAwait(false);
        return await FindRequiredAsync(noteId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteViewModel> VotePollAsync(
        string noteId,
        int choiceIndex,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await api.PostAsync(
            "/api/notes/polls/vote",
            new { noteId, choice = choiceIndex },
            cancellationToken,
            idempotencyKey).ConfigureAwait(false);
        return await FindRequiredAsync(noteId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NoteViewModel?> FindForStreamAsync(
        Guid id,
        TimelineKind kind,
        CancellationToken cancellationToken)
    {
        _ = kind;
        return noteIds.TryGetValue(id, out string? externalId)
            ? await FindAsync(externalId, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public Task<string> MapNoteIdAsync(Guid id, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        _ = occurredAt;
        cancellationToken.ThrowIfCancellationRequested();
        return noteIds.TryGetValue(id, out string? externalId)
            ? Task.FromResult(externalId)
            : throw new TimelineCursorException("NOTE_ID_NOT_AVAILABLE_IN_BROWSER_SESSION");
    }

    public async Task<NoteViewModel?> FindAsync(string noteId, CancellationToken cancellationToken)
    {
        try
        {
            JsonElement value = await api.PostAsync(
                "/api/notes/show",
                new { noteId },
                cancellationToken).ConfigureAwait(false);
            return MapNote(value);
        }
        catch (MisskeyBrowserApiException exception) when (exception.Code == "NO_SUCH_NOTE")
        {
            return null;
        }
    }

    private async Task<NoteViewModel> FindRequiredAsync(string noteId, CancellationToken cancellationToken) =>
        await FindAsync(noteId, cancellationToken).ConfigureAwait(false)
        ?? throw new MisskeyBrowserApiException("NO_SUCH_NOTE", 404);

    internal NoteViewModel MapNote(JsonElement value)
    {
        string id = value.RequiredString("id");
        Guid internalId = StableGuid(id);
        noteIds[internalId] = id;
        NoteViewModel? renote = value.TryGetProperty("renote", out JsonElement renoteElement) &&
                                  renoteElement.ValueKind == JsonValueKind.Object
            ? MapNote(renoteElement)
            : null;
        JsonElement reactions = value.OptionalObject("reactions");
        JsonElement emojis = value.OptionalObject("emojis");
        string? myReaction = value.OptionalString("myReaction");
        IReadOnlyDictionary<string, long> reactionValues = reactions.ValueKind == JsonValueKind.Object
            ? reactions.EnumerateObject().Where(item => item.Value.TryGetInt64(out _))
                .ToDictionary(item => item.Name, item => item.Value.GetInt64(), StringComparer.Ordinal)
            : new Dictionary<string, long>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> emojiValues = emojis.ValueKind == JsonValueKind.Object
            ? emojis.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        NoteMediaViewModel[] media = value.OptionalArray("files").Select(file => new NoteMediaViewModel(
            file.RequiredString("id"),
            file.RequiredString("type"),
            file.RequiredString("url"),
            file.OptionalString("thumbnailUrl") ?? file.RequiredString("url"),
            file.OptionalString("comment"),
            file.OptionalString("blurhash"),
            file.OptionalObject("properties").OptionalInt32("width"),
            file.OptionalObject("properties").OptionalInt32("height"),
            file.OptionalBoolean("isSensitive"),
            file.OptionalInt64Value("size"))).ToArray();
        NotePollViewModel? poll = MapPoll(value);
        return new NoteViewModel(
            internalId,
            id,
            value.RequiredDateTimeOffset("createdAt"),
            MapAuthor(value.GetProperty("user")),
            value.OptionalString("text") ?? string.Empty,
            value.OptionalString("cw"),
            FromWireVisibility(value.OptionalString("visibility")),
            value.OptionalString("replyId"),
            value.OptionalInt64("repliesCount"),
            value.OptionalInt64("renoteCount"),
            reactionValues.Values.Sum(),
            myReaction is not null,
            reactionValues,
            myReaction,
            media,
            [],
            [],
            emojiValues,
            poll,
            renote,
            value.OptionalBoolean("localOnly"),
            value.OptionalArray("visibleUserIds").Select(item => item.GetString() ?? string.Empty)
                .Where(item => item.Length > 0).ToArray(),
            false,
            null,
            value.OptionalString("renoteId"),
            null,
            false,
            false,
            value.OptionalString("url") ?? value.OptionalString("uri"));
    }

    internal NoteViewModel MapStreamNote(JsonElement value) => MapNote(value);

    internal string ResolveNoteId(Guid id) => noteIds.TryGetValue(id, out string? externalId)
        ? externalId
        : throw new MisskeyBrowserApiException("NO_SUCH_NOTE", 404);

    internal void RegisterMediaId(Guid internalId, string externalId) => mediaIds[internalId] = externalId;

    internal static NoteAuthorViewModel MapAuthor(JsonElement value)
    {
        string username = value.RequiredString("username");
        string? host = value.OptionalString("host");
        string acct = host is null ? username : $"{username}@{host}";
        return new NoteAuthorViewModel(
            value.RequiredString("id"),
            username,
            acct,
            value.OptionalString("name") ?? username,
            value.OptionalString("avatarUrl") ?? "/static-assets/user-unknown.png",
            value.OptionalBoolean("isBot"),
            value.OptionalBoolean("isCat"),
            value.OptionalString("avatarBlurhash"),
            value.OptionalString("onlineStatus") ?? "unknown",
            value.OptionalObject("emojis").ValueKind == JsonValueKind.Object
                ? value.GetProperty("emojis").EnumerateObject()
                    .Where(item => item.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static NotePollViewModel? MapPoll(JsonElement note)
    {
        if (!note.TryGetProperty("poll", out JsonElement poll) || poll.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        NotePollOptionViewModel[] choices = poll.OptionalArray("choices").Select(choice =>
            new NotePollOptionViewModel(
                choice.RequiredString("text"),
                choice.OptionalInt64("votes"))).ToArray();
        int[] ownVotes = poll.OptionalArray("choices")
            .Select((choice, index) => (choice, index))
            .Where(item => item.choice.OptionalBoolean("isVoted"))
            .Select(item => item.index)
            .ToArray();
        DateTimeOffset? expiresAt = poll.OptionalDateTimeOffset("expiresAt");
        return new NotePollViewModel(
            poll.RequiredString("id"),
            expiresAt,
            expiresAt <= DateTimeOffset.UtcNow,
            poll.OptionalBoolean("multiple"),
            ownVotes.Length > 0,
            ownVotes,
            choices);
    }

    private string? ResolveInternalId(Guid? id) =>
        id is Guid value && noteIds.TryGetValue(value, out string? externalId) ? externalId : null;

    private string ResolveMediaId(Guid id) => mediaIds.TryGetValue(id, out string? externalId)
        ? externalId
        : throw new MisskeyBrowserApiException("NO_SUCH_FILE", 404);

    private static string ToWireVisibility(Visibility value) => value switch
    {
        Visibility.Unlisted => "home",
        Visibility.FollowersOnly => "followers",
        Visibility.MentionedOnly => "specified",
        _ => "public"
    };

    private static Visibility FromWireVisibility(string? value) => value switch
    {
        "home" => Visibility.Unlisted,
        "followers" => Visibility.FollowersOnly,
        "specified" => Visibility.MentionedOnly,
        _ => Visibility.Public
    };

    internal static Guid StableGuid(string value)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(digest.AsSpan(0, 16));
    }
}

internal static class MisskeyJsonExtensions
{
    public static string RequiredString(this JsonElement value, string property) =>
        value.TryGetProperty(property, out JsonElement item) && item.ValueKind == JsonValueKind.String &&
        item.GetString() is { Length: > 0 } result
            ? result
            : throw new JsonException($"Required Misskey property {property} is missing.");

    public static string? OptionalString(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    public static bool OptionalBoolean(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind is JsonValueKind.True or JsonValueKind.False && item.GetBoolean();

    public static bool RequiredBoolean(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? item.GetBoolean()
            : throw new JsonException($"Required Misskey property {property} is missing.");

    public static int? OptionalInt32(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.TryGetInt32(out int result) ? result : null;

    public static long OptionalInt64(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.TryGetInt64(out long result) ? result : 0;

    public static long? OptionalInt64Value(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.TryGetInt64(out long result) ? result : null;

    public static DateTimeOffset RequiredDateTimeOffset(this JsonElement value, string property) =>
        value.OptionalDateTimeOffset(property)
        ?? throw new JsonException($"Required Misskey timestamp {property} is missing.");

    public static DateTimeOffset? OptionalDateTimeOffset(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind == JsonValueKind.String && item.TryGetDateTimeOffset(out DateTimeOffset result)
            ? result
            : null;

    public static JsonElement OptionalObject(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind == JsonValueKind.Object ? item : default;

    public static IEnumerable<JsonElement> OptionalArray(this JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement item) &&
        item.ValueKind == JsonValueKind.Array ? item.EnumerateArray() : [];

    public static IEnumerable<JsonElement> EnumerateRequiredArray(this JsonElement value) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : throw new JsonException("The Misskey API response must be an array.");
}
