using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.Security;
using Microsoft.AspNetCore.Components;

namespace ActivityPub.Misskey.Blazor.Overlays;

public interface IMisskeyOverlayService
{
    event Action? Changed;

    IReadOnlyList<MisskeyOverlayEntry> Entries { get; }

    IReadOnlyList<MisskeyContextMenuEntry> ContextMenus { get; }

    IReadOnlyList<MisskeyVisibilityPickerEntry> VisibilityPickers { get; }

    IReadOnlyList<MisskeyEmojiPickerEntry> EmojiPickers { get; }

    IReadOnlyList<MisskeyAutocompleteEntry> Autocompletes { get; }

    IReadOnlyList<MisskeyUsersTooltipEntry> UserTooltips { get; }

    IReadOnlyList<MisskeyReactionTooltipEntry> ReactionTooltips { get; }

    IReadOnlyList<MisskeySimpleReactionTooltipEntry> SimpleReactionTooltips { get; }

    IReadOnlyList<MisskeyUserPreviewEntry> UserPreviews { get; }

    Guid ShowPopupMenu(
        ElementReference source,
        IReadOnlyList<MisskeyMenuItem> items,
        bool openedViaKeyboard = false,
        bool matchSourceWidth = false,
        string? align = null,
        double? width = null,
        Func<Task>? closed = null);

    Guid ShowContextMenu(
        double x,
        double y,
        IReadOnlyList<MisskeyMenuItem> items,
        Func<Task>? closed = null);

    Guid ShowLaunchPad(
        ElementReference? source,
        IReadOnlyList<MisskeyMenuItem> items,
        Func<Task>? closed = null);

    Guid ShowPostForm(MisskeyPostFormOptions? options = null);

    Guid ShowSignIn(string returnUrl = "/", string? errorCode = null);

    Guid ShowSignUp(string returnUrl = "/", string? errorCode = null);

    Guid ShowForgotPassword();

    Guid ShowAlert(MisskeyAlertOptions options);

    Guid ShowDialog(MisskeyDialogOptions options);

    Guid ShowFormDialog(MisskeyFormDialogOptions options);

    Guid ShowImageViewer(NoteMediaViewModel image);

    Guid ShowVisibilityPicker(
        ElementReference source,
        Visibility currentVisibility,
        bool currentLocalOnly,
        Func<Visibility, bool, Task> changed);

    Guid ShowEmojiPicker(
        ElementReference source,
        Func<string, Task> chosen,
        bool asReactionPicker = false,
        IReadOnlyList<EmojiPickerCustomEmoji>? customEmojis = null);

    Guid ShowAutocomplete(
        string type,
        string? query,
        double x,
        double y,
        ElementReference textarea,
        Func<MisskeyAutocompleteChoice, Task> chosen);

    void UpdateAutocomplete(Guid id, string? query, double x, double y);

    Guid ShowUsersTooltip(ElementReference source, IReadOnlyList<string> userIds);

    Guid ShowUsersTooltip(
        ElementReference source,
        IReadOnlyList<NoteAuthorViewModel> users,
        long count);

    bool SetUsersTooltipShowing(Guid id, bool showing);

    Guid ShowReactionTooltip(
        ElementReference source,
        string reaction,
        IReadOnlyList<EmojiPickerCustomEmoji> emojis,
        IReadOnlyList<NoteAuthorViewModel> users,
        long count);

    bool SetReactionTooltipShowing(Guid id, bool showing);

    Guid ShowSimpleReactionTooltip(
        ElementReference source,
        string reaction,
        IReadOnlyList<EmojiPickerCustomEmoji> emojis);

    bool SetSimpleReactionTooltipShowing(Guid id, bool showing);

    Guid ShowUserPreview(string hostId, string sourceId, string query, long generation);

    bool HideUserPreview(string hostId, string sourceId, long generation);

    void RegisterCloseHandler(Guid id, Func<Task> closeHandler);

    void UnregisterCloseHandler(Guid id);

    Task<bool> RequestCloseTopAsync();

    void Close(Guid id);
}

