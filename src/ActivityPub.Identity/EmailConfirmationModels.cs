namespace ActivityPub.Identity;

public sealed class LocalEmailConfirmationRequest
{
    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    private LocalEmailConfirmationRequest()
    {
    }
}

public sealed record EmailConfirmationEmail(
    string RecipientAddress,
    Uri ConfirmationUri,
    DateTimeOffset ExpiresAt);

public enum EmailConfirmationStatus
{
    Succeeded = 0,
    InvalidOrExpiredToken = 1,
    Disabled = 2
}

public sealed record EmailConfirmationResult(
    EmailConfirmationStatus Status,
    LocalIdentityUser? User);

public interface IEmailConfirmationStore
{
    Task<bool> TryReserveAsync(
        Guid userId,
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt,
        TimeSpan cooldown,
        CancellationToken cancellationToken);

    Task ReleaseAsync(Guid userId, ReadOnlyMemory<byte> tokenHash, CancellationToken cancellationToken);

    Task<Guid?> TryClaimAsync(
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken);
}

public interface IEmailConfirmationSender
{
    Task SendAsync(EmailConfirmationEmail email, CancellationToken cancellationToken);
}

public interface IEmailConfirmationService
{
    Task RequestForUserAsync(
        LocalIdentityUser user,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken);

    Task RequestAsync(
        string username,
        string email,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken);

    Task<EmailConfirmationResult> ConfirmAsync(string token, CancellationToken cancellationToken);
}
