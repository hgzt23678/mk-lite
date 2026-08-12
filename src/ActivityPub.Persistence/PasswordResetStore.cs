using ActivityPub.Identity;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class PasswordResetStore(IDbContextFactory<LocalIdentityDbContext> factory) : IPasswordResetStore
{
    public async Task<bool> TryReserveAsync(
        Guid userId,
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || tokenHash.Length != 32 || expiresAt <= requestedAt || cooldown <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenHash), "A valid password reset reservation is required.");
        }

        byte[] hash = tokenHash.ToArray();
        DateTimeOffset cutoff = requestedAt.Subtract(cooldown);
        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.password_reset_requests
                (user_id, token_hash, requested_at, expires_at, claimed_at)
            VALUES
                ({userId}, {hash}, {requestedAt}, {expiresAt}, NULL)
            ON CONFLICT (user_id) DO UPDATE SET
                token_hash = EXCLUDED.token_hash,
                requested_at = EXCLUDED.requested_at,
                expires_at = EXCLUDED.expires_at,
                claimed_at = NULL
            WHERE identity.password_reset_requests.claimed_at IS NOT NULL
               OR identity.password_reset_requests.expires_at <= {requestedAt}
               OR identity.password_reset_requests.requested_at <= {cutoff};
            """, cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task ReleaseAsync(
        Guid userId,
        ReadOnlyMemory<byte> tokenHash,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || tokenHash.Length != 32)
        {
            return;
        }

        byte[] hash = tokenHash.ToArray();
        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM identity.password_reset_requests
            WHERE user_id = {userId} AND token_hash = {hash} AND claimed_at IS NULL;
            """, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid?> FindActiveUserIdAsync(
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (tokenHash.Length != 32)
        {
            return null;
        }

        byte[] hash = tokenHash.ToArray();
        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<LocalPasswordResetRequest>()
            .AsNoTracking()
            .Where(request => request.TokenHash == hash && request.ClaimedAt == null && request.ExpiresAt > now)
            .Select(request => (Guid?)request.UserId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid?> TryClaimAsync(
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken)
    {
        Guid? userId = await FindActiveUserIdAsync(tokenHash, claimedAt, cancellationToken).ConfigureAwait(false);
        if (userId is null)
        {
            return null;
        }

        byte[] hash = tokenHash.ToArray();
        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE identity.password_reset_requests
            SET claimed_at = {claimedAt}
            WHERE user_id = {userId.Value}
              AND token_hash = {hash}
              AND claimed_at IS NULL
              AND expires_at > {claimedAt};
            """, cancellationToken).ConfigureAwait(false);
        return affected == 1 ? userId : null;
    }
}
