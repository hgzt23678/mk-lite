using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ActivityPub.Api.Tests;

[Collection(ActivityPubApiFixtureDefinition.Name)]
public sealed class QueueManagementCompatibilityTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task QueueStatsAndJobsRequireAdministratorAndExposeNoPayloadOrSignature()
    {
        using HttpClient anonymous = CreateApiClient();
        using HttpClient user = AuthorizedClient("fixture-alice");
        using HttpClient administrator = AuthorizedClient("fixture-admin");

        using HttpResponseMessage anonymousResponse = await anonymous.PostAsJsonAsync(
            "/api/admin/queue/stats", new { });
        using HttpResponseMessage userResponse = await user.PostAsJsonAsync(
            "/api/admin/queue/stats", new { });
        Assert.True(anonymousResponse.StatusCode == HttpStatusCode.Unauthorized,
            await anonymousResponse.Content.ReadAsStringAsync());
        Assert.True(userResponse.StatusCode == HttpStatusCode.Forbidden,
            await userResponse.Content.ReadAsStringAsync());

        using HttpResponseMessage statsResponse = await administrator.PostAsJsonAsync(
            "/api/admin/queue/stats", new { });
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        using JsonDocument stats = await JsonDocument.ParseAsync(await statsResponse.Content.ReadAsStreamAsync());
        Assert.True(stats.RootElement.GetProperty("deliver").GetProperty("waiting").TryGetInt64(out _));
        Assert.True(stats.RootElement.GetProperty("inbox").GetProperty("active").TryGetInt64(out _));

        using HttpResponseMessage jobsResponse = await administrator.PostAsJsonAsync(
            "/api/admin/queue/jobs",
            new { domain = "deliver", state = "waiting", limit = 50 });
        Assert.Equal(HttpStatusCode.OK, jobsResponse.StatusCode);
        string jobsJson = await jobsResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("payload", jobsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", jobsJson, StringComparison.OrdinalIgnoreCase);

        using HttpResponseMessage deliveryDomains = await administrator.PostAsJsonAsync(
            "/api/admin/queue/deliver-delayed", new { });
        using HttpResponseMessage inboxDomains = await administrator.PostAsJsonAsync(
            "/api/admin/queue/inbox-delayed", new { });
        Assert.Equal(HttpStatusCode.OK, deliveryDomains.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inboxDomains.StatusCode);
        using JsonDocument deliveryDomainJson = await JsonDocument.ParseAsync(
            await deliveryDomains.Content.ReadAsStreamAsync());
        using JsonDocument inboxDomainJson = await JsonDocument.ParseAsync(
            await inboxDomains.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Array, deliveryDomainJson.RootElement.ValueKind);
        Assert.Equal(JsonValueKind.Array, inboxDomainJson.RootElement.ValueKind);

        using HttpResponseMessage unsupported = await administrator.PostAsJsonAsync(
            "/api/admin/queue/jobs",
            new { domain = "objectStorage", state = "waiting", limit = 50 });
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
    }

    [Fact]
    public async Task OperationsQueueEndpointsExposePostgresBackedDeliveryAndInboxViews()
    {
        using HttpClient administrator = AuthorizedClient("fixture-admin");
        using HttpResponseMessage stats = await administrator.GetAsync("/admin/federation/queue/stats");
        using HttpResponseMessage deliveries = await administrator.GetAsync(
            "/admin/federation/queue/jobs?state=Pending&limit=20");
        using HttpResponseMessage inbox = await administrator.GetAsync(
            "/admin/federation/queue/inbox-jobs?state=Pending&limit=20");

        Assert.True(stats.StatusCode == HttpStatusCode.OK, await stats.Content.ReadAsStringAsync());
        Assert.True(deliveries.StatusCode == HttpStatusCode.OK, await deliveries.Content.ReadAsStringAsync());
        Assert.True(inbox.StatusCode == HttpStatusCode.OK, await inbox.Content.ReadAsStringAsync());
    }

    private HttpClient AuthorizedClient(string token)
    {
        HttpClient client = CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateApiClient() => fixture.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://local.example", UriKind.Absolute)
    });
}
