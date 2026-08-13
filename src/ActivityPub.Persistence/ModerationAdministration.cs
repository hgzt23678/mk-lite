using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class ModerationAdministration(
    IDbContextFactory<FederationDbContext> contextFactory,
    IClock clock,
    IFederationQueueSignal queueSignal) : IModerationAdministration
{
    private const long AuditAdvisoryLock = 4_165_550_803_371_912_001;
    private const string OutboundPauseControl = "outbound-delivery-pause";

    public async Task<IReadOnlyList<DeadLetterSummary>> ListDeadLettersAsync(
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken)
    {
        int safeLimit = ValidateLimit(limit);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<DeadLetter> query = db.DeadLetters;
        if (before is not null)
        {
            query = query.Where(x => x.CreatedAt < before);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(safeLimit)
            .Select(x => new DeadLetterSummary(
                x.Id,
                x.SourceType,
                x.SourceId,
                x.ReasonCode,
                x.Reason,
                x.CreatedAt,
                x.ReplayedAt,
                x.ReplayedBy))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReportSummary>> ListReportsAsync(
        DateTimeOffset? before,
        int limit,
        bool unresolvedOnly,
        CancellationToken cancellationToken)
    {
        int safeLimit = ValidateLimit(limit);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<Report> query = db.Reports;
        if (before is not null)
        {
            query = query.Where(x => x.CreatedAt < before);
        }

        if (unresolvedOnly)
        {
            query = query.Where(x => x.ResolvedAt == null);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(safeLimit)
            .Select(x => new ReportSummary(
                x.Id,
                x.Iri,
                x.ReporterIri,
                x.TargetIri,
                x.CreatedAt,
                x.ResolvedAt,
                x.ResolvedBy))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Guid> CreateDomainPolicyAsync(
        string domain,
        FederationPolicyKind kind,
        string reason,
        string operatorId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        ValidateExpiration(expiresAt, now);
        DomainPolicy policy = DomainPolicy.Create(domain, kind, reason, operatorId, now, expiresAt);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        db.DomainPolicies.Add(policy);
        db.ModerationActions.Add(ModerationAction.Create(ToActionKind(kind), policy.Domain, reason, operatorId, now, expiresAt));
        await AppendAuditAsync(db, "moderation", "domain-policy-created", operatorId, policy.Domain, new { policy.Id, kind, expiresAt }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return policy.Id;
    }

    public async Task<bool> RevokeDomainPolicyAsync(Guid policyId, string operatorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        DomainPolicy? policy = await db.DomainPolicies.SingleOrDefaultAsync(x => x.Id == policyId, cancellationToken).ConfigureAwait(false);
        if (policy is null || policy.RevokedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        policy.Revoke(operatorId, now);
        await AppendAuditAsync(db, "moderation", "domain-policy-revoked", operatorId, policy.Domain, new { policy.Id }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<Guid> CreateActorPolicyAsync(
        string actorIri,
        ModerationActionKind kind,
        string reason,
        string operatorId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        if (kind is not (ModerationActionKind.BlockActor or ModerationActionKind.MuteActor))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Actor policies support BlockActor and MuteActor only.");
        }

        DateTimeOffset now = clock.UtcNow;
        ValidateExpiration(expiresAt, now);
        ActorPolicy policy = ActorPolicy.Create(actorIri, kind, reason, operatorId, now, expiresAt);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        db.ActorPolicies.Add(policy);
        db.ModerationActions.Add(ModerationAction.Create(kind, policy.ActorIri, reason, operatorId, now, expiresAt));
        await AppendAuditAsync(db, "moderation", "actor-policy-created", operatorId, policy.ActorIri, new { policy.Id, kind, expiresAt }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return policy.Id;
    }

    public async Task<bool> RevokeActorPolicyAsync(Guid policyId, string operatorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        ActorPolicy? policy = await db.ActorPolicies.SingleOrDefaultAsync(x => x.Id == policyId, cancellationToken).ConfigureAwait(false);
        if (policy is null || policy.RevokedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        policy.Revoke(operatorId, now);
        await AppendAuditAsync(db, "moderation", "actor-policy-revoked", operatorId, policy.ActorIri, new { policy.Id }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ResolveReportAsync(Guid reportId, string operatorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        Report? report = await db.Reports.SingleOrDefaultAsync(x => x.Id == reportId, cancellationToken).ConfigureAwait(false);
        if (report is null || report.ResolvedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        report.Resolve(operatorId, now);
        await AppendAuditAsync(db, "moderation", "report-resolved", operatorId, report.TargetIri, new { report.Id }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RequeueDeadLetterAsync(Guid deadLetterId, string operatorId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        DeadLetter? deadLetter = await db.DeadLetters.SingleOrDefaultAsync(x => x.Id == deadLetterId, cancellationToken).ConfigureAwait(false);
        if (deadLetter is null || deadLetter.ReplayedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        string activityIri;
        Guid workItemId;
        bool deliveryWasRequeued = false;
        bool inboxWasRequeued = false;
        if (string.Equals(deadLetter.SourceType, "delivery", StringComparison.Ordinal))
        {
            Delivery? delivery = await db.Deliveries.SingleOrDefaultAsync(x => x.Id == deadLetter.SourceId, cancellationToken).ConfigureAwait(false);
            if (delivery is null || delivery.State != WorkItemState.DeadLettered)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            delivery.RequeueFromDeadLetter(now);
            deliveryWasRequeued = true;
            activityIri = delivery.ActivityIri;
            workItemId = delivery.Id;
        }
        else if (string.Equals(deadLetter.SourceType, "inbox", StringComparison.Ordinal))
        {
            InboxItem? inboxItem = await db.InboxItems.SingleOrDefaultAsync(x => x.Id == deadLetter.SourceId, cancellationToken).ConfigureAwait(false);
            if (inboxItem is null || inboxItem.State != WorkItemState.DeadLettered)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            inboxItem.RequeueInboxFromDeadLetter(now);
            inboxWasRequeued = true;
            activityIri = inboxItem.ActivityIri;
            workItemId = inboxItem.Id;
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        deadLetter.MarkReplayed(operatorId, now);
        await AppendAuditAsync(
            db,
            "operations",
            "dead-letter-requeued",
            operatorId,
            activityIri,
            new { deadLetterId = deadLetter.Id, sourceType = deadLetter.SourceType, workItemId },
            now,
            cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (deliveryWasRequeued)
        {
            await queueSignal.NotifyDeliveryAvailableAsync(cancellationToken).ConfigureAwait(false);
        }

        if (inboxWasRequeued)
        {
            await queueSignal.NotifyInboxAvailableAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<OperationalControlState> GetOperationalControlAsync(CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        OperationalControl? control = await db.OperationalControls.SingleOrDefaultAsync(x => x.Name == OutboundPauseControl, cancellationToken).ConfigureAwait(false);
        return control is null
            ? new(false, null, null, null)
            : new(control.Enabled, control.Reason, control.UpdatedBy, control.UpdatedAt);
    }

    public async Task SetOutboundDeliveryPausedAsync(bool paused, string reason, string operatorId, CancellationToken cancellationToken)
    {
        string safeReason = DomainText.Required(reason, nameof(reason), 2_000);
        string safeOperator = DomainText.Required(operatorId, nameof(operatorId), 256);
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        OperationalControl? control = await db.OperationalControls.SingleOrDefaultAsync(x => x.Name == OutboundPauseControl, cancellationToken).ConfigureAwait(false);
        if (control is null)
        {
            control = new OperationalControl
            {
                Name = OutboundPauseControl,
                Enabled = paused,
                Reason = safeReason,
                UpdatedBy = safeOperator,
                UpdatedAt = now,
                Version = 0
            };
            db.OperationalControls.Add(control);
        }
        else
        {
            control.Enabled = paused;
            control.Reason = safeReason;
            control.UpdatedBy = safeOperator;
            control.UpdatedAt = now;
            control.Version++;
        }

        await AppendAuditAsync(db, "operations", paused ? "outbound-paused" : "outbound-resumed", safeOperator, "all-domains", new { reason = safeReason }, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CancelPendingDeliveriesForDomainAsync(
        string domain,
        string reason,
        string operatorId,
        CancellationToken cancellationToken)
    {
        DomainPolicy validated = DomainPolicy.Create(
            domain,
            FederationPolicyKind.PauseOutbound,
            reason,
            operatorId,
            clock.UtcNow,
            null);
        DateTimeOffset now = clock.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        int cancelled = await db.Deliveries
            .Where(x => x.RemoteDomain == validated.Domain &&
                (x.State == WorkItemState.Pending || x.State == WorkItemState.Leased))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.State, WorkItemState.Cancelled)
                .SetProperty(x => x.LeaseOwner, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.LastErrorCode, "cancelled_by_operator")
                .SetProperty(x => x.LastError, validated.Reason)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, x => x.Version + 1),
                cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync(
            db,
            "operations",
            "domain-deliveries-cancelled",
            validated.CreatedBy,
            validated.Domain,
            new { count = cancelled, reason = validated.Reason },
            now,
            cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return cancelled;
    }

    private static int ValidateLimit(int limit) => limit is >= 1 and <= 200
        ? limit
        : throw new ArgumentOutOfRangeException(nameof(limit), "Admin page size must be between 1 and 200.");

    private static void ValidateExpiration(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Policy expiration must be in the future.");
        }
    }

    private static ModerationActionKind ToActionKind(FederationPolicyKind kind) => kind switch
    {
        FederationPolicyKind.Allow => ModerationActionKind.AllowDomain,
        FederationPolicyKind.Limit => ModerationActionKind.LimitDomain,
        FederationPolicyKind.Reject => ModerationActionKind.RejectDomain,
        FederationPolicyKind.Silence => ModerationActionKind.SilenceDomain,
        FederationPolicyKind.RejectMedia => ModerationActionKind.RejectMedia,
        FederationPolicyKind.PauseOutbound => ModerationActionKind.PauseOutbound,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

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
        string json = JsonSerializer.Serialize(details);
        db.AuditEvents.Add(AuditEvent.Create(category, action, actor, target, json, previousHash, now));
    }
}
