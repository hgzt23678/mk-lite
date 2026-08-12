namespace ActivityPub.Application;

public sealed record RegistrationInvitationReservation(Guid Id);

public sealed record RegistrationInvitationIssueResult(string Code, DateTimeOffset ExpiresAt);

public interface IRegistrationInvitationStore
{
    Task<bool> CreateAsync(
        byte[] codeHash,
        string operatorId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<RegistrationInvitationReservation?> ReserveAsync(
        byte[] codeHash,
        DateTimeOffset now,
        DateTimeOffset reservationExpiresAt,
        CancellationToken cancellationToken);

    Task<bool> ConsumeAsync(
        RegistrationInvitationReservation reservation,
        string username,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        RegistrationInvitationReservation reservation,
        CancellationToken cancellationToken);
}

public interface IRegistrationInvitationService
{
    Task<RegistrationInvitationIssueResult> IssueAsync(
        string operatorId,
        CancellationToken cancellationToken);
}
