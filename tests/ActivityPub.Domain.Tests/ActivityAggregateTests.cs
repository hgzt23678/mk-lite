using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class ActivityAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CollectionMembershipRequiresActorOwnedCollection()
    {
        Assert.Throws<DomainException>(() => CollectionMembership.Add(
            "https://origin.example/users/alice",
            "https://other.example/collections/featured",
            "https://content.example/objects/1",
            "https://origin.example/activities/add-1",
            Now));
    }

    [Fact]
    public void CollectionMembershipRemoveAndUndoPreserveAuthority()
    {
        CollectionMembership membership = CollectionMembership.Add(
            "https://origin.example/users/alice",
            "https://origin.example/users/alice/featured",
            "https://content.example/objects/1",
            "https://origin.example/activities/add-1",
            Now);

        membership.Remove(
            "https://origin.example/users/alice",
            "https://origin.example/activities/remove-1",
            Now.AddMinutes(1));
        Assert.Equal(FederatedRelationState.Reversed, membership.State);
        membership.UndoRemove("https://origin.example/users/alice", Now.AddMinutes(2));

        Assert.Equal(FederatedRelationState.Active, membership.State);
        Assert.Null(membership.RemoveActivityIri);
        Assert.Throws<DomainException>(() => membership.UndoAdd(
            "https://origin.example/users/mallory",
            Now.AddMinutes(3)));
    }

    [Fact]
    public void LikeAndAnnounceCanOnlyBeUndoneByTheirActor()
    {
        LikeRelation like = LikeRelation.Create(
            "https://origin.example/users/alice",
            "https://content.example/objects/1",
            "https://origin.example/activities/like-1",
            Now);
        AnnounceRelation announce = AnnounceRelation.Create(
            "https://origin.example/users/alice",
            "https://content.example/objects/1",
            "https://origin.example/activities/announce-1",
            Now);

        like.Undo("https://origin.example/users/alice", Now.AddMinutes(1));
        announce.Undo("https://origin.example/users/alice", Now.AddMinutes(1));

        Assert.Equal(FederatedRelationState.Reversed, like.State);
        Assert.Equal(FederatedRelationState.Reversed, announce.State);
        Assert.Throws<DomainException>(() => LikeRelation.Create(
            "https://origin.example/users/alice",
            "https://content.example/objects/1",
            "https://origin.example/activities/like-2",
            Now).Undo("https://origin.example/users/mallory", Now));
    }

    [Theory]
    [InlineData(null, "👍")]
    [InlineData("love", "❤")]
    [InlineData("❤️", "❤")]
    [InlineData("👩🏽‍💻", "👩🏽‍💻")]
    [InlineData(":party:", ":party@origin.example:")]
    [InlineData(":party@emoji.example:", ":party@emoji.example:")]
    public void ReactionValuesAreNormalizedLikeMisskeyV12(string? input, string expected)
    {
        FederatedReaction reaction = FederatedReaction.Create(input, "https://origin.example/users/alice");

        Assert.Equal(expected, reaction.Value);
    }

    [Fact]
    public void CustomEmojiMetadataIsBoundToShortcode()
    {
        FederatedReaction reaction = FederatedReaction.Create(
            ":party:",
            "https://origin.example/users/alice",
            "https://origin.example/emojis/party",
            ":party:",
            "https://cdn.example/party.png",
            "image/png");
        LikeRelation relation = LikeRelation.Create(
            "https://origin.example/users/alice",
            "https://content.example/objects/1",
            "https://origin.example/activities/like-emoji",
            reaction,
            Now);

        Assert.Equal(":party@origin.example:", relation.EffectiveReaction);
        Assert.Equal("https://cdn.example/party.png", relation.CustomEmojiUrl);
        Assert.Throws<DomainException>(() => FederatedReaction.Create(
            ":party:",
            "https://origin.example/users/alice",
            customEmojiName: ":different:"));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData(":bad emoji:")]
    [InlineData("ab")]
    public void InvalidReactionValuesAreRejected(string input)
    {
        Assert.Throws<DomainException>(() => FederatedReaction.Create(input, "https://origin.example/users/alice"));
    }

    [Fact]
    public void ActorCannotMoveToItself()
    {
        Assert.Throws<DomainException>(() => ActorMove.Create(
            "https://origin.example/users/alice",
            "https://origin.example/users/alice",
            "https://origin.example/activities/move-1",
            Now));
    }

    [Fact]
    public void FollowCanBeRenewedWithoutCreatingADuplicatePair()
    {
        FollowRelation follow = FollowRelation.Request(
            "https://origin.example/users/alice",
            "https://remote.example/users/bob",
            "https://origin.example/activities/follow-1",
            Now);
        follow.Accept(
            "https://remote.example/users/bob",
            "https://remote.example/activities/accept-1",
            Now.AddMinutes(1));

        follow.RequestAgain(
            "https://origin.example/users/alice",
            "https://origin.example/activities/follow-2",
            Now.AddMinutes(2));

        Assert.Equal(FollowState.Pending, follow.State);
        Assert.Equal("https://origin.example/activities/follow-2", follow.FollowActivityIri);
        Assert.Null(follow.DecisionActivityIri);
        Assert.Throws<DomainException>(() => follow.RequestAgain(
            "https://origin.example/users/mallory",
            "https://origin.example/activities/follow-3",
            Now.AddMinutes(3)));
    }

    [Fact]
    public void UserBlockCanOnlyBeUndoneByBlockingActorAndPreservesExactActivityIds()
    {
        UserBlock block = UserBlock.Create(
            "https://origin.example/users/alice",
            "https://remote.example/users/bob",
            "https://origin.example/activities/block-1",
            Now);

        Assert.Throws<DomainException>(() => block.Undo(
            "https://remote.example/users/bob",
            "https://remote.example/activities/undo-1",
            Now.AddMinutes(1)));
        block.Undo(
            "https://origin.example/users/alice",
            "https://origin.example/activities/undo-1",
            Now.AddMinutes(2));

        Assert.Equal(FederatedRelationState.Reversed, block.State);
        Assert.Equal("https://origin.example/activities/block-1", block.BlockActivityIri);
        Assert.Equal("https://origin.example/activities/undo-1", block.UndoActivityIri);
    }

    [Fact]
    public void EitherParticipantCanCancelFollowBecauseOfBlock()
    {
        FollowRelation follow = FollowRelation.Request(
            "https://origin.example/users/alice",
            "https://remote.example/users/bob",
            "https://origin.example/activities/follow-1",
            Now);
        follow.Accept(
            "https://remote.example/users/bob",
            "https://remote.example/activities/accept-1",
            Now.AddMinutes(1));

        follow.CancelBecauseBlocked("https://remote.example/users/bob", Now.AddMinutes(2));

        Assert.Equal(FollowState.Cancelled, follow.State);
        Assert.Throws<DomainException>(() => FollowRelation.Request(
            "https://origin.example/users/alice",
            "https://remote.example/users/bob",
            "https://origin.example/activities/follow-2",
            Now).CancelBecauseBlocked("https://other.example/users/mallory", Now));
    }
}
