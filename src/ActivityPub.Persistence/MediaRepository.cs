using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class MediaRepository(IDbContextFactory<FederationDbContext> contextFactory) : IMediaRepository
{
    public async Task AddAsync(MediaResource media, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Media.Add(media);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(MediaResource media, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Attach(media);
        db.Entry(media).State = EntityState.Modified;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaResource?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Media.SingleOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsAuthorizedAsync(Guid id, string requesterActorIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        MediaResource? media = await db.Media.SingleOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            return false;
        }

        if (string.Equals(media.OwnerActorIri, requesterActorIri, StringComparison.Ordinal))
        {
            return true;
        }

        bool directRecipient = await (
            from attachment in db.MediaAttachments
            join federatedObject in db.Objects on attachment.ObjectId equals federatedObject.Id
            join activity in db.Activities on federatedObject.Iri equals activity.ObjectIri
            join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
            where attachment.MediaId == id && recipient.RecipientIri == requesterActorIri
            select attachment.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
        if (directRecipient)
        {
            return true;
        }

        return await (
            from attachment in db.MediaAttachments
            join federatedObject in db.Objects on attachment.ObjectId equals federatedObject.Id
            join follow in db.FollowRelations on federatedObject.OwnerIri equals follow.FollowedIri
            where attachment.MediaId == id && federatedObject.Visibility == Visibility.FollowersOnly &&
                follow.FollowerIri == requesterActorIri && follow.State == FollowState.Accepted
            select attachment.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MediaGarbageCandidate>> ClaimGarbageAsync(
        DateTimeOffset unreferencedBefore,
        DateTimeOffset deletedRetryBefore,
        DateTimeOffset now,
        int count,
        CancellationToken cancellationToken)
    {
        if (count is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Database.SqlQuery<MediaGarbageCandidate>($"""
            WITH candidates AS (
                SELECT m.id
                FROM activitypub.media AS m
                WHERE m.purged_at IS NULL
                  AND (
                    (m.state = 'Deleted' AND m.updated_at <= {deletedRetryBefore})
                    OR
                    (m.state <> 'Deleted' AND m.updated_at <= {unreferencedBefore}
                     AND NOT EXISTS (
                         SELECT 1 FROM activitypub.media_attachments AS a WHERE a.media_id = m.id)
                     AND NOT EXISTS (
                         SELECT 1
                         FROM activitypub.announcements AS n
                         WHERE n.deleted_at IS NULL
                           AND (n.expires_at IS NULL OR n.expires_at > {now})
                           AND n.image_url = '/media/' || m.id::text)
                     AND NOT EXISTS (
                         SELECT 1
                         FROM activitypub.remote_actor_media_cache AS c
                         WHERE c.media_id = m.id
                           AND (c.expires_at > {now} OR c.lease_expires_at > {now})))
                  )
                ORDER BY m.updated_at, m.id
                LIMIT {count}
                FOR UPDATE SKIP LOCKED
            )
            UPDATE activitypub.media AS m
            SET state = 'Deleted',
                deleted_at = COALESCE(m.deleted_at, {now}),
                updated_at = {now}
            FROM candidates
            WHERE m.id = candidates.id
            RETURNING m.id AS "Id", m.storage_key AS "StorageKey", m.thumbnail_storage_key AS "ThumbnailStorageKey"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkPurgedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int changed = await db.Media
            .Where(x => x.Id == id && x.State == MediaState.Deleted && x.PurgedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.PurgedAt, now)
                    .SetProperty(x => x.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
        if (changed != 1)
        {
            throw new InvalidOperationException("The media GC claim no longer exists or was already purged.");
        }
    }
}
