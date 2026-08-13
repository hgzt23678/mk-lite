using System.Text.Json;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class VisibilityTests : BunitContext
{
    [Fact]
    public void PreservesTheTwoUpstreamSpansIconsAndAttributeFallthrough()
    {
        RecordingVisibilityTooltipInterop browser = Configure(new ImmediateVisibleUsers([]));
        NoteViewModel note = CreateNote(
            Visibility.FollowersOnly,
            localOnly: true,
            visibleUserIds: []);

        IRenderedComponent<MkVisibility> component = Render<MkVisibility>(parameters => parameters
            .Add(value => value.Note, note)
            .AddUnmatched("class", "header-visibility")
            .AddUnmatched("data-note-contract", "visibility"));

        Assert.Single(component.FindAll("span._visibility_1rbrq_1 > i.fas.fa-unlock"));
        Assert.Single(component.FindAll("span._localOnly_1rbrq_1 > i.fas.fa-biohazard"));
        Assert.Contains("header-visibility", component.Find("span._visibility_1rbrq_1").ClassList);
        Assert.Equal("visibility", component.Find("span._visibility_1rbrq_1").GetAttribute("data-note-contract"));
        Assert.DoesNotContain("header-visibility", component.Find("span._localOnly_1rbrq_1").ClassList);
        Assert.Equal(0, browser.TriggerAttachCalls);
    }

    [Fact]
    public void PublicLocalOnlyNoteFallsAttributesThroughToItsOnlyRoot()
    {
        Configure(new ImmediateVisibleUsers([]));

        IRenderedComponent<MkVisibility> component = Render<MkVisibility>(parameters => parameters
            .Add(value => value.Note, CreateNote(Visibility.Public, localOnly: true, visibleUserIds: []))
            .AddUnmatched("class", "local-contract")
            .AddUnmatched("data-local", "true"));

        Assert.Empty(component.FindAll("span._visibility_1rbrq_1"));
        Assert.Contains("local-contract", component.Find("span._localOnly_1rbrq_1").ClassList);
        Assert.Equal("true", component.Find("span._localOnly_1rbrq_1").GetAttribute("data-local"));
    }

    [Fact]
    public async Task SpecifiedVisibilityLoadsAtMostTenUsersAndRendersTheExactTooltipHierarchy()
    {
        IReadOnlyList<NoteAuthorViewModel> users = Enumerable.Range(0, 10)
            .Select(index => CreateUser(index))
            .ToArray();
        var lookup = new DeferredVisibleUsers();
        RecordingVisibilityTooltipInterop browser = Configure(lookup);
        string[] visibleIds = Enumerable.Range(0, 12).Select(index => $"9user{index}").ToArray();
        IRenderedComponent<VisibilityHost> host = Render<VisibilityHost>(parameters => parameters
            .Add(value => value.Note, CreateNote(Visibility.MentionedOnly, localOnly: false, visibleIds)));
        IRenderedComponent<MkVisibility> visibility = host.FindComponent<MkVisibility>();
        visibility.WaitForAssertion(() => Assert.Equal(1, browser.TriggerAttachCalls));

        await visibility.InvokeAsync(visibility.Instance.ShowVisibilityTooltipAsync);
        host.WaitForAssertion(() =>
            Assert.Equal("loading", host.Find(".beaffaef").GetAttribute("data-tooltip-load-state")));
        Assert.Equal(visibleIds, lookup.RequestedIds);

        lookup.Complete(users);
        host.WaitForAssertion(() =>
        {
            Assert.Equal("loaded", host.Find(".beaffaef").GetAttribute("data-tooltip-load-state"));
            Assert.Equal(10, host.FindAll(".beaffaef > .user").Count);
            Assert.Equal(10, host.FindAll(".beaffaef > .user > .avatar.eiwwqkts").Count);
            Assert.Equal(10, host.FindAll(".beaffaef > .user > .name.havbbuyv.nowrap").Count);
            Assert.Equal("+2", host.Find(".beaffaef > .omitted").TextContent);
            Assert.Equal(1, browser.TooltipAttachCalls);
        });

        var trigger = host.Find("span._visibility_1rbrq_1");
        Assert.Equal("0", trigger.GetAttribute("tabindex"));
        Assert.Equal("button", trigger.GetAttribute("role"));
        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));
        Assert.Equal(host.Find(".buebdbiu").Id, trigger.GetAttribute("aria-describedby"));

        await visibility.InvokeAsync(visibility.Instance.HideVisibilityTooltipAsync);
        host.WaitForAssertion(() => Assert.Equal("false", trigger.GetAttribute("aria-expanded")));
        Assert.Contains("hide", browser.TooltipHandle.Invocations);
    }

    [Fact]
    public async Task LookupFailureIsAnExplicitSafeErrorInsteadOfAnEmptySuccessfulTooltip()
    {
        Configure(new FailingVisibleUsers("VISIBILITY_USER_NOT_FOUND"));
        IRenderedComponent<VisibilityHost> host = Render<VisibilityHost>(parameters => parameters
            .Add(value => value.Note, CreateNote(
                Visibility.MentionedOnly,
                localOnly: false,
                ["missing-user"])));
        IRenderedComponent<MkVisibility> visibility = host.FindComponent<MkVisibility>();

        await visibility.InvokeAsync(visibility.Instance.ShowVisibilityTooltipAsync);

        host.WaitForAssertion(() =>
        {
            Assert.Equal("error", host.Find(".beaffaef").GetAttribute("data-tooltip-load-state"));
            Assert.Equal("VISIBILITY_USER_NOT_FOUND", host.Find(".beaffaef > .error").GetAttribute("data-error-code"));
            Assert.Empty(host.FindAll(".beaffaef > .user"));
        });
    }

    [Fact]
    public async Task DisposingTheNoteReleasesTheTriggerAndRemovesItsPopup()
    {
        RecordingVisibilityTooltipInterop browser = Configure(new ImmediateVisibleUsers([]));
        IRenderedComponent<VisibilityHost> host = Render<VisibilityHost>(parameters => parameters
            .Add(value => value.Note, CreateNote(
                Visibility.MentionedOnly,
                localOnly: false,
                ["9user0"])));
        IRenderedComponent<MkVisibility> visibility = host.FindComponent<MkVisibility>();
        visibility.WaitForAssertion(() => Assert.Equal(1, browser.TriggerAttachCalls));
        await visibility.InvokeAsync(visibility.Instance.ShowVisibilityTooltipAsync);
        host.WaitForAssertion(() => Assert.Single(host.FindAll(".buebdbiu")));

        await visibility.Instance.DisposeAsync();
        host.Dispose();

        Assert.True(browser.TriggerHandle.DisposeInvoked);
        Assert.Empty(Services.GetRequiredService<IMisskeyOverlayService>().UserTooltips);
    }

    private RecordingVisibilityTooltipInterop Configure(IVisibleUsersPresentationService users)
    {
        var browser = new RecordingVisibilityTooltipInterop();
        Services.AddSingleton<IVisibilityTooltipInterop>(browser);
        Services.AddSingleton(users);
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IMisskeyTransientFeedbackService, MisskeyTransientFeedbackService>();
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IUserPreviewInterop>(new NoOpUserPreviewInterop());
        return browser;
    }

    private static NoteViewModel CreateNote(
        ActivityPub.Misskey.Blazor.Presentation.Visibility visibility,
        bool localOnly,
        IReadOnlyList<string> visibleUserIds) => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "9visibility",
        new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
        CreateUser(99),
        "visibility fixture",
        null,
        visibility,
        null,
        0,
        0,
        0,
        false,
        new Dictionary<string, long>(StringComparer.Ordinal),
        null,
        [],
        [],
        [],
        new Dictionary<string, string>(StringComparer.Ordinal),
        null,
        null,
        localOnly,
        visibleUserIds);

    private static NoteAuthorViewModel CreateUser(int index) => new(
        $"9user{index}",
        $"user{index}",
        $"user{index}@remote.example",
        $"User {index}",
        "/static-assets/favicon.png",
        IsBot: false);

    private sealed class VisibilityHost : ComponentBase
    {
        [Parameter, EditorRequired]
        public NoteViewModel Note { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MkVisibility>(0);
            builder.AddAttribute(1, nameof(MkVisibility.Note), Note);
            builder.CloseComponent();
            builder.OpenComponent<OverlayHost>(2);
            builder.CloseComponent();
        }
    }

    private sealed class ImmediateVisibleUsers(IReadOnlyList<NoteAuthorViewModel> users) : IVisibleUsersPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            IReadOnlyList<string> userIds,
            CancellationToken cancellationToken) => Task.FromResult(users);
    }

    private sealed class DeferredVisibleUsers : IVisibleUsersPresentationService
    {
        private readonly TaskCompletionSource<IReadOnlyList<NoteAuthorViewModel>> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string>? RequestedIds { get; private set; }

        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            IReadOnlyList<string> userIds,
            CancellationToken cancellationToken)
        {
            RequestedIds = userIds.ToArray();
            return completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(IReadOnlyList<NoteAuthorViewModel> users) => completion.SetResult(users);
    }

    private sealed class FailingVisibleUsers(string errorCode) : IVisibleUsersPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            IReadOnlyList<string> userIds,
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<NoteAuthorViewModel>>(new VisibleUsersPresentationException(errorCode));
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

    private sealed class RecordingVisibilityTooltipInterop : IVisibilityTooltipInterop
    {
        public RecordingHandle TriggerHandle { get; } = new();
        public RecordingHandle TooltipHandle { get; } = new();
        public int TriggerAttachCalls { get; private set; }
        public int TooltipAttachCalls { get; private set; }

        public ValueTask<IJSObjectReference> AttachTriggerAsync(
            ElementReference target,
            DotNetObjectReference<MkVisibility> receiver,
            CancellationToken cancellationToken)
        {
            TriggerAttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(TriggerHandle);
        }

        public ValueTask<IJSObjectReference> AttachTooltipAsync(
            ElementReference target,
            ElementReference tooltip,
            DotNetObjectReference<MkTooltip> receiver,
            CancellationToken cancellationToken)
        {
            TooltipAttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(TooltipHandle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpUserPreviewInterop : IUserPreviewInterop
    {
        public ValueTask<IJSObjectReference> AttachDirectiveHostAsync(
            DotNetObjectReference<UserPreviewDirectiveHost> receiver,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask<IJSObjectReference> AttachPreviewAsync(
            string hostId,
            string sourceId,
            long generation,
            ElementReference preview,
            DotNetObjectReference<MkUserPreview> receiver,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public List<string> Invocations { get; } = [];
        public bool DisposeInvoked { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            DisposeInvoked = true;
            return ValueTask.CompletedTask;
        }
    }
}
