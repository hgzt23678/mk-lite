using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class DriveService(
    IDbContextFactory<FederationDbContext> contextFactory,
    IMediaService mediaService) : IClientDriveService
{
    private const long DefaultCapacityBytes = 10L * 1024 * 1024 * 1024;

    public async Task<IReadOnlyList<ClientDriveFileView>> ListFilesAsync(
        string ownerActorIri,
        Guid? folderId,
        Guid? sinceId,
        Guid? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<MediaResource> query = db.Media
            .Where(media => media.OwnerActorIri == ownerActorIri &&
                            media.State == MediaState.Available &&
                            media.FolderId == folderId);
        if (sinceId is not null)
        {
            query = query.Where(media => media.Id > sinceId);
        }

        if (untilId is not null)
        {
            query = query.Where(media => media.Id < untilId);
        }

        MediaResource[] files = await query
            .OrderByDescending(media => media.Id)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return files.Select(MapFile).ToArray();
    }

    public async Task<ClientDriveFileView> UploadFileAsync(
        string ownerActorIri,
        Guid? folderId,
        string? name,
        bool isSensitive,
        string? comment,
        string? declaredType,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        MediaUploadResult result = await mediaService.UploadAsync(
            new MediaUploadCommand(
                ownerActorIri,
                fileName,
                declaredType,
                Visibility.Public,
                content),
            cancellationToken).ConfigureAwait(false);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        MediaResource? media = await db.Media.AsTracking().SingleOrDefaultAsync(
            item => item.Id == result.Id,
            cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            throw new KeyNotFoundException("The uploaded media was not persisted.");
        }

        if (folderId is not null)
        {
            await RequireFolderAsync(db, ownerActorIri, folderId.Value, cancellationToken).ConfigureAwait(false);
        }

        media.AssignDriveMetadata(folderId, isSensitive, comment, DateTimeOffset.UtcNow);
        if (!string.IsNullOrWhiteSpace(name))
        {
            media.Rename(name);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapFile(media);
    }

    public async Task<ClientDriveFileView?> ShowFileAsync(
        string ownerActorIri,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        MediaResource? media = await db.Media.SingleOrDefaultAsync(
            item => item.Id == fileId && item.OwnerActorIri == ownerActorIri && item.State != MediaState.Deleted,
            cancellationToken).ConfigureAwait(false);
        return media is null ? null : MapFile(media);
    }

    public async Task DeleteFileAsync(
        string ownerActorIri,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        MediaResource? media = await db.Media.AsTracking().SingleOrDefaultAsync(
            item => item.Id == fileId && item.OwnerActorIri == ownerActorIri && item.State != MediaState.Deleted,
            cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            throw new KeyNotFoundException("File was not found.");
        }

        media.Delete(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientDriveFileView?> UpdateFileAsync(
        string ownerActorIri,
        Guid fileId,
        string? name,
        Guid? folderId,
        string? comment,
        bool? isSensitive,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        MediaResource? media = await db.Media.AsTracking().SingleOrDefaultAsync(
            item => item.Id == fileId && item.OwnerActorIri == ownerActorIri && item.State != MediaState.Deleted,
            cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            return null;
        }

        if (folderId is not null)
        {
            await RequireFolderAsync(db, ownerActorIri, folderId.Value, cancellationToken).ConfigureAwait(false);
        }

        media.AssignDriveMetadata(
            folderId ?? media.FolderId,
            isSensitive ?? media.IsSensitive,
            comment ?? media.Comment,
            DateTimeOffset.UtcNow);
        if (!string.IsNullOrWhiteSpace(name))
        {
            media.Rename(name);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapFile(media);
    }

    public async Task<IReadOnlyList<ClientDriveFolderView>> ListFoldersAsync(
        string ownerActorIri,
        Guid? parentId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DriveFolder[] folders = await db.DriveFolders.AsNoTracking()
            .Where(folder => folder.OwnerActorIri == ownerActorIri && folder.ParentId == parentId)
            .OrderByDescending(folder => folder.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return folders.Select(MapFolder).ToArray();
    }

    public async Task<ClientDriveFolderView> CreateFolderAsync(
        string ownerActorIri,
        string name,
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (parentId is not null)
        {
            await RequireFolderAsync(db, ownerActorIri, parentId.Value, cancellationToken).ConfigureAwait(false);
        }

        DriveFolder folder = DriveFolder.Create(ownerActorIri, name, parentId, DateTimeOffset.UtcNow);
        db.DriveFolders.Add(folder);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapFolder(folder);
    }

    public async Task DeleteFolderAsync(
        string ownerActorIri,
        Guid folderId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DriveFolder? folder = await db.DriveFolders.SingleOrDefaultAsync(
            item => item.Id == folderId && item.OwnerActorIri == ownerActorIri,
            cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            throw new KeyNotFoundException("Folder was not found.");
        }

        db.DriveFolders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientDriveFolderView?> UpdateFolderAsync(
        string ownerActorIri,
        Guid folderId,
        string? name,
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DriveFolder? folder = await db.DriveFolders.AsTracking().SingleOrDefaultAsync(
            item => item.Id == folderId && item.OwnerActorIri == ownerActorIri,
            cancellationToken).ConfigureAwait(false);
        if (folder is null)
        {
            return null;
        }

        if (parentId is not null && parentId != folderId)
        {
            await RequireFolderAsync(db, ownerActorIri, parentId.Value, cancellationToken).ConfigureAwait(false);
        }

        folder.Update(name, parentId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MapFolder(folder);
    }

    public async Task<(long Usage, long Capacity)> GetUsageAsync(
        string ownerActorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        long usage = await db.Media
            .Where(media => media.OwnerActorIri == ownerActorIri && media.State == MediaState.Available)
            .SumAsync(media => (long?)media.Length, cancellationToken).ConfigureAwait(false) ?? 0;
        return (usage, DefaultCapacityBytes);
    }

    private static async Task RequireFolderAsync(
        FederationDbContext db,
        string ownerActorIri,
        Guid folderId,
        CancellationToken cancellationToken)
    {
        bool exists = await db.DriveFolders.AnyAsync(
            folder => folder.Id == folderId && folder.OwnerActorIri == ownerActorIri,
            cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new KeyNotFoundException("Folder was not found.");
        }
    }

    private static ClientDriveFileView MapFile(MediaResource media) =>
        new(
            media.Id,
            media.OriginalFileName,
            media.DetectedMediaType,
            media.ContentHash,
            media.Length,
            $"/media/{media.Id:D}",
            media.IsSensitive,
            Blurhash: null,
            media.Width,
            media.Height,
            media.FolderId,
            media.Comment,
            media.CreatedAt);

    private static ClientDriveFolderView MapFolder(DriveFolder folder) =>
        new(folder.Id, folder.Name, folder.ParentId, folder.CreatedAt);
}
