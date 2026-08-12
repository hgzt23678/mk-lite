using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class AutocompleteCompatibilityTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task SearchUsersByUsernameAndHostReturnsPrefixMatchesWithoutCredential()
    {
        using HttpClient anonymous = CreateApiClient();

        using HttpResponseMessage empty = await anonymous.PostAsJsonAsync(
            "/api/users/search-by-username-and-host",
            new { username = "zzz-no-such-user", limit = 10 });
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using JsonDocument emptyDocument = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.Equal(0, emptyDocument.RootElement.GetArrayLength());

        using HttpResponseMessage invalid = await anonymous.PostAsJsonAsync(
            "/api/users/search-by-username-and-host",
            new { limit = 1000 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using HttpResponseMessage found = await anonymous.PostAsJsonAsync(
            "/api/users/search-by-username-and-host",
            new { username = "ali", limit = 10, detail = false });
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        using JsonDocument foundDocument = JsonDocument.Parse(await found.Content.ReadAsStringAsync());
        Assert.True(
            foundDocument.RootElement.GetArrayLength() >= 1,
            "Expected at least the fixture-local alice account to match the ali prefix.");
        JsonElement first = foundDocument.RootElement.EnumerateArray().First();
        Assert.Equal("alice", first.GetProperty("username").GetString());
        Assert.False(first.TryGetProperty("followersCount", out _),
            "detail=false must omit the detailed profile fields.");
    }

    [Fact]
    public async Task HashtagSearchUsesPinnedContractAndNoteCreationRecordsTags()
    {
        using HttpClient anonymous = CreateApiClient();
        using HttpClient user = AuthorizedClient("fixture-alice");

        using HttpResponseMessage missingQuery = await anonymous.PostAsJsonAsync(
            "/api/hashtags/search",
            new { limit = 30 });
        Assert.Equal(HttpStatusCode.BadRequest, missingQuery.StatusCode);

        string tag = "parity" + Guid.NewGuid().ToString("N")[..10];
        using HttpResponseMessage created = await user.PostAsJsonAsync(
            "/api/notes/create",
            new { text = $"a note with #{tag} and another #{tag} usage" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using HttpResponseMessage searched = await anonymous.PostAsJsonAsync(
            "/api/hashtags/search",
            new { query = tag, limit = 30 });
        Assert.Equal(HttpStatusCode.OK, searched.StatusCode);
        using JsonDocument searchedDocument = JsonDocument.Parse(await searched.Content.ReadAsStringAsync());
        Assert.Contains(
            searchedDocument.RootElement.EnumerateArray(),
            item => item.GetString() == tag);
    }

    [Fact]
    public async Task HashtagTrendReturnsThePinnedTagChartUsersCountContract()
    {
        using HttpClient anonymous = CreateApiClient();
        using HttpClient user = AuthorizedClient("fixture-alice");

        string trendTag = "trend" + Guid.NewGuid().ToString("N")[..8];
        using HttpResponseMessage created = await user.PostAsJsonAsync(
            "/api/notes/create",
            new { text = $"hot topic #{trendTag} right now" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using HttpResponseMessage trend = await anonymous.PostAsJsonAsync(
            "/api/hashtags/trend",
            new { });
        Assert.Equal(HttpStatusCode.OK, trend.StatusCode);
        using JsonDocument trendDocument = JsonDocument.Parse(await trend.Content.ReadAsStringAsync());
        JsonElement match = trendDocument.RootElement.EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("tag").GetString() == trendTag);
        Assert.NotEqual(default, match);
        Assert.True(match.GetProperty("usersCount").GetInt64() >= 1);
        Assert.Equal(20, match.GetProperty("chart").GetArrayLength());
    }

    private HttpClient CreateApiClient() => fixture.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://local.example", UriKind.Absolute)
    });

    private HttpClient AuthorizedClient(string principal)
    {
        HttpClient client = fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://local.example", UriKind.Absolute)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", principal);
        return client;
    }
}
