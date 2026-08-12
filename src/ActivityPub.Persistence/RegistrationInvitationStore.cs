using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Identity;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class RegistrationInvitationStore(
    IDbContextFactory<LocalIdentityDbContext> factory,
    IAuditLog audit) : IRegistrationInvitationStore
{
    public async Task<bool> CreateAsync(
        byte[] codeHash,
        string operatorId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codeHash);
        LocalRegistrationInvitation invitation = LocalRegistrationInvitation.Create(
            codeHash,
            operatorId,
            createdAt,
            expiresAt);
        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.registration_invitations
                (id, code_hash, created_by, created_at, expires_at, reservation_id,
                 reserved_at, reservation_expires_at, consumed_at, consumed_by_username)
            VALUES
                ({invitation.Id}, {invitation.CodeHash}, {invitation.CreatedBy}, {invitation.CreatedAt},
                 {invitation.ExpiresAt}, NULL, NULL, NULL, NULL, NULL)
            ON CONFLICT (code_hash) DO NOTHING;
            """, cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return false;
        }

        await audit.AppendAsync(
            "identity",
            "registration-invitation-issued",
            operatorId,
            invitation.Id.ToString("N"),
            JsonSerializer.Serialize(new { invitationId = invitation.Id, expiresAt }),
            createdAt,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<RegistrationInvitationReservation?> ReserveAsync(
        byte[] codeHash,
        DateTimeOffset now,
        DateTimeOffset reservationExpiresAt,
        CancellationToken cancellationToken)
    {
        if (codeHash.Length != 32 || reservationExpiresAt <= now)
        {
            return null;
        }

        Guid reservationId = Guid.NewGuid();
        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE identity.registration_invitations
            SET reservation_id = {reservationId},
                reserved_at = {now},
                reservation_expires_at = {reservationExpiresAt}
            WHERE code_hash = {codeHash}
              AND consumed_at IS NULL
              AND expires_at > {now}
              AND (reservation_id IS NULL OR reservation_expires_at <= {now});
            """, cancellationToken).ConfigureAwait(false);
        return affected == 1 ? new RegistrationInvitationReservation(reservationId) : null;
    }

    public async Task<bool> ConsumeAsync(
        RegistrationInvitationReservation reservation,
        string username,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (string.IsNullOrWhiteSpace(username) || username.Length > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(username));
        }

        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        Guid? invitationId = await db.Set<LocalRegistrationInvitation>()
            .AsNoTracking()
            .Where(invitation => invitation.ReservationId == reservation.Id &&
                invitation.ConsumedAt == null &&
                invitation.ReservationExpiresAt > consumedAt &&
                invitation.ExpiresAt > consumedAt)
            .Select(invitation => (Guid?)invitation.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (invitationId is null)
        {
            return false;
        }

        int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE identity.registration_invitations
            SET consumed_at = {consumedAt},
                consumed_by_username = {username},
                reservation_id = NULL,
                reserved_at = NULL,
                reservation_expires_at = NULL
            WHERE id = {invitationId.Value}
              AND reservation_id = {reservation.Id}
              AND consumed_at IS NULL
              AND expires_at > {consumedAt};
            """, cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return false;
        }

        await audit.AppendAsync(
            "identity",
            "registration-invitation-consumed",
            $"registration:{username}",
            invitationId.Value.ToString("N"),
            JsonSerializer.Serialize(new { invitationId, username }),
            consumedAt,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task ReleaseAsync(
        RegistrationInvitationReservation reservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        await using LocalIdentityDbContext db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE identity.registration_invitations
            SET reservation_id = NULL,
                reserved_at = NULL,
                reservation_expires_at = NULL
            WHERE reservation_id = {reservation.Id} AND consumed_at IS NULL;
            """, cancellationToken).ConfigureAwait(false);
    }
}
