using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using ActivityPub.Misskey.Blazor.Streaming;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NotificationsTests : BunitContext
{
    [Fact]
    public void PreservesPaginationFiltersDateListAndFullNoteRouting()
    {
        NotificationViewModel reply = Notification(
            "reply-notification",
            MisskeyNotificationType.Reply,
            FullNote("reply-note"));
        NotificationViewModel reaction = Notification(
            "reaction-notification",
            MisskeyNotificationType.Reaction,
            FullNote("reaction-note"));
        TestServices dependencies = Configure([reply, reaction]);
        var included = new HashSet<MisskeyNotificationType>
        {
            MisskeyNotificationType.Reply,
            MisskeyNotificationType.Reaction
        };
        var excluded = new HashSet<MisskeyNotificationType> { MisskeyNotificationType.Follow };

        using IRenderedComponent<MkNotifications> component = Render<MkNotifications>(parameters => parameters
            .Add(value => value.IncludeTypes, included)
            .Add(value => value.ExcludeTypes, excluded)
            .Add(value => value.UnreadOnly, true)
            .AddUnmatched("data-contract", "mk-notifications"));

        component.WaitForAssertion(() =>
        {
            NotificationPresentationQuery query = Assert.Single(dependencies.Presentation.Queries);
            Assert.True(query.UnreadOnly);
            Assert.Equal(included.OrderBy(value => value), query.IncludeTypes!.OrderBy(value => value));
            Assert.Equal(excluded, query.ExcludeTypes);
            IElement list = component.Find(".sqadhkmv.elsfgstc.noGap");
            Assert.Contains("elsfgstc", list.ClassList);
            Assert.Equal("mk-notifications", component.Find("[data-contract='mk-notifications']").GetAttribute("data-contract"));
            Assert.Single(component.FindAll("[data-stub-full-note]"));
            Assert.Single(component.FindAll("[data-stub-notification]"));
            Assert.DoesNotContain("NOTIFICATION_NOTE_PROJECTION_UNAVAILABLE", component.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task StreamUpsertsWithoutDuplicatesMarksVisibleNotificationsReadAndDisposes()
    {
        NotificationViewModel existing = Notification(
            "existing",
            MisskeyNotificationType.Reaction,
            FullNote("existing-note"));
        TestServices dependencies = Configure([existing]);
        IRenderedComponent<MkNotifications> component = Render<MkNotifications>();
        component.WaitForAssertion(() => Assert.Equal(1, dependencies.Subscription.ActiveSubscriptions));

        dependencies.Subscription.Publish(existing with { Reaction = "🎉" }, 42);
        NotificationViewModel created = Notification(
            "created",
            MisskeyNotificationType.Mention,
            FullNote("created-note"));
        dependencies.Subscription.Publish(created, 43);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, component.FindComponents<Bunit.TestDoubles.Stub<MkNotification>>().Count +
                component.FindComponents<Bunit.TestDoubles.Stub<NoteView>>().Count);
            Assert.Equal(2, dependencies.Presentation.MarkedIds.Count);
            Assert.Equal([existing.InternalId, created.InternalId], dependencies.Presentation.MarkedIds);
            Assert.Equal(43, dependencies.Subscription.LastDeliveredCursor);
        });

        await component.Instance.DisposeAsync();
        Assert.Equal(0, dependencies.Subscription.ActiveSubscriptions);
        Assert.True(dependencies.NotificationsInterop.Disposed);
    }

    [Fact]
    public void EmptyBranchUsesThePortedMisskeyAssetAndLocalizedCopy()
    {
        Configure([]);

        using IRenderedComponent<MkNotifications> component = Render<MkNotifications>();

        component.WaitForAssertion(() =>
        {
            IElement empty = component.Find(".empty > ._fullinfo");
            Assert.Equal("/client-assets/about-icon.png", empty.QuerySelector("img._ghost")?.GetAttribute("src"));
            Assert.Equal("通知はありません", empty.QuerySelector("div")?.TextContent);
        });
    }

    private TestServices Configure(IReadOnlyList<NotificationViewModel> initial)
    {
        var presentation = new RecordingNotificationPresentation(initial);
        var subscription = new ControlledNotificationSubscription();
        var notificationsInterop = new RecordingNotificationsInterop();
        Services.AddSingleton<INotificationPresentationService>(presentation);
        Services.AddSingleton<INotificationSubscriptionService>(subscription);
        Services.AddSingleton<INotificationsInterop>(notificationsInterop);
        Services.AddSingleton<IPaginationInterop>(new NoOpPaginationInterop());
        Services.AddSingleton<IDateSeparatedListInterop>(new NoOpDateSeparatedListInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<IMisskeyLocalizer>(new NotificationsLocalizer());
        Services.AddSingleton<IButtonRippleInterop>(new NoOpButtonRippleInterop());
        Services.AddSingleton<IErrorAppearInterop>(new NoOpErrorAppearInterop());
        ComponentFactories.AddStub<NoteView>("<article data-stub-full-note></article>");
        ComponentFactories.AddStub<MkNotification>("<article data-stub-notification></article>");
        return new(presentation, subscription, notificationsInterop);
    }

    private static NotificationViewModel Notification(
        string id,
        MisskeyNotificationType type,
        NoteViewModel fullNote)
    {
        var summary = new NotificationNoteViewModel(
            fullNote.InternalId,
            fullNote.Id,
            fullNote.CreatedAt,
            fullNote.Author,
            fullNote.Text,
            fullNote.ContentWarning,
            fullNote.ReplyId is not null,
            fullNote.Media.Count,
            fullNote.Poll is not null,
            fullNote.Emojis,
            Renote: null);
        return new(
            Guid.NewGuid(),
            id,
            fullNote.CreatedAt,
            type,
            IsRead: false,
            fullNote.Author,
            summary,
            type == MisskeyNotificationType.Reaction ? "👍" : null,
            FullNote: fullNote);
    }

    private static NoteViewModel FullNote(string id)
    {
        var author = new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice@remote.example",
            "Alice",
            "/static-assets/user-unknown.png",
            IsBot: false);
        return new(
            Guid.NewGuid(),
            id,
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            author,
            $"note {id}",
            ContentWarning: null,
            ActivityPub.Domain.Visibility.Public,
            ReplyId: null,
            RepliesCount: 0,
            RenotesCount: 0,
            ReactionsCount: 0,
            ReactedByViewer: false,
            new Dictionary<string, long>(StringComparer.Ordinal),
            ViewerReaction: null,
            Media: [],
            Mentions: [],
            Hashtags: [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            Poll: null,
            Renote: null);
    }

    private sealed record TestServices(
        RecordingNotificationPresentation Presentation,
        ControlledNotificationSubscription Subscription,
        RecordingNotificationsInterop NotificationsInterop);

    private sealed class RecordingNotificationPresentation(IReadOnlyList<NotificationViewModel> initial)
        : INotificationPresentationService
    {
        public List<NotificationPresentationQuery> Queries { get; } = [];
        public List<Guid> MarkedIds { get; } = [];

        public Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
            NotificationPresentationQuery request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(request);
            return Task.FromResult(initial);
        }

        public Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(initial.SingleOrDefault(value => value.InternalId == notificationId));

        public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MarkedIds.Add(notificationId);
            return Task.FromResult(true);
        }

        public Task<int> MarkAllReadAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class ControlledNotificationSubscription : INotificationSubscriptionService
    {
        private readonly Channel<NotificationMutation> channel = Channel.CreateUnbounded<NotificationMutation>();

        public int ActiveSubscriptions { get; private set; }
        public long LastDeliveredCursor { get; private set; }

        public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken) => Task.FromResult(41L);

        public async IAsyncEnumerable<NotificationMutation> SubscribeAsync(
            long afterCursor,
            IReadOnlySet<MisskeyNotificationType>? includeTypes,
            IReadOnlySet<MisskeyNotificationType>? excludeTypes,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ActiveSubscriptions++;
            try
            {
                await foreach (NotificationMutation mutation in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    LastDeliveredCursor = mutation.Cursor;
                    yield return mutation;
                }
            }
            finally
            {
                ActiveSubscriptions--;
            }
        }

        public void Publish(NotificationViewModel notification, long cursor) =>
            channel.Writer.TryWrite(new(cursor, NotificationMutationKind.Created, notification));
    }

    private sealed class RecordingNotificationsInterop : INotificationsInterop
    {
        public bool Disposed { get; private set; }
        public ValueTask<bool> IsDocumentVisibleAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoOpPaginationInterop : IPaginationInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(ElementReference root, DotNetObjectReference<T> receiver, bool enableAutoLoad, CancellationToken cancellationToken) where T : class =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsReference());
        public ValueTask<bool> IsTopVisibleAsync(ElementReference root, CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask<bool> IsBottomVisibleAsync(ElementReference root, double tolerance, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<PaginationScrollSnapshot> CaptureScrollAsync(ElementReference root, CancellationToken cancellationToken) => ValueTask.FromResult(new PaginationScrollSnapshot(0, 0, false, false));
        public ValueTask RestoreScrollAsync(ElementReference root, PaginationScrollSnapshot snapshot, bool stickToBottom, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ScrollToTopAsync(ElementReference root, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<bool> IsWindowAtTopAsync(CancellationToken cancellationToken) => ValueTask.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpDateSeparatedListInterop : IDateSeparatedListInterop
    {
        public ValueTask<DateSeparatedCalendarPart[]> GetCalendarPartsAsync(IReadOnlyList<long> values, CancellationToken cancellationToken) =>
            ValueTask.FromResult(values.Select(_ => new DateSeparatedCalendarPart(8, 4)).ToArray());
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference root, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsReference());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);
        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class NoOpButtonRippleInterop : IButtonRippleInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsReference());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpErrorAppearInterop : IErrorAppearInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, bool animate, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IJSObjectReference>(new NoOpJsReference());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsReference : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NotificationsLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "noNotifications" => "通知はありません",
            "loadMore" => "もっと見る",
            "somethingHappened" => "問題が発生しました",
            "retry" => "再試行",
            _ => key
        };
        public bool TrySelectLocale(string? locale) => false;
    }
}