public sealed record MisskeyOverlayEntry(
    Guid Id,
    MisskeyOverlayKind Kind,
    ElementReference Source,
    IReadOnlyList<MisskeyMenuItem> MenuItems,
    bool OpenedViaKeyboard)
{
    public MisskeyPostFormOptions? PostForm { get; init; }
    public MisskeyAuthenticationDialogOptions? Authentication { get; init; }
    public MisskeyAlertOptions? Alert { get; init; }
    public MisskeyDialogOptions? Dialog { get; init; }
    public MisskeyFormDialogOptions? FormDialog { get; init; }
    public MisskeyImageViewerOptions? ImageViewer { get; init; }
    public MisskeyLaunchPadOptions? LaunchPad { get; init; }
    public bool MatchSourceWidth { get; init; }
    public string? PopupAlign { get; init; }
    public double? PopupWidth { get; init; }
    public Func<Task>? PopupClosed { get; init; }
}

public sealed record MisskeyContextMenuEntry(
    Guid Id,
    double X,
    double Y,
    IReadOnlyList<MisskeyMenuItem> Items,
    Func<Task>? Closed = null);

public sealed record MisskeyPostFormOptions(
    string? InitialText = null,
    bool Instant = false,
    NoteViewModel? Renote = null,
    NoteViewModel? Reply = null);

public sealed record MisskeyAuthenticationDialogOptions(string ReturnUrl, string? ErrorCode = null);

public sealed record MisskeyAlertOptions(
    string Type,
    string? Title,
    string? Text,
    string AccessibleLabel,
    string AcknowledgementLabel);

public sealed record MisskeyDialogOptions(
    string Type,
    string Title,
    string? Text = null,
    MisskeyDialogInput? Input = null,
    MisskeyDialogSelect? Select = null,
    string? Icon = null,
    IReadOnlyList<MisskeyDialogAction>? Actions = null,
    bool ShowOkButton = true,
    bool ShowCancelButton = false,
    bool CancelableByBgClick = true,
    string? AccessibleLabel = null,
    Func<MisskeyDialogResult, Task>? Done = null);

public sealed record MisskeyFormDialogOptions(
    string Title,
    IReadOnlyList<MisskeyFormDialogItem> Form,
    Func<MisskeyFormDialogResult, Task>? Done = null);

public sealed record MisskeyImageViewerOptions(NoteMediaViewModel Image);

public sealed record MisskeyLaunchPadOptions(
    ElementReference? Source,
    IReadOnlyList<MisskeyMenuItem> Items,
    Func<Task>? Closed = null);

public sealed record MisskeyVisibilityPickerEntry(
    Guid Id,
    ElementReference Source,
    Visibility CurrentVisibility,
    bool CurrentLocalOnly,
    Func<Visibility, bool, Task> Changed);

public sealed record MisskeyAutocompleteEntry(
    Guid Id,
    string Type,
    string? Query,
    double X,
    double Y,
    ElementReference Textarea,
    Func<MisskeyAutocompleteChoice, Task> Chosen);

public sealed record MisskeyAutocompleteChoice(string Type, string Value);

public sealed record MisskeyEmojiPickerEntry(
    Guid Id,
    ElementReference Source,
    Func<string, Task> Chosen,
    bool AsReactionPicker,
    IReadOnlyList<EmojiPickerCustomEmoji> CustomEmojis);

public sealed record MisskeyUsersTooltipEntry(
    Guid Id,
    ElementReference Source,
    IReadOnlyList<string> UserIds,
    IReadOnlyList<NoteAuthorViewModel>? Users,
    long Count,
    bool Showing);

public sealed record MisskeyReactionTooltipEntry(
    Guid Id,
    ElementReference Source,
    string Reaction,
    IReadOnlyList<EmojiPickerCustomEmoji> Emojis,
    IReadOnlyList<NoteAuthorViewModel> Users,
    long Count,
    bool Showing);

public sealed record MisskeySimpleReactionTooltipEntry(
    Guid Id,
    ElementReference Source,
    string Reaction,
    IReadOnlyList<EmojiPickerCustomEmoji> Emojis,
    bool Showing);

public sealed record MisskeyUserPreviewEntry(
    Guid Id,
    string HostId,
    string SourceId,
    string Query,
    long Generation,
    bool Showing);

public enum MisskeyOverlayKind
{
    PopupMenu,
    PostForm,
    SignIn,
    SignUp,
    ForgotPassword,
    Alert,
    Dialog,
    FormDialog,
    ImageViewer,
    LaunchPad
}

