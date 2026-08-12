using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Protocol;

namespace ActivityPub.Federation.Outbound;

public sealed class ClientOutboxService(
    IFederationQueryStore queryStore,
    IInboxRepository objectRepository,
    IDeliveryRepository deliveryRepository,
    IRemoteRecipientResolver recipientResolver,
    IActorMoveValidator moveValidator,
    IIncomingHtmlSanitizer htmlSanitizer,
    PublicIriFactory iriFactory,
    FederationOptions options,
    IClock clock,
    IRelayCommandService relays) : IClientOutboxService
{
    private static readonly string[] AudienceProperties = ["to", "cc", "bto", "bcc", "audience"];
    private static readonly string[] AttachmentIriProperties = ["id", "url", "href"];

    public async Task<ClientOutboxResult> SubmitAsync(
        string username,
        string idempotencyKey,
        byte[] requestBody,
        CancellationToken cancellationToken)
    {
        if (!options.ClientToServerEnabled)
        {
            throw new InvalidOperationException("ActivityPub Client-to-Server is disabled.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        string safeIdempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        ArgumentNullException.ThrowIfNull(requestBody);
        if (requestBody.Length is 0 || requestBody.Length > options.MaximumInboxBodyBytes)
        {
            throw new ActivityStreamsProtocolException("Client outbox body size is outside the accepted range.");
        }

        string requestHash = PayloadDigest.Sha256Hex(requestBody);
        ClientIdempotencyRecord? existingRequest = await deliveryRepository
            .FindClientIdempotencyAsync(username, safeIdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existingRequest is not null)
        {
            if (!string.Equals(existingRequest.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ActivityStreamsProtocolException("Idempotency key was already used with a different request body.");
            }

            return new(existingRequest.ActivityIri, existingRequest.ObjectIri, existingRequest.ResponseBody);
        }

        ActorDocument actor = await queryStore.FindLocalActorByUsernameAsync(username, cancellationToken).ConfigureAwait(false)
            ?? throw new ActivityStreamsProtocolException("Local actor does not exist.");
        JsonSafetyValidator.Validate(requestBody);
        JsonObject input = JsonNode.Parse(requestBody, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
            ?? throw new ActivityStreamsProtocolException("Client outbox body must be a JSON object.");
        DateTimeOffset now = clock.UtcNow;
        (JsonObject activityNode, JsonObject? embeddedObject) = NormalizeSubmission(input, actor.Iri, now);
        int? localPollChoiceIndex = RemoveLocalPollChoiceIndex(embeddedObject);

        byte[] fullActivityBytes = JsonSerializer.SerializeToUtf8Bytes(activityNode);
        ActivityStreamsDocument parsed = ActivityStreamsParser.ParseActivity(fullActivityBytes);
        byte[] deliveryPayload = ActivityStreamsSerializer.StripBlindRecipientsAndSerialize(parsed.Root);
        string activityJson = Encoding.UTF8.GetString(deliveryPayload);
        var activity = ActivityRecord.Create(
            parsed.Id,
            actor.Iri,
            parsed.PrimaryType,
            parsed.ObjectIri,
            ActivityDirection.Outbound,
            parsed.Visibility,
            activityJson,
            PayloadDigest.Sha256Hex(deliveryPayload),
            isTransient: false,
            now,
            now);

        (FederatedObject? federatedObject, ObjectRevision? revision) = await ApplyObjectMutationAsync(
            parsed,
            embeddedObject,
            activity,
            cancellationToken).ConfigureAwait(false);
        (QuestionPoll? questionPoll, IReadOnlyList<PollOption>? pollOptions, PollVote? pollVote) =
            await ExtractPollMutationAsync(
                parsed,
                embeddedObject,
                federatedObject,
                localPollChoiceIndex,
                activity,
                cancellationToken).ConfigureAwait(false);
        if (pollVote is not null)
        {
            // A federated vote is a transient Create/Note directed at the Question
            // owner. The dedicated aggregate and Activity record retain it without
            // leaking the private ballot into local timelines as an ordinary Note.
            federatedObject = null;
            revision = null;
        }
        FollowRelation? followRelation = await ApplyFollowMutationAsync(parsed, activity, cancellationToken).ConfigureAwait(false);
        ActivityAggregateMutation aggregateMutation = await ApplyActivityAggregateAsync(
            parsed,
            activity,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MediaAttachment>? mediaAttachments = federatedObject is null || parsed.PrimaryType is not ("Create" or "Update")
            ? null
            : ExtractMediaAttachments(embeddedObject!, federatedObject.Id);
        ActivityRecipient[] recipients = parsed.Audience
            .Select(address => ActivityRecipient.Create(activity.Id, address.Iri, address.Field))
            .ToArray();
        IReadOnlyList<RemoteActorEndpoint> endpoints = await (aggregateMutation.UserBlock is null
                ? recipientResolver.ResolveAsync(actor.Iri, parsed.Audience, cancellationToken)
                : recipientResolver.ResolveIncludingBlockedAsync(actor.Iri, parsed.Audience, cancellationToken))
            .ConfigureAwait(false);
        (Delivery[] deliveries, DeliveryTarget[] deliveryTargets) = CreateDeliveries(
            endpoints,
            activity,
            actor.Iri,
            deliveryPayload,
            now);
        ClientIdempotencyRecord idempotency = ClientIdempotencyRecord.Create(
            username,
            safeIdempotencyKey,
            requestHash,
            activity.Iri,
            parsed.ObjectIri,
            deliveryPayload,
            now,
            now.Add(options.ClientIdempotencyRetention));
        OutboundCommitResult commitResult = await deliveryRepository.CommitOutboundAsync(
            new OutboundCommit(
                activity,
                federatedObject,
                revision,
                followRelation,
                mediaAttachments,
                idempotency,
                recipients,
                deliveries,
                aggregateMutation.CollectionMembership,
                aggregateMutation.LikeRelation,
                aggregateMutation.AnnounceRelation,
                aggregateMutation.ActorMove,
                deliveryTargets,
                EmojiReactionRelation: aggregateMutation.EmojiReactionRelation,
                UserBlock: aggregateMutation.UserBlock,
                QuestionPoll: questionPoll,
                PollOptions: pollOptions,
                PollVote: pollVote),
            cancellationToken).ConfigureAwait(false);
        if (parsed.PrimaryType == "Create" && parsed.Visibility == Visibility.Public)
        {
            await relays.DeliverToAcceptedRelaysAsync(
                activity.Id,
                activity.Iri,
                actor.Iri,
                deliveryPayload,
                cancellationToken).ConfigureAwait(false);
        }

        return commitResult.ExistingResult ?? new(activity.Iri, parsed.ObjectIri, deliveryPayload);
    }

    private static int? RemoveLocalPollChoiceIndex(JsonObject? embeddedObject)
    {
        if (embeddedObject is null ||
            !embeddedObject.TryGetPropertyValue("_activitypubServerChoiceIndex", out JsonNode? raw))
        {
            return null;
        }

        embeddedObject.Remove("_activitypubServerChoiceIndex");
        return raw is JsonValue value && value.TryGetValue(out int choice) ? choice : null;
    }

    private async Task<(QuestionPoll? Poll, IReadOnlyList<PollOption>? Options, PollVote? Vote)> ExtractPollMutationAsync(
        ActivityStreamsDocument parsed,
        JsonObject? embeddedObject,
        FederatedObject? federatedObject,
        int? localPollChoiceIndex,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        if (parsed.PrimaryType != "Create" || embeddedObject is null)
        {
            return (null, null, null);
        }

        if (federatedObject?.Type == "Question")
        {
            (QuestionPoll poll, PollOption[] options) = ParseQuestionPoll(
                federatedObject,
                embeddedObject,
                activity.ReceivedAt);
            return (poll, options, null);
        }

        string? inReplyTo = ReadStringValue(embeddedObject, "inReplyTo");
        string? choiceName = ReadStringValue(embeddedObject, "name");
        if (inReplyTo is null || choiceName is null)
        {
            return (null, null, null);
        }

        FederatedObject? question = await objectRepository.FindObjectAsync(inReplyTo, cancellationToken).ConfigureAwait(false);
        if (question is null || question.IsDeleted || question.Type != "Question")
        {
            return (null, null, null);
        }

        JsonObject questionNode = JsonNode.Parse(question.RawJson) as JsonObject
            ?? throw new ActivityStreamsProtocolException("Stored Question payload is not a JSON object.");
        (QuestionPoll pollSnapshot, PollOption[] optionSnapshot) = ParseQuestionPoll(
            question,
            questionNode,
            question.PublishedAt);
        int choiceIndex = localPollChoiceIndex ?? Array.FindIndex(
            optionSnapshot,
            option => string.Equals(option.Title, choiceName, StringComparison.Ordinal));
        if (choiceIndex < 0 || choiceIndex >= optionSnapshot.Length ||
            !string.Equals(optionSnapshot[choiceIndex].Title, choiceName, StringComparison.Ordinal))
        {
            throw new ClientPollVoteException(ClientPollVoteError.InvalidChoice, "The poll choice is invalid.");
        }

        PollVote vote;
        try
        {
            vote = pollSnapshot.CastVote(
                parsed.ActorIri,
                choiceIndex,
                optionSnapshot.Select(option => option.ChoiceIndex).ToHashSet(),
                activity.Iri,
                activity.ReceivedAt);
        }
        catch (DomainException exception) when (pollSnapshot.IsExpired(activity.ReceivedAt))
        {
            throw new ClientPollVoteException(ClientPollVoteError.Expired, exception.Message);
        }
        catch (DomainException exception)
        {
            throw new ClientPollVoteException(ClientPollVoteError.InvalidChoice, exception.Message);
        }

        return (pollSnapshot, optionSnapshot, vote);
    }

    private static (QuestionPoll Poll, PollOption[] Options) ParseQuestionPoll(
        FederatedObject question,
        JsonObject node,
        DateTimeOffset createdAt)
    {
        bool multiple = node.TryGetPropertyValue("anyOf", out JsonNode? anyOf) && anyOf is not null;
        JsonNode? rawChoices = multiple ? anyOf : node["oneOf"];
        JsonArray choices = rawChoices switch
        {
            JsonArray array => array,
            JsonObject single => new JsonArray(single.DeepClone()),
            _ => throw new ActivityStreamsProtocolException("Question must contain oneOf or anyOf choices.")
        };
        if (choices.Count is < 2 or > 10)
        {
            throw new ActivityStreamsProtocolException("Question must contain between two and ten choices.");
        }

        var options = new List<PollOption>(choices.Count);
        for (int index = 0; index < choices.Count; index++)
        {
            if (choices[index] is not JsonObject choice || ReadStringValue(choice, "name") is not { } title)
            {
                throw new ActivityStreamsProtocolException("Question choice is missing its name.");
            }

            long votes = 0;
            if (choice["replies"] is JsonObject replies && replies["totalItems"] is JsonValue total &&
                total.TryGetValue(out long parsedVotes))
            {
                votes = parsedVotes;
            }

            options.Add(PollOption.Create(question.Id, index, title, votes));
        }

        DateTimeOffset? expiresAt = null;
        if (ReadStringValue(node, "endTime") is { } rawEndTime &&
            DateTimeOffset.TryParse(
                rawEndTime,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsedEndTime))
        {
            expiresAt = parsedEndTime;
        }

        long votersCount = node["votersCount"] is JsonValue voters && voters.TryGetValue(out long parsedVoters)
            ? parsedVoters
            : 0;
        return (
            QuestionPoll.Create(question.Id, multiple, expiresAt, votersCount, createdAt),
            options.ToArray());
    }

    private static string? ReadStringValue(JsonObject node, string property) =>
        node[property] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private (JsonObject Activity, JsonObject? EmbeddedObject) NormalizeSubmission(
        JsonObject input,
        string actorIri,
        DateTimeOffset now)
    {
        IReadOnlyList<string> types = ReadTypes(input);
        bool isActivity = types.Any(ActivityStreamsConstants.SupportedActivities.Contains);
        string published = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        string activityIri = iriFactory.ActivityIri(Guid.NewGuid());
        if (!isActivity)
        {
            JsonObject embedded = (JsonObject)input.DeepClone();
            NormalizeNewObject(embedded, actorIri, published);
            var activity = new JsonObject
            {
                ["@context"] = ActivityStreamsConstants.ActivityStreamsContext,
                ["id"] = activityIri,
                ["type"] = "Create",
                ["actor"] = actorIri,
                ["published"] = published,
                ["object"] = embedded
            };
            CopyAudience(embedded, activity);
            return (activity, embedded);
        }

        JsonObject normalized = (JsonObject)input.DeepClone();
        normalized["@context"] ??= ActivityStreamsConstants.ActivityStreamsContext;
        normalized["id"] = activityIri;
        normalized["actor"] = actorIri;
        normalized["published"] = published;
        string activityType = types.First(ActivityStreamsConstants.SupportedActivities.Contains);
        JsonObject? objectNode = normalized["object"] as JsonObject;
        if (activityType == "Create")
        {
            if (objectNode is null)
            {
                throw new ActivityStreamsProtocolException("Client Create requires an embedded object.");
            }

            NormalizeNewObject(objectNode, actorIri, published);
            CopyAudience(objectNode, normalized);
        }
        else if (activityType == "Update")
        {
            if (objectNode is null)
            {
                throw new ActivityStreamsProtocolException("Client Update requires an embedded object.");
            }

            objectNode["attributedTo"] = actorIri;
            objectNode["actor"] = actorIri;
            SanitizeNode(objectNode);
        }
        else if (activityType is "Like" or "EmojiReaction" or "EmojiReact")
        {
            NormalizeReaction(normalized, actorIri, activityType);
        }

        return (normalized, objectNode);
    }

    private static void NormalizeReaction(JsonObject activity, string actorIri, string activityType)
    {
        using JsonDocument source = JsonDocument.Parse(activity.ToJsonString());
        FederatedReaction reaction = ActivityReactionParser.Parse(source.RootElement, actorIri);
        if (reaction.IsCustomEmoji && (reaction.CustomEmojiIri is null || reaction.CustomEmojiUrl is null))
        {
            throw new ActivityStreamsProtocolException("Outbound custom emoji reactions require a matching Emoji tag with id and icon.url.");
        }

        bool litePub = activityType is "EmojiReact" or "EmojiReaction";
        activity["type"] = litePub ? "EmojiReact" : "Like";
        activity["content"] = litePub && reaction.IsCustomEmoji ? reaction.CustomEmojiName : reaction.Value;
        if (litePub)
        {
            activity.Remove("_misskey_reaction");
        }
        else
        {
            activity["_misskey_reaction"] = reaction.Value;
        }

        activity["@context"] = litePub
            ? new JsonArray
            {
                ActivityStreamsConstants.ActivityStreamsContext,
                new JsonObject
                {
                    ["litepub"] = "http://litepub.social/ns#",
                    ["EmojiReact"] = "litepub:EmojiReact"
                }
            }
            : new JsonArray
        {
            ActivityStreamsConstants.ActivityStreamsContext,
            reaction.IsCustomEmoji ? "http://joinmastodon.org/ns#" : null,
            new JsonObject
            {
                ["misskey"] = "https://misskey-hub.net/ns#",
                ["_misskey_reaction"] = "misskey:_misskey_reaction"
            }
        };
        for (int index = activity["@context"]!.AsArray().Count - 1; index >= 0; index--)
        {
            if (activity["@context"]!.AsArray()[index] is null)
            {
                activity["@context"]!.AsArray().RemoveAt(index);
            }
        }

        activity.Remove("name");
        activity.Remove("tag");
        if (reaction.IsCustomEmoji)
        {
            activity["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = reaction.CustomEmojiIri,
                    ["type"] = "Emoji",
                    ["name"] = reaction.CustomEmojiName,
                    ["icon"] = new JsonObject
                    {
                        ["type"] = "Image",
                        ["mediaType"] = reaction.CustomEmojiMediaType ?? "image/png",
                        ["url"] = reaction.CustomEmojiUrl
                    }
                }
            };
        }
    }

    private void NormalizeNewObject(JsonObject embedded, string actorIri, string published)
    {
        embedded["id"] = iriFactory.ObjectIri(Guid.NewGuid());
        embedded["attributedTo"] = actorIri;
        // Pleroma/LitePub serializes the owning actor on objects as `actor`.
        // Keep the normative attributedTo property and include this explicit
        // compatibility alias so both object representations can verify owner.
        embedded["actor"] = actorIri;
        embedded["published"] = published;
        SanitizeNode(embedded);
    }

    private async Task<(FederatedObject? Object, ObjectRevision? Revision)> ApplyObjectMutationAsync(
        ActivityStreamsDocument parsed,
        JsonObject? embeddedObject,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        if (parsed.PrimaryType == "Create")
        {
            if (embeddedObject is null)
            {
                throw new ActivityStreamsProtocolException("Create has no embedded object.");
            }

            string objectIri = parsed.ObjectIri ?? throw new ActivityStreamsProtocolException("Create object has no id.");
            string objectJson = SerializeForExternalStorage(embeddedObject);
            string objectType = ReadTypes(embeddedObject)[0];
            return (FederatedObject.Create(
                objectIri,
                parsed.ActorIri,
                objectType,
                parsed.Visibility,
                objectJson,
                PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(objectJson)),
                activity.OccurredAt,
                activity.ReceivedAt), null);
        }

        if (parsed.PrimaryType == "Update")
        {
            if (embeddedObject is null)
            {
                throw new ActivityStreamsProtocolException("Update has no embedded object.");
            }

            string objectIri = parsed.ObjectIri ?? throw new ActivityStreamsProtocolException("Update object has no id.");
            FederatedObject existing = await objectRepository.FindObjectAsync(objectIri, cancellationToken).ConfigureAwait(false)
                ?? throw new ActivityStreamsProtocolException("Update target does not exist.");
            string objectJson = SerializeForExternalStorage(embeddedObject);
            ObjectRevision revision = existing.Replace(
                parsed.ActorIri,
                ReadTypes(embeddedObject)[0],
                parsed.Visibility,
                objectJson,
                PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(objectJson)),
                activity.ReceivedAt);
            return (existing, revision);
        }

        if (parsed.PrimaryType == "Delete")
        {
            string objectIri = parsed.ObjectIri ?? throw new ActivityStreamsProtocolException("Delete object has no id.");
            FederatedObject existing = await objectRepository.FindObjectAsync(objectIri, cancellationToken).ConfigureAwait(false)
                ?? throw new ActivityStreamsProtocolException("Delete target does not exist.");
            string tombstone = JsonSerializer.Serialize(new
            {
                id = objectIri,
                type = "Tombstone",
                formerType = existing.Type,
                deleted = activity.ReceivedAt
            });
            ObjectRevision revision = existing.Delete(
                parsed.ActorIri,
                tombstone,
                PayloadDigest.Sha256Hex(Encoding.UTF8.GetBytes(tombstone)),
                activity.ReceivedAt);
            return (existing, revision);
        }

        return (null, null);
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

    private async Task<FollowRelation?> ApplyFollowMutationAsync(
        ActivityStreamsDocument parsed,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        if (parsed.PrimaryType == "Follow")
        {
            string target = parsed.ObjectIri ?? throw new ActivityStreamsProtocolException("Follow requires an object actor IRI.");
            FollowRelation? existing = await objectRepository.FindFollowByPairAsync(
                parsed.ActorIri,
                target,
                cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                return FollowRelation.Request(parsed.ActorIri, target, parsed.Id, activity.ReceivedAt);
            }

            existing.RequestAgain(parsed.ActorIri, parsed.Id, activity.ReceivedAt);
            return existing;
        }

        if (parsed.PrimaryType is not ("Accept" or "Reject" or "Undo"))
        {
            return null;
        }

        string nestedActivityIri = parsed.ObjectIri
            ?? throw new ActivityStreamsProtocolException($"{parsed.PrimaryType} requires a referenced activity id.");
        FollowRelation? follow = await objectRepository.FindFollowByActivityAsync(nestedActivityIri, cancellationToken).ConfigureAwait(false);
        if (follow is null && parsed.PrimaryType == "Undo")
        {
            return null;
        }

        if (follow is null)
        {
            throw new ActivityStreamsProtocolException($"{parsed.PrimaryType} references an unknown Follow.");
        }

        if (parsed.PrimaryType == "Accept")
        {
            follow.Accept(parsed.ActorIri, parsed.Id, activity.ReceivedAt);
        }
        else if (parsed.PrimaryType == "Reject")
        {
            follow.Reject(parsed.ActorIri, parsed.Id, activity.ReceivedAt);
        }
        else
        {
            follow.Undo(parsed.ActorIri, activity.ReceivedAt);
        }

        return follow;
    }

    private async Task<ActivityAggregateMutation> ApplyActivityAggregateAsync(
        ActivityStreamsDocument parsed,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string? objectIri = parsed.ObjectIri;
        switch (parsed.PrimaryType)
        {
            case "Add":
                {
                    string item = objectIri ?? throw new ActivityStreamsProtocolException("Add requires an object IRI.");
                    string collection = ReadRequiredIriProperty(parsed.Root, "target", "Add");
                    CollectionMembership? existing = await objectRepository.FindActiveCollectionMembershipAsync(
                        collection,
                        item,
                        cancellationToken).ConfigureAwait(false);
                    return existing is null
                        ? new(CollectionMembership.Add(parsed.ActorIri, collection, item, parsed.Id, activity.ReceivedAt), null, null, null)
                        : default;
                }
            case "Remove":
                {
                    string item = objectIri ?? throw new ActivityStreamsProtocolException("Remove requires an object IRI.");
                    string collection = ReadRequiredIriProperty(parsed.Root, "target", "Remove");
                    CollectionMembership? membership = await objectRepository.FindActiveCollectionMembershipAsync(
                        collection,
                        item,
                        cancellationToken).ConfigureAwait(false);
                    if (membership is null)
                    {
                        return default;
                    }

                    membership.Remove(parsed.ActorIri, parsed.Id, activity.ReceivedAt);
                    return new(membership, null, null, null);
                }
            case "Like":
                {
                    string likedObject = objectIri ?? throw new ActivityStreamsProtocolException("Like requires an object IRI.");
                    FederatedReaction reaction = ActivityReactionParser.Parse(parsed.Root, parsed.ActorIri);
                    LikeRelation? existing = await objectRepository.FindActiveLikeAsync(
                        parsed.ActorIri,
                        likedObject,
                        cancellationToken).ConfigureAwait(false);
                    if (existing is not null)
                    {
                        throw new ActivityStreamsProtocolException("An active reaction already exists; Undo it before creating a replacement.");
                    }

                    return new(
                        null,
                        LikeRelation.Create(parsed.ActorIri, likedObject, parsed.Id, reaction, activity.ReceivedAt),
                        null,
                        null);
                }
            case "EmojiReact":
            case "EmojiReaction":
                {
                    string reactedObject = objectIri ?? throw new ActivityStreamsProtocolException("EmojiReact requires an object IRI.");
                    FederatedReaction reaction = ActivityReactionParser.Parse(parsed.Root, parsed.ActorIri);
                    EmojiReactionRelation? existing = await objectRepository.FindActiveEmojiReactionAsync(
                        parsed.ActorIri,
                        reactedObject,
                        reaction.Value,
                        cancellationToken).ConfigureAwait(false);
                    return existing is null
                        ? new(
                            null,
                            null,
                            null,
                            null,
                            EmojiReactionRelation.Create(parsed.ActorIri, reactedObject, parsed.Id, reaction, activity.ReceivedAt))
                        : default;
                }
            case "Announce":
                {
                    string announcedObject = objectIri ?? throw new ActivityStreamsProtocolException("Announce requires an object IRI.");
                    AnnounceRelation? existing = await objectRepository.FindActiveAnnounceAsync(
                        parsed.ActorIri,
                        announcedObject,
                        cancellationToken).ConfigureAwait(false);
                    return existing is null
                        ? new(null, null, AnnounceRelation.Create(parsed.ActorIri, announcedObject, parsed.Id, activity.ReceivedAt), null)
                        : default;
                }
            case "Move":
                {
                    string sourceActor = objectIri ?? throw new ActivityStreamsProtocolException("Move requires its actor as object.");
                    if (!string.Equals(sourceActor, parsed.ActorIri, StringComparison.Ordinal))
                    {
                        throw new ActivityStreamsProtocolException("Move object must match its actor.");
                    }

                    string targetActor = ReadRequiredIriProperty(parsed.Root, "target", "Move");
                    await moveValidator.ValidateAsync(sourceActor, targetActor, cancellationToken).ConfigureAwait(false);
                    return new(null, null, null, ActorMove.Create(sourceActor, targetActor, parsed.Id, activity.ReceivedAt));
                }
            case "Block":
                {
                    string targetActor = objectIri ?? throw new ActivityStreamsProtocolException("Block requires an actor IRI.");
                    UserBlock? existing = await objectRepository.FindActiveBlockAsync(
                        parsed.ActorIri,
                        targetActor,
                        cancellationToken).ConfigureAwait(false);
                    return existing is null
                        ? new(null, null, null, null, null, UserBlock.Create(
                            parsed.ActorIri,
                            targetActor,
                            parsed.Id,
                            activity.ReceivedAt))
                        : default;
                }
            case "Undo":
                return await UndoActivityAggregateAsync(parsed, activity, cancellationToken).ConfigureAwait(false);
            default:
                return default;
        }
    }

    private async Task<ActivityAggregateMutation> UndoActivityAggregateAsync(
        ActivityStreamsDocument parsed,
        ActivityRecord activity,
        CancellationToken cancellationToken)
    {
        string nestedId = parsed.ObjectIri ?? throw new ActivityStreamsProtocolException("Undo requires an activity IRI.");
        CollectionMembership? membership = await objectRepository
            .FindCollectionMembershipByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (membership is not null)
        {
            if (string.Equals(membership.RemoveActivityIri, nestedId, StringComparison.Ordinal))
            {
                membership.UndoRemove(parsed.ActorIri, activity.ReceivedAt);
            }
            else
            {
                membership.UndoAdd(parsed.ActorIri, activity.ReceivedAt);
            }

            return new(membership, null, null, null);
        }

        LikeRelation? like = await objectRepository.FindLikeByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (like is not null)
        {
            like.Undo(parsed.ActorIri, activity.ReceivedAt);
            return new(null, like, null, null);
        }

        EmojiReactionRelation? emojiReaction = await objectRepository
            .FindEmojiReactionByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (emojiReaction is not null)
        {
            emojiReaction.Undo(parsed.ActorIri, activity.ReceivedAt);
            return new(null, null, null, null, emojiReaction);
        }

        AnnounceRelation? announce = await objectRepository.FindAnnounceByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (announce is not null)
        {
            announce.Undo(parsed.ActorIri, activity.ReceivedAt);
            return new(null, null, announce, null);
        }

        ActorMove? move = await objectRepository.FindMoveByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (move is not null)
        {
            move.Undo(parsed.ActorIri, activity.ReceivedAt);
            return new(null, null, null, move);
        }

        UserBlock? block = await objectRepository.FindBlockByActivityAsync(nestedId, cancellationToken).ConfigureAwait(false);
        if (block is not null)
        {
            block.Undo(parsed.ActorIri, parsed.Id, activity.ReceivedAt);
            return new(null, null, null, null, null, block);
        }

        return default;
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

    private readonly record struct ActivityAggregateMutation(
        CollectionMembership? CollectionMembership,
        LikeRelation? LikeRelation,
        AnnounceRelation? AnnounceRelation,
        ActorMove? ActorMove,
        EmojiReactionRelation? EmojiReactionRelation = null,
        UserBlock? UserBlock = null);

    private static (Delivery[] Deliveries, DeliveryTarget[] Targets) CreateDeliveries(
        IReadOnlyList<RemoteActorEndpoint> endpoints,
        ActivityRecord activity,
        string actorIri,
        byte[] payload,
        DateTimeOffset now)
    {
        var deliveries = new List<Delivery>();
        var targets = new List<DeliveryTarget>();
        bool mayUseSharedInbox = activity.Visibility is Visibility.Public or Visibility.Unlisted;
        foreach (IGrouping<string, RemoteActorEndpoint> group in endpoints.GroupBy(
                     endpoint => mayUseSharedInbox ? endpoint.SharedInboxIri ?? endpoint.InboxIri : endpoint.InboxIri,
                     StringComparer.Ordinal))
        {
            Delivery delivery = Delivery.Create(
                activity.Id,
                activity.Iri,
                group.Key,
                actorIri,
                payload,
                SignatureProfile.LegacyCavage,
                now);
            deliveries.Add(delivery);
            targets.AddRange(group.Select(endpoint => DeliveryTarget.Create(delivery.Id, endpoint.ActorIri)));
        }

        return (deliveries.ToArray(), targets.ToArray());
    }

    private static IReadOnlyList<string> ReadTypes(JsonObject node)
    {
        using JsonDocument document = JsonDocument.Parse(node.ToJsonString());
        return ActivityStreamsParser.ReadTypes(document.RootElement);
    }

    private static void CopyAudience(JsonObject source, JsonObject target)
    {
        foreach (string property in AudienceProperties)
        {
            if (source[property] is { } value)
            {
                target[property] = value.DeepClone();
            }
        }
    }

    private static string SerializeForExternalStorage(JsonObject value)
    {
        using JsonDocument document = JsonDocument.Parse(value.ToJsonString());
        return Encoding.UTF8.GetString(ActivityStreamsSerializer.StripBlindRecipientsAndSerialize(document.RootElement));
    }

    private static string ValidateIdempotencyKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length is < 8 or > 200 || value.Any(char.IsControl))
        {
            throw new ActivityStreamsProtocolException("Idempotency-Key must contain 8 to 200 non-control characters.");
        }

        return value;
    }

    private MediaAttachment[] ExtractMediaAttachments(JsonObject embeddedObject, Guid objectId)
    {
        if (embeddedObject["attachment"] is not { } attachment)
        {
            return [];
        }

        var mediaIds = new HashSet<Guid>();
        CollectLocalMediaIds(attachment, mediaIds);
        return mediaIds.Select(mediaId => MediaAttachment.Create(mediaId, objectId)).ToArray();
    }

    private void CollectLocalMediaIds(JsonNode node, ISet<Guid> mediaIds)
    {
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item is not null)
                {
                    CollectLocalMediaIds(item, mediaIds);
                }
            }

            return;
        }

        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            TryAddLocalMediaId(text, mediaIds);
            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        foreach (string property in AttachmentIriProperties)
        {
            if (obj[property] is { } candidate)
            {
                CollectLocalMediaIds(candidate, mediaIds);
            }
        }
    }

    private void TryAddLocalMediaId(string? candidate, ISet<Guid> mediaIds)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, options.PublicBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.IdnHost, options.PublicBaseUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != options.PublicBaseUri.Port || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return;
        }

        string basePath = options.PublicBaseUri.AbsolutePath.TrimEnd('/');
        string prefix = basePath + "/media/";
        if (uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParse(uri.AbsolutePath[prefix.Length..], out Guid mediaId))
        {
            mediaIds.Add(mediaId);
        }
    }
}
