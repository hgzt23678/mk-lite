using ActivityPub.Domain;

namespace ActivityPub.Domain.Tests;

public sealed class QuestionPollTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid PollId = Guid.Parse("77136f72-37f1-448a-bddd-c374aa5ec209");

    [Fact]
    public void SingleChoiceVoteUsesOneBallotKeyAcrossEveryChoice()
    {
        QuestionPoll poll = QuestionPoll.Create(PollId, multiple: false, Now.AddHours(1), 0, Now);

        PollVote first = poll.CastVote(
            "https://local.example/users/alice",
            0,
            new HashSet<int> { 0, 1 },
            "https://local.example/activities/vote-1",
            Now);
        PollVote second = poll.CastVote(
            "https://local.example/users/alice",
            1,
            new HashSet<int> { 0, 1 },
            "https://local.example/activities/vote-2",
            Now);

        Assert.Equal(-1, first.BallotKey);
        Assert.Equal(first.BallotKey, second.BallotKey);
    }

    [Fact]
    public void MultipleChoiceVoteUsesChoiceAsBallotKey()
    {
        QuestionPoll poll = QuestionPoll.Create(PollId, multiple: true, Now.AddHours(1), 0, Now);

        PollVote first = poll.CastVote(
            "https://local.example/users/alice",
            0,
            new HashSet<int> { 0, 1 },
            "https://local.example/activities/vote-1",
            Now);
        PollVote second = poll.CastVote(
            "https://local.example/users/alice",
            1,
            new HashSet<int> { 0, 1 },
            "https://local.example/activities/vote-2",
            Now);

        Assert.Equal(0, first.BallotKey);
        Assert.Equal(1, second.BallotKey);
    }

    [Fact]
    public void ExpiredPollRejectsVoteAtTheExactExpirationInstant()
    {
        QuestionPoll poll = QuestionPoll.Create(PollId, multiple: false, Now, 0, Now.AddMinutes(-1));

        DomainException exception = Assert.Throws<DomainException>(() => poll.CastVote(
            "https://local.example/users/alice",
            0,
            new HashSet<int> { 0, 1 },
            "https://local.example/activities/vote-expired",
            Now));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownChoiceIsRejectedBeforeARecordCanBePersisted()
    {
        QuestionPoll poll = QuestionPoll.Create(PollId, multiple: true, Now.AddHours(1), 0, Now);

        DomainException exception = Assert.Throws<DomainException>(() => poll.CastVote(
            "https://local.example/users/alice",
            2,
            new HashSet<int> { 0, 1 },
            "https://local.example/activities/vote-invalid",
            Now));

        Assert.Contains("choice", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PollOptionRejectsInvalidPersistedCountsAndIndexes()
    {
        Assert.Throws<DomainException>(() => PollOption.Create(PollId, -1, "invalid"));
        Assert.Throws<DomainException>(() => PollOption.Create(PollId, 0, "invalid", -1));
    }
}
