using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Protocol;

namespace ActivityPub.Workers.Inbox;

public interface IInboxItemProcessor
{
    Task ProcessAsync(InboxItem item, string workerId, CancellationToken cancellationToken);
}

public sealed class InboxItemProcessor(
    IInboxRepository repository,
    IFederationQueryStore queryStore,
    IRemoteRecipientResolver recipientResolver,
    PublicIriFactory iriFactory,
    IFederationInstrumentation instrumentation,
    IInboundSpamEvaluator spamEvaluator,
    IIncomingHtmlSanitizer htmlSanitizer,
    IActorMoveValidator moveValidator,
    IRelayCommandService relays,
    IAnnounceChainGuard announceChainGuard,
    IClock clock) : IInboxItemProcessor
{
    public async Task ProcessAsync(InboxItem item, string workerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ActivityStreamsDocument document = ActivityStreamsParser.ParseActivity(item.RawBody);
        IReadOnlyList<string> acceptedRecipients = await repository
            .FindAcceptedRecipientsAsync(item.Id, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        string canonicalActivityJson = SanitizeAndRedactObject(document.Root);
        ActivityRecord activity = ActivityRecord.Create(
            document.Id,
            document.ActorIri,
            document.PrimaryType,
            document.ObjectIri,
            ActivityDirection.Inbound,
            document.Visibility,
            canonicalActivityJson,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(canonicalActivityJson)),
            isTransient: false,
            document.PublishedAt ?? item.CreatedAt,
            item.CreatedAt,
            Encoding.UTF8.GetString(item.RawBody));

        SpamAssessment spam = await spamEvaluator.EvaluateAsync(
            document.ActorIri,
            document.PrimaryType,
            item.RawBody,
            cancellationToken).ConfigureAwait(false);
        if (spam.Disposition == SpamDisposition.Quarantine)
        {
            string reason = spam.Reason.Length <= 4_096 ? spam.Reason : spam.Reason[..4_096];
            item.Quarantine(workerId, now, reason);
            await repository.SaveProcessedAsync(item, new InboxSideEffects(
                activity,
                null,
                null,
                null,
                null,
                null,
                DeadLetter.Create("inbox", item.Id, "spam_suspected", reason, now),
                null,
                BuildRecipients(document, activity.Id, acceptedRecipients)), cancellationToken).ConfigureAwait(false);
            instrumentation.ActivityProcessed(document.PrimaryType, now - item.CreatedAt);
            return;
        }

        try
        {
            InboxSideEffects effects = await BuildEffectsAsync(document, activity, cancellationToken).ConfigureAwait(false);
            effects = effects with { Recipients = BuildRecipients(document, activity.Id, acceptedRecipients) };
            item.Succeed(workerId, now);
            await repository.SaveProcessedAsync(item, effects, cancellationToken).ConfigureAwait(false);
            instrumentation.ActivityProcessed(document.PrimaryType, now - item.CreatedAt);
        }
        catch (Exception exception) when (exception is DomainException or ActivityStreamsProtocolException or JsonException)
        {
            string reason = exception.Message.Length <= 4_096 ? exception.Message : exception.Message[..4_096];
            item.Quarantine(workerId, now, reason);
            var effects = new InboxSideEffects(
                activity,
                null,
                null,
                null,
                null,
                ActorPolicy.Create(document.ActorIri, ModerationActionKind.QuarantineActivity, reason, "system", now, null),
                DeadLetter.Create("inbox", item.Id, "unsafe_activity", reason, now),
                null,
                BuildRecipients(document, activity.Id, acceptedRecipients));
            await repository.SaveProcessedAsync(item, effects, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<InboxSideEffects> BuildEffectsAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        if (!document.IsSupportedActivity)
        {
            return Empty(activity);
        }

        return document.PrimaryType switch
        {
            "Create" => BuildCreate(document, activity),
            "Update" => await BuildUpdateAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Delete" => await BuildDeleteAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Follow" => await BuildFollowAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Accept" => await BuildFollowDecisionAsync(document, activity, accepted: true, cancellationToken).ConfigureAwait(false),
            "Reject" => await BuildFollowDecisionAsync(document, activity, accepted: false, cancellationToken).ConfigureAwait(false),
            "Undo" => await BuildUndoAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Add" => await BuildAddAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Remove" => await BuildRemoveAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Like" => await BuildLikeAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "EmojiReaction" or "EmojiReact" => await BuildEmojiReactionAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Announce" => await BuildAnnounceAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Move" => await BuildMoveAsync(document, activity, cancellationToken).ConfigureAwait(false),
            "Flag" => BuildFlag(document, activity),
            "Block" => await BuildBlockAsync(document, activity, cancellationToken).ConfigureAwait(false),
            _ => Empty(activity)
        };
    }

    private InboxSideEffects BuildCreate(ActivityStreamsDocument document, ActivityRecord activity)
    {
        JsonElement embedded = RequireEmbeddedObject(document.Root, "Create");
        string objectIri = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Create object requires an id.");
        string owner = document.ObjectOwnerIri ?? document.ActorIri;
        if (!string.Equals(owner, document.ActorIri, StringComparison.Ordinal))
        {
            throw new ActivityStreamsProtocolException("Create actor does not match object attributedTo.");
        }

        string objectType = ActivityStreamsParser.ReadTypes(embedded)[0];
        IReadOnlyList<AudienceAddress> objectAudience = ActivityStreamsParser.ReadAudience(embedded);
        Visibility visibility = objectAudience.Count == 0
            ? document.Visibility
            : ActivityStreamsParser.NormalizeVisibility(owner, objectAudience);
        string sanitized = SanitizeAndRedactObject(embedded);
        FederatedObject federatedObject = FederatedObject.Create(
            objectIri,
            owner,
            objectType,
            visibility,
            sanitized,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(sanitized)),
            document.PublishedAt ?? activity.ReceivedAt,
            activity.ReceivedAt,
            embedded.GetRawText());
        return new(activity, federatedObject, null, null, null, null, null, null);
    }

    private async Task<InboxSideEffects> BuildUpdateAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        JsonElement embedded = RequireEmbeddedObject(document.Root, "Update");
        string objectIri = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Update object requires an id.");
        FederatedObject existing = await repository.FindObjectAsync(objectIri, cancellationToken).ConfigureAwait(false)
            ?? throw new ActivityStreamsProtocolException("Update target is unknown.");
        string objectType = ActivityStreamsParser.ReadTypes(embedded)[0];
        IReadOnlyList<AudienceAddress> audience = ActivityStreamsParser.ReadAudience(embedded);
        Visibility visibility = audience.Count == 0
            ? existing.Visibility
            : ActivityStreamsParser.NormalizeVisibility(document.ActorIri, audience);
        string sanitized = SanitizeAndRedactObject(embedded);
        ObjectRevision revision = existing.Replace(
            document.ActorIri,
            objectType,
            visibility,
            sanitized,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(sanitized)),
            activity.ReceivedAt,
            embedded.GetRawText());
        return new(activity, existing, revision, null, null, null, null, null);
    }

    private async Task<InboxSideEffects> BuildDeleteAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string objectIri = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Delete object requires an id.");
        FederatedObject existing = await repository.FindObjectAsync(objectIri, cancellationToken).ConfigureAwait(false)
            ?? throw new ActivityStreamsProtocolException("Delete target is unknown.");
        string tombstone = JsonSerializer.Serialize(new
        {
            id = objectIri,
            type = "Tombstone",
            formerType = existing.Type,
            deleted = activity.ReceivedAt
        });
        ObjectRevision revision = existing.Delete(
            document.ActorIri,
            tombstone,
            PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(tombstone)),
            activity.ReceivedAt);
        return new(activity, existing, revision, null, null, null, null, null);
    }

    private async Task<InboxSideEffects> BuildFollowAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string target = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Follow requires an object actor IRI.");
        FollowRelation? existing = await repository.FindFollowByPairAsync(
            document.ActorIri,
            target,
            cancellationToken).ConfigureAwait(false);
        FollowRelation follow;
        if (existing is null)
        {
            follow = FollowRelation.Request(document.ActorIri, target, document.Id, activity.ReceivedAt);
        }
        else
        {
            existing.RequestAgain(document.ActorIri, document.Id, activity.ReceivedAt);
            follow = existing;
        }

        ActorDocument localActor = await queryStore.FindLocalActorByIriAsync(target, cancellationToken).ConfigureAwait(false)
            ?? throw new ActivityStreamsProtocolException("Follow target is not a local actor.");
        if (localActor.ManuallyApprovesFollowers)
        {
            return new(activity, null, null, follow, null, null, null, null);
        }

        string decisionIri = iriFactory.ActivityIri(Guid.NewGuid());
        follow.Accept(localActor.Iri, decisionIri, activity.ReceivedAt);
        JsonNode nestedFollow = JsonNode.Parse(document.Root.GetRawText())
            ?? throw new ActivityStreamsProtocolException("Follow activity cannot be serialized.");
        var response = new JsonObject
        {
            ["@context"] = ActivityStreamsConstants.ActivityStreamsContext,
            ["id"] = decisionIri,
            ["type"] = "Accept",
            ["actor"] = localActor.Iri,
            ["object"] = nestedFollow,
            ["to"] = document.ActorIri
        };
        byte[] responseBytes = JsonSerializer.SerializeToUtf8Bytes(response);
        var outboundActivity = ActivityRecord.Create(
            decisionIri,
            localActor.Iri,
            "Accept",
            document.Id,
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            Encoding.UTF8.GetString(responseBytes),
            PayloadDigest.Sha256Hex(responseBytes),
            false,
            activity.ReceivedAt,
            activity.ReceivedAt);
        var recipient = new AudienceAddress(document.ActorIri, AudienceField.To);
        IReadOnlyList<RemoteActorEndpoint> endpoints = await recipientResolver.ResolveAsync(
            localActor.Iri,
            [recipient],
            cancellationToken).ConfigureAwait(false);
        var deliveries = new List<ActivityPub.Domain.Delivery>();
        var deliveryTargets = new List<DeliveryTarget>();
        foreach (IGrouping<string, RemoteActorEndpoint> group in endpoints.GroupBy(
                     endpoint => endpoint.SharedInboxIri ?? endpoint.InboxIri,
                     StringComparer.Ordinal))
        {
            ActivityPub.Domain.Delivery delivery = ActivityPub.Domain.Delivery.Create(
                outboundActivity.Id,
                outboundActivity.Iri,
                group.Key,
                localActor.Iri,
                responseBytes,
                SignatureProfile.LegacyCavage,
                activity.ReceivedAt);
            deliveries.Add(delivery);
            deliveryTargets.AddRange(group.Select(endpoint => DeliveryTarget.Create(delivery.Id, endpoint.ActorIri)));
        }
        var outbound = new OutboundCommit(
            outboundActivity,
            null,
            null,
            null,
            null,
            null,
            [ActivityRecipient.Create(outboundActivity.Id, document.ActorIri, AudienceField.To)],
            deliveries,
            DeliveryTargets: deliveryTargets);
        return new(activity, null, null, follow, null, null, null, outbound);
    }

    private async Task<InboxSideEffects> BuildFollowDecisionAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        bool accepted,
        CancellationToken cancellationToken)
    {
        string followActivityIri = ReadNestedActivityId(document.Root, accepted ? "Accept" : "Reject");
        FollowRelation? follow = await repository.FindFollowByActivityAsync(followActivityIri, cancellationToken).ConfigureAwait(false);
        if (follow is not null)
        {
            if (accepted)
            {
                follow.Accept(document.ActorIri, document.Id, activity.ReceivedAt);
            }
            else
            {
                follow.Reject(document.ActorIri, document.Id, activity.ReceivedAt);
            }

            return new(activity, null, null, follow, null, null, null, null);
        }

        if (accepted)
        {
            await relays.AcceptAsync(followActivityIri, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await relays.RejectAsync(followActivityIri, cancellationToken).ConfigureAwait(false);
        }

        return new(activity, null, null, null, null, null, null, null);
    }

    private async Task<InboxSideEffects> BuildUndoAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string nestedId = ReadNestedActivityId(document.Root, "Undo");
        FollowRelation? follow = await repository.FindFollowByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (follow is not null)
        {
            follow.Undo(document.ActorIri, activity.ReceivedAt);
            return new(activity, null, null, follow, null, null, null, null);
        }

        CollectionMembership? membership = await repository
            .FindCollectionMembershipByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (membership is not null)
        {
            if (string.Equals(membership.RemoveActivityIri, nestedId, StringComparison.Ordinal))
            {
                membership.UndoRemove(document.ActorIri, activity.ReceivedAt);
            }
            else
            {
                membership.UndoAdd(document.ActorIri, activity.ReceivedAt);
            }

            return Empty(activity) with { CollectionMembership = membership };
        }

        LikeRelation? like = await repository.FindLikeByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (like is not null)
        {
            like.Undo(document.ActorIri, activity.ReceivedAt);
            return Empty(activity) with { LikeRelation = like };
        }

        EmojiReactionRelation? emojiReaction = await repository
            .FindEmojiReactionByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (emojiReaction is not null)
        {
            emojiReaction.Undo(document.ActorIri, activity.ReceivedAt);
            return Empty(activity) with { EmojiReactionRelation = emojiReaction };
        }

        AnnounceRelation? announce = await repository.FindAnnounceByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (announce is not null)
        {
            announce.Undo(document.ActorIri, activity.ReceivedAt);
            return Empty(activity) with { AnnounceRelation = announce };
        }

        ActorMove? move = await repository.FindMoveByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (move is not null)
        {
            move.Undo(document.ActorIri, activity.ReceivedAt);
            return Empty(activity) with { ActorMove = move };
        }

        UserBlock? block = await repository.FindBlockByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (block is not null)
        {
            block.Undo(document.ActorIri, document.Id, activity.ReceivedAt);
            return Empty(activity) with { UserBlock = block };
        }

        return Empty(activity);
    }

    private async Task<InboxSideEffects> BuildBlockAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string targetActor = document.ObjectIri ??
            throw new ActivityStreamsProtocolException("Block requires an actor IRI.");
        ActorDocument? localTarget = await queryStore.FindLocalActorByIriAsync(targetActor, cancellationToken).ConfigureAwait(false);
        if (localTarget is null)
        {
            throw new ActivityStreamsProtocolException("Block target is not a local actor.");
        }

        UserBlock? existing = await repository.FindActiveBlockAsync(
            document.ActorIri,
            localTarget.Iri,
            cancellationToken).ConfigureAwait(false);
        return existing is null
            ? Empty(activity) with
            {
                UserBlock = UserBlock.Create(
                    document.ActorIri,
                    localTarget.Iri,
                    document.Id,
                    activity.ReceivedAt)
            }
            : Empty(activity);
    }

    private async Task<InboxSideEffects> BuildAddAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string objectIri = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Add requires an object IRI.");
        string collectionIri = ReadRequiredIriProperty(document.Root, "target", "Add");
        CollectionMembership? existing = await repository.FindActiveCollectionMembershipAsync(
            collectionIri,
            objectIri,
            cancellationToken).ConfigureAwait(false);
        return existing is null
            ? Empty(activity) with
            {
                CollectionMembership = CollectionMembership.Add(
                    document.ActorIri,
                    collectionIri,
                    objectIri,
                    document.Id,
                    activity.ReceivedAt)
            }
            : Empty(activity);
    }

    private async Task<InboxSideEffects> BuildRemoveAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string objectIri = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Remove requires an object IRI.");
        string collectionIri = ReadRequiredIriProperty(document.Root, "target", "Remove");
        CollectionMembership? membership = await repository.FindActiveCollectionMembershipAsync(
            collectionIri,
            objectIri,
            cancellationToken).ConfigureAwait(false);
        if (membership is null)
        {
            return Empty(activity);
        }

        membership.Remove(document.ActorIri, document.Id, activity.ReceivedAt);
        return Empty(activity) with { CollectionMembership = membership };
    }

    private async Task<InboxSideEffects> BuildLikeAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string objectIri = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Like requires an object IRI.");
        FederatedReaction reaction = ActivityReactionParser.Parse(document.Root, document.ActorIri);
        LikeRelation? existing = await repository.FindActiveLikeAsync(
            document.ActorIri,
            objectIri,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null && string.Equals(existing.EffectiveReaction, reaction.Value, StringComparison.Ordinal))
        {
            return Empty(activity);
        }

        existing?.Undo(document.ActorIri, activity.ReceivedAt);

        return Empty(activity) with
        {
            LikeRelation = LikeRelation.Create(document.ActorIri, objectIri, document.Id, reaction, activity.ReceivedAt),
            ReplacedLikeRelation = existing
        };
    }

    private async Task<InboxSideEffects> BuildAnnounceAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string objectIri = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Announce requires an object IRI.");
        if (await repository.FindActiveAnnounceAsync(document.ActorIri, objectIri, cancellationToken).ConfigureAwait(false) is not null)
        {
            return Empty(activity);
        }

        if (!await announceChainGuard.IsWithinChainLimitAsync(objectIri, cancellationToken).ConfigureAwait(false))
        {
            return Empty(activity);
        }

        return Empty(activity) with
        {
            AnnounceRelation = AnnounceRelation.Create(document.ActorIri, objectIri, document.Id, activity.ReceivedAt)
        };
    }

    private async Task<InboxSideEffects> BuildEmojiReactionAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string objectIri = document.ObjectIri
            ?? throw new ActivityStreamsProtocolException("EmojiReact requires an object IRI.");
        FederatedReaction reaction = ActivityReactionParser.Parse(document.Root, document.ActorIri);
        EmojiReactionRelation? existing = await repository.FindActiveEmojiReactionAsync(
            document.ActorIri,
            objectIri,
            reaction.Value,
            cancellationToken).ConfigureAwait(false);
        return existing is null
            ? Empty(activity) with
            {
                EmojiReactionRelation = EmojiReactionRelation.Create(
                    document.ActorIri,
                    objectIri,
                    document.Id,
                    reaction,
                    activity.ReceivedAt)
            }
            : Empty(activity);
    }

    private async Task<InboxSideEffects> BuildMoveAsync(
        ActivityStreamsDocument document,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string sourceActor = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Move requires the source actor as object.");
        if (!string.Equals(sourceActor, document.ActorIri, StringComparison.Ordinal))
        {
            throw new ActivityStreamsProtocolException("Move object must be the actor performing the Move.");
        }

        string targetActor = ReadRequiredIriProperty(document.Root, "target", "Move");
        await moveValidator.ValidateAsync(sourceActor, targetActor, cancellationToken).ConfigureAwait(false);
        return Empty(activity) with
        {
            ActorMove = ActorMove.Create(sourceActor, targetActor, document.Id, activity.ReceivedAt)
        };
    }

    private static InboxSideEffects BuildFlag(ActivityStreamsDocument document, ActivityRecord activity)
    {
        string target = document.ObjectIri ?? throw new ActivityStreamsProtocolException("Flag requires a target IRI.");
        Report report = Report.Create(document.Id, document.ActorIri, target, document.Root.GetRawText(), activity.ReceivedAt);
        return new(activity, null, null, null, report, null, null, null);
    }

    private string SanitizeAndRedactObject(JsonElement embedded)
    {
        JsonNode node = JsonNode.Parse(embedded.GetRawText(), documentOptions: new JsonDocumentOptions { MaxDepth = 64 })
            ?? throw new ActivityStreamsProtocolException("Embedded object is empty.");
        SanitizeNode(node);
        using JsonDocument sanitized = JsonDocument.Parse(node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        return Encoding.UTF8.GetString(ActivityStreamsSerializer.StripBlindRecipientsAndSerialize(sanitized.RootElement));
    }

    private void SanitizeNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> property in obj.ToArray())
            {
                if (property.Value is JsonValue value && property.Key is "content" or "summary" && value.TryGetValue(out string? html))
                {
                    obj[property.Key] = htmlSanitizer.Sanitize(html);
                }
                else if (property.Value is not null)
                {
                    SanitizeNode(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item is not null)
                {
                    SanitizeNode(item);
                }
            }
        }
    }

    private static JsonElement RequireEmbeddedObject(JsonElement root, string activityType)
    {
        if (!root.TryGetProperty("object", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new ActivityStreamsProtocolException($"{activityType} requires an embedded object.");
        }

        return value;
    }

    private static string ReadNestedActivityId(JsonElement root, string outerType)
    {
        if (!root.TryGetProperty("object", out JsonElement nested))
        {
            throw new ActivityStreamsProtocolException($"{outerType} requires an object activity.");
        }

        if (nested.ValueKind == JsonValueKind.String)
        {
            return CanonicalIri.RequireAbsoluteHttp(nested.GetString()!, "object");
        }

        if (nested.ValueKind == JsonValueKind.Object && nested.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
        {
            return CanonicalIri.RequireAbsoluteHttp(id.GetString()!, "object.id");
        }

        throw new ActivityStreamsProtocolException($"{outerType} object activity has no id.");
    }

    private static string ReadRequiredIriProperty(JsonElement root, string property, string activityType)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
        {
            throw new ActivityStreamsProtocolException($"{activityType} requires {property}.");
        }

        string? candidate = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String => id.GetString(),
            JsonValueKind.Object when value.TryGetProperty("href", out JsonElement href) && href.ValueKind == JsonValueKind.String => href.GetString(),
            _ => null
        };
        return candidate is null
            ? throw new ActivityStreamsProtocolException($"{activityType} {property} is not an IRI.")
            : CanonicalIri.RequireAbsoluteHttp(candidate, property);
    }

    private static InboxSideEffects Empty(ActivityRecord activity) =>
        new(activity, null, null, null, null, null, null, null);

    private static ActivityRecipient[] BuildRecipients(
        ActivityStreamsDocument document,
        Guid activityId,
        IReadOnlyList<string> acceptedRecipients) =>
        document.Audience
            .Concat(acceptedRecipients.Select(actorIri => new AudienceAddress(actorIri, AudienceField.Audience)))
            .DistinctBy(address => (address.Iri, address.Field))
            .Select(address => ActivityRecipient.Create(activityId, address.Iri, address.Field))
            .ToArray();
}
