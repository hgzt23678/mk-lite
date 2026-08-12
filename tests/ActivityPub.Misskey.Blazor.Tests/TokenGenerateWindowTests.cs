using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class TokenGenerateWindowTests : BunitContext
{
    private readonly NoOpBrowser browser = new();

    public TokenGenerateWindowTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new TokenLocalizer());
        Services.AddSingleton<IMisskeyOverlayService, MisskeyOverlayService>();
        Services.AddSingleton<IDialogWindowInterop>(browser);
        Services.AddSingleton<IFormInputInterop>(browser);
        Services.AddSingleton<IButtonRippleInterop>(browser);
    }

    [Fact]
    public void RendersPinnedWindowHeaderInputAndEveryPermissionSwitch()
    {
        using IRenderedComponent<MkTokenGenerateWindow> component = Render<MkTokenGenerateWindow>(parameters => parameters
            .Add(window => window.Title, "My token"));

        Assert.Equal("width: 400px; height: 450px;", component.Find(".qzhlnise > .content > .ebkgoccj").GetAttribute("style"));
        Assert.Equal("My token", component.Find(".ebkgoccj > .header > .title").TextContent.Trim());
        Assert.NotNull(component.Find(".ebkgoccj > .body input"));
        Assert.Equal(MisskeyPermissions.All.Count, component.FindAll(".ebkgoccj > .body .ziffeomt").Count);
        Assert.Equal(2, component.FindAll(".ebkgoccj > .body button._button").Count);
    }

    private static readonly string[] InitialPermissions = ["read:account", "write:notes"];

    [Fact]
    public void InitialPermissionsAreCheckedAndOkEmitsGrantedPermissionsOnce()
    {
        var results = new List<MkTokenGenerateWindowResult>();
        using IRenderedComponent<MkTokenGenerateWindow> component = Render<MkTokenGenerateWindow>(parameters => parameters
            .Add(window => window.InitialName, "cli")
            .Add(window => window.InitialPermissions, InitialPermissions)
            .Add(window => window.Done, result => results.Add(result)));

        Assert.Equal("cli", component.Find("input[type=text]").GetAttribute("value"));
        Assert.Contains(component.FindAll(".ebkgoccj > .body .ziffeomt.checked"), toggle =>
            toggle.TextContent.Contains("read:account", StringComparison.Ordinal));
        Assert.Contains(component.FindAll(".ebkgoccj > .body .ziffeomt.checked"), toggle =>
            toggle.TextContent.Contains("write:notes", StringComparison.Ordinal));
        Assert.Equal(InitialPermissions.Length, component.FindAll(".ebkgoccj > .body .ziffeomt").Count);

        component.Find(".ebkgoccj > .header > button[aria-label=決定]").Click();

        MkTokenGenerateWindowResult result = Assert.Single(results);
        Assert.Equal("cli", result.Name);
        Assert.Equal(InitialPermissions, result.Permissions);
    }

    [Fact]
    public void DisableAllAndEnableAllToggleEveryPermission()
    {
        var results = new List<MkTokenGenerateWindowResult>();
        using IRenderedComponent<MkTokenGenerateWindow> component = Render<MkTokenGenerateWindow>(parameters => parameters
            .Add(window => window.InitialName, "cli")
            .Add(window => window.Done, result => results.Add(result)));

        IReadOnlyList<IElement> actions = component.FindAll(".ebkgoccj > .body button._button");
        actions[0].Click();
        component.Find(".ebkgoccj > .header > button[aria-label=決定]").Click();
        Assert.Empty(Assert.Single(results).Permissions);

        results.Clear();
        actions[^1].Click();
        component.Find(".ebkgoccj > .header > button[aria-label=決定]").Click();
        Assert.Equal(MisskeyPermissions.All.OrderBy(p => p), Assert.Single(results).Permissions.OrderBy(p => p));
    }

    private sealed class NoOpBrowser : IDialogWindowInterop, IFormInputInterop, IButtonRippleInterop
    {
        private readonly NoOpJsObject handle = new();

        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference input,
            ElementReference prefix,
            ElementReference suffix,
            bool autofocus,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            CancellationToken cancellationToken) => ValueTask.FromResult<IJSObjectReference>(handle);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpJsObject : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TokenLocalizer : IMisskeyLocalizer
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            _ = arguments;
            return key switch
            {
                "generateAccessToken" => "generateAccessToken",
                "name" => "name",
                "permission" => "permission",
                "disableAll" => "disableAll",
                "enableAll" => "enableAll",
                _ => key
            };
        }

        public bool TrySelectLocale(string? locale) =>
            string.Equals(locale, CurrentLocale, StringComparison.Ordinal);
    }
}
