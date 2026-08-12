using System.Text.Json;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class NotePreviewTests : BunitContext
{
    [Fact]
    public void PreservesPinnedHierarchyUserNameTrimmedMfmAndAttributeFallthrough()
    {
        Configure();
        NoteAuthorViewModel account = Account();

        IRenderedComponent<MkNotePreview> component = Render<MkNotePreview>(parameters => parameters
            .Add(value => value.Text, " \n preview text :party: \n ")
            .Add(value => value.Account, account)
            .Add(value => value.CssClass, "component-class")
            .AddUnmatched("class", "fallthrough-class")
            .AddUnmatched("data-contract", "note-preview")
            .AddUnmatched("aria-label", "投稿プレビュー"));

        IElement root = component.Find(".fefdfafb.component-class.fallthrough-class");
        Assert.Equal("note-preview", root.GetAttribute("data-contract"));
        Assert.Equal("投稿プレビュー", root.GetAttribute("aria-label"));
        Assert.Equal(2, root.Children.Length);
        Assert.NotNull(root.QuerySelector(":scope > .avatar.eiwwqkts"));
        Assert.Equal("Alice", root.QuerySelector(":scope > .main > .header")?.TextContent.Trim());
        Assert.Equal(
            "preview text :party:",
            root.QuerySelector(":scope > .main > .body > .content > .havbbuyv")?.TextContent);
        Assert.Single(component.FindComponents<MkUserName>());
        IReadOnlyList<IRenderedComponent<MfmView>> mfm = component.FindComponents<MfmView>();
        Assert.Equal(2, mfm.Count);
        MfmView preview = Assert.Single(
            mfm.Select(value => value.Instance),
            value => value.Text == "preview text :party:");
        Assert.Same(account, preview.Author);
        Assert.Same(account.Emojis, preview.CustomEmojis);
    }

    [Fact]
    public async Task AppliesPinnedSizeThresholdsAndDisposesTheObservation()
    {
        RecordingElementSizeInterop size = Configure();
        IRenderedComponent<MkNotePreview> component = Render<MkNotePreview>(parameters => parameters
            .Add(value => value.Text, "preview")
            .Add(value => value.Account, Account()));

        component.WaitForAssertion(() => Assert.Equal(1, size.ObserveCalls));
        await component.Instance.UpdateElementSize(350, 1280);
        component.WaitForAssertion(() =>
        {
            IElement root = component.Find(".fefdfafb");
            Assert.Contains("min-width_350px", root.ClassList);
            Assert.DoesNotContain("min-width_500px", root.ClassList);
        });

        await component.Instance.UpdateElementSize(500, 1280);
        component.WaitForAssertion(() =>
        {
            IElement root = component.Find(".fefdfafb");
            Assert.Contains("min-width_350px", root.ClassList);
            Assert.Contains("min-width_500px", root.ClassList);
        });

        await component.Instance.UpdateElementSize(349, 1280);
        component.WaitForAssertion(() =>
        {
            IElement root = component.Find(".fefdfafb");
            Assert.DoesNotContain("min-width_350px", root.ClassList);
            Assert.DoesNotContain("min-width_500px", root.ClassList);
        });

        await component.Instance.DisposeAsync();
        Assert.True(size.ObservationToken.IsCancellationRequested);
        Assert.Equal(1, size.Handle.DisposeInvocations);
        Assert.Equal(1, size.Handle.DisposeCalls);
    }

    private RecordingElementSizeInterop Configure()
    {
        Services.AddSingleton<IMfmParserInterop>(new PlainMfmParser());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState());
        var size = new RecordingElementSizeInterop();
        Services.AddSingleton<IElementSizeInterop>(size);
        JSInterop.Mode = JSRuntimeMode.Loose;
        return size;
    }

    private static NoteAuthorViewModel Account() => new(
        "alice-id",
        "alice",
        "alice",
        "Alice",
        "/static-assets/user-unknown.png",
        IsBot: false,
        Emojis: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["party"] = "/static-assets/favicon.png"
        });

    private sealed class PlainMfmParser : IMfmParserInterop
    {
        public ValueTask<IReadOnlyList<MfmNode>> ParseAsync(
            string text,
            bool plain,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<MfmNode>>(
                [new("text", JsonSerializer.SerializeToElement(new { text }), null)]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(fallback);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingElementSizeInterop : IElementSizeInterop
    {
        public RecordingHandle Handle { get; } = new();
        public int ObserveCalls { get; private set; }
        public CancellationToken ObservationToken { get; private set; }

        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class
        {
            _ = element;
            _ = receiver;
            ObserveCalls++;
            ObservationToken = cancellationToken;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeInvocations { get; private set; }
        public int DisposeCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            _ = args;
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(identifier, "dispose", StringComparison.Ordinal))
            {
                DisposeInvocations++;
            }
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
