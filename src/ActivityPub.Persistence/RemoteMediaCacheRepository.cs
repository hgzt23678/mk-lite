using System.Data;
using System.Text.Json;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class RemoteMediaCacheRepository(
    IDbContextFactory<FederationDbContext> contextFactory) : IRemoteMediaCacheRepository
{
    public async Task<RemoteMediaSource?> ResolveAuthorizedSourceAsync(
        Guid objectId,
        string sourceToken,
        string? requesterActorIri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceToken);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        FederatedObject? item = await db.Objects.SingleOrDefaultAsync(x => x.Id == objectId && !x.IsDeleted, cancellationToken).ConfigureAwait(false);
        if (item is null || !await CanViewAsync(db, item, requesterActorIri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string? sourceIri = FindMediaSource(item.RawJson, sourceToken);
        string ownerActorIri = item.OwnerIri;
        if (sourceIri is null)
        {
            var reactionSources = await db.LikeRelations
                .Where(x => x.ObjectIri == item.Iri && x.CustomEmojiUrl != null)
                .Select(x => new { x.ActorIri, x.CustomEmojiUrl })
                .ToArrayAsync(cancellationToken).ConfigureAwait(false);
            foreach (var reactionSource in reactionSources)
            {
                string canonical = CanonicalIri.RequireAbsoluteHttp(reactionSource.CustomEmojiUrl!, "reaction.customEmojiUrl");
                if (string.Equals(Token(canonical), sourceToken, StringComparison.Ordinal))
                {
                    sourceIri = canonical;
                    ownerActorIri = reactionSource.ActorIri;
                    break;
                }
            }

            if (sourceIri is null)
            {
                var emojiReactionSources = await db.EmojiReactionRelations
                    .Where(x => x.ObjectIri == item.Iri && x.CustomEmojiUrl != null)
                    .Select(x => new { x.ActorIri, x.CustomEmojiUrl })
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false);
                foreach (var reactionSource in emojiReactionSources)
                {
                    string canonical = CanonicalIri.RequireAbsoluteHttp(reactionSource.CustomEmojiUrl!, "emojiReaction.customEmojiUrl");
                    if (string.Equals(Token(canonical), sourceToken, StringComparison.Ordinal))
                    {
                        sourceIri = canonical;
                        ownerActorIri = reactionSource.ActorIri;
                        break;
                    }
                }
            }
        }

        return sourceIri is null
            ? null
            : new(objectId, ownerActorIri, sourceIri, sourceToken, item.Visibility);
    }

    public async Task<RemoteMediaCacheEntry?> FindFreshAsync(
        Guid objectId,
        string sourceToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.RemoteMediaCache.SingleOrDefaultAsync(
            x => x.ObjectId == objectId && x.SourceToken == sourceToken && x.ExpiresAt > now,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(RemoteMediaCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        string lockKey = entry.ObjectId.ToString("N") + ":" + entry.SourceToken;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken).ConfigureAwait(false);
        RemoteMediaCacheEntry? existing = await db.RemoteMediaCache.SingleOrDefaultAsync(
            x => x.ObjectId == entry.ObjectId && x.SourceToken == entry.SourceToken,
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.RemoteMediaCache.Add(entry);
            bool attached = await db.MediaAttachments.AnyAsync(
                x => x.ObjectId == entry.ObjectId && x.MediaId == entry.MediaId,
                cancellationToken).ConfigureAwait(false);
            if (!attached)
            {
                db.MediaAttachments.Add(MediaAttachment.Create(entry.MediaId, entry.ObjectId));
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (existing.ExpiresAt <= entry.RefreshedAt)
        {
            Guid oldMediaId = existing.MediaId;
            existing.Refresh(entry.MediaId, entry.ETag, entry.LastModified, entry.RefreshedAt, entry.ExpiresAt);
            if (oldMediaId != entry.MediaId)
            {
                await db.MediaAttachments
                    .Where(x => x.ObjectId == entry.ObjectId && x.MediaId == oldMediaId)
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                bool attached = await db.MediaAttachments.AnyAsync(
                    x => x.ObjectId == entry.ObjectId && x.MediaId == entry.MediaId,
                    cancellationToken).ConfigureAwait(false);
                if (!attached)
                {
                    db.MediaAttachments.Add(MediaAttachment.Create(entry.MediaId, entry.ObjectId));
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        RemoteMediaCacheEntry[] expired = await db.RemoteMediaCache
            .FromSql($"""
                SELECT cache.*
                FROM activitypub.remote_media_cache AS cache
                WHERE cache.expires_at <= {now}
                ORDER BY cache.expires_at, cache.id
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (RemoteMediaCacheEntry entry in expired)
        {
            await db.MediaAttachments
                .Where(x => x.ObjectId == entry.ObjectId && x.MediaId == entry.MediaId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        db.RemoteMediaCache.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return expired.Length;
    }

    private static async Task<bool> CanViewAsync(
        FederationDbContext db,
        FederatedObject item,
        string? requesterActorIri,
        CancellationToken cancellationToken)
    {
        if (item.Visibility is Visibility.Public or Visibility.Unlisted)
        {
            return true;
        }

        if (requesterActorIri is null)
        {
            return false;
        }

        if (string.Equals(item.OwnerIri, requesterActorIri, StringComparison.Ordinal))
        {
            return true;
        }

        if (item.Visibility == Visibility.FollowersOnly && await db.FollowRelations.AnyAsync(x =>
                x.FollowerIri == requesterActorIri && x.FollowedIri == item.OwnerIri && x.State == FollowState.Accepted,
                cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await (from activity in db.Activities
                      join recipient in db.ActivityRecipients on activity.Id equals recipient.ActivityId
                      where activity.ObjectIri == item.Iri && recipient.RecipientIri == requesterActorIri
                      select recipient.Id).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? FindMediaSource(string rawJson, string token)
    {
        using JsonDocument document = JsonDocument.Parse(rawJson, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.TryGetProperty("attachment", out JsonElement attachment))
        {
            string? attachmentSource = FindSource(attachment, token, requireEmoji: false);
            if (attachmentSource is not null)
            {
                return attachmentSource;
            }
        }

        return document.RootElement.TryGetProperty("tag", out JsonElement tags)
            ? FindSource(tags, token, requireEmoji: true)
            : null;
    }

    private static string? FindSource(JsonElement values, string token, bool requireEmoji)
    {
        IEnumerable<JsonElement> entries = values.ValueKind == JsonValueKind.Array ? values.EnumerateArray() : [values];
        foreach (JsonElement entry in entries)
        {
            if (requireEmoji && (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("type", out JsonElement type) ||
                !ContainsType(type, "Emoji")))
            {
                continue;
            }

            string? source = requireEmoji && entry.TryGetProperty("icon", out JsonElement icon)
                ? ReadUrl(icon)
                : ReadUrl(entry);
            if (source is null)
            {
                continue;
            }

            string canonical = CanonicalIri.RequireAbsoluteHttp(source, "attachment.url");
            if (string.Equals(Token(canonical), token, StringComparison.Ordinal))
            {
                return canonical;
            }
        }

        return null;
    }

    private static bool ContainsType(JsonElement value, string expected) => value.ValueKind switch
    {
        JsonValueKind.String => string.Equals(value.GetString(), expected, StringComparison.Ordinal),
        JsonValueKind.Array => value.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), expected, StringComparison.Ordinal)),
        _ => false
    };

    private static string Token(string canonical) =>
        RemoteMediaSourceToken.Create(canonical);

    private static string? ReadUrl(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.String)
        {
            return entry.GetString();
        }

        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (entry.TryGetProperty("url", out JsonElement url))
        {
            if (url.ValueKind == JsonValueKind.String)
            {
                return url.GetString();
            }

            if (url.ValueKind == JsonValueKind.Object && url.TryGetProperty("href", out JsonElement href) && href.ValueKind == JsonValueKind.String)
            {
                return href.GetString();
            }
        }

        return entry.TryGetProperty("href", out JsonElement directHref) && directHref.ValueKind == JsonValueKind.String
            ? directHref.GetString()
            : null;
    }
}
