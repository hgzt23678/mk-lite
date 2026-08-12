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
public sealed class AnnouncementCompatibilityTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task MisskeyAnnouncementLifecycleUsesDurableStateAuthorizationReadsAndAudit()
    {
        string marker = Guid.NewGuid().ToString("N");
        using HttpClient anonymous = CreateApiClient();
        using HttpClient user = AuthorizedClient("fixture-alice");
        using HttpClient administrator = AuthorizedClient("fixture-admin");

        using HttpResponseMessage unauthenticatedCreate = await anonymous.PostAsJsonAsync(
            "/api/admin/announcements/create",
            new { title = "unauthorized", text = marker, imageUrl = (string?)null });
        using HttpResponseMessage unauthorizedCreate = await user.PostAsJsonAsync(
            "/api/admin/announcements/create",
            new { title = "unauthorized", text = marker, imageUrl = (string?)null });
        Assert.True(
            unauthenticatedCreate.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected an authentication challenge but received {(int)unauthenticatedCreate.StatusCode}: " +
            await unauthenticatedCreate.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedCreate.StatusCode);

        using HttpResponseMessage unsafeCreate = await administrator.PostAsJsonAsync(
            "/api/admin/announcements/create",
            new { title = "unsafe", text = marker, imageUrl = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeCreate.StatusCode);

        string unavailableTitle = "Remote media unavailable " + marker;
        using HttpResponseMessage unavailableCreate = await administrator.PostAsJsonAsync(
            "/api/admin/announcements/create",
            new
            {
                title = unavailableTitle,
                text = marker,
                imageUrl = "https://remote.example/announcement.png"
            });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableCreate.StatusCode);
        using JsonDocument unavailableJson = await JsonDocument.ParseAsync(
            await unavailableCreate.Content.ReadAsStreamAsync());
        Assert.Equal("MEDIA_UNAVAILABLE", unavailableJson.RootElement.GetProperty("error").GetProperty("code").GetString());
        await AssertAnnouncementWasNotPersistedAsync(unavailableTitle);

        using HttpResponseMessage created = await administrator.PostAsJsonAsync(
            "/api/admin/announcements/create",
            new
            {
                title = "Maintenance " + marker,
                text = "Initial " + marker,
                imageUrl = "/media/announcement.png"
            });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using JsonDocument createdJson = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        string externalId = createdJson.RootElement.GetProperty("id").GetString()!;
        Assert.False(createdJson.RootElement.TryGetProperty("isRead", out _));
        Assert.Equal("/media/announcement.png", createdJson.RootElement.GetProperty("imageUrl").GetString());

        await CreateVisibilityFixturesAsync(marker);

        JsonElement publicItem = await FindAnnouncementAsync(anonymous, externalId);
        Assert.Equal("Initial " + marker, publicItem.GetProperty("text").GetString());
        Assert.False(publicItem.TryGetProperty("isRead", out _));
        Assert.DoesNotContain(marker + " future", await ReadAnnouncementTextsAsync(anonymous));
        Assert.DoesNotContain(marker + " expired", await ReadAnnouncementTextsAsync(anonymous));
        Assert.DoesNotContain(marker + " authenticated", await ReadAnnouncementTextsAsync(anonymous));

        JsonElement unreadItem = await FindAnnouncementAsync(user, externalId);
        Assert.False(unreadItem.GetProperty("isRead").GetBoolean());
        Assert.Contains(marker + " authenticated", await ReadAnnouncementTextsAsync(user));

        using HttpResponseMessage marked = await user.PostAsJsonAsync(
            "/api/i/read-announcement",
            new { announcementId = externalId });
        using HttpResponseMessage duplicateMark = await user.PostAsJsonAsync(
            "/api/i/read-announcement",
            new { announcementId = externalId });
        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicateMark.StatusCode);
        JsonElement readItem = await FindAnnouncementAsync(user, externalId);
        Assert.True(readItem.GetProperty("isRead").GetBoolean());

        using HttpResponseMessage unreadOnly = await user.PostAsJsonAsync(
            "/api/announcements",
            new { limit = 100, withUnreads = true });
        using JsonDocument unreadOnlyJson = await JsonDocument.ParseAsync(await unreadOnly.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(unreadOnlyJson.RootElement.EnumerateArray(), item =>
            string.Equals(item.GetProperty("id").GetString(), externalId, StringComparison.Ordinal));

        using HttpResponseMessage adminList = await administrator.PostAsJsonAsync(
            "/api/admin/announcements/list",
            new { limit = 100 });
        using JsonDocument adminListJson = await JsonDocument.ParseAsync(await adminList.Content.ReadAsStreamAsync());
        JsonElement adminItem = Assert.Single(adminListJson.RootElement.EnumerateArray(), item =>
            string.Equals(item.GetProperty("id").GetString(), externalId, StringComparison.Ordinal));
        Assert.Equal(1, adminItem.GetProperty("reads").GetInt64());

        using HttpResponseMessage updated = await administrator.PostAsJsonAsync(
            "/api/admin/announcements/update",
            new
            {
                id = externalId,
                title = "Updated " + marker,
                text = "Updated text " + marker,
                imageUrl = (string?)null
            });
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        JsonElement updatedItem = await FindAnnouncementAsync(anonymous, externalId);
        Assert.Equal("Updated " + marker, updatedItem.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, updatedItem.GetProperty("imageUrl").ValueKind);

        using HttpResponseMessage deleted = await administrator.PostAsJsonAsync(
            "/api/admin/announcements/delete",
            new { id = externalId });
        using HttpResponseMessage duplicateDelete = await administrator.PostAsJsonAsync(
            "/api/admin/announcements/delete",
            new { id = externalId });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, duplicateDelete.StatusCode);
        Assert.DoesNotContain(await ReadAnnouncementsAsync(anonymous), item =>
            string.Equals(item.GetProperty("id").GetString(), externalId, StringComparison.Ordinal));

        await AssertPersistenceAndAuditAsync(externalId);
    }

    private async Task CreateVisibilityFixturesAsync(string marker)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IAnnouncementService service = scope.ServiceProvider.GetRequiredService<IAnnouncementService>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await service.CreateAsync(
            new(marker + " future", marker + " future", null, AnnouncementAudience.Public, now.AddDays(1), null),
            "fixture-admin",
            CancellationToken.None);
        await service.CreateAsync(
            new(marker + " expired", marker + " expired", null, AnnouncementAudience.Public, now.AddDays(-2), now.AddDays(-1)),
            "fixture-admin",
            CancellationToken.None);
        await service.CreateAsync(
            new(marker + " authenticated", marker + " authenticated", null, AnnouncementAudience.Authenticated, now, null),
            "fixture-admin",
            CancellationToken.None);
    }

    private async Task AssertPersistenceAndAuditAsync(string externalId)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        Guid internalId = Assert.IsType<Guid>(await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Announcement,
            externalId,
            CancellationToken.None));
        IDbContextFactory<FederationDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        Announcement persisted = await db.Announcements.SingleAsync(value => value.Id == internalId);
        Assert.NotNull(persisted.DeletedAt);
        Assert.Equal(1, await db.AnnouncementReads.CountAsync(value => value.AnnouncementId == internalId));
        AuditEvent[] audit = await db.AuditEvents
            .Where(value => value.Category == "announcement" && value.Target == internalId.ToString("D"))
            .ToArrayAsync();
        Assert.Equal(["create", "delete", "update"], audit.Select(value => value.Action).Order(StringComparer.Ordinal));
        Assert.Equal(
            Assert.Single(audit, value => value.Action == "update").EventHash,
            Assert.Single(audit, value => value.Action == "delete").PreviousHash);
        Assert.All(audit, value => Assert.DoesNotContain("Updated text", value.DetailsJson, StringComparison.Ordinal));
    }

    private async Task AssertAnnouncementWasNotPersistedAsync(string title)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        Assert.False(await db.Announcements.AnyAsync(value => value.Title == title));
    }

    private static async Task<JsonElement> FindAnnouncementAsync(HttpClient client, string externalId)
    {
        JsonElement[] values = await ReadAnnouncementsAsync(client);
        return Assert.Single(values, item =>
            string.Equals(item.GetProperty("id").GetString(), externalId, StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> ReadAnnouncementTextsAsync(HttpClient client) =>
        (await ReadAnnouncementsAsync(client))
            .Select(item => item.GetProperty("text").GetString() ?? string.Empty)
            .ToArray();

    private static async Task<JsonElement[]> ReadAnnouncementsAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/announcements", new { limit = 100 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private HttpClient AuthorizedClient(string token)
    {
        HttpClient result = CreateApiClient();
        result.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return result;
    }

    private HttpClient CreateApiClient() => fixture.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://local.example", UriKind.Absolute)
    });
}
