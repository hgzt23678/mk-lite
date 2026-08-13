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

public sealed class ReactionsViewerTests : BunitContext
{
    [Fact]
    public void PreservesUpstreamOrderClassesCountsAndZeroCountBranch()
    {
        Configure(new FixedDetails([]));
        NoteViewModel note = Note() with
        {
            ViewerReaction = "🎉",
            Reactions = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["🎉"] = 2,
                [":party@remote.example:"] = 1,
                ["👍"] = 0
            }
        };

        IRenderedComponent<MkReactionsViewer> component = Render<MkReactionsViewer>(parameters => parameters
            .Add(value => value.Note, note));

        IReadOnlyList<AngleSharp.Dom.IElement> buttons = component.FindAll(".tdflqwzn > .hkzvhatu");
        Assert.Equal(2, buttons.Count);
        Assert.Equal("2", buttons[0].QuerySelector(".count")?.TextContent);
        Assert.Contains("reacted", buttons[0].ClassList);
        Assert.DoesNotContain("canToggle", buttons[0].ClassList);
        Assert.Equal("1", buttons[1].QuerySelector(".count")?.TextContent);
        Assert.DoesNotContain("canToggle", buttons[1].ClassList);
    }

    [Fact]
    public async Task AuthenticatedLocalReactionDelegatesTheRealMutationWhileRemoteReactionCannotToggle()
    {
        Configure(new FixedDetails([]));
        string? selected = null;
        NoteViewModel note = Note() with
        {
            Reactions = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["🎉"] = 2,
                [":party@remote.example:"] = 1
            }
        };
        IRenderedComponent<MkReactionsViewerReaction> local = Render<MkReactionsViewerReaction>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.Reaction, "🎉")
            .Add(value => value.Count, 2)
            .Add(value => value.IsAuthenticated, true)
            .Add(value => value.ReactionSelected, reaction => selected = reaction));
        IRenderedComponent<MkReactionsViewerReaction> remote = Render<MkReactionsViewerReaction>(parameters => parameters
            .Add(value => value.Note, note)
            .Add(value => value.Reaction, ":party@remote.example:")
            .Add(value => value.Count, 1)
            .Add(value => value.IsAuthenticated, true)
            .Add(value => value.ReactionSelected, reaction => selected = reaction));

        Assert.Contains("canToggle", local.Find("button").ClassList);
        await local.Find("button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Equal("🎉", selected);

        selected = null;
        Assert.DoesNotContain("canToggle", remote.Find("button").ClassList);
        await remote.Find("button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Null(selected);
    }

    [Fact]
    public async Task HoverDetailsPreserveTheExactReactionUsersAndOmittedHierarchy()
    {
        IReadOnlyList<NoteAuthorViewModel> users = Enumerable.Range(0, 11)
            .Select(User)
            .ToArray();
        Configure(new FixedDetails(users));
        IRenderedComponent<ReactionHost> host = Render<ReactionHost>(parameters => parameters
            .Add(value => value.Note, Note() with
            {
                Reactions = new Dictionary<string, long>(StringComparer.Ordinal) { ["🎉"] = 15 }
            }));
        IRenderedComponent<MkReactionsViewerReaction> reaction = host.FindComponent<MkReactionsViewerReaction>();

        await reaction.InvokeAsync(reaction.Instance.ShowReactionTooltipAsync);

        host.WaitForAssertion(() =>
        {
            Assert.Single(host.FindAll(".buebdbiu > .bqxuuuey"));
            Assert.Single(host.FindAll(".bqxuuuey > .reaction > .icon"));
            Assert.Equal("🎉", host.Find(".bqxuuuey > .reaction > .name").TextContent);
            Assert.Equal(11, host.FindAll(".bqxuuuey > .users > .user").Count);
            Assert.Equal(11, host.FindAll(".bqxuuuey > .users > .user > .avatar").Count);
            Assert.Equal(11, host.FindAll(".bqxuuuey > .users > .user > .name").Count);
            Assert.Equal("+5", host.Find(".bqxuuuey > .users > .omitted").TextContent);
            Assert.Contains("max-width: 340px", host.Find(".buebdbiu").GetAttribute("style"), StringComparison.Ordinal);
        });

        await reaction.InvokeAsync(reaction.Instance.HideReactionTooltipAsync);
        host.WaitForAssertion(() => Assert.Contains("hide", VisibilityInterop.Handle.Invocations));
    }

    private RecordingVisibilityInterop VisibilityInterop { get; } = new();

    private void Configure(IReactionDetailsPresentationService details)
    {
        Services.AddSingleton<IReactionViewerInterop>(new RecordingReactionInterop());
        Services.AddSingleton(details);
        Services.AddSingleton<IVisibilityTooltipInterop>(VisibilityInterop);
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IMisskeyTransientFeedbackService, MisskeyTransientFeedbackService>();
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IUserPreviewInterop>(new NoOpUserPreviewInterop());
    }

    private static NoteViewModel Note() => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "9reaction",
        new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
        User(99),
        "reaction fixture",
        null,
        Visibility.Public,
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
        null);

    private static NoteAuthorViewModel User(int index) => new(
        $"9user{index}",
        $"user{index}",
        $"user{index}@remote.example",
        $"User {index}",
        "/static-assets/favicon.png",
        IsBot: false);

    private sealed class ReactionHost : ComponentBase
    {
        [Parameter, EditorRequired]
        public NoteViewModel Note { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MkReactionsViewerReaction>(0);
            builder.AddAttribute(1, nameof(MkReactionsViewerReaction.Note), Note);
            builder.AddAttribute(2, nameof(MkReactionsViewerReaction.Reaction), "🎉");
            builder.AddAttribute(3, nameof(MkReactionsViewerReaction.Count), 15L);
            builder.CloseComponent();
            builder.OpenComponent<OverlayHost>(4);
            builder.CloseComponent();
        }
    }

    private sealed class FixedDetails(IReadOnlyList<NoteAuthorViewModel> users) : IReactionDetailsPresentationService
    {
        public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
            Guid postId,
            string reaction,
            int limit,
            CancellationToken cancellationToken) => Task.FromResult(users);
    }

    private sealed class RecordingReactionInterop : IReactionViewerInterop
    {
        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference target,
            DotNetObjectReference<MkReactionsViewerReaction> receiver,
            bool canToggle,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(new RecordingHandle());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingVisibilityInterop : IVisibilityTooltipInterop
    {
        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachTriggerAsync(
            ElementReference target,
            DotNetObjectReference<MkVisibility> receiver,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(Handle);

        public ValueTask<IJSObjectReference> AttachTooltipAsync(
            ElementReference target,
            ElementReference tooltip,
            DotNetObjectReference<MkTooltip> receiver,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(Handle);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