public sealed class MisskeyOverlayService : IMisskeyOverlayService
{
    private readonly List<MisskeyOverlayEntry> entries = [];
    private readonly List<MisskeyContextMenuEntry> contextMenus = [];
    private readonly List<MisskeyVisibilityPickerEntry> visibilityPickers = [];
    private readonly List<MisskeyEmojiPickerEntry> emojiPickers = [];
    private readonly List<MisskeyAutocompleteEntry> autocompletes = [];
    private readonly List<MisskeyUsersTooltipEntry> userTooltips = [];
    private readonly List<MisskeyReactionTooltipEntry> reactionTooltips = [];
    private readonly List<MisskeySimpleReactionTooltipEntry> simpleReactionTooltips = [];
    private readonly List<MisskeyUserPreviewEntry> userPreviews = [];
    private readonly List<Guid> overlayOrder = [];
    private readonly Dictionary<Guid, Func<Task>> closeHandlers = [];
    private readonly Dictionary<string, long> userPreviewGenerations = new(StringComparer.Ordinal);

    public event Action? Changed;

    public IReadOnlyList<MisskeyOverlayEntry> Entries => entries;

    public IReadOnlyList<MisskeyContextMenuEntry> ContextMenus => contextMenus;

    public IReadOnlyList<MisskeyVisibilityPickerEntry> VisibilityPickers => visibilityPickers;

    public IReadOnlyList<MisskeyEmojiPickerEntry> EmojiPickers => emojiPickers;

    public IReadOnlyList<MisskeyAutocompleteEntry> Autocompletes => autocompletes;

    public IReadOnlyList<MisskeyUsersTooltipEntry> UserTooltips => userTooltips;

    public IReadOnlyList<MisskeyReactionTooltipEntry> ReactionTooltips => reactionTooltips;

    public IReadOnlyList<MisskeySimpleReactionTooltipEntry> SimpleReactionTooltips => simpleReactionTooltips;

    public IReadOnlyList<MisskeyUserPreviewEntry> UserPreviews => userPreviews;

