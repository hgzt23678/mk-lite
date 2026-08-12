namespace ActivityPub.Identity;

public sealed class LocalPasswordResetRequest
{
    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    private LocalPasswordResetRequest()
    {
    }
}

public enum PasswordResetCompletionStatus
{
    Succeeded = 0,
    InvalidOrExpiredToken = 1,
    InvalidPassword = 2,
    Disabled = 3
}

public sealed record PasswordResetCompletionResult(
    PasswordResetCompletionStatus Status,
    IReadOnlyList<string> SafeErrorCodes);

public sealed record PasswordResetEmail(
    string RecipientAddress,
    Uri ResetUri,
    DateTimeOffset ExpiresAt);

public interface IPasswordResetStore
{
    Task<bool> TryReserveAsync(
        Guid userId,
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt,
        TimeSpan cooldown,
        CancellationToken cancellationToken);

    Task ReleaseAsync(Guid userId, ReadOnlyMemory<byte> tokenHash, CancellationToken cancellationToken);

    Task<Guid?> FindActiveUserIdAsync(
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<Guid?> TryClaimAsync(
        ReadOnlyMemory<byte> tokenHash,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken);
}

public interface IPasswordResetEmailSender
{
    Task SendAsync(PasswordResetEmail email, CancellationToken cancellationToken);
}

public interface IPasswordResetService
{
    Task RequestAsync(
        string username,
        string email,
        Uri frontendPublicBaseUri,
        CancellationToken cancellationToken);

    Task<PasswordResetCompletionResult> ResetAsync(
        string token,
        string password,
        CancellationToken cancellationToken);
}
