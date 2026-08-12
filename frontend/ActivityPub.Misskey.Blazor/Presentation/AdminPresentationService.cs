using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.MisskeyApi;

namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record AdminAnnouncementViewModel(
    string Id,
    DateTimeOffset CreatedAt,
    string Title,
    string Text,
    string? ImageUrl,
    long Reads);

public sealed record AdminRelayViewModel(
    string Id,
    string Inbox,
    string Status);

public sealed record AdminOverviewViewModel(
    IReadOnlyList<AdminRelayViewModel> Relays,
    IReadOnlyList<AdminAnnouncementViewModel> Announcements);

public sealed record AdminInvitationViewModel(string Code, DateTimeOffset ExpiresAt);

public interface IAdminPresentationService
{
    Task<AdminOverviewViewModel> ReadOverviewAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminRelayViewModel>> ListRelaysAsync(CancellationToken cancellationToken);

    Task<AdminRelayViewModel> AddRelayAsync(string inbox, CancellationToken cancellationToken);

    Task RemoveRelayAsync(string inbox, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminAnnouncementViewModel>> ListAnnouncementsAsync(CancellationToken cancellationToken);

    Task CreateAnnouncementAsync(string title, string text, string? imageUrl, CancellationToken cancellationToken);

    Task DeleteAnnouncementAsync(string announcementId, CancellationToken cancellationToken);

    Task<AdminInvitationViewModel> CreateInvitationAsync(CancellationToken cancellationToken);
}

public sealed class AdminPresentationService(
    MisskeyAnnouncementService announcements,
    IRelayCommandService relays,
    IRegistrationInvitationService invitations,
    IAuthenticatedActorContext actorContext) : IAdminPresentationService
{
    public async Task<AdminOverviewViewModel> ReadOverviewAsync(CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AdminRelayViewModel> relayViews = await ListRelaysAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AdminAnnouncementViewModel> announcementViews =
            await ListAnnouncementsAsync(cancellationToken).ConfigureAwait(false);
        return new AdminOverviewViewModel(relayViews, announcementViews);
    }

    public async Task<IReadOnlyList<AdminRelayViewModel>> ListRelaysAsync(CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Relay> values = await relays.ListAsync(cancellationToken).ConfigureAwait(false);
        return values.Select(relay => new AdminRelayViewModel(
            relay.Id.ToString("D"),
            relay.Inbox,
            RelayStatusName(relay.Status))).ToArray();
    }

    public async Task<AdminRelayViewModel> AddRelayAsync(string inbox, CancellationToken cancellationToken)
    {
        Relay relay = await relays.AddAsync(inbox, cancellationToken).ConfigureAwait(false);
        return new AdminRelayViewModel(relay.Id.ToString("D"), relay.Inbox, RelayStatusName(relay.Status));
    }

    public Task RemoveRelayAsync(string inbox, CancellationToken cancellationToken) =>
        relays.RemoveAsync(inbox, cancellationToken);

    public async Task<IReadOnlyList<AdminAnnouncementViewModel>> ListAnnouncementsAsync(CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MisskeyAdminAnnouncement> values = await announcements
            .ReadForAdministrationAsync(null, null, 100, cancellationToken).ConfigureAwait(false);
        return values.Select(value => new AdminAnnouncementViewModel(
            value.Id,
            value.CreatedAt,
            value.Title,
            value.Text,
            value.ImageUrl,
            value.Reads)).ToArray();
    }

    public async Task CreateAnnouncementAsync(
        string title,
        string text,
        string? imageUrl,
        CancellationToken cancellationToken)
    {
        await announcements.CreateAsync(
            new MisskeyAnnouncementMutation(title, text, imageUrl),
            "admin-frontend",
            "https://local.example/admin",
            cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAnnouncementAsync(string announcementId, CancellationToken cancellationToken) =>
        announcements.DeleteAsync(announcementId, "admin-frontend", cancellationToken);

    public async Task<AdminInvitationViewModel> CreateInvitationAsync(CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken).ConfigureAwait(false);
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        RegistrationInvitationIssueResult invitation = await invitations
            .IssueAsync(actor.Username, cancellationToken)
            .ConfigureAwait(false);
        return new AdminInvitationViewModel(invitation.Code, invitation.ExpiresAt);
    }

    private async Task RequireAdministratorAsync(CancellationToken cancellationToken)
    {
        if (await actorContext.FindAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            throw new FrontendAuthenticationException("AUTH_REQUIRED");
        }

        if (!await actorContext.IsAdministratorAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new FrontendAuthenticationException("ADMIN_REQUIRED");
        }
    }

    private static string RelayStatusName(RelayStatus status) => status switch
    {
        RelayStatus.Accepted => "accepted",
        RelayStatus.Rejected => "rejected",
        _ => "requesting"
    };
}
