using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.MisskeyApi;

public sealed class MisskeyAnnouncementService(
    IAnnouncementService announcements,
    IExternalEntityIdService externalIds,
    IAnnouncementImageImporter imageImporter)
{
    public async Task<IReadOnlyList<MisskeyAnnouncement>> ReadAsync(
        MisskeyAnnouncementQuery query,
        string? viewerActorIri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid? sinceId = await ResolveCursorAsync(query.SinceId, cancellationToken).ConfigureAwait(false);
        Guid? untilId = await ResolveCursorAsync(query.UntilId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AnnouncementView> values = await announcements.ReadAsync(
            new(sinceId, untilId, query.Limit, query.WithUnreads, viewerActorIri),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> ids = await MapIdsAsync(values, cancellationToken).ConfigureAwait(false);
        return values.Select(value => new MisskeyAnnouncement(
            ids[value.Id],
            value.CreatedAt,
            value.UpdatedAt,
            value.Text,
            value.Title,
            value.ImageUrl,
            value.IsRead)).ToArray();
    }

    public async Task<IReadOnlyList<MisskeyAdminAnnouncement>> ReadForAdministrationAsync(
        string? sinceExternalId,
        string? untilExternalId,
        int limit,
        CancellationToken cancellationToken)
    {
        Guid? sinceId = await ResolveCursorAsync(sinceExternalId, cancellationToken).ConfigureAwait(false);
        Guid? untilId = await ResolveCursorAsync(untilExternalId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AnnouncementView> values = await announcements.ReadForAdministrationAsync(
            sinceId,
            untilId,
            limit,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, string> ids = await MapIdsAsync(values, cancellationToken).ConfigureAwait(false);
        return values.Select(value => new MisskeyAdminAnnouncement(
            ids[value.Id],
            value.CreatedAt,
            value.UpdatedAt,
            value.Text,
            value.Title,
            value.ImageUrl,
            value.Reads)).ToArray();
    }

    public async Task<MisskeyAnnouncement> CreateAsync(
        MisskeyAnnouncementMutation mutation,
        string operatorId,
        string ownerActorIri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        string? imageUrl = await imageImporter.ImportAsync(
            mutation.ImageUrl,
            ownerActorIri,
            cancellationToken).ConfigureAwait(false);
        AnnouncementView value = await announcements.CreateAsync(
            new(
                mutation.Title,
                mutation.Text,
                imageUrl,
                Audience: AnnouncementAudience.Public,
                PublishedAt: null,
                ExpiresAt: null),
            operatorId,
            cancellationToken).ConfigureAwait(false);
        string id = await MapIdAsync(value, cancellationToken).ConfigureAwait(false);
        return new(id, value.CreatedAt, value.UpdatedAt, value.Text, value.Title, value.ImageUrl, IsRead: null);
    }

    public async Task<bool> UpdateAsync(
        string externalId,
        MisskeyAnnouncementMutation mutation,
        string operatorId,
        string ownerActorIri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        Guid? id = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Announcement,
            externalId,
            cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            return false;
        }

        string? imageUrl = await imageImporter.ImportAsync(
            mutation.ImageUrl,
            ownerActorIri,
            cancellationToken).ConfigureAwait(false);
        AnnouncementView? updated = await announcements.UpdateAsync(
            id.Value,
            new(
                mutation.Title,
                mutation.Text,
                imageUrl,
                Audience: null,
                PublishedAt: null,
                ExpiresAt: null),
            operatorId,
            cancellationToken).ConfigureAwait(false);
        return updated is not null;
    }

    public async Task<bool> DeleteAsync(
        string externalId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        Guid? id = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Announcement,
            externalId,
            cancellationToken).ConfigureAwait(false);
        return id is not null && await announcements.DeleteAsync(
            id.Value,
            operatorId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkReadAsync(
        string externalId,
        string readerActorIri,
        CancellationToken cancellationToken)
    {
        Guid? id = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Announcement,
            externalId,
            cancellationToken).ConfigureAwait(false);
        return id is not null && await announcements.MarkReadAsync(
            id.Value,
            readerActorIri,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveCursorAsync(string? externalId, CancellationToken cancellationToken)
    {
        if (externalId is null)
        {
            return null;
        }

        Guid? id = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Announcement,
            externalId,
            cancellationToken).ConfigureAwait(false);
        if (id is null)
        {
            throw new MisskeyApiException(
                400,
                "Invalid announcement cursor.",
                "INVALID_PARAM",
                "3d81ceae-475f-4600-b2a8-2bc116157532");
        }

        return id;
    }

    private Task<IReadOnlyDictionary<Guid, string>> MapIdsAsync(
        IReadOnlyList<AnnouncementView> values,
        CancellationToken cancellationToken) =>
        externalIds.GetOrCreateManyAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Announcement,
            values.Select(value => (value.Id, value.CreatedAt)).ToArray(),
            cancellationToken);

    private Task<string> MapIdAsync(AnnouncementView value, CancellationToken cancellationToken) =>
        externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Announcement,
            value.Id,
            value.CreatedAt,
            cancellationToken);
}
