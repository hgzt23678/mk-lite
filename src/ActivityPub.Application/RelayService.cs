using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActivityPub.Domain;

namespace ActivityPub.Application;

public sealed class RelayService(
    IRelayRepository relays,
    IDeliveryRepository deliveries,
    ILocalActorAdministration actorAdministration,
    IFederationQueryStore queryStore,
    PublicIriFactory iriFactory,
    IClock clock) : IRelayCommandService
{
    private const string RelayActorUsername = "relay.actor";
    private const string PublicAudience = "https://www.w3.org/ns/activitystreams#Public";
    private const string ActivityStreamsContext = "https://www.w3.org/ns/activitystreams";

    public async Task<Relay> AddAsync(string inbox, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inbox) ||
            !Uri.TryCreate(inbox, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new DomainException("Relay inbox must be an absolute HTTPS URL.");
        }

        Relay? existing = await relays.FindByInboxAsync(inbox, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        Relay relay = Relay.Request(inbox, clock.UtcNow);
        await relays.AddAsync(relay, cancellationToken).ConfigureAwait(false);
        string actorIri = await EnsureRelayActorAsync(cancellationToken).ConfigureAwait(false);
        await DeliverFollowAsync(relay, actorIri, cancellationToken).ConfigureAwait(false);
        return relay;
    }

    public async Task RemoveAsync(string inbox, CancellationToken cancellationToken)
    {
        Relay? relay = await relays.FindByInboxAsync(inbox, cancellationToken).ConfigureAwait(false);
        if (relay is null)
        {
            throw new DomainException("Relay not found.");
        }

        string actorIri = await EnsureRelayActorAsync(cancellationToken).ConfigureAwait(false);
        await DeliverUndoAsync(relay, actorIri, cancellationToken).ConfigureAwait(false);
        await relays.DeleteAsync(relay.Id, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Relay>> ListAsync(CancellationToken cancellationToken) =>
        relays.ListAsync(cancellationToken);

    public async Task AcceptAsync(string followActivityIri, CancellationToken cancellationToken)
    {
        Guid? relayId = TryParseRelayFollowId(followActivityIri);
        if (relayId is null)
        {
            return;
        }

        await relays.UpdateStatusAsync(relayId.Value, RelayStatus.Accepted, cancellationToken).ConfigureAwait(false);
    }

    public async Task RejectAsync(string followActivityIri, CancellationToken cancellationToken)
    {
        Guid? relayId = TryParseRelayFollowId(followActivityIri);
        if (relayId is null)
        {
            return;
        }

        await relays.UpdateStatusAsync(relayId.Value, RelayStatus.Rejected, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeliverToAcceptedRelaysAsync(
        Guid activityId,
        string activityIri,
        string actorIri,
        byte[] activityPayload,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Relay> accepted = await relays.ListAcceptedAsync(cancellationToken).ConfigureAwait(false);
        if (accepted.Count == 0)
        {
            return;
        }

        DateTimeOffset now = clock.UtcNow;
        var newDeliveries = new List<Delivery>(accepted.Count);
        foreach (Relay relay in accepted)
        {
            newDeliveries.Add(Delivery.Create(
                activityId,
                activityIri,
                relay.Inbox,
                actorIri,
                activityPayload,
                SignatureProfile.LegacyCavage,
                now));
        }

        await deliveries.CommitRelayDeliveriesAsync(newDeliveries, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> EnsureRelayActorAsync(CancellationToken cancellationToken)
    {
        ActorDocument? existing = await queryStore
            .FindLocalActorByUsernameAsync(RelayActorUsername, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.Iri;
        }

        LocalActorAdministrationResult created = await actorAdministration.CreateAsync(
            RelayActorUsername,
            ActorKind.Person,
            "Relay Actor",
            string.Empty,
            manuallyApprovesFollowers: false,
            discoverable: true,
            indexable: true,
            operatorId: "relay",
            cancellationToken).ConfigureAwait(false);
        return created.ActorIri;
    }

    private async Task DeliverFollowAsync(Relay relay, string actorIri, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        string activityIri = RelayFollowIri(relay.Id);
        byte[] payload = BuildFollowPayload(activityIri, actorIri, embed: false);
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            actorIri,
            "Follow",
            PublicAudience,
            ActivityDirection.Outbound,
            Visibility.Public,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            isTransient: false,
            now,
            now);
        Delivery delivery = Delivery.Create(
            activity.Id,
            activityIri,
            relay.Inbox,
            actorIri,
            payload,
            SignatureProfile.LegacyCavage,
            now);
        await deliveries.CommitOutboundAsync(
            new OutboundCommit(
                activity,
                FederatedObject: null,
                ObjectRevision: null,
                FollowRelation: null,
                MediaAttachments: null,
                ClientIdempotency: null,
                Recipients: [],
                Deliveries: [delivery]),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DeliverUndoAsync(Relay relay, string actorIri, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;
        string followIri = RelayFollowIri(relay.Id);
        byte[] followPayload = BuildFollowPayload(followIri, actorIri, embed: true);
        string undoIri = iriFactory.ActivityIri(Guid.NewGuid());
        var undo = new JsonObject
        {
            ["@context"] = ActivityStreamsContext,
            ["id"] = undoIri,
            ["type"] = "Undo",
            ["actor"] = actorIri,
            ["object"] = JsonNode.Parse(followPayload)
        };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(undo);
        ActivityRecord activity = ActivityRecord.Create(
            undoIri,
            actorIri,
            "Undo",
            followIri,
            ActivityDirection.Outbound,
            Visibility.Public,
            Encoding.UTF8.GetString(payload),
            PayloadDigest.Sha256Hex(payload),
            isTransient: false,
            now,
            now);
        Delivery delivery = Delivery.Create(
            activity.Id,
            undoIri,
            relay.Inbox,
            actorIri,
            payload,
            SignatureProfile.LegacyCavage,
            now);
        await deliveries.CommitOutboundAsync(
            new OutboundCommit(
                activity,
                FederatedObject: null,
                ObjectRevision: null,
                FollowRelation: null,
                MediaAttachments: null,
                ClientIdempotency: null,
                Recipients: [],
                Deliveries: [delivery]),
            cancellationToken).ConfigureAwait(false);
    }

    private static byte[] BuildFollowPayload(string activityIri, string actorIri, bool embed)
    {
        var follow = new JsonObject
        {
            ["@context"] = ActivityStreamsContext,
            ["id"] = activityIri,
            ["type"] = "Follow",
            ["actor"] = actorIri,
            ["object"] = PublicAudience
        };
        return JsonSerializer.SerializeToUtf8Bytes(follow);
    }

    private string RelayFollowIri(Guid relayId) => iriFactory.RelayFollow(relayId);

    private static Guid? TryParseRelayFollowId(string activityIri)
    {
        const string marker = "/activities/follow-relay/";
        int index = activityIri.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        string candidate = activityIri[(index + marker.Length)..];
        return Guid.TryParse(candidate, out Guid id) ? id : null;
    }

}
