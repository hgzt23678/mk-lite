using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class CalendarWidgetTests : BunitContext
{
    private readonly RecordingCalendarInterop browser = new();

    public CalendarWidgetTests()
    {
        Services.AddSingleton<ICalendarWidgetInterop>(browser);
        Services.AddSingleton<IMisskeyLocalizer>(new CalendarLocalizer());
    }

    [Fact]
    public async Task PreservesPinnedCalendarDomAndBrowserLocalProgress()
    {
        using IRenderedComponent<MkwCalendar> component = Render<MkwCalendar>();
        Assert.Single(browser.Attachments);

        await component.InvokeAsync(() => component.Instance.UpdateCalendar(
            new CalendarWidgetSnapshot(2026, 1, 1, 4, 25.04, 0.81, 0.22)));

        IElement root = component.Find(".mkw-calendar._panel");
        Assert.Equal(["calendar", "info"], root.Children.Select(child => child.ClassName));
        Assert.DoesNotContain("isHoliday", component.Find(".calendar").ClassList);
        Assert.Equal("2026年1月", component.Find(".month-and-year").TextContent);
        Assert.Equal("🎉1日🎉", component.Find("p.day").TextContent);
        Assert.Equal("木曜日", component.Find(".week-day").TextContent);
        Assert.Equal(["今日: 25.0%", "今月: 0.8%", "今年: 0.2%"],
            component.FindAll(".info > div > p").Select(item => item.TextContent.Trim()));
        Assert.Equal("width: 25.04%;", component.Find(".info > div:nth-child(1) .val").GetAttribute("style"));
    }

    [Fact]
    public async Task TransparentHolidayStateAndDisposalMatchTheWidgetContract()
    {
        IRenderedComponent<MkwCalendar> component = Render<MkwCalendar>(parameters => parameters
            .Add(widget => widget.Transparent, true));

        await component.InvokeAsync(() => component.Instance.UpdateCalendar(
            new CalendarWidgetSnapshot(2026, 8, 2, 0, 10, 20, 30)));

        Assert.Equal("mkw-calendar", component.Find(".mkw-calendar").ClassName);
        Assert.Contains("isHoliday", component.Find(".calendar").ClassList);
        await component.Instance.DisposeAsync();
        Assert.Equal(1, browser.Handle.DisposeCalls);
        Assert.True(browser.Handle.ReferenceDisposed);
    }

    [Fact]
    public async Task RejectsInvalidBrowserSnapshots()
    {
        using IRenderedComponent<MkwCalendar> component = Render<MkwCalendar>();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => component.Instance.UpdateCalendar(
            new CalendarWidgetSnapshot(2026, 13, 1, 0, 0, 0, 0)));
    }

    private sealed class RecordingCalendarInterop : ICalendarWidgetInterop
    {
        public List<ElementReference> Attachments { get; } = [];
        public RecordingHandle Handle { get; } = new();

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference element,
            DotNetObjectReference<MkwCalendar> receiver,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(receiver);
            Attachments.Add(element);
            return ValueTask.FromResult<IJSObjectReference>(Handle);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHandle : IJSObjectReference
    {
        public int DisposeCalls { get; private set; }
        public bool ReferenceDisposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier == "dispose")
            {
                DisposeCalls++;
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            ReferenceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CalendarLocalizer : IMisskeyLocalizer
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
            "yearX" => $"{arguments!["year"]}年",
            "monthX" => $"{arguments!["month"]}月",
            "dayX" => $"{arguments!["day"]}日",
            "_weekday.sunday" => "日曜日",
            "_weekday.monday" => "月曜日",
            "_weekday.tuesday" => "火曜日",
            "_weekday.wednesday" => "水曜日",
            "_weekday.thursday" => "木曜日",
            "_weekday.friday" => "金曜日",
            "_weekday.saturday" => "土曜日",
            "today" => "今日",
            "thisMonth" => "今月",
            "thisYear" => "今年",
            _ => key
        };

        public bool TrySelectLocale(string? locale) => false;
    }
}
