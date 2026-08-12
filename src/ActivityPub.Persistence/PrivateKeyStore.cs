using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

internal sealed class PrivateKeyStore(IDbContextFactory<FederationDbContext> contextFactory) : IPrivateKeyStore
{
    public async Task<KeyMaterial> GetSigningKeyAsync(string actorIri, CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var key = await (
            from actor in db.LocalActors
            join actorKey in db.ActorKeys on actor.ActiveKeyId equals actorKey.Id
            where actor.Iri == actorIri && !actor.IsSuspended && actorKey.IsLocal && actorKey.State == ActorKeyState.Active
            select new
            {
                actorKey.KeyIri,
                actorKey.OwnerIri,
                actorKey.PublicKeyPem,
                actorKey.PrivateKeyHandle,
                actorKey.Algorithm
            }).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (key is null || string.IsNullOrWhiteSpace(key.PrivateKeyHandle))
        {
            throw new InvalidOperationException("Local actor has no active external signing key handle.");
        }

        return new(key.KeyIri, key.OwnerIri, key.PublicKeyPem, key.PrivateKeyHandle, key.Algorithm);
    }
}
