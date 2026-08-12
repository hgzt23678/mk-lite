using System.Globalization;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ActivityPub.Persistence;

public sealed class ExternalEntityIdService(IDbContextFactory<FederationDbContext> contextFactory) : IExternalEntityIdService
{
    private const long MisskeyEpochMilliseconds = 946_684_800_000;

    public async Task<string> GetOrCreateAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        Guid internalId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (internalId == Guid.Empty)
        {
            throw new ArgumentException("The internal entity identifier cannot be empty.", nameof(internalId));
        }

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        string? existing = await db.ExternalEntityIds
            .Where(x => x.Dialect == dialect && x.EntityType == entityType && x.InternalId == internalId)
            .Select(x => x.ExternalId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            long ordinal = await NextOrdinalAsync(db, dialect, cancellationToken).ConfigureAwait(false);
            string externalId = dialect switch
            {
                ApiDialect.Mastodon => ordinal.ToString(CultureInfo.InvariantCulture),
                ApiDialect.Misskey => CreateMisskeyAid(timestamp, ordinal),
                _ => throw new ArgumentOutOfRangeException(nameof(dialect))
            };
            db.ExternalEntityIds.Add(ExternalEntityId.Create(dialect, entityType, internalId, externalId, ordinal, timestamp));
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return externalId;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                db.ChangeTracker.Clear();
                existing = await db.ExternalEntityIds
                    .Where(x => x.Dialect == dialect && x.EntityType == entityType && x.InternalId == internalId)
                    .Select(x => x.ExternalId)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    return existing;
                }
            }
        }

        throw new InvalidOperationException("A unique external identifier could not be allocated after four attempts.");
    }

    public async Task<Guid?> ResolveAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        string externalId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ExternalEntityIds
            .Where(x => x.Dialect == dialect && x.EntityType == entityType && x.ExternalId == externalId && x.RetiredAt == null)
            .Select(x => (Guid?)x.InternalId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetOrCreateManyAsync(
        ApiDialect dialect,
        ExternalEntityType entityType,
        IReadOnlyCollection<(Guid InternalId, DateTimeOffset Timestamp)> entities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var result = new Dictionary<Guid, string>(entities.Count);
        foreach ((Guid internalId, DateTimeOffset timestamp) in entities.DistinctBy(item => item.InternalId))
        {
            result[internalId] = await GetOrCreateAsync(dialect, entityType, internalId, timestamp, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public static string CreateMisskeyAid(DateTimeOffset timestamp, long ordinal)
    {
        long milliseconds = Math.Max(0, timestamp.ToUnixTimeMilliseconds() - MisskeyEpochMilliseconds);
        string time = ToBase36(milliseconds).PadLeft(8, '0');
        if (time.Length > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "The timestamp cannot be represented by a Misskey AID.");
        }

        string noise = ToBase36(ordinal % 1_296).PadLeft(2, '0');
        return time + noise;
    }

    private static async Task<long> NextOrdinalAsync(
        FederationDbContext db,
        ApiDialect dialect,
        CancellationToken cancellationToken)
    {
        string sequence = dialect switch
        {
            ApiDialect.Mastodon => "activitypub.external_mastodon_id_seq",
            ApiDialect.Misskey => "activitypub.external_misskey_id_seq",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect))
        };
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT nextval(CAST(@sequence_name AS regclass))";
        System.Data.Common.DbParameter sequenceParameter = command.CreateParameter();
        sequenceParameter.ParameterName = "sequence_name";
        sequenceParameter.Value = sequence;
        command.Parameters.Add(sequenceParameter);
        if (command.Connection?.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string ToBase36(long value)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> buffer = stackalloc char[13];
        int index = buffer.Length;
        do
        {
            buffer[--index] = alphabet[(int)(value % 36)];
            value /= 36;
        }
        while (value > 0);
        return new string(buffer[index..]);
    }
}
