using System.Globalization;
using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NotificationTests : BunitContext
{
    [Fact]
    public void ReactionPreservesPinnedHierarchyClassesSummaryTimeAndFullMode()
    {
        Dependencies dependencies = Configure();
        NotificationViewModel notification = ReactionNotification();

        IRenderedComponent<MkNotification> component = Render<MkNotification>(parameters => parameters
            .Add(value => value.Notification, notification)
            .Add(value => value.WithTime, true)
            .Add(value => value.Full, true)
            .AddUnmatched("class", "_panel notification")
            .AddUnmatched("data-contract", "notification"));
        component.WaitForAssertion(() => Assert.NotNull(dependencies.NotificationInterop.Receiver));

        IElement root = component.Find(".qglefbjs.reaction._panel.notification");
        Assert.Equal("notification", root.GetAttribute("data-contract"));
        Assert.Single(root.QuerySelectorAll(":scope > .head"));
        Assert.Single(root.QuerySelectorAll(":scope > .head > .icon"));
        Assert.Single(root.QuerySelectorAll(":scope > .head > .sub-icon.reaction > .mk-emoji"));
        Assert.Equal(":party@.:", root.QuerySelector(".sub-icon.reaction .mk-emoji")?.GetAttribute("alt"));
        Assert.Single(root.QuerySelectorAll(":scope > .tail > header > .name"));
        Assert.Single(root.QuerySelectorAll(":scope > .tail > header > .time"));
        IElement text = Assert.IsAssignableFrom<IElement>(root.QuerySelector(":scope > .tail > a.text"));
        Assert.Equal("/notes/9note", text.GetAttribute("href"));
        Assert.Equal(2, text.QuerySelectorAll(":scope > i").Length);
        IElement mfm = Assert.IsAssignableFrom<IElement>(text.QuerySelector(".havbbuyv"));
        Assert.DoesNotContain("nowrap", mfm.ClassList);
        Assert.Contains("2 files", text.GetAttribute("title"), StringComparison.Ordinal);
        Assert.Contains("poll", text.GetAttribute("title"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntersectionCallbackPersistsReadOnceAndReactionTooltipUsesTheSimpleOverlay()
    {
        Dependencies dependencies = Configure();
        IRenderedComponent<MkNotification> component = Render<MkNotification>(parameters => parameters
            .Add(value => value.Notification, ReactionNotification()));
        component.WaitForAssertion(() => Assert.NotNull(dependencies.NotificationInterop.Receiver));

        await component.InvokeAsync(dependencies.NotificationInterop.Receiver!.MarkNotificationReadAsync);
        await component.InvokeAsync(dependencies.NotificationInterop.Receiver.MarkNotificationReadAsync);
        Assert.Equal(1, dependencies.Notifications.MarkReadCalls);
        Assert.True(component.Instance.Notification.IsRead);

        await component.InvokeAsync(dependencies.NotificationInterop.Receiver.ShowReactionTooltipAsync);
        MisskeySimpleReactionTooltipEntry tooltip = Assert.Single(dependencies.Overlays.SimpleReactionTooltips);
        Assert.Equal(":party@.:", tooltip.Reaction);
        Assert.Contains(tooltip.Emojis, emoji => emoji.Name == "party@.");
        await component.InvokeAsync(dependencies.NotificationInterop.Receiver.HideReactionTooltipAsync);
        Assert.False(Assert.Single(dependencies.Overlays.SimpleReactionTooltips).Showing);
    }

    [Theory]
    [InlineData(MisskeyNotificationType.ReceiveFollowRequest, "FOLLOW_REQUEST_ACTIONS_UNAVAILABLE")]
    [InlineData(MisskeyNotificationType.GroupInvited, "GROUP_INVITATION_ACTIONS_UNAVAILABLE")]
    [InlineData(MisskeyNotificationType.App, "NOTIFICATION_APPLICATION_PAYLOAD_UNAVAILABLE")]
    public void UnsupportedBackendActionsAreExplicitAndNeverRenderFakeSuccessControls(
        MisskeyNotificationType type,
        string errorCode)
    {
        _ = Configure();
        NotificationViewModel notification = ReactionNotification() with
        {
            Type = type,
            Note = null,
            Reaction = null,
            Body = null,
            BlockedReason = type == MisskeyNotificationType.App ? errorCode : null
        };

        IRenderedComponent<MkNotification> component = Render<MkNotification>(parameters => parameters
            .Add(value => value.Notification, notification)
            .Add(value => value.Full, true));

        Assert.NotNull(component.Find($"[data-error-code='{errorCode}']"));
        Assert.Empty(component.FindAll("button"));
    }

    [Fact]
    public async Task ToastKeepsPinnedSurfaceAndClosesOnceForItsCurrentGeneration()
    {
        Dependencies dependencies = Configure();
        int closed = 0;
        IRenderedComponent<MkNotificationToast> component = Render<MkNotificationToast>(parameters => parameters
            .Add(value => value.Notification, ReactionNotification() with { IsRead = true })
            .Add(value => value.Closed, () => closed++)
            .AddUnmatched("class", "fixture"));
        component.WaitForAssertion(() => Assert.NotNull(dependencies.ToastInterop.Receiver));

        IElement root = component.Find(".mk-notification-toast.fixture");
        Assert.Single(root.QuerySelectorAll(":scope > .notification._acrylic.qglefbjs"));
        Assert.True(dependencies.ToastInterop.Animate);
        await component.InvokeAsync(() => dependencies.ToastInterop.Receiver!.NotifyClosed(dependencies.ToastInterop.Generation + 1));
        await component.InvokeAsync(() => dependencies.ToastInterop.Receiver!.NotifyClosed(dependencies.ToastInterop.Generation));
        await component.InvokeAsync(() => dependencies.ToastInterop.Receiver!.NotifyClosed(dependencies.ToastInterop.Generation));
        await component.Instance.DisposeAsync();

        Assert.Equal(1, closed);
        Assert.Equal(1, dependencies.ToastInterop.Handle.DisposeCalls);
        Assert.True(dependencies.ToastInterop.Handle.ReferenceDisposed);
    }

    private Dependencies Configure()
    {
        var notificationInterop = new RecordingNotificationInterop();
        var toastInterop = new RecordingNotificationToastInterop();
        var notifications = new RecordingNotificationPresentationService();
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<INotificationInterop>(notificationInterop);
        Services.AddSingleton<INotificationToastInterop>(toastInterop);
        Services.AddSingleton<INotificationPresentationService>(notifications);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new NotificationLocalizer());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<ITimeInterop>(new NoOpTimeInterop());
        return new(notificationInterop, toastInterop, notifications, overlays);
    }

    private static NotificationViewModel ReactionNotification()
    {
        var user = new NoteAuthorViewModel(
            "9user",
            "alice",
            "alice@remote.example",
            "Alice",
            "/static-assets/favicon.png",
            IsBot: false);
        var note = new NotificationNoteViewModel(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "9note",
            new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero),
            user,
            "hello",
            null,
            HasReply: false,
            MediaCount: 2,
            HasPoll: true,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["party"] = "/media/party.webp"
            },
            Renote: null);
        return new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "9notification",
            new DateTimeOffset(2026, 8, 4, 10, 5, 0, TimeSpan.Zero),
            MisskeyNotificationType.Reaction,
            IsRead: false,
            user,
            note,
            ":party:");
    }

    private sealed record Dependencies(
        RecordingNotificationInterop NotificationInterop,
        RecordingNotificationToastInterop ToastInterop,
        RecordingNotificationPresentationService Notifications,
        MisskeyOverlayService Overlays);

    private sealed class RecordingNotificationPresentationService : INotificationPresentationService
    {
        public int MarkReadCalls { get; private set; }

        public Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
            NotificationPresentationQuery request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MarkReadCalls++;
            return Task.FromResult(true);
        }

        public Task<int> MarkAllReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotificationInterop : INotificationInterop
    {
        public MkNotification? Receiver { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            ElementReference? reaction,
            DotNetObjectReference<MkNotification> receiver,
            bool unread,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Receiver = receiver.Value;
            Assert.True(unread || Receiver.Notification.IsRead);
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingNotificationToastInterop : INotificationToastInterop
    {
        public MkNotificationToast? Receiver { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public long Generation { get; private set; }

        public bool Animate { get; private set; }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            ElementReference notification,
            DotNetObjectReference<MkNotificationToast> receiver,
            long generation,
            bool animate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Receiver = receiver.Value;
            Generation = generation;
            Animate = animate;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeCalls { get; private set; }

        public bool ReferenceDisposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "dispose")
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            ReferenceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(string.Equals(propertyName, "animation", StringComparison.Ordinal)
                ? (T)(object)true
                : fallback);
        }

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<MfmNode>>(
            [new("text", JsonSerializer.SerializeToElement(new { text }), null)]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpTimeInterop : ITimeInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            DotNetObjectReference<MkTime> receiver,
            long generation,
            long unixTimeMilliseconds,
            bool updateRelativeTime,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NotificationLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }

        public string CurrentLocale => "en-US";

        public string Direction => "ltr";

        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-US");

        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "withNFiles" => $"{arguments!["n"]} files",
            "poll" => "poll",
            "youGotNewFollower" => "followed you",
            "receiveFollowRequest" => "Follow request received",
            "groupInvited" => "Invited to a group",
            "_notification.pollEnded" => "Poll results are available",
            "_ago.justNow" => "Just now",
            "_ago.future" => "Future",
            _ when key.StartsWith("_time.", StringComparison.Ordinal) => key,
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
