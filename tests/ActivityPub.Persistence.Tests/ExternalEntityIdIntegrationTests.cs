using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class ExternalEntityIdIntegrationTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentAllocationReturnsOnePersistentIdentifierPerDialectAndEntity()
    {
        Guid internalId = Guid.NewGuid();
        Task<string>[] allocations = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
            IExternalEntityIdService service = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
            return await service.GetOrCreateAsync(
                ApiDialect.Mastodon,
                ExternalEntityType.Post,
                internalId,
                Now,
                CancellationToken.None);
        }).ToArray();

        string[] results = await Task.WhenAll(allocations);

        Assert.Single(results.Distinct(StringComparer.Ordinal));
        Assert.True(long.TryParse(results[0], System.Globalization.CultureInfo.InvariantCulture, out long numericId));
        Assert.True(numericId > 0);
        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = verificationScope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.ExternalEntityIds.CountAsync(x =>
            x.Dialect == ApiDialect.Mastodon &&
            x.EntityType == ExternalEntityType.Post &&
            x.InternalId == internalId));
    }

    [Fact]
    public async Task DialectsHaveIndependentStableIdsAndResolveOnlyWithinTheirNamespace()
    {
        Guid internalId = Guid.NewGuid();
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IExternalEntityIdService service = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();

        string mastodon = await service.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            internalId,
            Now,
            CancellationToken.None);
        string misskey = await service.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            internalId,
            Now,
            CancellationToken.None);

        Assert.Matches("^[0-9]+$", mastodon);
        Assert.Matches("^[0-9a-z]{10}$", misskey);
        Assert.Equal(internalId, await service.ResolveAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            mastodon,
            CancellationToken.None));
        Assert.Equal(internalId, await service.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            misskey,
            CancellationToken.None));
        Assert.Null(await service.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            mastodon,
            CancellationToken.None));
        Assert.Equal(mastodon, await service.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            internalId,
            Now.AddYears(1),
            CancellationToken.None));
    }

    [Fact]
    public void MisskeyAidMatchesVersionTwelveLengthAlphabetAndChronologicalOrdering()
    {
        string first = ExternalEntityIdService.CreateMisskeyAid(Now, 1);
        string second = ExternalEntityIdService.CreateMisskeyAid(Now, 2);
        string later = ExternalEntityIdService.CreateMisskeyAid(Now.AddMilliseconds(1), 1);

        Assert.Matches("^[0-9a-z]{10}$", first);
        Assert.True(string.CompareOrdinal(first, second) < 0);
        Assert.True(string.CompareOrdinal(second, later) < 0);
    }
}
