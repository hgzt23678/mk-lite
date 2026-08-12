using System.Data;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class StreamConnectionLeaseStore(IDbContextFactory<FederationDbContext> contextFactory)
    : IStreamConnectionLeaseStore
{
    public async Task<StreamConnectionLeaseToken?> TryAcquireAsync(
        string? subject,
        string remoteAddress,
        string instanceId,
        int maximumPerSubject,
        int maximumPerAddress,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPerSubject, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPerAddress, 1);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        string[] lockKeys = subject is null
            ? [$"stream-address:{remoteAddress}"]
            : [$"stream-address:{remoteAddress}", $"stream-subject:{subject}"];
        foreach (string lockKey in lockKeys.Order(StringComparer.Ordinal))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken).ConfigureAwait(false);
        }

        await db.StreamConnectionLeases.Where(x => x.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        int addressCount = await db.StreamConnectionLeases.CountAsync(
            x => x.RemoteAddress == remoteAddress,
            cancellationToken).ConfigureAwait(false);
        if (addressCount >= maximumPerAddress)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (subject is not null)
        {
            int subjectCount = await db.StreamConnectionLeases.CountAsync(
                x => x.Subject == subject,
                cancellationToken).ConfigureAwait(false);
            if (subjectCount >= maximumPerSubject)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        StreamConnectionLease lease = StreamConnectionLease.Acquire(
            subject,
            remoteAddress,
            instanceId,
            now,
            duration);
        db.StreamConnectionLeases.Add(lease);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(lease.Id, lease.InstanceId);
    }

    public async Task<bool> ExtendAsync(
        StreamConnectionLeaseToken token,
        DateTimeOffset now,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        StreamConnectionLease? lease = await db.StreamConnectionLeases.SingleOrDefaultAsync(
            x => x.Id == token.Id && x.InstanceId == token.InstanceId,
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            return false;
        }

        lease.Extend(token.InstanceId, now, duration);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task ReleaseAsync(StreamConnectionLeaseToken token, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.StreamConnectionLeases.Where(x => x.Id == token.Id && x.InstanceId == token.InstanceId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
