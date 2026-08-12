using System.Net;
using System.Text.Json;
using ActivityPub.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class UrlPreviewCompatibilityTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task UrlPreviewReturnsThePinnedSummalyContractAndCachesForSevenDays()
    {
        using HttpClient client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://local.example", UriKind.Absolute)
        });

        using HttpResponseMessage invalid = await client.GetAsync("/url?lang=ja-JP");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using HttpResponseMessage malformed = await client.GetAsync("/url?url=not-a-url");
        Assert.Equal(HttpStatusCode.OK, malformed.StatusCode);

        using HttpResponseMessage missing = await client.GetAsync("/url?url=" + Uri.EscapeDataString("https://unknown.example/page"));
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        using JsonDocument missingDocument = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Empty(missingDocument.RootElement.EnumerateObject());

        using HttpResponseMessage known = await client.GetAsync("/url?url=" + Uri.EscapeDataString("https://known.example/article"));
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        using JsonDocument knownDocument = JsonDocument.Parse(await known.Content.ReadAsStringAsync());
        Assert.Equal("Known Title", knownDocument.RootElement.GetProperty("title").GetString());
        Assert.Equal("Known description", knownDocument.RootElement.GetProperty("description").GetString());
        Assert.Equal("https://images.example/known.png", knownDocument.RootElement.GetProperty("thumbnail").GetString());
        Assert.Equal("https://images.example/favicon.ico", knownDocument.RootElement.GetProperty("icon").GetString());
        Assert.Equal("KnownSite", knownDocument.RootElement.GetProperty("sitename").GetString());
        Assert.Equal("https://player.example/video.mp4", knownDocument.RootElement.GetProperty("player").GetProperty("url").GetString());
        Assert.Equal(640, knownDocument.RootElement.GetProperty("player").GetProperty("width").GetInt32());
        Assert.Equal(360, knownDocument.RootElement.GetProperty("player").GetProperty("height").GetInt32());

        var fetcher = fixture.Services.GetRequiredService<IUrlPreviewFetcher>() as FixtureUrlPreviewFetcher
            ?? throw new InvalidOperationException("The fixture fetcher is required.");
        Assert.Equal(2, fetcher.FetchCount);

        using HttpResponseMessage cached = await client.GetAsync("/url?url=" + Uri.EscapeDataString("https://known.example/article"));
        Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
        Assert.Equal(2, fetcher.FetchCount);
        Assert.Equal("https://known.example/article", fetcher.LastUrl);
    }
}
