using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class AnnouncementIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task PostgreSqlMaintainsVisibilityOrderIdempotentReadsSoftDeleteAndAudit()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IAnnouncementService service = scope.ServiceProvider.GetRequiredService<IAnnouncementService>();
        TestClock clock = scope.ServiceProvider.GetRequiredService<TestClock>();
        DateTimeOffset now = clock.UtcNow;
        string marker = Guid.NewGuid().ToString("N");
        const string reader = "https://identity-tests.example/users/alice";

        AnnouncementView first = await service.CreateAsync(
            new("First " + marker, "First body", null, AnnouncementAudience.Public, now, null),
            "postgres-admin",
            CancellationToken.None);
        AnnouncementView second = await service.CreateAsync(
            new("Second " + marker, "Second body", "/media/second.png", AnnouncementAudience.Public, now, null),
            "postgres-admin",
            CancellationToken.None);
        _ = await service.CreateAsync(
            new("Future " + marker, "Future body", null, AnnouncementAudience.Public, now.AddHours(1), null),
            "postgres-admin",
            CancellationToken.None);
        _ = await service.CreateAsync(
            new("Expired " + marker, "Expired body", null, AnnouncementAudience.Public, now.AddHours(-2), now.AddHours(-1)),
            "postgres-admin",
            CancellationToken.None);
        AnnouncementView authenticated = await service.CreateAsync(
            new("Authenticated " + marker, "Members only", null, AnnouncementAudience.Authenticated, now, null),
            "postgres-admin",
            CancellationToken.None);

        IReadOnlyList<AnnouncementView> anonymous = await service.ReadAsync(
            new(null, null, 100, WithUnreads: false, ViewerActorIri: null),
            CancellationToken.None);
        AnnouncementView[] matching = anonymous.Where(value => value.Title.EndsWith(marker, StringComparison.Ordinal)).ToArray();
        Assert.Equal([second.Id, first.Id], matching.Select(value => value.Id));
        Assert.DoesNotContain(anonymous, value => value.Id == authenticated.Id);

        IReadOnlyList<AnnouncementView> beforeFirst = await service.ReadAsync(
            new(null, second.Id, 100, WithUnreads: false, ViewerActorIri: null),
            CancellationToken.None);
        Assert.Contains(beforeFirst, value => value.Id == first.Id);
        Assert.DoesNotContain(beforeFirst, value => value.Id == second.Id);

        Assert.True(await service.MarkReadAsync(second.Id, reader, CancellationToken.None));
        Assert.True(await service.MarkReadAsync(second.Id, reader, CancellationToken.None));
        IReadOnlyList<AnnouncementView> signedIn = await service.ReadAsync(
            new(null, null, 100, WithUnreads: false, reader),
            CancellationToken.None);
        Assert.True(Assert.Single(signedIn, value => value.Id == second.Id).IsRead);
        Assert.Contains(signedIn, value => value.Id == authenticated.Id);
        IReadOnlyList<AnnouncementView> unreads = await service.ReadAsync(
            new(null, null, 100, WithUnreads: true, reader),
            CancellationToken.None);
        Assert.DoesNotContain(unreads, value => value.Id == second.Id);

        clock.Advance(TimeSpan.FromMinutes(1));
        AnnouncementView? updated = await service.UpdateAsync(
            second.Id,
            new(
                "Updated " + marker,
                "Updated body",
                null,
                AnnouncementAudience.Public,
                PublishedAt: now,
                ExpiresAt: now.AddHours(2),
                ReplaceExpiresAt: true),
            "postgres-editor",
            CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(now.AddHours(2), updated.ExpiresAt);
        Assert.True(await service.DeleteAsync(second.Id, "postgres-admin", CancellationToken.None));
        Assert.False(await service.DeleteAsync(second.Id, "postgres-admin", CancellationToken.None));

        IDbContextFactory<FederationDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.AnnouncementReads.CountAsync(value =>
            value.AnnouncementId == second.Id && value.ReaderActorIri == reader));
        Announcement persisted = await db.Announcements.SingleAsync(value => value.Id == second.Id);
        Assert.NotNull(persisted.DeletedAt);
        Assert.Equal(2, persisted.Version);
        AuditEvent[] audit = await db.AuditEvents
            .Where(value => value.Category == "announcement" && value.Target == second.Id.ToString("D"))
            .ToArrayAsync();
        Assert.Equal(["create", "delete", "update"], audit.Select(value => value.Action).Order(StringComparer.Ordinal));
        Assert.Equal(
            Assert.Single(audit, value => value.Action == "update").EventHash,
            Assert.Single(audit, value => value.Action == "delete").PreviousHash);
        Assert.All(audit, value => Assert.Equal(64, value.EventHash.Length));
    }
}
