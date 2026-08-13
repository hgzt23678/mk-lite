using System.Globalization;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class DisplayPrimitiveTests : BunitContext
{
    private static readonly string[] AvatarUserIds = ["alice-id", "bob-id"];

    [Fact]
    public void FileTypeIconPreservesPinnedImageBranchAndAttributeFallthrough()
    {
        IRenderedComponent<MkFileTypeIcon> image = Render<MkFileTypeIcon>(parameters => parameters
            .Add(component => component.Type, "image/png")
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "file-type"));

        IElement root = image.Find("span");
        Assert.Equal("mk-file-type-icon fixture", root.ClassName);
        Assert.Equal("file-type", root.GetAttribute("data-contract"));
        Assert.NotNull(root.QuerySelector(":scope > i.fas.fa-file-image"));

        IRenderedComponent<MkFileTypeIcon> audio = Render<MkFileTypeIcon>(parameters => parameters
            .Add(component => component.Type, "audio/ogg"));
        Assert.Empty(audio.FindAll("i"));
    }

    [Fact]
    public void RemoteCautionPreservesPinnedDomLocalizationLinkAndFallthrough()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());

        IRenderedComponent<MkRemoteCaution> component = Render<MkRemoteCaution>(parameters => parameters
            .Add(value => value.Href, "https://remote.example/@alice")
            .AddUnmatched("class", "fixture")
            .AddUnmatched("data-contract", "remote-caution"));

        IElement root = component.Find("div");
        Assert.Equal("jmgmzlwq _block fixture", root.ClassName);
        Assert.Equal("remote-caution", root.GetAttribute("data-contract"));
        Assert.NotNull(root.QuerySelector(":scope > i.fas.fa-exclamation-triangle[style='margin-right: 8px;']"));
        IElement link = Assert.IsAssignableFrom<IElement>(root.QuerySelector(":scope > a.link"));
        Assert.Equal("https://remote.example/@alice", link.GetAttribute("href"));
        Assert.Equal("nofollow noopener", link.GetAttribute("rel"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("リモートで表示", link.TextContent);
        Assert.Contains("リモートユーザーのため、情報が不完全です。", root.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void MentionPreservesCanonicalAvatarAccountDomViewerColorAndFullAccountSetting()
    {
        Services.AddSingleton(new MisskeyFrontendRuntimeConfiguration(
            MisskeyFrontendRuntimeConfiguration.PortVersion,
            null,
            new Uri("https://local.example", UriKind.Absolute)));
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(showFullAcct: true));
        Services.AddSingleton<IAuthenticatedActorContext>(new FixedActorContext("alice"));

        IRenderedComponent<MkMention> component = Render<MkMention>(parameters => parameters
            .Add(value => value.Username, "alice")
            .Add(value => value.Host, "local.example")
            .AddUnmatched("class", "fixture")
            .AddUnmatched("style", "vertical-align: middle;")
            .AddUnmatched("data-contract", "mention"));

        component.WaitForAssertion(() =>
        {
            IElement root = component.Find("a");
            Assert.Equal("akbvjaqn isMe fixture", root.ClassName);
            Assert.Equal("@alice", root.GetAttribute("href"));
            Assert.Equal("@alice", root.GetAttribute("data-user-preview"));
            Assert.Equal("mention", root.GetAttribute("data-contract"));
            Assert.Equal(
                "background: color-mix(in srgb, var(--mentionMe) 10%, transparent); vertical-align: middle;",
                root.GetAttribute("style"));
            Assert.Equal("/avatar/@alice@local.example", root.QuerySelector(":scope > img.icon")?.GetAttribute("src"));
            Assert.Equal("@alice", root.QuerySelector(":scope > .main > .username")?.TextContent);
            Assert.Equal("@local.example", root.QuerySelector(":scope > .main > .host")?.TextContent);
        });
    }

    [Fact]
    public void AvatarsLoadsUsersOnceAndPreservesPinnedOrderGeometryAndIndicators()
    {
        var service = new FixedAvatarsService();
        Services.AddSingleton<IAvatarsPresentationService>(service);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(showFullAcct: false));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());

        IRenderedComponent<MkAvatars> component = Render<MkAvatars>(parameters => parameters
            .Add(value => value.UserIds, AvatarUserIds)
            .AddUnmatched("data-contract", "avatars"));

        IElement root = component.Find("div[data-contract='avatars']");
        IHtmlCollection<IElement> wrappers = root.QuerySelectorAll(":scope > div");
        Assert.Equal(2, wrappers.Length);
        Assert.Equal("display:inline-block;width:32px;height:32px;margin-right:8px;", wrappers[0].GetAttribute("style"));
        Assert.Equal("@alice", wrappers[0].QuerySelector(":scope > a.eiwwqkts")?.GetAttribute("title"));
        Assert.NotNull(wrappers[0].QuerySelector(":scope > a > .indicator.fzgwjkgc.online"));
        Assert.Equal("@bob@remote.example", wrappers[1].QuerySelector(":scope > a.eiwwqkts")?.GetAttribute("title"));
        Assert.NotNull(wrappers[1].QuerySelector(":scope > a > .indicator.fzgwjkgc.offline"));
        Assert.Equal(AvatarUserIds, service.RequestedIds);
        Assert.Equal(1, service.ReadCalls);
    }

    [Fact]
    public async Task AvatarsPresentationResolvesPersistentMisskeyIdsInRequestOrder()
    {
        var query = new StubClientQuery();
        var externalIds = new InMemoryExternalIds();
        ClientAccountView alice = ClientViewFactory.Post().Account;
        ClientAccountView bob = alice with
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Username = "bob",
            Acct = "bob@remote.example",
            DisplayName = "Bob"
        };
        query.AccountsById[alice.Id] = alice;
        query.AccountsById[bob.Id] = bob;
        string aliceId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            alice.Id,
            alice.CreatedAt,
            CancellationToken.None);
        string bobId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            bob.Id,
            bob.CreatedAt,
            CancellationToken.None);
        var requested = new List<string> { bobId, aliceId };
        var service = new AvatarsPresentationService(
            query,
            externalIds,
            new MisskeyFrontendRuntimeConfiguration(
                MisskeyFrontendRuntimeConfiguration.PortVersion,
                null,
                new Uri("https://local.example", UriKind.Absolute)));

        IReadOnlyList<NoteAuthorViewModel> result = await service.ReadAsync(
            requested,
            CancellationToken.None);

        Assert.Equal(new[] { bobId, aliceId }, result.Select(user => user.Id));
        Assert.Equal(new[] { bob.Id, alice.Id }, query.AccountIdsRead);
        Assert.Equal("bob@remote.example", result[0].Acct);
        Assert.Equal("alice", result[1].Acct);
    }

    [Fact]
    public void OnlineIndicatorAcceptsPinnedUserProp()
    {
        var user = new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice",
            "Alice",
            "/static-assets/favicon.png",
            IsBot: false,
            OnlineStatus: "active");

        IRenderedComponent<MkUserOnlineIndicator> component = Render<MkUserOnlineIndicator>(parameters => parameters
            .Add(value => value.User, user));

        Assert.Equal("fzgwjkgc active", component.Find("div").ClassName);
    }

    private sealed class FixedActorContext(string? username) : IAuthenticatedActorContext
    {
        public Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(username is null
                ? null
                : new AuthenticatedActor(username, $"https://local.example/users/{username}"));
        }

        public async Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
            await FindAsync(cancellationToken) ?? throw new FrontendAuthenticationException("AUTH_REQUIRED");

        public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FixedDeviceState(bool showFullAcct) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = propertyName == "showFullAcct" ? showFullAcct : fallback!;
            return ValueTask.FromResult((T)value);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FixedAvatarsService : IAvatarsPresentationService
    {
        public int ReadCalls { get; private set; }
        public IReadOnlyList<string> RequestedIds { get; private set; } = [];

        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            IReadOnlyList<string> userIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            RequestedIds = userIds.ToArray();
            return Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>(
            [
                new(
                    "alice-id",
                    "alice",
                    "alice",
                    "Alice",
                    "/static-assets/favicon.png",
                    IsBot: false,
                    OnlineStatus: "online"),
                new(
                    "bob-id",
                    "bob",
                    "bob@remote.example",
                    "Bob",
                    "/static-assets/favicon.png",
                    IsBot: false,
                    OnlineStatus: "offline")
            ]);
        }
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "ja-JP";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "remoteUserCaution" => "リモートユーザーのため、情報が不完全です。",
            "showOnRemote" => "リモートで表示",
            "online" => "オンライン",
            "active" => "アクティブ",
            "offline" => "オフライン",
            "unknown" => "不明",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
