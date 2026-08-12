using ActivityPub.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class LocalIdentityDbContext(
    DbContextOptions<LocalIdentityDbContext> options)
    : IdentityDbContext<LocalIdentityUser, LocalIdentityRole, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");

        builder.Entity<LocalIdentityUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(user => user.ProvisioningState)
                .HasColumnName("provisioning_state")
                .HasConversion<string>()
                .HasMaxLength(32);
            entity.Property(user => user.LocalActorId).HasColumnName("local_actor_id");
            entity.Property(user => user.LocalActorIri).HasColumnName("local_actor_iri").HasMaxLength(2_048);
            entity.Property(user => user.ProvisioningAttempts).HasColumnName("provisioning_attempts");
            entity.Property(user => user.ProvisioningErrorCode).HasColumnName("provisioning_error_code").HasMaxLength(64);
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at");
            entity.Property(user => user.ActivatedAt).HasColumnName("activated_at");
            entity.Property(user => user.LastSignedInAt).HasColumnName("last_signed_in_at");
            entity.HasIndex(user => user.LocalActorId)
                .IsUnique()
                .HasFilter("local_actor_id IS NOT NULL")
                .HasDatabaseName("ux_identity_users_local_actor_id");
            entity.HasIndex(user => user.LocalActorIri)
                .IsUnique()
                .HasFilter("local_actor_iri IS NOT NULL")
                .HasDatabaseName("ux_identity_users_local_actor_iri");
            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique()
                .HasFilter("\"NormalizedEmail\" IS NOT NULL")
                .HasDatabaseName("EmailIndex");
            entity.HasIndex(user => new { user.ProvisioningState, user.UpdatedAt })
                .HasDatabaseName("ix_identity_users_provisioning_state_updated");
        });
        builder.Entity<LocalIdentityRole>().ToTable("roles");
        builder.Entity<LocalPasswordResetRequest>(entity =>
        {
            entity.ToTable("password_reset_requests");
            entity.HasKey(request => request.UserId);
            entity.Property(request => request.UserId).HasColumnName("user_id");
            entity.Property(request => request.TokenHash).HasColumnName("token_hash").HasMaxLength(32);
            entity.Property(request => request.RequestedAt).HasColumnName("requested_at");
            entity.Property(request => request.ExpiresAt).HasColumnName("expires_at");
            entity.Property(request => request.ClaimedAt).HasColumnName("claimed_at");
            entity.HasIndex(request => request.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_identity_password_reset_requests_token_hash");
            entity.HasIndex(request => new { request.ExpiresAt, request.ClaimedAt })
                .HasDatabaseName("ix_identity_password_reset_requests_expiry");
            entity.HasOne<LocalIdentityUser>()
                .WithOne()
                .HasForeignKey<LocalPasswordResetRequest>(request => request.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<LocalEmailConfirmationRequest>(entity =>
        {
            entity.ToTable("email_confirmation_requests");
            entity.HasKey(request => request.UserId);
            entity.Property(request => request.UserId).HasColumnName("user_id");
            entity.Property(request => request.TokenHash).HasColumnName("token_hash").HasMaxLength(32);
            entity.Property(request => request.RequestedAt).HasColumnName("requested_at");
            entity.Property(request => request.ExpiresAt).HasColumnName("expires_at");
            entity.Property(request => request.ClaimedAt).HasColumnName("claimed_at");
            entity.HasIndex(request => request.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_identity_email_confirmation_requests_token_hash");
            entity.HasIndex(request => new { request.ExpiresAt, request.ClaimedAt })
                .HasDatabaseName("ix_identity_email_confirmation_requests_expiry");
            entity.HasOne<LocalIdentityUser>()
                .WithOne()
                .HasForeignKey<LocalEmailConfirmationRequest>(request => request.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<LocalRegistrationInvitation>(entity =>
        {
            entity.ToTable("registration_invitations", table =>
            {
                table.HasCheckConstraint(
                    "ck_identity_registration_invitations_code_hash_length",
                    "octet_length(code_hash) = 32");
                table.HasCheckConstraint(
                    "ck_identity_registration_invitations_expiry",
                    "expires_at > created_at");
                table.HasCheckConstraint(
                    "ck_identity_registration_invitations_reservation",
                    "(reservation_id IS NULL AND reserved_at IS NULL AND reservation_expires_at IS NULL) OR " +
                    "(reservation_id IS NOT NULL AND reserved_at IS NOT NULL AND reservation_expires_at IS NOT NULL " +
                    "AND reservation_expires_at > reserved_at)");
                table.HasCheckConstraint(
                    "ck_identity_registration_invitations_consumption",
                    "(consumed_at IS NULL AND consumed_by_username IS NULL) OR " +
                    "(consumed_at IS NOT NULL AND consumed_by_username IS NOT NULL)");
            });
            entity.HasKey(invitation => invitation.Id);
            entity.Property(invitation => invitation.Id).HasColumnName("id");
            entity.Property(invitation => invitation.CodeHash).HasColumnName("code_hash").HasMaxLength(32);
            entity.Property(invitation => invitation.CreatedBy).HasColumnName("created_by").HasMaxLength(256);
            entity.Property(invitation => invitation.CreatedAt).HasColumnName("created_at");
            entity.Property(invitation => invitation.ExpiresAt).HasColumnName("expires_at");
            entity.Property(invitation => invitation.ReservationId).HasColumnName("reservation_id");
            entity.Property(invitation => invitation.ReservedAt).HasColumnName("reserved_at");
            entity.Property(invitation => invitation.ReservationExpiresAt).HasColumnName("reservation_expires_at");
            entity.Property(invitation => invitation.ConsumedAt).HasColumnName("consumed_at");
            entity.Property(invitation => invitation.ConsumedByUsername)
                .HasColumnName("consumed_by_username")
                .HasMaxLength(20);
            entity.HasIndex(invitation => invitation.CodeHash)
                .IsUnique()
                .HasDatabaseName("ux_identity_registration_invitations_code_hash");
            entity.HasIndex(invitation => invitation.ReservationId)
                .IsUnique()
                .HasFilter("reservation_id IS NOT NULL")
                .HasDatabaseName("ux_identity_registration_invitations_reservation_id");
            entity.HasIndex(invitation => new { invitation.ExpiresAt, invitation.ConsumedAt })
                .HasDatabaseName("ix_identity_registration_invitations_expiry");
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityUserPasskey<Guid>>().ToTable("user_passkeys");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
    }
}
