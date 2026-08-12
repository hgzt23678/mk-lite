using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class PostFormAttachesTests : BunitContext
{
    private readonly RecordingInterop interop = new();
    private readonly MisskeyOverlayService overlays = new();
    private readonly ComposerMediaViewModel first = Media("first.png", sensitive: false);
    private readonly ComposerMediaViewModel second = Media("second.pdf", sensitive: true, "application/pdf");

    public PostFormAttachesTests()
    {
        Services.AddSingleton<IPostFormAttachesInterop>(interop);
        Services.AddSingleton<IMisskeyOverlayService>(overlays);
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
        Services.AddSingleton<IBlurhashImageInterop>(new NoOpBlurhashInterop());
    }

    [Fact]
    public void PreservesPinnedHiddenRootFilesThumbnailsSensitiveOverlayAndRemainingCount()
    {
        using IRenderedComponent<MkPostFormAttaches> empty = Render<MkPostFormAttaches>(parameters => parameters
            .Add(attaches => attaches.Files, []));
        empty.WaitForAssertion(() => Assert.Equal(1, interop.AttachCalls));
        Assert.Equal("display: none;", empty.Find(".skeikyzd").GetAttribute("style"));
        Assert.Equal("16/16", empty.Find(".remain").TextContent);

        using IRenderedComponent<MkPostFormAttaches> component = Render<MkPostFormAttaches>(parameters => parameters
            .Add(attaches => attaches.Files, new[] { first, second })
            .Add(attaches => attaches.CssClass, "attaches"));
        component.WaitForAssertion(() => Assert.Equal(2, interop.AttachCalls));
        IElement root = component.Find(".skeikyzd.attaches");
        Assert.Null(root.GetAttribute("style"));
        Assert.Equal(2, root.QuerySelectorAll(":scope > .files > .file").Length);
        Assert.Equal(first.Id.ToString(), root.QuerySelector(":scope > .files > .file:first-child > .thumbnail")?.GetAttribute("data-id"));
        Assert.NotNull(root.QuerySelector(":scope > .files > .file:first-child > .thumbnail.zdjebgpv .xubzgfgb.cover"));
        Assert.NotNull(root.QuerySelector(":scope > .files > .file:nth-child(2) > .thumbnail.zdjebgpv > .fa-file-pdf.icon"));
        Assert.NotNull(root.QuerySelector(":scope > .files > .file:nth-child(2) > .sensitive > .fa-exclamation-triangle.icon"));
        Assert.Equal("14/16", root.QuerySelector(":scope > .remain")?.TextContent);
    }

    [Fact]
    public async Task ReorderAndPinnedMenuActionsEmitRealComposeMutationsWithoutDriveStubs()
    {
        IReadOnlyList<ComposerMediaViewModel>? reordered = null;
        (Guid Id, bool Sensitive)? sensitive = null;
        (Guid Id, string Name)? renamed = null;
        Guid? detached = null;
        using IRenderedComponent<MkPostFormAttaches> component = Render<MkPostFormAttaches>(parameters => parameters
            .Add(attaches => attaches.Files, new[] { first, second })
            .Add(attaches => attaches.Updated, value => reordered = value)
            .Add(attaches => attaches.SensitiveChanged, value => sensitive = value)
            .Add(attaches => attaches.NameChanged, value => renamed = value)
            .Add(attaches => attaches.Detach, value => detached = value));
        component.WaitForAssertion(() => Assert.Equal(1, interop.AttachCalls));

        await component.Instance.NotifyReordered([second.Id.ToString(), first.Id.ToString()]);
        Assert.Equal([second.Id, first.Id], reordered?.Select(file => file.Id));
        await component.Instance.NotifyReordered([first.Id.ToString(), first.Id.ToString()]);
        Assert.Equal([second.Id, first.Id], reordered?.Select(file => file.Id));

        component.Find(".file:first-child").Click();
        MisskeyOverlayEntry menu = Assert.Single(overlays.Entries);
        Assert.Equal(
            ["Rename file", "Mark as sensitive", "Add caption", "Remove attachment"],
            menu.MenuItems.Select(item => item.Text));

        await component.InvokeAsync(menu.MenuItems[1].Action!);
        Assert.Equal((first.Id, true), sensitive);
        await component.InvokeAsync(menu.MenuItems[3].Action!);
        Assert.Equal(first.Id, detached);

        await menu.PopupClosed!();
        overlays.Close(menu.Id);
        component.Find(".file:first-child").Click();
        menu = overlays.Entries.Single(entry => entry.Kind == MisskeyOverlayKind.PopupMenu);
        await component.InvokeAsync(menu.MenuItems[0].Action!);
        MisskeyOverlayEntry dialog = overlays.Entries.Single(entry => entry.Kind == MisskeyOverlayKind.Dialog);
        Assert.Equal("Enter filename", dialog.Dialog?.Title);
        Assert.Equal(first.Name, dialog.Dialog?.Input?.Default);
        await component.InvokeAsync(() => dialog.Dialog!.Done!(new(Canceled: false, Result: "renamed.png")));
        Assert.Equal((first.Id, "renamed.png"), renamed);
    }

    [Fact]
    public async Task DetachMediaFunctionTakesThePinnedPrecedenceOverTheDetachEvent()
    {
        Guid? functionId = null;
        int eventCalls = 0;
        using IRenderedComponent<MkPostFormAttaches> component = Render<MkPostFormAttaches>(parameters => parameters
            .Add(attaches => attaches.Files, new[] { first })
            .Add(attaches => attaches.DetachMedia, id =>
            {
                functionId = id;
                return Task.CompletedTask;
            })
            .Add(attaches => attaches.Detach, _ => eventCalls++));

        component.Find(".file").Click();
        MisskeyOverlayEntry menu = Assert.Single(overlays.Entries);
        await component.InvokeAsync(menu.MenuItems[3].Action!);
        Assert.Equal(first.Id, functionId);
        Assert.Equal(0, eventCalls);
    }

    private static ComposerMediaViewModel Media(
        string name,
        bool sensitive,
        string mediaType = "image/png") => new(
        Guid.NewGuid(),
        name,
        mediaType,
        "/static-assets/favicon.png",
        "/static-assets/favicon.png",
        sensitive,
        null,
        64,
        64,
        1024);

    private sealed class RecordingInterop : IPostFormAttachesInterop
    {
        public int AttachCalls { get; private set; }

        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference files,
            DotNetObjectReference<MkPostFormAttaches> receiver,
            CancellationToken cancellationToken)
        {
            _ = files;
            _ = receiver;
            cancellationToken.ThrowIfCancellationRequested();
            AttachCalls++;
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpBlurhashInterop : IBlurhashImageInterop
    {
        public ValueTask<bool> DrawAsync(
            ElementReference canvas,
            ElementReference image,
            string? hash,
            int size,
            CancellationToken cancellationToken)
        {
            _ = canvas;
            _ = image;
            _ = hash;
            _ = size;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged
        {
            add { }
            remove { }
        }

        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _ = arguments;
            return key switch
            {
                "renameFile" => "Rename file",
                "markAsSensitive" => "Mark as sensitive",
                "unmarkAsSensitive" => "Unmark as sensitive",
                "describeFile" => "Add caption",
                "attachCancel" => "Remove attachment",
                "enterFileName" => "Enter filename",
                "inputNewDescription" => "Enter caption",
                _ => key
            };
        }

        public bool TrySelectLocale(string? locale) => false;
    }
}
