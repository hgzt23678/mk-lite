namespace ActivityPub.Domain;

public sealed class QuestionPoll : Entity
{
    private QuestionPoll()
    {
    }

    private QuestionPoll(
        Guid questionObjectId,
        bool multiple,
        DateTimeOffset? expiresAt,
        long baselineVotersCount,
        DateTimeOffset createdAt)
        : base(questionObjectId)
    {
        if (baselineVotersCount < 0)
        {
            throw new DomainException("A poll baseline voter count cannot be negative.");
        }

        QuestionObjectId = questionObjectId;
        Multiple = multiple;
        ExpiresAt = expiresAt;
        BaselineVotersCount = baselineVotersCount;
        CreatedAt = createdAt;
    }

    public Guid QuestionObjectId { get; private set; }
    public bool Multiple { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public long BaselineVotersCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is not null && ExpiresAt <= now;

    public PollVote CastVote(
        string voterActorIri,
        int choiceIndex,
        IReadOnlySet<int> availableChoices,
        string activityIri,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(availableChoices);
        if (IsExpired(now))
        {
            throw new DomainException("The poll is already expired.");
        }

        if (!availableChoices.Contains(choiceIndex))
        {
            throw new DomainException("The poll choice is invalid.");
        }

        return PollVote.Create(
            QuestionObjectId,
            voterActorIri,
            choiceIndex,
            Multiple ? choiceIndex : -1,
            activityIri,
            now);
    }

    public static QuestionPoll Create(
        Guid questionObjectId,
        bool multiple,
        DateTimeOffset? expiresAt,
        long baselineVotersCount,
        DateTimeOffset createdAt) =>
        new(questionObjectId, multiple, expiresAt, baselineVotersCount, createdAt);
}

public sealed class PollOption : Entity
{
    private PollOption()
    {
    }

    private PollOption(Guid id, Guid pollId, int choiceIndex, string title, long baselineVotesCount)
        : base(id)
    {
        if (pollId == Guid.Empty)
        {
            throw new DomainException("A poll option requires a poll identifier.");
        }

        if (choiceIndex is < 0 or > 99)
        {
            throw new DomainException("A poll choice index is outside the supported range.");
        }

        if (baselineVotesCount < 0)
        {
            throw new DomainException("A poll option baseline vote count cannot be negative.");
        }

        PollId = pollId;
        ChoiceIndex = choiceIndex;
        Title = DomainText.Required(title, nameof(title), 100);
        BaselineVotesCount = baselineVotesCount;
    }

    public Guid PollId { get; private set; }
    public int ChoiceIndex { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public long BaselineVotesCount { get; private set; }

    public static PollOption Create(Guid pollId, int choiceIndex, string title, long baselineVotesCount = 0) =>
        new(Guid.NewGuid(), pollId, choiceIndex, title, baselineVotesCount);
}

public sealed class PollVote : Entity
{
    private PollVote()
    {
    }

    private PollVote(
        Guid id,
        Guid pollId,
        string voterActorIri,
        int choiceIndex,
        int ballotKey,
        string activityIri,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (pollId == Guid.Empty)
        {
            throw new DomainException("A poll vote requires a poll identifier.");
        }

        if (choiceIndex is < 0 or > 99 || ballotKey is < -1 or > 99)
        {
            throw new DomainException("A poll vote choice is outside the supported range.");
        }

        PollId = pollId;
        VoterActorIri = DomainText.RequiredIri(voterActorIri, nameof(voterActorIri));
        ChoiceIndex = choiceIndex;
        BallotKey = ballotKey;
        ActivityIri = DomainText.RequiredIri(activityIri, nameof(activityIri));
        CreatedAt = createdAt;
    }

    public Guid PollId { get; private set; }
    public string VoterActorIri { get; private set; } = string.Empty;
    public int ChoiceIndex { get; private set; }
    public int BallotKey { get; private set; }
    public string ActivityIri { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    internal static PollVote Create(
        Guid pollId,
        string voterActorIri,
        int choiceIndex,
        int ballotKey,
        string activityIri,
        DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), pollId, voterActorIri, choiceIndex, ballotKey, activityIri, createdAt);
}
