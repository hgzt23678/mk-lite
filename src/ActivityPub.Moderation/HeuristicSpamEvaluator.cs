using System.Text.Json;
using ActivityPub.Application;

namespace ActivityPub.Moderation;

public sealed class SpamEvaluationOptions
{
    public int QuarantineScore { get; init; } = 100;
    public int MaximumLinks { get; init; } = 40;
    public int MaximumMentions { get; init; } = 100;
    public int MaximumHashtags { get; init; } = 100;

    public void Validate()
    {
        if (QuarantineScore < 1 || MaximumLinks < 1 || MaximumMentions < 1 || MaximumHashtags < 1)
        {
            throw new InvalidOperationException("Spam evaluation thresholds must be positive.");
        }
    }
}

public sealed class HeuristicSpamEvaluator(SpamEvaluationOptions options) : IInboundSpamEvaluator
{
    public ValueTask<SpamAssessment> EvaluateAsync(
        string actorIri,
        string activityType,
        ReadOnlyMemory<byte> rawJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(actorIri);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityType);
        using JsonDocument document = JsonDocument.Parse(rawJson, new JsonDocumentOptions { MaxDepth = 64 });
        var counts = new ContentCounts();
        Count(document.RootElement, counts);
        int score = 0;
        var reasons = new List<string>();
        AddExcess(counts.Links, options.MaximumLinks, "links", ref score, reasons);
        AddExcess(counts.Mentions, options.MaximumMentions, "mentions", ref score, reasons);
        AddExcess(counts.Hashtags, options.MaximumHashtags, "hashtags", ref score, reasons);
        if (counts.MaximumRepeatedCharacterRun > 256)
        {
            score += 50;
            reasons.Add("excessive repeated characters");
        }

        SpamDisposition disposition = score >= options.QuarantineScore ? SpamDisposition.Quarantine : SpamDisposition.Allow;
        string reason = reasons.Count == 0 ? "No spam threshold exceeded." : string.Join(", ", reasons);
        return ValueTask.FromResult(new SpamAssessment(disposition, reason, score));
    }

    private static void Count(JsonElement value, ContentCounts counts)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            string? type = value.TryGetProperty("type", out JsonElement typeValue) && typeValue.ValueKind == JsonValueKind.String
                ? typeValue.GetString()
                : null;
            if (type == "Mention")
            {
                counts.Mentions++;
            }
            else if (type == "Hashtag")
            {
                counts.Hashtags++;
            }

            foreach (JsonProperty property in value.EnumerateObject())
            {
                Count(property.Value, counts);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                Count(item, counts);
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString()!;
            counts.Links += CountOccurrences(text, "https://") + CountOccurrences(text, "http://");
            counts.MaximumRepeatedCharacterRun = Math.Max(counts.MaximumRepeatedCharacterRun, MaximumRun(text));
        }
    }

    private static void AddExcess(int count, int maximum, string label, ref int score, List<string> reasons)
    {
        if (count <= maximum)
        {
            return;
        }

        score += 100 + Math.Min(100, count - maximum);
        reasons.Add($"{label}={count}");
    }

    private static int CountOccurrences(string value, string needle)
    {
        int count = 0;
        int position = 0;
        while ((position = value.IndexOf(needle, position, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            position += needle.Length;
        }

        return count;
    }

    private static int MaximumRun(string value)
    {
        int maximum = 0;
        int current = 0;
        char previous = '\0';
        foreach (char character in value)
        {
            current = character == previous ? current + 1 : 1;
            previous = character;
            maximum = Math.Max(maximum, current);
        }

        return maximum;
    }

    private sealed class ContentCounts
    {
        public int Links { get; set; }
        public int Mentions { get; set; }
        public int Hashtags { get; set; }
        public int MaximumRepeatedCharacterRun { get; set; }
    }
}
