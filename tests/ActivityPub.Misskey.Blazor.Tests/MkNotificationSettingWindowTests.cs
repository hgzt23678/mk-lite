using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class MkNotificationSettingWindowTests : BunitContext
{
    public MkNotificationSettingWindowTests()
    {
        Services.AddSingleton<IMisskeyLocalizer>(new Localizer());
        Services.AddScoped<IDialogWindowInterop, DisconnectedDialogWindowInterop>();
        Services.AddScoped<IButtonRippleInterop, DisconnectedButtonRippleInterop>();
    }

    [Fact]
    public void PreservesV12TypeOrderBulkActionsAndDoneProjection()
    {
        NotificationSettingResult? result = null;
        using IRenderedComponent<MkNotificationSettingWindow> component = Render<MkNotificationSettingWindow>(parameters => parameters
            .Add(item => item.IncludingTypes, new HashSet<MisskeyNotificationType>
            {
                MisskeyNotificationType.Mention,
                MisskeyNotificationType.Reaction
            })
            .Add(item => item.Done, value => result = value));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("通知設定", component.Find(".title").TextContent);
            Assert.Equal(
                ["follow", "mention", "reply", "renote", "quote", "reaction", "pollVote", "pollEnded", "receiveFollowRequest", "followRequestAccepted", "groupInvited", "app"],
                component.FindAll(".ziffeomt .label > span").Select(value => value.TextContent.Trim()).Skip(1));
            Assert.Equal(13, component.FindAll(".ziffeomt").Count);
        });

        Assert.Equal(2, component.FindAll(".ziffeomt.checked").Count);
        component.Find("button[aria-label='disableAll']").Click();
        Assert.Empty(component.FindAll(".ziffeomt.checked"));
        component.Find("button[aria-label='enableAll']").Click();
        Assert.Equal(12, component.FindAll(".ziffeomt.checked").Count);

        component.Find("button[aria-label='ok']").Click();
        Assert.NotNull(result);
        Assert.Equal(12, result!.IncludingTypes!.Count);
    }

    [Fact]
    public void EmptyIncludingTypesUsesGlobalSettingAndReturnsNull()
    {
        NotificationSettingResult? result = null;
        using IRenderedComponent<MkNotificationSettingWindow> component = Render<MkNotificationSettingWindow>(parameters => parameters
            .Add(item => item.IncludingTypes, new HashSet<MisskeyNotificationType>())
            .Add(item => item.Done, value => result = value));

        Assert.Single(component.FindAll(".ziffeomt"));
        Assert.Empty(component.FindAll("button[aria-label='disableAll']"));
        component.Find("button[aria-label='ok']").Click();
        Assert.NotNull(result);
        Assert.Null(result!.IncludingTypes);
    }

    private sealed class DisconnectedDialogWindowInterop : IDialogWindowInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync<T>(
            ElementReference modal,
            ElementReference content,
            ElementReference window,
            DotNetObjectReference<T> receiver,
            CancellationToken cancellationToken) where T : class =>
            throw new JSDisconnectedException("bUnit has no browser transition runtime.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DisconnectedButtonRippleInterop : IButtonRippleInterop, IDisposable
    {
        public ValueTask<IJSObjectReference> AttachAsync(ElementReference element, CancellationToken cancellationToken) =>
            throw new JSDisconnectedException("bUnit has no browser ripple runtime.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class Localizer : IMisskeyLocalizer
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

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null) => key switch
        {
            "notificationSetting" => "通知設定",
            "ok" => "ok",
            "close" => "close",
            "disableAll" => "disableAll",
            "enableAll" => "enableAll",
            "_notification._types.follow" => "follow",
            "_notification._types.mention" => "mention",
            "_notification._types.reply" => "reply",
            "_notification._types.renote" => "renote",
            "_notification._types.quote" => "quote",
            "_notification._types.reaction" => "reaction",
            "_notification._types.pollVote" => "pollVote",
            "_notification._types.pollEnded" => "pollEnded",
            "_notification._types.receiveFollowRequest" => "receiveFollowRequest",
            "_notification._types.followRequestAccepted" => "followRequestAccepted",
            "_notification._types.groupInvited" => "groupInvited",
            "_notification._types.app" => "app",
            _ => key
        };
        public bool TrySelectLocale(string? locale) => false;
    }
}
