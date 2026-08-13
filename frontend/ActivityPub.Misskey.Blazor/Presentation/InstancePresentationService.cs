using ActivityPub.MisskeyApi;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IInstancePresentationService
{
    Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(CancellationToken cancellationToken);
}

public sealed class InstancePresentationService(
    MisskeyMetadataService metadata,
    MisskeyQueryService query) : IInstancePresentationService
{
    public async Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MisskeyInstanceMetadata value = await metadata.GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        return new InstanceSummaryViewModel(
            value.Name,
            value.Description,
            value.Version,
            value.IconUrl,
            value.BackgroundImageUrl,
            value.LogoImageUrl,
            value.DisableRegistration,
            value.EmailRequiredForSignup,
            value.EnableEmail,
            value.TosUrl,
            value.EnableHcaptcha,
            value.HcaptchaSiteKey,
            value.EnableRecaptcha,
            value.RecaptchaSiteKey,
            value.EnableTurnstile,
            value.TurnstileSiteKey,
            value.TurnstileAction,
            value.TurnstileCdata,
            value.MaintainerName,
            value.MaintainerEmail,
            value.RequireSetup);
    }

    public async Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MisskeyFederationInstance> values = await query.ReadFederationInstancesAsync(
            new(
                Host: null,
                Blocked: null,
                NotResponding: null,
                Suspended: null,
                Federating: null,
                Subscribing: null,
                Publishing: null,
                Limit: 20,
                Offset: 0,
                Sort: "+pubSub"),
            cancellationToken).ConfigureAwait(false);
        return values.Select(value => new FederationInstanceViewModel(
            value.Id,
            value.Host,
            value.IconUrl,
            value.IsNotResponding,
            value.IsBlocked,
            value.IsSuspended,
            value.SoftwareName,
            value.SoftwareVersion,
            value.Name,
            value.CaughtAt,
            value.UsersCount,
            value.NotesCount,
            value.FollowingCount,
            value.FollowersCount,
            value.LatestRequestSentAt,
            value.LastCommunicatedAt)).ToArray();
    }
}

public sealed record InstanceSummaryViewModel(
    string Name,
    string Description,
    string Version,
    string IconUrl,
    string? BackgroundImageUrl,
    string? LogoImageUrl,
    bool DisableRegistration,
    bool EmailRequiredForSignup,
    bool EnableEmail,
    string? TosUrl,
    bool EnableHcaptcha = false,
    string? HcaptchaSiteKey = null,
    bool EnableRecaptcha = false,
    string? RecaptchaSiteKey = null,
    bool EnableTurnstile = false,
    string? TurnstileSiteKey = null,
    string? TurnstileAction = null,
    string? TurnstileCdata = null,
    string? MaintainerName = null,
    string? MaintainerEmail = null,
    bool RequireSetup = false);

public sealed record FederationInstanceViewModel(
    string Id,
    string Host,
    string? IconUrl,
    bool IsNotResponding = false,
    bool IsBlocked = false,
    bool IsSuspended = false,
    string? SoftwareName = null,
    string? SoftwareVersion = null,
    string? Name = null,
    DateTimeOffset? CaughtAt = null,
    long UsersCount = 0,
    long NotesCount = 0,
    long FollowingCount = 0,
    long FollowersCount = 0,
    DateTimeOffset? LatestRequestSentAt = null,
    DateTimeOffset? LastCommunicatedAt = null);
