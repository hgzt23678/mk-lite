namespace ActivityPub.Misskey.Blazor.Presentation;

public enum ComposerPollExpiration
{
    Infinite,
    At,
    After
}

public enum ComposerPollUnit
{
    Second,
    Minute,
    Hour,
    Day
}

public sealed class ComposerPollViewModel
{
    public List<string> Choices { get; init; } = [string.Empty, string.Empty];

    public bool Multiple { get; set; }

    public ComposerPollExpiration Expiration { get; set; }

    public DateOnly AtDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    public TimeOnly AtTime { get; set; }

    public int After { get; set; }

    public ComposerPollUnit Unit { get; set; }

    public bool HasEnoughChoices => Choices.Count(choice => !string.IsNullOrWhiteSpace(choice)) >= 2;
}
