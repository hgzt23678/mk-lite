using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class LocalActorAdministration(
    IDbContextFactory<FederationDbContext> contextFactory,
    IExternalKeyProvisioner keyProvisioner,
    PublicIriFactory iriFactory,
    IClock clock) : ILocalActorAdministration
{
    private const long AuditAdvisoryLock = 4_165_550_803_371_912_001;

    public async Task<LocalActorAdministrationResult> CreateAsync(
        string username,
        ActorKind kind,
        string displayName,
        string summaryHtml,
        bool manuallyApprovesFollowers,
        bool discoverable,
        bool indexable,
        string operatorId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        LocalActor actor = LocalActor.Create(iriFactory.Actor(username), username, kind, now);
        actor.UpdateProfile(displayName, summaryHtml, manuallyApprovesFollowers, discoverable, indexable, now);
        Guid externalKeyId = Guid.NewGuid();
        string handle = $"ap-{actor.Id:N}-{externalKeyId:N}";
        ExternalKeyProvision provision = await keyProvisioner.CreateRsaKeyAsync(handle, cancellationToken).ConfigureAwait(false);
        ActorKey key = ActorKey.CreateLocal(iriFactory.Key(actor.Username, externalKeyId), actor.Iri, provision.PublicKeyPem, provision.Handle, now);
        key.Activate(now);
        actor.SetActiveKey(key.Id, now);

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        db.ActorKeys.Add(key);
        db.LocalActors.Add(actor);
        await AppendAuditAsync(db, "identity", "local-actor-created", operatorId, actor.Iri, new { actor.Id, keyId = key.Id }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(actor.Id, actor.Iri, key.Id, key.KeyIri);
    }

    public async Task<LocalActorAdministrationResult?> RotateKeyAsync(
        string username,
        TimeSpan overlap,
        string operatorId,
        CancellationToken cancellationToken)
    {
        if (overlap < TimeSpan.FromHours(1) || overlap > TimeSpan.FromDays(30))
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Key overlap must be between one hour and thirty days.");
        }

        string normalizedUsername = username.ToUpperInvariant();
        await using (FederationDbContext lookup = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await lookup.LocalActors.AnyAsync(x => x.NormalizedUsername == normalizedUsername, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        Guid externalKeyId = Guid.NewGuid();
        string handle = $"ap-{Guid.NewGuid():N}-{externalKeyId:N}";
        ExternalKeyProvision provision = await keyProvisioner.CreateRsaKeyAsync(handle, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        LocalActor? actor = await db.LocalActors.SingleOrDefaultAsync(
            x => x.NormalizedUsername == normalizedUsername,
            cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        ActorKey? currentKey = actor.ActiveKeyId is null
            ? null
            : await db.ActorKeys.SingleOrDefaultAsync(x => x.Id == actor.ActiveKeyId, cancellationToken).ConfigureAwait(false);
        var newKey = ActorKey.CreateLocal(iriFactory.Key(actor.Username, externalKeyId), actor.Iri, provision.PublicKeyPem, provision.Handle, now);
        newKey.Activate(now);
        currentKey?.Retire(now, now.Add(overlap));
        actor.SetActiveKey(newKey.Id, now);
        db.ActorKeys.Add(newKey);
        await AppendAuditAsync(db, "identity", "actor-key-rotated", operatorId, actor.Iri, new
        {
            actor.Id,
            previousKeyId = currentKey?.Id,
            newKeyId = newKey.Id,
            overlapEndsAt = now.Add(overlap)
        }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(actor.Id, actor.Iri, newKey.Id, newKey.KeyIri);
    }

    private static async Task AppendAuditAsync(
        FederationDbContext db,
        string category,
        string action,
        string actor,
        string target,
        object details,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AuditAdvisoryLock})", cancellationToken).ConfigureAwait(false);
        string? previousHash = await db.AuditEvents
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => x.EventHash)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        db.AuditEvents.Add(AuditEvent.Create(
            category,
            action,
            actor,
            target,
            JsonSerializer.Serialize(details),
            previousHash,
            now));
    }
}
