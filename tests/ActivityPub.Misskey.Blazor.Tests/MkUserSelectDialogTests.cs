using ActivityPub.Application;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MkUserSelectDialogTests : BunitContext
{
    private readonly DeviceState device = new(["recent-id"]);
    private readonly UserPreviewViewModel recent = User("recent-id", "recent");
    private readonly UserPreviewViewModel result = User("result-id", "alice");

    public MkUserSelectDialogTests()
    {
        Services.AddSingleton<IPizzaxDeviceState>(device);
        Services.AddSingleton<IUserSearchPresentationService>(new SearchService(() => result));
        Services.AddSingleton<IUserPreviewPresentationService>(new PreviewService(recent, result));
        Services.AddSingleton<IMisskeyLocalizer>(new Localizer());
        Services.AddScoped<IDialogWindowInterop, DisconnectedDialogWindowInterop>();
        Services.AddScoped<IFormInputInterop, DisconnectedFormInputInterop>();
        ComponentFactories.AddStub<MkAvatar>();
        ComponentFactories.AddStub<MkUserName>();
        ComponentFactories.AddStub<MkAcct>();
    }

    [Fact]
    public void PreservesRecentSearchSelectionAndPersistentRecentUserContract()
    {
        UserPreviewViewModel? selected = null;
        using IRenderedComponent<MkUserSelectDialog> component = Render<MkUserSelectDialog>(parameters => parameters
            .Add(item => item.Ok, value => selected = value));

        component.WaitForAssertion(() => Assert.Contains("recent", component.Markup));
        Assert.Single(component.FindAll(".recent .user"));
        component.FindAll("input")[0].Input("alice");
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".result .user")));
        component.Find(".result .user").Click();
        Assert.Contains("selected", component.Find(".result .user").ClassList);

        component.Find("button[aria-label='ok']").Click();
        Assert.Equal("result-id", selected?.Id);
        Assert.Equal(["result-id", "recent-id"], device.Values["recentlyUsedUsers"]);
    }

    private static UserPreviewViewModel User(string id, string username) =>
        new(Guid.NewGuid(), id, new($"{id}-actor", username, username, username, "https://local.example/avatar.png", false), string.Empty, null, 0, 0, 0, false, true, false, false, false);

    private sealed class SearchService(Func<UserPreviewViewModel> result) : IUserSearchPresentationService
    {
        public Task<IReadOnlyList<UserPreviewViewModel>> SearchAsync(string query, string origin, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserPreviewViewModel>>([result()]);
    }

    private sealed class PreviewService(UserPreviewViewModel recent, UserPreviewViewModel result) : IUserPreviewPresentationService
    {
        public Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(query == recent.Id ? recent : result);
        public Task<UserPreviewViewModel> FollowAsync(UserPreviewViewModel user, string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult(user);
        public Task<UserPreviewViewModel> UnfollowAsync(UserPreviewViewModel user, string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult(user);
    }

    private sealed class DeviceState(IEnumerable<string> initial) : IPizzaxDeviceState
    {
        public Dictionary<string, string[]> Values { get; } = new(StringComparer.Ordinal)
        {
            ["recentlyUsedUsers"] = initial.ToArray()
        };

        public ValueTask<T> ReadAsync<T>(string propertyName, T fallback, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(string[]) && Values.TryGetValue(propertyName, out string[]? values))
            {
                return ValueTask.FromResult((T)(object)values);
            }
            return ValueTask.FromResult(fallback);
        }

        public ValueTask WriteAsync<T>(string propertyName, T value, CancellationToken cancellationToken = default)
        {
            Values[propertyName] = Assert.IsType<string[]>(value);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisconnectedDialogWindowInterop : IDialogWindowInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(ElementReference modal, ElementReference content, ElementReference window, DotNetObjectReference<T> receiver, CancellationToken cancellationToken) where T : class =>
            throw new JSDisconnectedException("bUnit has no dialog runtime.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DisconnectedFormInputInterop : IFormInputInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference input, ElementReference prefix, ElementReference suffix, bool autofocus, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit has no input runtime.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class Localizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];
        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key;
        public bool TrySelectLocale(string? locale) => false;
    }
}
