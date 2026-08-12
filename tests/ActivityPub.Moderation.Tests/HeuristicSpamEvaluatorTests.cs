using System.Text;
using ActivityPub.Application;
using ActivityPub.Moderation;

namespace ActivityPub.Moderation.Tests;

public sealed class HeuristicSpamEvaluatorTests
{
    [Fact]
    public async Task OrdinaryActivityIsAllowed()
    {
        var evaluator = new HeuristicSpamEvaluator(new SpamEvaluationOptions());

        SpamAssessment result = await evaluator.EvaluateAsync(
            "https://remote.example/users/alice",
            "Create",
            Encoding.UTF8.GetBytes("{\"object\":{\"type\":\"Note\",\"content\":\"hello https://example.org\"}}"),
            CancellationToken.None);

        Assert.Equal(SpamDisposition.Allow, result.Disposition);
    }

    [Fact]
    public async Task LinkFloodIsQuarantinedWithoutAutomaticActorBlock()
    {
        var evaluator = new HeuristicSpamEvaluator(new SpamEvaluationOptions { MaximumLinks = 2 });
        string links = string.Join(' ', Enumerable.Repeat("https://spam.example/x", 5));

        SpamAssessment result = await evaluator.EvaluateAsync(
            "https://remote.example/users/spammer",
            "Create",
            Encoding.UTF8.GetBytes($"{{\"object\":{{\"type\":\"Note\",\"content\":\"{links}\"}}}}"),
            CancellationToken.None);

        Assert.Equal(SpamDisposition.Quarantine, result.Disposition);
        Assert.Contains("links=5", result.Reason, StringComparison.Ordinal);
    }
}
