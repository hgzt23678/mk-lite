using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ActivityPub.Persistence;

public sealed partial class AtomicLocalAccountRegistration(
    LocalIdentityDbContext db,
    UserManager<LocalIdentityUser> users,
    IAuditLog audit,
    ILogger<AtomicLocalAccountRegistration> logger) : IAtomicLocalAccountRegistration
{
    public async Task<AtomicLocalAccountCreationResult> CreateAsync(
        LocalIdentityUser user,
        string password,
        RegistrationInvitationReservation? invitationReservation,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);
        if (invitationReservation is null)
        {
            IdentityResult result = await users.CreateAsync(user, password).ConfigureAwait(false);
            return new(true, false, result);
        }

        await using IDbContextTransaction transaction = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            int consumed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE identity.registration_invitations
                SET consumed_at = {createdAt},
                    consumed_by_username = {user.UserName},
                    reservation_id = NULL,
                    reserved_at = NULL,
                    reservation_expires_at = NULL
                WHERE reservation_id = {invitationReservation.Id}
                  AND consumed_at IS NULL
                  AND reservation_expires_at > {createdAt}
                  AND expires_at > {createdAt};
                """, cancellationToken).ConfigureAwait(false);
            if (consumed != 1)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return new(false, false, IdentityResult.Failed());
            }

            IdentityResult result = await users.CreateAsync(user, password).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                db.ChangeTracker.Clear();
                return new(true, false, result);
            }

            // Once both writes have succeeded, finish the atomic commit even if the HTTP request
            // disconnects. Cancelling here would make the caller unable to distinguish rollback
            // from an already-committed account/invitation pair.
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            await AppendAuditBestEffortAsync(user, invitationReservation, createdAt).ConfigureAwait(false);
            return new(true, true, result);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task AppendAuditBestEffortAsync(
        LocalIdentityUser user,
        RegistrationInvitationReservation reservation,
        DateTimeOffset consumedAt)
    {
        try
        {
            await audit.AppendAsync(
                "identity",
                "registration-invitation-consumed",
                $"registration:{user.UserName}",
                reservation.Id.ToString("N"),
                JsonSerializer.Serialize(new { reservationId = reservation.Id, username = user.UserName }),
                consumedAt,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogAuditFailure(logger, user.Id, exception);
        }
    }

    [LoggerMessage(
        EventId = 2451,
        Level = LogLevel.Error,
        Message = "Registration invitation audit persistence failed after atomic account creation. UserId={UserId}")]
    private static partial void LogAuditFailure(ILogger logger, Guid userId, Exception exception);
}
