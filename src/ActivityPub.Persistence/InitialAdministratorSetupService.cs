using System.Data;
using ActivityPub.Application;
using ActivityPub.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ActivityPub.Persistence;

public sealed class InitialSetupState(
    IDbContextFactory<LocalIdentityDbContext> contextFactory,
    LocalAccountOptions options) : IInitialSetupState
{
    public async Task<bool> IsRequiredAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return false;
        }

        await using LocalIdentityDbContext db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return !await db.Users.AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class InitialAdministratorSetupService(
    LocalIdentityDbContext db,
    LocalAccountService accounts,
    LocalAccountOptions options) : IInitialAdministratorSetupService
{
    public async Task<InitialAdministratorSetupResult> CreateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Failure(InitialAdministratorSetupStatus.Disabled, "LOCAL_ACCOUNTS_DISABLED");
        }

        await using IDbContextTransaction transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        // Serialize first-run creation across API replicas and prevent a concurrent regular
        // Identity insert from overtaking the zero-user check. The lock is transaction-scoped.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"LOCK TABLE identity.users IN SHARE ROW EXCLUSIVE MODE;",
            cancellationToken).ConfigureAwait(false);
        if (await db.Users.AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return Failure(InitialAdministratorSetupStatus.AlreadyInitialized, "INITIAL_SETUP_ALREADY_COMPLETED");
        }

        LocalAccountRegistrationResult registration = await accounts
            .CreateInitialAdministratorAsync(username, password, cancellationToken)
            .ConfigureAwait(false);
        if (registration.Status != LocalAccountRegistrationStatus.Created || registration.User is null)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            InitialAdministratorSetupStatus status = registration.Status switch
            {
                LocalAccountRegistrationStatus.InvalidUsername or
                LocalAccountRegistrationStatus.InvalidPassword or
                LocalAccountRegistrationStatus.UsernameUnavailable => InitialAdministratorSetupStatus.ValidationFailed,
                LocalAccountRegistrationStatus.RegistrationDisabled => InitialAdministratorSetupStatus.Disabled,
                _ => InitialAdministratorSetupStatus.ProvisioningFailed
            };
            return new(status, null, null, null, registration.SafeErrorCodes);
        }

        // Once the account, role, actor, and signing key have all been provisioned, complete
        // the identity commit even if the browser disconnects before receiving the response.
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        LocalIdentityUser user = registration.User;
        return new(
            InitialAdministratorSetupStatus.Created,
            user.Id,
            user.UserName,
            user.LocalActorIri,
            []);
    }

    private static InitialAdministratorSetupResult Failure(
        InitialAdministratorSetupStatus status,
        string errorCode) => new(status, null, null, null, [errorCode]);
}
