using System.Data;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class RemoteActorDirectory(IDbContextFactory<FederationDbContext> contextFactory) : IRemoteActorDirectory
{
    public async Task<RemoteActorEndpoint?> FindEndpointAsync(string actorIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string? inbox = await db.RemoteEndpoints
            .Where(x => x.ActorIri == actorIri && x.Kind == EndpointKind.Inbox && x.GoneAt == null)
            .Select(x => x.EndpointIri)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (inbox is null)
        {
            return null;
        }

        string? shared = await db.RemoteEndpoints
            .Where(x => x.ActorIri == actorIri && x.Kind == EndpointKind.SharedInbox && x.GoneAt == null)
            .Select(x => x.EndpointIri)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new(actorIri, inbox, shared);
    }

    public async Task<IReadOnlyList<RemoteActorEndpoint>> FindAcceptedFollowerEndpointsAsync(
        string localActorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<string> actorIris = await db.FollowRelations
            .Where(x => x.FollowedIri == localActorIri && x.State == FollowState.Accepted)
            .Select(x => x.FollowerIri)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (actorIris.Count == 0)
        {
            return [];
        }

        var rows = await db.RemoteEndpoints
            .Where(x => actorIris.Contains(x.ActorIri) && x.GoneAt == null &&
                (x.Kind == EndpointKind.Inbox || x.Kind == EndpointKind.SharedInbox))
            .Select(x => new { x.ActorIri, x.Kind, x.EndpointIri })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(x => x.ActorIri, StringComparer.Ordinal)
            .Select(group => new RemoteActorEndpoint(
                group.Key,
                group.Single(x => x.Kind == EndpointKind.Inbox).EndpointIri,
                group.SingleOrDefault(x => x.Kind == EndpointKind.SharedInbox)?.EndpointIri))
            .ToArray();
    }

    public async Task SaveAsync(RemoteActorSnapshot actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        RemoteActor? remoteActor = await db.RemoteActors.SingleOrDefaultAsync(x => x.Iri == actor.ActorIri, cancellationToken).ConfigureAwait(false);
        if (remoteActor is null)
        {
            db.RemoteActors.Add(RemoteActor.Create(actor.ActorIri, actor.Type, actor.PreferredUsername, actor.RawJson, actor.FetchedAt));
        }
        else
        {
            remoteActor.Refresh(actor.Type, actor.PreferredUsername, actor.RawJson, actor.ETag, actor.LastModified, actor.FetchedAt);
        }

        await UpsertEndpointAsync(db, actor.ActorIri, EndpointKind.Inbox, actor.InboxIri, actor.FetchedAt, cancellationToken).ConfigureAwait(false);
        if (actor.SharedInboxIri is not null)
        {
            await UpsertEndpointAsync(db, actor.ActorIri, EndpointKind.SharedInbox, actor.SharedInboxIri, actor.FetchedAt, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkEndpointGoneAsync(
        string endpointIri,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string canonicalEndpoint = CanonicalIri.RequireAbsoluteHttp(endpointIri, nameof(endpointIri));
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        List<RemoteEndpoint> endpoints = await db.RemoteEndpoints
            .Where(x => x.EndpointIri == canonicalEndpoint && x.GoneAt == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (RemoteEndpoint endpoint in endpoints)
        {
            endpoint.MarkGone(now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertEndpointAsync(
        FederationDbContext db,
        string actorIri,
        EndpointKind kind,
        string endpointIri,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        RemoteEndpoint? endpoint = await db.RemoteEndpoints.SingleOrDefaultAsync(
            x => x.ActorIri == actorIri && x.Kind == kind,
            cancellationToken).ConfigureAwait(false);
        if (endpoint is null)
        {
            db.RemoteEndpoints.Add(RemoteEndpoint.Create(actorIri, kind, endpointIri, now));
        }
        else
        {
            endpoint.Refresh(endpointIri, now);
        }
    }
}
