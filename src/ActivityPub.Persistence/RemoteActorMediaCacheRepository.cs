using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class RemoteActorMediaCacheRepository(
    IDbContextFactory<FederationDbContext> contextFactory) : IRemoteActorMediaCacheRepository
{
    public async Task<RemoteActorMediaSource?> ResolveSourceAsync(
        Guid remoteActorId,
        string sourceToken,
        CancellationToken cancellationToken)
    {
        if (remoteActorId == Guid.Empty || !RemoteMediaSourceToken.TryNormalize(sourceToken, out string token))
        {
            return null;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        RemoteActor? actor = await db.RemoteActors.SingleOrDefaultAsync(
            value => value.Id == remoteActorId && value.GoneAt == null,
            cancellationToken).ConfigureAwait(false);
        return actor is null ? null : FindSource(actor, token);
    }

    public async Task<RemoteActorMediaCacheClaim?> ClaimFetchAsync(
        RemoteActorMediaSource source,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.RemoteActorId == Guid.Empty || leaseExpiresAt <= now ||
            !RemoteMediaSourceToken.TryNormalize(source.SourceToken, out string token))
        {
            throw new ArgumentException("The remote actor media cache claim is invalid.", nameof(source));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        string lockKey = source.RemoteActorId.ToString("N") + ":" + token;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken).ConfigureAwait(false);

        RemoteActor? actor = await db.RemoteActors.SingleOrDefaultAsync(
            value => value.Id == source.RemoteActorId && value.GoneAt == null,
            cancellationToken).ConfigureAwait(false);
        RemoteActorMediaSource? currentSource = actor is null ? null : FindSource(actor, token);
        if (currentSource is null ||
            !string.Equals(currentSource.SourceIri, source.SourceIri, StringComparison.Ordinal) ||
            !string.Equals(currentSource.ActorIri, source.ActorIri, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        RemoteActorMediaCacheEntry? entry = await db.RemoteActorMediaCache
            .FromSql($"""
                SELECT cache.*
                FROM activitypub.remote_actor_media_cache AS cache
                WHERE cache.remote_actor_id = {source.RemoteActorId}
                  AND cache.source_token = {token}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            string leaseOwner = Guid.NewGuid().ToString("N");
            entry = RemoteActorMediaCacheEntry.CreateClaimed(
                source.RemoteActorId,
                currentSource.Kind,
                currentSource.SourceIri,
                token,
                leaseOwner,
                now,
                leaseExpiresAt);
            db.RemoteActorMediaCache.Add(entry);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ToClaim(entry, currentSource, RemoteActorMediaCacheClaimState.Acquired);
        }

        RemoteActorMediaCacheClaimState state;
        if (!string.Equals(entry.SourceIri, currentSource.SourceIri, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (entry.IsFresh(now))
        {
            state = RemoteActorMediaCacheClaimState.Fresh;
        }
        else if (entry.HasActiveFailure(now))
        {
            state = RemoteActorMediaCacheClaimState.Failed;
        }
        else if (entry.HasActiveLease(now))
        {
            state = RemoteActorMediaCacheClaimState.Busy;
        }
        else
        {
            string leaseOwner = Guid.NewGuid().ToString("N");
            if (!entry.TryClaim(leaseOwner, now, leaseExpiresAt))
            {
                throw new InvalidOperationException("The remote actor media cache entry could not be claimed under its database lock.");
            }

            state = RemoteActorMediaCacheClaimState.Acquired;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToClaim(entry, currentSource, state);
    }

    public async Task<RemoteActorMediaCacheClaim?> ReadAsync(
        Guid remoteActorId,
        string sourceToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (remoteActorId == Guid.Empty || !RemoteMediaSourceToken.TryNormalize(sourceToken, out string token))
        {
            return null;
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        RemoteActor? actor = await db.RemoteActors.SingleOrDefaultAsync(
            value => value.Id == remoteActorId && value.GoneAt == null,
            cancellationToken).ConfigureAwait(false);
        RemoteActorMediaSource? source = actor is null ? null : FindSource(actor, token);
        if (source is null)
        {
            return null;
        }

        RemoteActorMediaCacheEntry? entry = await db.RemoteActorMediaCache.SingleOrDefaultAsync(
            value => value.RemoteActorId == remoteActorId && value.SourceToken == token,
            cancellationToken).ConfigureAwait(false);
        if (entry is null || !string.Equals(entry.SourceIri, source.SourceIri, StringComparison.Ordinal))
        {
            return null;
        }

        RemoteActorMediaCacheClaimState state = entry.IsFresh(now)
            ? RemoteActorMediaCacheClaimState.Fresh
            : entry.HasActiveFailure(now)
                ? RemoteActorMediaCacheClaimState.Failed
                : entry.HasActiveLease(now)
                    ? RemoteActorMediaCacheClaimState.Busy
                    : RemoteActorMediaCacheClaimState.Busy;
        return ToClaim(entry, source, state);
    }

    public async Task<bool> RenewLeaseAsync(
        Guid entryId,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseExpiresAt, now);

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int changed = await db.RemoteActorMediaCache
            .Where(value => value.Id == entryId && value.LeaseOwner == leaseOwner && value.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.LeaseExpiresAt, leaseExpiresAt),
                cancellationToken).ConfigureAwait(false);
        return changed == 1;
    }

    public async Task<bool> CompleteAsync(
        Guid entryId,
        string leaseOwner,
        Guid mediaId,
        string? remoteETag,
        DateTimeOffset? remoteLastModified,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await CreateTrackingContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        RemoteActorMediaCacheEntry? entry = await FindForUpdateAsync(db, entryId, cancellationToken).ConfigureAwait(false);
        MediaResource? media = await db.Media.SingleOrDefaultAsync(
            value => value.Id == mediaId && value.State == MediaState.Available,
            cancellationToken).ConfigureAwait(false);
        if (entry is null || media is null || !entry.Complete(
                leaseOwner,
                mediaId,
                remoteETag,
                remoteLastModified,
                now,
                expiresAt))
        {
            return false;
        }

        media.RefreshCacheReference(now < media.UpdatedAt ? media.UpdatedAt : now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> FailAsync(
        Guid entryId,
        string leaseOwner,
        RemoteMediaCacheFailureKind failureKind,
        DateTimeOffset now,
        DateTimeOffset retryAfter,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await CreateTrackingContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        RemoteActorMediaCacheEntry? entry = await FindForUpdateAsync(db, entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null || !entry.Fail(leaseOwner, failureKind, now, retryAfter))
        {
            return false;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<int> ExpireAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        await using FederationDbContext db = await CreateTrackingContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        RemoteActorMediaCacheEntry[] expired = await db.RemoteActorMediaCache
            .FromSql($"""
                SELECT cache.*
                FROM activitypub.remote_actor_media_cache AS cache
                WHERE cache.expires_at <= {now}
                  AND (cache.lease_expires_at IS NULL OR cache.lease_expires_at <= {now})
                ORDER BY cache.expires_at, cache.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        db.RemoteActorMediaCache.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expired.Length;
    }

    private async Task<FederationDbContext> CreateTrackingContextAsync(CancellationToken cancellationToken)
    {
        FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        return db;
    }

    private static Task<RemoteActorMediaCacheEntry?> FindForUpdateAsync(
        FederationDbContext db,
        Guid entryId,
        CancellationToken cancellationToken) =>
        db.RemoteActorMediaCache
            .FromSql($"""
                SELECT cache.*
                FROM activitypub.remote_actor_media_cache AS cache
                WHERE cache.id = {entryId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

    private static RemoteActorMediaCacheClaim ToClaim(
        RemoteActorMediaCacheEntry entry,
        RemoteActorMediaSource source,
        RemoteActorMediaCacheClaimState state) =>
        new(
            entry.Id,
            state,
            source,
            entry.LeaseOwner,
            entry.MediaId,
            entry.RemoteETag,
            entry.RemoteLastModified,
            entry.FailureKind,
            entry.RetryAfter);

    private static RemoteActorMediaSource? FindSource(RemoteActor actor, string sourceToken)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(actor.RawJson, new JsonDocumentOptions { MaxDepth = 64 });
            if (TryFindSource(document.RootElement, "icon", sourceToken, out string? avatar))
            {
                return new(actor.Id, actor.Iri, RemoteActorMediaKind.Avatar, avatar, sourceToken);
            }

            return TryFindSource(document.RootElement, "image", sourceToken, out string? banner)
                ? new(actor.Id, actor.Iri, RemoteActorMediaKind.Banner, banner, sourceToken)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryFindSource(
        JsonElement root,
        string propertyName,
        string sourceToken,
        out string sourceIri)
    {
        sourceIri = string.Empty;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out JsonElement value))
        {
            return false;
        }

        IEnumerable<JsonElement> candidates = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];
        foreach (JsonElement candidate in candidates)
        {
            foreach (string raw in ReadUrls(candidate))
            {
                try
                {
                    string canonical = CanonicalIri.RequireAbsoluteHttp(raw, propertyName);
                    if (string.Equals(RemoteMediaSourceToken.Create(canonical), sourceToken, StringComparison.Ordinal))
                    {
                        sourceIri = canonical;
                        return true;
                    }
                }
                catch (DomainException)
                {
                    // Invalid remote URLs are ignored rather than becoming proxy targets.
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> ReadUrls(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            if (value.GetString() is { } direct)
            {
                yield return direct;
            }

            yield break;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                foreach (string nested in ReadUrls(item))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (value.TryGetProperty("url", out JsonElement url))
        {
            foreach (string nested in ReadUrls(url))
            {
                yield return nested;
            }
        }

        if (value.TryGetProperty("href", out JsonElement directHref) &&
            directHref.ValueKind == JsonValueKind.String &&
            directHref.GetString() is { } href)
        {
            yield return href;
        }
    }
}
