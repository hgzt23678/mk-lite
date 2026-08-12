using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class StreamEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 13, 45, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Create", StreamEventKind.PostCreated)]
    [InlineData("Update", StreamEventKind.PostUpdated)]
    [InlineData("Delete", StreamEventKind.PostDeleted)]
    public void ObjectMutationCreatesContentFreeDurableEvent(string activityType, StreamEventKind expectedKind)
    {
        string objectIri = "https://local.example/objects/1";
        string actorIri = "https://local.example/users/alice";
        FederatedObject item = FederatedObject.Create(
            objectIri,
            actorIri,
            "Note",
            Visibility.FollowersOnly,
            "{\"type\":\"Note\",\"content\":\"must not enter the stream log\"}",
            new string('a', 64),
            Now,
            Now);
        ActivityRecord activity = ActivityRecord.Create(
            $"https://local.example/activities/{activityType.ToLowerInvariant()}",
            actorIri,
            activityType,
            objectIri,
            ActivityDirection.Outbound,
            Visibility.FollowersOnly,
            "{\"content\":\"must not enter the stream log\"}",
            new string('b', 64),
            false,
            Now,
            Now);

        StreamEvent result = Assert.IsType<StreamEvent>(StreamEvent.FromObjectMutation(activity, item, isLocal: true));

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(item.Id, result.ResourceId);
        Assert.Equal(item.Iri, result.ResourceIri);
        Assert.Equal(Visibility.FollowersOnly, result.Visibility);
        Assert.DoesNotContain("content", result.DeduplicationKey, StringComparison.Ordinal);
    }

    [Fact]
    public void NonObjectActivityDoesNotCreatePostEvent()
    {
        ActivityRecord activity = ActivityRecord.Create(
            "https://local.example/activities/like",
            "https://local.example/users/alice",
            "Like",
            "https://local.example/objects/1",
            ActivityDirection.Outbound,
            Visibility.Public,
            "{\"type\":\"Like\"}",
            new string('c', 64),
            false,
            Now,
            Now);

        Assert.Null(StreamEvent.FromObjectMutation(activity, null, isLocal: true));
    }

    [Fact]
    public void PollVoteCreatesContentFreeQuestionEventWithChoiceIndex()
    {
        FederatedObject question = FederatedObject.Create(
            "https://local.example/objects/question",
            "https://remote.example/users/bob",
            "Question",
            Visibility.FollowersOnly,
            "{\"type\":\"Question\",\"content\":\"must not enter the stream log\"}",
            new string('d', 64),
            Now,
            Now);
        ActivityRecord activity = ActivityRecord.Create(
            "https://local.example/activities/poll-vote",
            "https://local.example/users/alice",
            "Create",
            null,
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            "{\"type\":\"Create\",\"content\":\"must not enter the stream log\"}",
            new string('e', 64),
            false,
            Now,
            Now);

        StreamEvent result = StreamEvent.FromPollVote(activity, question, 1, isLocal: false);

        Assert.Equal(StreamEventKind.PollVoted, result.Kind);
        Assert.Equal(question.Id, result.ResourceId);
        Assert.Equal(question.Iri, result.ResourceIri);
        Assert.Equal(1, result.PollChoiceIndex);
        Assert.Equal(question.Visibility, result.Visibility);
        Assert.DoesNotContain("content", result.DeduplicationKey, StringComparison.Ordinal);
    }

    [Fact]
    public void RelationshipMutationIsRecipientScopedAndContainsNoProfilePayload()
    {
        const string follower = "https://local.example/users/alice";
        const string followed = "https://remote.example/users/bob";
        FollowRelation relationship = FollowRelation.Request(
            follower,
            followed,
            "https://local.example/activities/follow-bob",
            Now);
        ActivityRecord activity = ActivityRecord.Create(
            "https://local.example/activities/follow-bob",
            follower,
            "Follow",
            followed,
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            "{\"type\":\"Follow\",\"privateProfile\":\"must not enter the stream log\"}",
            new string('f', 64),
            false,
            Now,
            Now);
        Guid targetActorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        StreamEvent result = StreamEvent.FromRelationshipMutation(
            activity,
            relationship,
            targetActorId,
            follower,
            isLocal: true);

        Assert.Equal(StreamEventKind.RelationshipChanged, result.Kind);
        Assert.Equal(targetActorId, result.ResourceId);
        Assert.Equal(followed, result.ResourceIri);
        Assert.Equal(follower, result.RecipientActorIri);
        Assert.Equal(Visibility.MentionedOnly, result.Visibility);
        Assert.DoesNotContain("privateProfile", result.DeduplicationKey, StringComparison.Ordinal);
    }
}
