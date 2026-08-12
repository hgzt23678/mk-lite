using System.Text.Json;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Components;
using ActivityPub.Misskey.Blazor.Presentation;
using AngleSharp.Dom;
using Bunit;

namespace ActivityPub.Misskey.Blazor.Tests;

public sealed class CwButtonTests : BunitContext
{
    [Fact]
    public void CollapsedButtonPreservesPinnedDomLabelsAndAttributeFallthrough()
    {
        NoteViewModel note = CreateNote(
            "A👨‍👩‍👧‍👦é",
            mediaCount: 2,
            poll: true);

        IRenderedComponent<MkCwButton> component = Render<MkCwButton>(parameters => parameters
            .Add(button => button.Note, note)
            .Add(button => button.Expanded, false)
            .Add(button => button.CssClassAdditional, "component-class")
            .AddUnmatched("class", "fallthrough-class")
            .AddUnmatched("data-cw-fixture", "all")
            .AddUnmatched("title", "CWを開く"));

        IElement button = component.Find("button.nrvgflfu._button");
        Assert.Equal("nrvgflfu _button component-class fallthrough-class", button.GetAttribute("class"));
        Assert.Null(button.GetAttribute("type"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal("all", button.GetAttribute("data-cw-fixture"));
        Assert.Equal("CWを開く", button.GetAttribute("title"));
        Assert.Equal(2, button.Children.Length);
        Assert.Equal("もっと見る", button.QuerySelector(":scope > b")?.TextContent);
        Assert.Equal("3文字 / 2ファイル / アンケート", button.QuerySelector(":scope > span")?.TextContent);
    }

    [Theory]
    [InlineData("", 2, false, "2ファイル")]
    [InlineData("abc", 0, true, "3文字 / アンケート")]
    [InlineData("", 0, true, "アンケート")]
    [InlineData("", 0, false, "")]
    public void LabelPreservesEveryTextFileAndPollBranch(
        string text,
        int mediaCount,
        bool poll,
        string expected)
    {
        IRenderedComponent<MkCwButton> component = Render<MkCwButton>(parameters => parameters
            .Add(button => button.Note, CreateNote(text, mediaCount, poll)));

        Assert.Equal(expected, component.Find("button > span").TextContent);
    }

    [Fact]
    public void ExpandedButtonEmitsTheInverseModelValueAndHidesTheCollapsedLabel()
    {
        bool? emitted = null;
        IRenderedComponent<MkCwButton> component = Render<MkCwButton>(parameters => parameters
            .Add(button => button.Note, CreateNote("content", 1, poll: false))
            .Add(button => button.Expanded, true)
            .Add(button => button.ExpandedChanged, value => emitted = value));

        IElement button = component.Find("button.nrvgflfu._button");
        Assert.Equal("隠す", button.QuerySelector(":scope > b")?.TextContent);
        Assert.Null(button.QuerySelector(":scope > span"));
        Assert.Equal("true", button.GetAttribute("aria-expanded"));

        button.Click();

        Assert.False(emitted);
    }

    [Fact]
    public void PinnedStringzFixtureProducesTheVisibleHistoricalCounts()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "mk-cw-button-stringz-2.1.0.json")));

        foreach (JsonElement item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            string value = item.GetProperty("value").GetString()!;
            int expected = item.GetProperty("length").GetInt32();
            IRenderedComponent<MkCwButton> component = Render<MkCwButton>(parameters => parameters
                .Add(button => button.Note, CreateNote(value, 0, poll: false)));

            Assert.Equal($"{expected}文字", component.Find("button > span").TextContent);
        }
    }

    private static NoteViewModel CreateNote(string text, int mediaCount, bool poll)
    {
        IReadOnlyList<NoteMediaViewModel> media = Enumerable.Range(0, mediaCount)
            .Select(index => new NoteMediaViewModel(
                $"media-{index}",
                "image/png",
                $"/media/{index}.png",
                $"/media/{index}-preview.png",
                null,
                null,
                640,
                480,
                Sensitive: false))
            .ToArray();
        NotePollViewModel? notePoll = poll
            ? new NotePollViewModel(
                "poll-1",
                null,
                Expired: false,
                Multiple: false,
                VotedByViewer: false,
                OwnVotes: [],
                Options: [new NotePollOptionViewModel("はい", 0), new NotePollOptionViewModel("いいえ", 0)])
            : null;

        return new NoteViewModel(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "note-id",
            new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero),
            new NoteAuthorViewModel("alice-id", "alice", "alice", "Alice", "/static-assets/user-unknown.png", false),
            text,
            "閲覧注意",
            ActivityPub.Domain.Visibility.Public,
            null,
            0,
            0,
            0,
            false,
            new Dictionary<string, long>(StringComparer.Ordinal),
            null,
            media,
            [],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            notePoll,
            null);
    }
}
