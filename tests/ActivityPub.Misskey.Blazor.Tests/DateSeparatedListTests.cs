using System.Globalization;
using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.State;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class DateSeparatedListTests : BunitContext
{
    private readonly RecordingDateSeparatedListInterop browser = new();

    public DateSeparatedListTests()
    {
        Services.AddSingleton<IDateSeparatedListInterop>(browser);
        Services.AddSingleton<IMisskeyLocalizer>(new DateListLocalizer());
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: true));
    }

    [Fact]
    public void PreservesSeparatorPrecedenceAdvertisementOrderAndDayOnlyComparison()
    {
        browser.CalendarParts = [new(1, 31), new(2, 1), new(3, 1)];
        DateListItem[] items =
        [
            new("a", DateTimeOffset.Parse("2026-01-31T12:00:00Z", CultureInfo.InvariantCulture), true),
            new("b", DateTimeOffset.Parse("2026-02-01T12:00:00Z", CultureInfo.InvariantCulture), true),
            new("c", DateTimeOffset.Parse("2026-03-01T12:00:00Z", CultureInfo.InvariantCulture), true)
        ];

        using IRenderedComponent<MkDateSeparatedList<DateListItem>> component = RenderList(
            items,
            ad: true,
            noGap: true,
            additionalAttributes: new Dictionary<string, object>
            {
                ["class"] = "notes",
                ["aria-live"] = "polite"
            });

        component.WaitForAssertion(() =>
        {
            IElement root = component.Find(".sqadhkmv");
            Assert.Equal("sqadhkmv noGap notes", root.ClassName);
            Assert.Equal("down", root.GetAttribute("data-direction"));
            Assert.Equal("false", root.GetAttribute("data-reversed"));
            Assert.Equal("polite", root.GetAttribute("aria-live"));
            Assert.Equal(
                ["item:a", "separator:1/31|2/1", "ad:b", "item:b", "ad:c", "item:c"],
                root.Children.Select(DescribeChild));
            Assert.True(browser.Attached);
        });
    }

    [Fact]
    public void BrowserCalendarPartsCorrectServerFallbackAfterHydration()
    {
        browser.CalendarParts = [new(4, 3), new(4, 2)];
        DateListItem[] items =
        [
            new("a", DateTimeOffset.Parse("2026-04-03T00:30:00Z", CultureInfo.InvariantCulture)),
            new("b", DateTimeOffset.Parse("2026-04-03T00:15:00Z", CultureInfo.InvariantCulture))
        ];

        using IRenderedComponent<MkDateSeparatedList<DateListItem>> component = RenderList(items);

        component.WaitForAssertion(() =>
        {
            Assert.Single(component.FindAll(".separator"));
            Assert.Equal("4/3", component.Find(".separator span:first-child").TextContent.Trim());
            Assert.Equal("4/2", component.Find(".separator span:last-child").TextContent.Trim());
            Assert.Single(browser.CalendarRequests);
        });
    }

    [Fact]
    public void AnimationDisabledUsesPlainDivContractWithoutTransitionAttributes()
    {
        Services.AddSingleton<IPizzaxDeviceState>(new FixedDeviceState(animation: false));
        DateListItem[] items = [new("a", DateTimeOffset.UtcNow)];

        using IRenderedComponent<MkDateSeparatedList<DateListItem>> component = RenderList(
            items,
            reversed: true,
            direction: "up");

        component.WaitForAssertion(() =>
        {
            IElement root = component.Find(".sqadhkmv");
            Assert.Null(root.GetAttribute("data-direction"));
            Assert.Null(root.GetAttribute("data-reversed"));
            Assert.False(browser.Attached);
        });
    }

    [Fact]
    public void EmptyListReturnsNoDomAndDoesNotAttach()
    {
        using IRenderedComponent<MkDateSeparatedList<DateListItem>> component = RenderList([]);

        Assert.Empty(component.Nodes);
        Assert.Empty(browser.CalendarRequests);
        Assert.False(browser.Attached);
    }

    [Fact]
    public void AdvertisementRequestWithoutRealContentFailsExplicitly()
    {
        DateListItem[] items = [new("a", DateTimeOffset.UtcNow, ShouldInsertAdvertisement: true)];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            RenderList(items, ad: true, includeAdvertisementContent: false));

        Assert.Contains("AdvertisementContent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReordersWithoutRecalculatingDatesAndRequestsOnlyNewItems()
    {
        DateListItem[] items =
        [
            new("a", DateTimeOffset.Parse("2026-04-03T00:00:00Z", CultureInfo.InvariantCulture)),
            new("b", DateTimeOffset.Parse("2026-04-02T00:00:00Z", CultureInfo.InvariantCulture))
        ];
        IRenderedComponent<MkDateSeparatedList<DateListItem>> component = RenderList(items);
        component.WaitForAssertion(() =>
        {
            Assert.True(browser.Attached);
            Assert.Single(browser.CalendarRequests);
            Assert.Equal(2, browser.CalendarRequests[0].Count);
        });

        DateListItem[] reordered = [items[1], items[0]];
        component.Render(parameters => parameters.Add(value => value.Items, reordered));
        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["b", "a"],
                component.FindAll("[data-date-item]").Select(element => element.GetAttribute("data-date-item")));
            Assert.Single(browser.CalendarRequests);
        });

        var prepended = new DateListItem(
            "live",
            DateTimeOffset.Parse("2026-04-04T00:00:00Z", CultureInfo.InvariantCulture));
        DateListItem[] extended = [prepended, .. reordered];
        component.Render(parameters => parameters.Add(value => value.Items, extended));
        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, browser.CalendarRequests.Count);
            Assert.Equal([prepended.CreatedAt.ToUnixTimeMilliseconds()], browser.CalendarRequests[1]);
        });

        await DisposeComponentsAsync();
        Assert.True(browser.Reference.Disposed);
    }

    private IRenderedComponent<MkDateSeparatedList<DateListItem>> RenderList(
        IReadOnlyList<DateListItem> items,
        bool ad = false,
        bool noGap = false,
        bool reversed = false,
        string direction = "down",
        bool includeAdvertisementContent = true,
        IReadOnlyDictionary<string, object>? additionalAttributes = null)
    {
        RenderFragment<DateListItem> content = item => builder =>
        {
            builder.OpenElement(0, "article");
            builder.AddAttribute(1, "data-date-item", item.Id);
            builder.AddContent(2, item.Id);
            builder.CloseElement();
        };
        RenderFragment<DateListItem> advertisement = item => builder =>
        {
            builder.OpenElement(0, "aside");
            builder.AddAttribute(1, "class", "a");
            builder.AddAttribute(2, "data-ad-item", item.Id);
            builder.AddContent(3, "advertisement");
            builder.CloseElement();
        };

        return Render<MkDateSeparatedList<DateListItem>>(parameters =>
        {
            parameters.Add(component => component.Items, items);
            parameters.Add(component => component.GetId, static item => item.Id);
            parameters.Add(component => component.GetCreatedAt, static item => item.CreatedAt);
            parameters.Add(component => component.ShouldInsertAdvertisement, static item => item.ShouldInsertAdvertisement);
            parameters.Add(component => component.ChildContent, content);
            parameters.Add(component => component.Direction, direction);
            parameters.Add(component => component.Reversed, reversed);
            parameters.Add(component => component.NoGap, noGap);
            parameters.Add(component => component.Ad, ad);
            if (includeAdvertisementContent)
            {
                parameters.Add(component => component.AdvertisementContent, advertisement);
            }
            if (additionalAttributes is not null)
            {
                parameters.Add(component => component.AdditionalAttributes, additionalAttributes);
            }
        });
    }

    private static string DescribeChild(IElement element)
    {
        if (element.HasAttribute("data-date-item"))
        {
            return "item:" + element.GetAttribute("data-date-item");
        }
        if (element.HasAttribute("data-ad-item"))
        {
            return "ad:" + element.GetAttribute("data-ad-item");
        }

        return "separator:" + string.Join('|', element.QuerySelectorAll("span").Select(value => value.TextContent.Trim()));
    }

    private sealed record DateListItem(
        string Id,
        DateTimeOffset CreatedAt,
        bool ShouldInsertAdvertisement = false);

    private sealed class RecordingDateSeparatedListInterop : IDateSeparatedListInterop
    {
        public DateSeparatedCalendarPart[]? CalendarParts { get; set; }
        public List<IReadOnlyList<long>> CalendarRequests { get; } = [];
        public RecordingJsReference Reference { get; } = new();
        public bool Attached { get; private set; }

        public ValueTask<DateSeparatedCalendarPart[]> GetCalendarPartsAsync(
            IReadOnlyList<long> unixTimeMilliseconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CalendarRequests.Add(unixTimeMilliseconds.ToArray());
            DateSeparatedCalendarPart[] values = CalendarParts ?? unixTimeMilliseconds
                .Select(value => DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime())
                .Select(value => new DateSeparatedCalendarPart(value.Month, value.Day))
                .ToArray();
            return ValueTask.FromResult(values);
        }

        public ValueTask<IJSObjectReference> AttachAsync(
            ElementReference root,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attached = true;
            return ValueTask.FromResult<IJSObjectReference>(Reference);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingJsReference : IJSObjectReference
    {
        public bool Disposed { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedDeviceState(bool animation) : IPizzaxDeviceState
    {
        public ValueTask<T> ReadAsync<T>(
            string propertyName,
            T fallback,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("animation", propertyName);
            return ValueTask.FromResult((T)(object)animation);
        }

        public ValueTask WriteAsync<T>(
            string propertyName,
            T value,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class DateListLocalizer : IMisskeyLocalizer
    {
        public event EventHandler? LocaleChanged { add { } remove { } }
        public string CurrentLocale => "en-US";
        public string Direction => "ltr";
        public CultureInfo Culture => CultureInfo.GetCultureInfo(CurrentLocale);
        public IReadOnlyList<MisskeyLocaleDefinition> SupportedLocales => [];

        public string Translate(string key, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            Assert.Equal("monthAndDay", key);
            Assert.NotNull(arguments);
            return $"{arguments["month"]}/{arguments["day"]}";
        }

        public bool TrySelectLocale(string? locale) => false;
    }
}