    public Guid ShowPopupMenu(
        ElementReference source,
        IReadOnlyList<MisskeyMenuItem> items,
        bool openedViaKeyboard = false,
        bool matchSourceWidth = false,
        string? align = null,
        double? width = null,
        Func<Task>? closed = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(
            id,
            MisskeyOverlayKind.PopupMenu,
            source,
            items,
            openedViaKeyboard)
        {
            MatchSourceWidth = matchSourceWidth,
            PopupAlign = align,
            PopupWidth = width,
            PopupClosed = closed
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowContextMenu(
        double x,
        double y,
        IReadOnlyList<MisskeyMenuItem> items,
        Func<Task>? closed = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "The context menu coordinates are invalid.");
        }

        Guid id = Guid.NewGuid();
        contextMenus.Add(new MisskeyContextMenuEntry(id, x, y, items.ToArray(), closed));
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowLaunchPad(
        ElementReference? source,
        IReadOnlyList<MisskeyMenuItem> items,
        Func<Task>? closed = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0 || items.Any(item =>
                item.Kind is not (MisskeyMenuItemKind.Action or MisskeyMenuItemKind.Link) ||
                string.IsNullOrWhiteSpace(item.Text) ||
                string.IsNullOrWhiteSpace(item.Icon) ||
                item.Kind == MisskeyMenuItemKind.Action && item.Action is null ||
                item.Kind == MisskeyMenuItemKind.Link && string.IsNullOrWhiteSpace(item.Href)))
        {
            throw new ArgumentException("The launch pad requires actionable link or button items.", nameof(items));
        }

        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(
            id,
            MisskeyOverlayKind.LaunchPad,
            default,
            [],
            OpenedViaKeyboard: false)
        {
            LaunchPad = new MisskeyLaunchPadOptions(source, items.ToArray(), closed)
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowPostForm(MisskeyPostFormOptions? options = null)
    {
        if (options?.InitialText is { Length: > 5_000 })
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Initial post text exceeds the server contract.");
        }

        // Misskey itself treats opening more than one global post form as a client bug.
        // Reuse the existing entry so repeated keyboard/pointer activation cannot duplicate a draft.
        MisskeyOverlayEntry? existing = entries.FirstOrDefault(entry => entry.Kind == MisskeyOverlayKind.PostForm);
        if (existing is not null)
        {
            return existing.Id;
        }

        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(
            id,
            MisskeyOverlayKind.PostForm,
            default,
            [],
            OpenedViaKeyboard: false)
        {
            PostForm = options
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowSignIn(string returnUrl = "/", string? errorCode = null) =>
        ShowAuthenticationDialog(MisskeyOverlayKind.SignIn, returnUrl, errorCode);

    public Guid ShowSignUp(string returnUrl = "/", string? errorCode = null) =>
        ShowAuthenticationDialog(MisskeyOverlayKind.SignUp, returnUrl, errorCode);

    public Guid ShowForgotPassword() =>
        ShowAuthenticationDialog(MisskeyOverlayKind.ForgotPassword, "/", null);

    public Guid ShowAlert(MisskeyAlertOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Type is not ("info" or "success" or "error" or "warning" or "waiting"))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The alert type is not supported.");
        }

        ValidateAlertText(options.Title, 512, nameof(options.Title));
        ValidateAlertText(options.Text, 4_096, nameof(options.Text));
        ValidateAlertText(options.AccessibleLabel, 512, nameof(options.AccessibleLabel), required: true);
        ValidateAlertText(options.AcknowledgementLabel, 512, nameof(options.AcknowledgementLabel), required: true);

        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(id, MisskeyOverlayKind.Alert, default, [], OpenedViaKeyboard: false)
        {
            Alert = options
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowDialog(MisskeyDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Type is not ("info" or "success" or "error" or "warning" or "question" or "waiting"))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The dialog type is not supported.");
        }

        ValidateAlertText(options.Title, 512, nameof(options.Title));
        ValidateAlertText(options.Text, 4_096, nameof(options.Text));
        if (options.Input is not null && options.Select is not null)
        {
            throw new ArgumentException("A dialog cannot contain both input and select controls.", nameof(options));
        }

        if (options.Actions is { Count: > 16 } ||
            options.Actions?.Any(action => string.IsNullOrWhiteSpace(action.Text) || action.Text.Length > 512) == true)
        {
            throw new ArgumentException("The dialog action list is invalid.", nameof(options));
        }

        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(id, MisskeyOverlayKind.Dialog, default, [], OpenedViaKeyboard: false)
        {
            Dialog = options
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowFormDialog(MisskeyFormDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        ArgumentNullException.ThrowIfNull(options.Form);

        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(id, MisskeyOverlayKind.FormDialog, default, [], OpenedViaKeyboard: false)
        {
            FormDialog = options
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowImageViewer(NoteMediaViewModel image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (SameOriginMediaUrl.Normalize(image.Url) is null)
        {
            throw new ArgumentException("The image viewer requires a same-origin media URL.", nameof(image));
        }

        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(id, MisskeyOverlayKind.ImageViewer, default, [], OpenedViaKeyboard: false)
        {
            ImageViewer = new MisskeyImageViewerOptions(image)
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    private Guid ShowAuthenticationDialog(MisskeyOverlayKind kind, string returnUrl, string? errorCode)
    {
        if (kind is not (MisskeyOverlayKind.SignIn or MisskeyOverlayKind.SignUp or MisskeyOverlayKind.ForgotPassword))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        MisskeyOverlayEntry? existing = entries.FirstOrDefault(entry => entry.Kind == kind);
        if (existing is not null)
        {
            return existing.Id;
        }

        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.Contains('\\'))
        {
            returnUrl = "/";
        }

        Guid id = Guid.NewGuid();
        entries.Add(new MisskeyOverlayEntry(id, kind, default, [], OpenedViaKeyboard: false)
        {
            Authentication = new MisskeyAuthenticationDialogOptions(returnUrl, errorCode)
        });
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowVisibilityPicker(
        ElementReference source,
        Visibility currentVisibility,
        bool currentLocalOnly,
        Func<Visibility, bool, Task> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Guid id = Guid.NewGuid();
        visibilityPickers.Add(new MisskeyVisibilityPickerEntry(
            id,
            source,
            currentVisibility,
            currentLocalOnly,
            changed));
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowAutocomplete(
        string type,
        string? query,
        double x,
        double y,
        ElementReference textarea,
        Func<MisskeyAutocompleteChoice, Task> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        Guid id = Guid.NewGuid();
        autocompletes.Add(new MisskeyAutocompleteEntry(id, type, query, x, y, textarea, chosen));
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public void UpdateAutocomplete(Guid id, string? query, double x, double y)
    {
        int index = autocompletes.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            return;
        }

        autocompletes[index] = autocompletes[index] with { Query = query, X = x, Y = y };
        Changed?.Invoke();
    }

    public Guid ShowEmojiPicker(
        ElementReference source,
        Func<string, Task> chosen,
        bool asReactionPicker = false,
        IReadOnlyList<EmojiPickerCustomEmoji>? customEmojis = null)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        Guid id = Guid.NewGuid();
        emojiPickers.Add(new MisskeyEmojiPickerEntry(id, source, chosen, asReactionPicker, customEmojis ?? []));
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public Guid ShowUsersTooltip(ElementReference source, IReadOnlyList<string> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        Guid id = Guid.NewGuid();
        userTooltips.Add(new MisskeyUsersTooltipEntry(
            id,
            source,
            userIds.ToArray(),
            Users: null,
            Count: userIds.Count,
            Showing: true));
        Changed?.Invoke();
        return id;
    }

    public Guid ShowUsersTooltip(
        ElementReference source,
        IReadOnlyList<NoteAuthorViewModel> users,
        long count)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Guid id = Guid.NewGuid();
        userTooltips.Add(new MisskeyUsersTooltipEntry(
            id,
            source,
            [],
            users.ToArray(),
            count,
            Showing: true));
        Changed?.Invoke();
        return id;
    }

    public bool SetUsersTooltipShowing(Guid id, bool showing)
    {
        int index = userTooltips.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            return false;
        }

        userTooltips[index] = userTooltips[index] with { Showing = showing };
        Changed?.Invoke();
        return true;
    }

    public Guid ShowReactionTooltip(
        ElementReference source,
        string reaction,
        IReadOnlyList<EmojiPickerCustomEmoji> emojis,
        IReadOnlyList<NoteAuthorViewModel> users,
        long count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reaction);
        ArgumentNullException.ThrowIfNull(emojis);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Guid id = Guid.NewGuid();
        reactionTooltips.Add(new(
            id,
            source,
            reaction,
            emojis.ToArray(),
            users.ToArray(),
            count,
            Showing: true));
        Changed?.Invoke();
        return id;
    }

    public bool SetReactionTooltipShowing(Guid id, bool showing)
    {
        int index = reactionTooltips.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            return false;
        }

        reactionTooltips[index] = reactionTooltips[index] with { Showing = showing };
        Changed?.Invoke();
        return true;
    }

    public Guid ShowSimpleReactionTooltip(
        ElementReference source,
        string reaction,
        IReadOnlyList<EmojiPickerCustomEmoji> emojis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reaction);
        ArgumentNullException.ThrowIfNull(emojis);
        Guid id = Guid.NewGuid();
        simpleReactionTooltips.Add(new(id, source, reaction, emojis.ToArray(), Showing: true));
        Changed?.Invoke();
        return id;
    }

    public bool SetSimpleReactionTooltipShowing(Guid id, bool showing)
    {
        int index = simpleReactionTooltips.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            return false;
        }

        simpleReactionTooltips[index] = simpleReactionTooltips[index] with { Showing = showing };
        Changed?.Invoke();
        return true;
    }

    public Guid ShowUserPreview(string hostId, string sourceId, string query, long generation)
    {
        ValidatePreviewToken(hostId, nameof(hostId));
        ValidatePreviewToken(sourceId, nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Length > 2_048 || query.Any(char.IsControl) || generation <= 0)
        {
            throw new ArgumentException("The user preview request is invalid.", nameof(query));
        }

        MisskeyUserPreviewEntry? existing = userPreviews.SingleOrDefault();
        if (userPreviewGenerations.TryGetValue(hostId, out long latestGeneration) && generation <= latestGeneration)
        {
            if (existing is null || existing.HostId != hostId || existing.SourceId != sourceId ||
                existing.Generation != generation)
            {
                return Guid.Empty;
            }

            if (!existing.Showing)
            {
                userPreviews[0] = existing with { Showing = true };
                Changed?.Invoke();
            }

            return existing.Id;
        }

        userPreviewGenerations[hostId] = generation;
        if (existing is not null)
        {
            userPreviews.Clear();
            RemoveOverlayState(existing.Id);
        }

        Guid id = Guid.NewGuid();
        userPreviews.Add(new(id, hostId, sourceId, query, generation, Showing: true));
        overlayOrder.Add(id);
        Changed?.Invoke();
        return id;
    }

    public bool HideUserPreview(string hostId, string sourceId, long generation)
    {
        int index = userPreviews.FindIndex(entry =>
            entry.HostId == hostId && entry.SourceId == sourceId && entry.Generation == generation);
        if (index < 0 || !userPreviews[index].Showing)
        {
            return false;
        }

        userPreviews[index] = userPreviews[index] with { Showing = false };
        Changed?.Invoke();
        return true;
    }

    public void RegisterCloseHandler(Guid id, Func<Task> closeHandler)
    {
        ArgumentNullException.ThrowIfNull(closeHandler);
        if (!overlayOrder.Contains(id))
        {
            throw new InvalidOperationException($"Cannot register a close handler for unknown overlay '{id}'.");
        }

        closeHandlers[id] = closeHandler;
    }

    public void UnregisterCloseHandler(Guid id) => closeHandlers.Remove(id);

    public async Task<bool> RequestCloseTopAsync()
    {
        if (overlayOrder.Count == 0)
        {
            return false;
        }

        Guid id = overlayOrder[^1];
        if (closeHandlers.TryGetValue(id, out Func<Task>? closeHandler))
        {
            await closeHandler();
        }
        else
        {
            // The component normally registers during OnInitialized.  This fallback only covers
            // a renderer disconnect between adding the entry and constructing its component.
            Close(id);
        }

        return true;
    }

    public void Close(Guid id)
    {
        int index = entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            int contextMenuIndex = contextMenus.FindIndex(entry => entry.Id == id);
            if (contextMenuIndex >= 0)
            {
                contextMenus.RemoveAt(contextMenuIndex);
                RemoveOverlayState(id);
                Changed?.Invoke();
                return;
            }

            int visibilityIndex = visibilityPickers.FindIndex(entry => entry.Id == id);
            if (visibilityIndex < 0)
            {
                int emojiIndex = emojiPickers.FindIndex(entry => entry.Id == id);
                if (emojiIndex < 0)
                {
                    int autocompleteIndex = autocompletes.FindIndex(entry => entry.Id == id);
                    if (autocompleteIndex < 0)
                    {
                        int tooltipIndex = userTooltips.FindIndex(entry => entry.Id == id);
                        if (tooltipIndex < 0)
                        {
                            int reactionTooltipIndex = reactionTooltips.FindIndex(entry => entry.Id == id);
                            if (reactionTooltipIndex >= 0)
                            {
                                reactionTooltips.RemoveAt(reactionTooltipIndex);
                                Changed?.Invoke();
                                return;
                            }

                            int simpleReactionTooltipIndex = simpleReactionTooltips.FindIndex(entry => entry.Id == id);
                            if (simpleReactionTooltipIndex >= 0)
                            {
                                simpleReactionTooltips.RemoveAt(simpleReactionTooltipIndex);
                                Changed?.Invoke();
                                return;
                            }

                            int previewIndex = userPreviews.FindIndex(entry => entry.Id == id);
                            if (previewIndex < 0)
                            {
                                return;
                            }

                            userPreviews.RemoveAt(previewIndex);
                            RemoveOverlayState(id);
                            Changed?.Invoke();
                            return;
                        }

                        userTooltips.RemoveAt(tooltipIndex);
                        Changed?.Invoke();
                        return;
                    }

                    autocompletes.RemoveAt(autocompleteIndex);
                    Changed?.Invoke();
                    return;
                }

                emojiPickers.RemoveAt(emojiIndex);
                RemoveOverlayState(id);
                Changed?.Invoke();
                return;
            }

            visibilityPickers.RemoveAt(visibilityIndex);
            RemoveOverlayState(id);
            Changed?.Invoke();
            return;
        }

        entries.RemoveAt(index);
        RemoveOverlayState(id);
        Changed?.Invoke();
    }

    private static void ValidatePreviewToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128 || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new ArgumentException("The user preview token is invalid.", parameterName);
        }
    }

    private static void ValidateAlertText(
        string? value,
        int maximumLength,
        string parameterName,
        bool required = false)
    {
        if (required)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        }

        if (value is { Length: > 0 } &&
            (value.Length > maximumLength || value.Any(character => character == '\0')))
        {
            throw new ArgumentException("The alert text is invalid.", parameterName);
        }
    }

    private void RemoveOverlayState(Guid id)
    {
        overlayOrder.Remove(id);
        closeHandlers.Remove(id);
    }
}
