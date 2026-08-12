using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class FormFolderTests : BunitContext
{
    public FormFolderTests()
    {
        Services.AddSingleton<ISpacerInterop>(new DisconnectedSpacerInterop());
        Services.AddSingleton<IPizzaxDeviceState>(new EmptyDeviceState());
    }

    [Fact]
    public void LazilyCreatesThenKeepsThePinnedBodyAndSlotsAcrossToggle()
    {
        IRenderedComponent<MkFormFolder> component = Render<MkFormFolder>(parameters => parameters
            .Add(folder => folder.Icon, builder => builder.AddMarkupContent(0, "<i class=\"fas fa-cog\"></i>"))
            .Add(folder => folder.Label, builder => builder.AddContent(0, "詳細設定"))
            .Add(folder => folder.Suffix, builder => builder.AddContent(0, "2項目"))
            .AddChildContent("保持される設定")
            .AddUnmatched("class", "fixture-folder")
            .AddUnmatched("data-folder", "advanced"));

        Assert.NotNull(component.Find(".dwzlatin.fixture-folder[data-folder=advanced] > .header._button"));
        Assert.Equal("詳細設定", component.Find(".header > .text").TextContent);
        Assert.Equal("2項目", component.Find(".header > .right > .text").TextContent);
        Assert.NotNull(component.Find(".header > .right > .fa-angle-down"));
        Assert.Empty(component.FindAll(".dwzlatin > .body"));

        component.Find(".dwzlatin > .header").Click();
        Assert.Contains("opened", component.Find(".dwzlatin").ClassName, StringComparison.Ordinal);
        Assert.Equal("保持される設定", component.Find(".dwzlatin > .body").TextContent.Trim());
        Assert.NotNull(component.Find(".header > .right > .fa-angle-up"));

        component.Find(".dwzlatin > .header").Click();
        Assert.DoesNotContain("opened", component.Find(".dwzlatin").ClassName, StringComparison.Ordinal);
        Assert.Equal("display: none;", component.Find(".dwzlatin > .body").GetAttribute("style"));
        Assert.Equal("保持される設定", component.Find(".dwzlatin > .body").TextContent.Trim());
    }

    [Fact]
    public void DefaultOpenCreatesTheBodyAndUpChevronOnFirstRender()
    {
        IRenderedComponent<MkFormFolder> component = Render<MkFormFolder>(parameters => parameters
            .Add(folder => folder.DefaultOpen, true)
            .Add(folder => folder.Label, builder => builder.AddContent(0, "Open"))
            .AddChildContent("Body"));

        Assert.Contains("opened", component.Find(".dwzlatin").ClassName, StringComparison.Ordinal);
        Assert.Null(component.Find(".dwzlatin > .body").GetAttribute("style"));
        Assert.NotNull(component.Find(".fa-angle-up"));
    }

    private sealed class EmptyDeviceState : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(fallback);

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class DisconnectedSpacerInterop : ISpacerInterop
    {
        public ValueTask<IJSObjectReference> ObserveAsync<T>(
            ElementReference element,
            SpacerObservationOptions options,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class =>
            throw new JSDisconnectedException("bUnit has no ResizeObserver.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
