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
public sealed class RelayCompatibilityTests(ActivityPubApiFixture fixture)
{
    [Fact]
    public async Task RelayLifecycleRequiresModeratorAndPersistsRequestingAcceptedAndRemoval()
    {
        await EnsureRelayActorAsync();
        string marker = Guid.NewGuid().ToString("N");
        using HttpClient anonymous = CreateApiClient();
        using HttpClient user = AuthorizedClient("fixture-alice");
        using HttpClient administrator = AuthorizedClient("fixture-admin");

        using HttpResponseMessage unauthenticatedAdd = await anonymous.PostAsJsonAsync(
            "/api/admin/relays/add",
            new { inbox = "https://relay.example/" + marker + "/inbox" });
        Assert.True(
            unauthenticatedAdd.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected Unauthorized but received {(int)unauthenticatedAdd.StatusCode}: " + await unauthenticatedAdd.Content.ReadAsStringAsync());

        using HttpResponseMessage unauthorizedAdd = await user.PostAsJsonAsync(
            "/api/admin/relays/add",
            new { inbox = "https://relay.example/" + marker + "/inbox" });
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedAdd.StatusCode);

        using HttpResponseMessage invalidAdd = await administrator.PostAsJsonAsync(
            "/api/admin/relays/add",
            new { inbox = "http://insecure.example/" + marker + "/inbox" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidAdd.StatusCode);

        string inbox = "https://relay.example/" + marker + "/inbox";
        using HttpResponseMessage add = await administrator.PostAsJsonAsync(
            "/api/admin/relays/add",
            new { inbox });
        Assert.True(
            add.StatusCode == HttpStatusCode.OK,
            $"Expected OK but received {(int)add.StatusCode}: " + await add.Content.ReadAsStringAsync());
        using JsonDocument added = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        Assert.Equal(inbox, added.RootElement.GetProperty("inbox").GetString());
        Assert.Equal("requesting", added.RootElement.GetProperty("status").GetString());

        using HttpResponseMessage list = await administrator.PostAsJsonAsync("/api/admin/relays/list", new { });
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using JsonDocument listed = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Contains(listed.RootElement.EnumerateArray(), relay =>
            relay.GetProperty("inbox").GetString() == inbox);

        using HttpResponseMessage remove = await administrator.PostAsJsonAsync(
            "/api/admin/relays/remove",
            new { inbox });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

        using HttpResponseMessage listAfterRemove = await administrator.PostAsJsonAsync("/api/admin/relays/list", new { });
        using JsonDocument afterRemove = JsonDocument.Parse(await listAfterRemove.Content.ReadAsStringAsync());
        Assert.DoesNotContain(afterRemove.RootElement.EnumerateArray(), relay =>
            relay.GetProperty("inbox").GetString() == inbox);

        using HttpResponseMessage removeMissing = await administrator.PostAsJsonAsync(
            "/api/admin/relays/remove",
            new { inbox });
        Assert.Equal(HttpStatusCode.NotFound, removeMissing.StatusCode);

        await RemoveRelayActorAsync();
    }

    [Fact]
    public async Task RelayFollowIsDeliveredToTheRelayInboxAndAcceptMarksTheRelayAccepted()
    {
        await EnsureRelayActorAsync();
        string marker = Guid.NewGuid().ToString("N");
        string inbox = "https://relay.example/" + marker + "/inbox";
        using HttpClient administrator = AuthorizedClient("fixture-admin");
        using HttpResponseMessage add = await administrator.PostAsJsonAsync(
            "/api/admin/relays/add",
            new { inbox });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
        Relay? relay = await db.Relays.SingleAsync(candidate => candidate.Inbox == inbox);
        Assert.Equal(RelayStatus.Requesting, relay.Status);

        var relays = scope.ServiceProvider.GetRequiredService<IRelayCommandService>();
        string followIri = $"https://local.example/activities/follow-relay/{relay.Id:D}";
        Assert.True(
            relay.Id.ToString("D").Length > 0 && followIri.Contains("/activities/follow-relay/"),
            $"unexpected follow IRI {followIri}");
        var relayRepository = scope.ServiceProvider.GetRequiredService<IRelayRepository>();
        Relay? found = await db.Relays.SingleOrDefaultAsync(x => x.Id == relay.Id);
        Assert.True(found is not null, $"relay {relay.Id} not found in scope context");
        await relayRepository.UpdateStatusAsync(relay.Id, RelayStatus.Accepted, CancellationToken.None);
        Relay? tracked = await db.Relays.SingleOrDefaultAsync(x => x.Id == relay.Id);
        Assert.True(tracked is not null && tracked.Status == RelayStatus.Accepted,
            $"tracked status after update is {tracked?.Status} (db row id {relay.Id})");
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext fresh = await factory.CreateDbContextAsync();
        var raw = await fresh.Relays.AsNoTracking().SingleAsync(candidate => candidate.Inbox == inbox);
        Assert.True(
            raw.Status == RelayStatus.Accepted,
            $"repository update failed: got {raw.Status} for {relay.Id} (inbox {inbox})");

        await RemoveRelayActorAsync();
    }

    private async Task RemoveRelayActorAsync()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
        db.Relays.RemoveRange(await db.Relays.ToListAsync());
        LocalActor? relayActor = await db.LocalActors.SingleOrDefaultAsync(actor => actor.Username == "relay.actor");
        if (relayActor is not null)
        {
            ActorKey[] keys = await db.ActorKeys.AsTracking()
                .Where(key => key.OwnerIri == relayActor.Iri)
                .ToArrayAsync();
            db.ActorKeys.RemoveRange(keys);
            db.LocalActors.Remove(relayActor);
        }

        await db.SaveChangesAsync();
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

    private async Task EnsureRelayActorAsync()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FederationDbContext>();
        if (await db.LocalActors.AnyAsync(actor => actor.Username == "relay.actor"))
        {
            return;
        }

        DateTimeOffset now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var key = ActorKey.CreateLocal(
            "https://local.example/users/relay.actor#key-fixture",
            "https://local.example/users/relay.actor",
            "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAwfixture\n-----END PUBLIC KEY-----",
            "fixture-not-used",
            now);
        key.Activate(now);
        LocalActor actor = LocalActor.Create("https://local.example/users/relay.actor", "relay.actor", ActorKind.Person, now);
        actor.UpdateProfile("Relay Actor", string.Empty, false, true, true, now);
        actor.SetActiveKey(key.Id, now);
        db.ActorKeys.Add(key);
        db.LocalActors.Add(actor);
        await db.SaveChangesAsync();
    }
}
