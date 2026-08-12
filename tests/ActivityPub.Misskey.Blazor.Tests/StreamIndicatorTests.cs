using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.State;
using ActivityPub.Misskey.Blazor.Streaming;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class StreamIndicatorTests : BunitContext
{
    [Fact]
    public async Task QuietDisconnectPreservesPinnedDomDismissesReloadsAndUnsubscribes()
    {
        var status = new RecordingStatus(isDisconnected: true);
        var browser = new RecordingBrowser();
        Register(status, browser, "quiet");

        IRenderedComponent<MisskeyStreamIndicator> component = Render<MisskeyStreamIndicator>();

        component.WaitForAssertion(() =>
        {
            IElement root = component.Find(".nsbbhtug");
            Assert.Equal(2, root.Children.Length);
            Assert.Equal("サーバーから切断されました", root.Children[0].TextContent);
            IElement command = root.Children[1];
            Assert.Equal("command", command.ClassName);
            IElement[] buttons = command.QuerySelectorAll(":scope > button._textButton").Cast<IElement>().ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.Equal("リロード", buttons[0].TextContent);
            Assert.Equal("なにもしない", buttons[1].TextContent);
        });
        Assert.Equal(1, status.SubscriberCount);

        await component.Find(".command > button:last-child").ClickAsync(new());
        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".nsbbhtug")));
        Assert.Equal(1, status.ResetCalls);

        status.ReportDisconnected();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".nsbbhtug")));
        await component.Find(".command > button:first-child").ClickAsync(new());
        Assert.Equal(1, browser.ReloadCalls);
        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".nsbbhtug")));
        Assert.Equal(2, status.ResetCalls);

        await component.Instance.DisposeAsync();
        Assert.Equal(0, status.SubscriberCount);
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("dialog")]
    [InlineData("invalid")]
    public void NonQuietBehaviorNeverRendersTheQuietIndicator(string behavior)
    {
        var status = new RecordingStatus(isDisconnected: true);
        Register(status, new RecordingBrowser(), behavior);

        IRenderedComponent<MisskeyStreamIndicator> component = Render<MisskeyStreamIndicator>();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".nsbbhtug")));
        status.ReportDisconnected();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".nsbbhtug")));
    }

    [Fact]
    public void ConnectionStatusEmitsOnlyDisconnectedTransitionsAndResetArmsTheNextOne()
    {
        var status = new MisskeyStreamConnectionStatus();
        int disconnected = 0;
        status.Disconnected += (_, _) => disconnected++;

        status.ReportDisconnected();
        status.ReportDisconnected();
        Assert.True(status.IsDisconnected);
        Assert.Equal(1, disconnected);

        status.ReportConnected();
        Assert.False(status.IsDisconnected);
        status.ReportDisconnected();
        Assert.Equal(2, disconnected);

        status.Reset();
        Assert.False(status.IsDisconnected);
        status.ReportDisconnected();
        Assert.Equal(3, disconnected);
    }

    private void Register(
        IMisskeyStreamConnectionStatus status,
        IStreamIndicatorInterop browser,
        string behavior)
    {
        Services.AddSingleton(status);
        Services.AddSingleton(browser);
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(behavior));
        Services.AddSingleton<IMisskeyLocalizer>(new FixedLocalizer());
    }

    private sealed class RecordingStatus(bool isDisconnected) : IMisskeyStreamConnectionStatus
    {
        private EventHandler? disconnected;

        public event EventHandler? Disconnected
        {
            add
            {
                disconnected += value;
                SubscriberCount++;
            }
            remove
            {
                disconnected -= value;
                SubscriberCount--;
            }
        }

        public bool IsDisconnected { get; private set; } = isDisconnected;
        public int ResetCalls { get; private set; }
        public int SubscriberCount { get; private set; }

        public void ReportConnected() => IsDisconnected = false;

        public void ReportDisconnected()
        {
            IsDisconnected = true;
            disconnected?.Invoke(this, EventArgs.Empty);
        }

        public void Reset()
        {
            ResetCalls++;
            IsDisconnected = false;
        }
    }

    private sealed class RecordingBrowser : IStreamIndicatorInterop
    {
        public int ReloadCalls { get; private set; }

        public ValueTask ReloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReloadCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedDeviceState(string behavior) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("serverDisconnectedBehavior", propertyName);
            return ValueTask.FromResult((T)(object)behavior);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
            "disconnectedFromServer" => "サーバーから切断されました",
            "reload" => "リロード",
            "doNothing" => "なにもしない",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
