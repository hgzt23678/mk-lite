using System.Globalization;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Pages;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NotificationsPageTests : BunitContext
{
    [Fact]
    public async Task PreservesHeaderTabsFiltersAndPersistentMarkAllContract()
    {
        TestServices dependencies = Configure();
        ComponentFactories.AddStub<MkNotifications>();
        ComponentFactories.AddStub<MkNotificationNotes>();

        using IRenderedComponent<NotificationsPage> component = Render<NotificationsPage>();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("通知", component.Find(".titleContainer .title .title").TextContent);
            Assert.NotNull(component.Find(".titleContainer > i.fa-bell"));
            Assert.Equal(
                ["全て", "未読", "あなた宛て", "ダイレクト投稿"],
                component.FindAll(".tabs > button.tab").Select(button => button.TextContent.Trim()));
            Bunit.TestDoubles.Stub<MkNotifications> list =
                component.FindComponent<Bunit.TestDoubles.Stub<MkNotifications>>().Instance;
            Assert.False(list.Parameters.Get(value => value.UnreadOnly));
            Assert.Null(list.Parameters.Get(value => value.IncludeTypes));
            Assert.Equal("max-width: 800px;", component.Find("._content_b6w6v_6").GetAttribute("style"));
        });

        component.Find(".tabs > button[title='未読']").Click();
        Bunit.TestDoubles.Stub<MkNotifications> unread =
            component.FindComponent<Bunit.TestDoubles.Stub<MkNotifications>>().Instance;
        Assert.True(unread.Parameters.Get(value => value.UnreadOnly));
        Assert.Empty(component.FindAll(".buttons.right > button"));

        component.Find(".tabs > button[title='あなた宛て']").Click();
        Assert.Single(component.FindComponents<Bunit.TestDoubles.Stub<MkNotificationNotes>>());

        component.Find(".tabs > button[title='ダイレクト投稿']").Click();
        Assert.True(component.FindComponent<Bunit.TestDoubles.Stub<MkNotificationNotes>>()
            .Instance.Parameters.Get(value => value.DirectOnly));

        component.Find(".tabs > button[title='全て']").Click();
        component.Find("button[aria-label='フィルタ']").Click();
        MisskeyOverlayEntry filterMenu = Assert.Single(dependencies.Overlays.Entries);
        Assert.Equal(MisskeyOverlayKind.PopupMenu, filterMenu.Kind);
        Assert.Equal(
            ["フォロー", "メンション", "リプライ", "Renote", "引用", "リアクション", "アンケートに投票された", "フォロー申請を受け取った", "フォローが受理された", "グループに招待された", "連携アプリからの通知"],
            filterMenu.MenuItems.Select(item => item.Text));

        await component.InvokeAsync(filterMenu.MenuItems[5].Action!);
        Bunit.TestDoubles.Stub<MkNotifications> filtered =
            component.FindComponent<Bunit.TestDoubles.Stub<MkNotifications>>().Instance;
        Assert.Equal(
            [MisskeyNotificationType.Reaction],
            filtered.Parameters.Get(value => value.IncludeTypes));
        Assert.Contains("highlighted", component.Find("button[aria-label='フィルタ']").ClassList);

        dependencies.Overlays.Close(filterMenu.Id);
        component.Find("button[aria-label='フィルタ']").Click();
        MisskeyOverlayEntry clearMenu = Assert.Single(dependencies.Overlays.Entries);
        Assert.Equal("クリア", clearMenu.MenuItems[0].Text);
        Assert.Equal(MisskeyMenuItemKind.Divider, clearMenu.MenuItems[1].Kind);
        Assert.True(clearMenu.MenuItems[7].Active);

        await component.InvokeAsync(clearMenu.MenuItems[0].Action!);
        Assert.Null(component.FindComponent<Bunit.TestDoubles.Stub<MkNotifications>>()
            .Instance.Parameters.Get(value => value.IncludeTypes));

        component.Find("button[aria-label='全て既読にする']").Click();
        component.WaitForAssertion(() => Assert.Equal(1, dependencies.Notifications.MarkAllCalls));
    }

    [Fact]
    public async Task MentionPaginationUsesRealProjectedNotesAndScansForSpecifiedVisibility()
    {
        NotificationViewModel publicMention = Notification(
            "notification-public",
            "note-public",
            ActivityPub.Domain.Visibility.Public);
        NotificationViewModel directMention = Notification(
            "notification-direct",
            "note-direct",
            ActivityPub.Domain.Visibility.MentionedOnly);
        var presentation = new PagingNotifications([publicMention, directMention]);
        var mentions = new MentionNotePaginationSource(presentation);
        var direct = new MentionNotePaginationSource(presentation, directOnly: true);

        IReadOnlyList<MentionNoteListItem> mentionItems = await mentions.FetchAsync(
            new(10),
            CancellationToken.None);
        IReadOnlyList<MentionNoteListItem> directItems = await direct.FetchAsync(
            new(10),
            CancellationToken.None);

        Assert.Equal(["note-public", "note-direct"], mentionItems.Select(item => item.Note.Id));
        Assert.Equal("notification-direct", Assert.Single(directItems).NotificationId);
        Assert.All(presentation.Queries, query => Assert.Equal(
            [MisskeyNotificationType.Reply, MisskeyNotificationType.Mention, MisskeyNotificationType.Quote],
            query.IncludeTypes!.OrderBy(value => value)));
    }

    private TestServices Configure()
    {
        var notifications = new PagingNotifications([]);
        var overlays = new MisskeyOverlayService();
        Services.AddSingleton<INotificationPresentationService>(notifications);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new PageLocalizer());
        Services.AddSingleton<IStickyContainerInterop>(new NoOpStickyInterop());
        Services.AddSingleton<IPageHeaderInterop>(new NoOpPageHeaderInterop());
        Services.AddSingleton<ISpacerInterop>(new NoOpSpacerInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        Services.AddSingleton<ICurrentAccountPresentationService>(new UnusedCurrentAccount());
        return new(notifications, overlays);
    }

    private static NotificationViewModel Notification(
        string notificationId,
        string noteId,
        ActivityPub.Domain.Visibility visibility)
    {
        var author = new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice@remote.example",
            "Alice",
            "/static-assets/user-unknown.png",
            IsBot: false);
        var note = new NoteViewModel(
            Guid.NewGuid(),
            noteId,
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            author,
            $"text {noteId}",
            ContentWarning: null,
            (Visibility)visibility,
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
        return new(
            Guid.NewGuid(),
            notificationId,
            note.CreatedAt,
            MisskeyNotificationType.Mention,
            IsRead: false,
            author,
            new(
                note.InternalId,
                note.Id,
                note.CreatedAt,
                author,
                note.Text,
                note.ContentWarning,
                HasReply: false,
                MediaCount: 0,
                HasPoll: false,
                note.Emojis,
                Renote: null),
            Reaction: null,
            FullNote: note);
    }

    private sealed record TestServices(
        PagingNotifications Notifications,
        MisskeyOverlayService Overlays);

    private sealed class PagingNotifications(IReadOnlyList<NotificationViewModel> items)
        : INotificationPresentationService
    {
        public List<NotificationPresentationQuery> Queries { get; } = [];
        public int MarkAllCalls { get; private set; }

        public Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
            NotificationPresentationQuery request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(request);
            IEnumerable<NotificationViewModel> result = items;
            if (request.IncludeTypes is not null)
            {
                result = result.Where(item => request.IncludeTypes.Contains(item.Type));
            }

            if (request.UntilId is not null)
            {
                result = result.SkipWhile(item => item.Id != request.UntilId).Skip(1);
            }

            return Task.FromResult<IReadOnlyList<NotificationViewModel>>(result.Take(request.Limit).ToArray());
        }

        public Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(items.SingleOrDefault(item => item.InternalId == notificationId));

        public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken) =>
            Task.FromResult(items.Any(item => item.InternalId == notificationId));

        public Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MarkAllCalls++;
            return Task.FromResult(items.Count(item => !item.IsRead));
        }
    }

    private sealed class PageLocalizer : IMisskeyLocalizer
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
        {
            ["notifications"] = "通知",
            ["all"] = "全て",
            ["unread"] = "未読",
            ["mentions"] = "あなた宛て",
            ["directNotes"] = "ダイレクト投稿",
            ["filter"] = "フィルタ",
            ["markAllAsRead"] = "全て既読にする",
            ["clear"] = "クリア",
            ["_notification._types.follow"] = "フォロー",
            ["_notification._types.mention"] = "メンション",
            ["_notification._types.reply"] = "リプライ",
            ["_notification._types.renote"] = "Renote",
            ["_notification._types.quote"] = "引用",
            ["_notification._types.reaction"] = "リアクション",
            ["_notification._types.pollVote"] = "アンケートに投票された",
            ["_notification._types.receiveFollowRequest"] = "フォロー申請を受け取った",
            ["_notification._types.followRequestAccepted"] = "フォローが受理された",
            ["_notification._types.groupInvited"] = "グループに招待された",
            ["_notification._types.app"] = "連携アプリからの通知"
        };

        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) =>
            Values.TryGetValue(key, out string? value) ? value : key;
        public bool TrySelectLocale(string? locale) => false;
    }

    private sealed class UnusedCurrentAccount : ICurrentAccountPresentationService
    {
        public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The page header does not request an account avatar.");
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(fallback);
        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class NoOpStickyInterop : IStickyContainerInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference root,
            ElementReference header,
            ElementReference body,
            double parentTop,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new NoOpJsReference());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpPageHeaderInterop : IPageHeaderInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new NoOpJsReference());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpSpacerInterop : ISpacerInterop
    {
        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken)
            where T : class => ValueTask.FromResult<IJSObjectReference>(new NoOpJsReference());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsReference : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
