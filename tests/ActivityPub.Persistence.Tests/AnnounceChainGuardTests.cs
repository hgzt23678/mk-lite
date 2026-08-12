using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class AnnounceChainGuardTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ChainGuardAllowsOrdinaryAnnouncesAndRejectsDeepNesting()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        var guard = scope.ServiceProvider.GetRequiredService<IAnnounceChainGuard>();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        string actor = "https://local.example/users/alice";
        string noteIri = "https://remote.example/objects/note-" + Guid.NewGuid().ToString("N");
        string previous = noteIri;
        for (int index = 0; index < 9; index++)
        {
            string activityIri = "https://remote.example/activities/announce-" + Guid.NewGuid().ToString("N");
            db.Activities.Add(ActivityRecord.Create(
                activityIri,
                actor,
                "Announce",
                previous,
                ActivityDirection.Inbound,
                Visibility.Public,
                "{}",
                PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(activityIri)),
                isTransient: false,
                Now,
                Now));
            previous = activityIri;
        }

        await db.SaveChangesAsync();

        Assert.True(await guard.IsWithinChainLimitAsync(noteIri, CancellationToken.None));
        Assert.True(await guard.IsWithinChainLimitAsync(previous, CancellationToken.None));

        string extraIri = "https://remote.example/activities/announce-extra-" + Guid.NewGuid().ToString("N");
        db.Activities.Add(ActivityRecord.Create(
            extraIri,
            actor,
            "Announce",
            previous,
            ActivityDirection.Inbound,
            Visibility.Public,
            "{}",
            PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(extraIri)),
            isTransient: false,
            Now,
            Now));
        await db.SaveChangesAsync();

        Assert.False(await guard.IsWithinChainLimitAsync(extraIri, CancellationToken.None));
    }
}
