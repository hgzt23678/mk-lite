using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ActivityPub.Identity;

/// <summary>
/// Performs the same configured password-hash verification work when an account lookup misses.
/// </summary>
public interface IPasswordVerificationTimingEqualizer
{
    void VerifyUnknownPassword(string suppliedPassword);
}

internal sealed class PasswordVerificationTimingEqualizer : IPasswordVerificationTimingEqualizer
{
    private readonly PasswordHasher<LocalIdentityUser> hasher;
    private readonly LocalIdentityUser syntheticUser;
    private readonly string syntheticPasswordHash;

    public PasswordVerificationTimingEqualizer(IOptions<PasswordHasherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        hasher = new PasswordHasher<LocalIdentityUser>(options);
        syntheticUser = LocalIdentityUser.Create("unknown_account", null, DateTimeOffset.UnixEpoch);
        string syntheticPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        syntheticPasswordHash = hasher.HashPassword(syntheticUser, syntheticPassword);
    }

    public void VerifyUnknownPassword(string suppliedPassword)
    {
        PasswordVerificationResult result = hasher.VerifyHashedPassword(
            syntheticUser,
            syntheticPasswordHash,
            suppliedPassword);
        GC.KeepAlive(result);
    }
}
