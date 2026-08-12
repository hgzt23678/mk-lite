using ActivityPub.Persistence.IdentityMigrations;
using ActivityPub.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ActivityPub.Persistence.Tests;

public sealed class MigrationSafetyTests
{
    [Fact]
    public void RegistrationInvitationsIsAnExpandOnlyMigrationWithBoundedLockWaits()
    {
        var migration = new AddRegistrationInvitations
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL"
        };

        MigrationOperation[] operations = migration.UpOperations.ToArray();

        Assert.Equal("SET LOCAL lock_timeout = '5s';", Assert.IsType<SqlOperation>(operations[0]).Sql);
        Assert.Equal("SET LOCAL statement_timeout = '60s';", Assert.IsType<SqlOperation>(operations[1]).Sql);
        CreateTableOperation table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("identity", table.Schema);
        Assert.Equal("registration_invitations", table.Name);
        Assert.Empty(table.ForeignKeys);
        Dictionary<string, string> constraints = table.CheckConstraints
            .ToDictionary(constraint => constraint.Name, constraint => constraint.Sql);
        Assert.Equal(4, constraints.Count);
        Assert.Equal(
            "octet_length(code_hash) = 32",
            constraints["ck_identity_registration_invitations_code_hash_length"]);
        Assert.Equal(
            "expires_at > created_at",
            constraints["ck_identity_registration_invitations_expiry"]);
        Assert.Equal(
            "(reservation_id IS NULL AND reserved_at IS NULL AND reservation_expires_at IS NULL) OR " +
            "(reservation_id IS NOT NULL AND reserved_at IS NOT NULL AND reservation_expires_at IS NOT NULL " +
            "AND reservation_expires_at > reserved_at)",
            constraints["ck_identity_registration_invitations_reservation"]);
        Assert.Equal(
            "(consumed_at IS NULL AND consumed_by_username IS NULL) OR " +
            "(consumed_at IS NOT NULL AND consumed_by_username IS NOT NULL)",
            constraints["ck_identity_registration_invitations_consumption"]);
        Assert.Equal(3, operations.OfType<CreateIndexOperation>().Count());
        Assert.Empty(operations.OfType<AddColumnOperation>());
        Assert.Empty(operations.OfType<AlterColumnOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<DeleteDataOperation>());
        Assert.Empty(operations.OfType<UpdateDataOperation>());
    }

    [Fact]
    public void RegistrationInvitationsDownIsExplicitlyDestructiveAndLockBounded()
    {
        var migration = new AddRegistrationInvitations
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL"
        };

        MigrationOperation[] operations = migration.DownOperations.ToArray();

        Assert.Equal("SET LOCAL lock_timeout = '5s';", Assert.IsType<SqlOperation>(operations[0]).Sql);
        DropTableOperation drop = Assert.IsType<DropTableOperation>(operations[1]);
        Assert.Equal("identity", drop.Schema);
        Assert.Equal("registration_invitations", drop.Name);
        Assert.Equal(2, operations.Length);
    }

    [Fact]
    public void RemoteActorMediaCacheIsAnExpandOnlyMigrationWithBoundedLockWaits()
    {
        var migration = new AddRemoteActorMediaCache
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL"
        };

        MigrationOperation[] operations = migration.UpOperations.ToArray();

        Assert.Equal("SET LOCAL lock_timeout = '5s';", Assert.IsType<SqlOperation>(operations[0]).Sql);
        Assert.Equal("SET LOCAL statement_timeout = '60s';", Assert.IsType<SqlOperation>(operations[1]).Sql);
        CreateTableOperation table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("activitypub", table.Schema);
        Assert.Equal("remote_actor_media_cache", table.Name);
        Assert.Equal(2, table.ForeignKeys.Count);
        Assert.Equal(4, operations.OfType<CreateIndexOperation>().Count());
        Assert.Empty(operations.OfType<AddColumnOperation>());
        Assert.Empty(operations.OfType<AlterColumnOperation>());
        Assert.Empty(operations.OfType<DropColumnOperation>());
        Assert.Empty(operations.OfType<DeleteDataOperation>());
        Assert.Empty(operations.OfType<UpdateDataOperation>());
    }

    [Fact]
    public void RemoteActorMediaCacheDownIsExplicitlyDestructiveAndLockBounded()
    {
        var migration = new AddRemoteActorMediaCache
        {
            ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL"
        };

        MigrationOperation[] operations = migration.DownOperations.ToArray();

        Assert.Equal("SET LOCAL lock_timeout = '5s';", Assert.IsType<SqlOperation>(operations[0]).Sql);
        DropTableOperation drop = Assert.IsType<DropTableOperation>(operations[1]);
        Assert.Equal("activitypub", drop.Schema);
        Assert.Equal("remote_actor_media_cache", drop.Name);
        Assert.Equal(2, operations.Length);
    }
}
