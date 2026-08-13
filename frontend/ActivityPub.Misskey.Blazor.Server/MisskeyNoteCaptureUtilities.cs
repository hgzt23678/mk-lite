using System.Collections.ObjectModel;
using System.Text.Json;
using ActivityPub.Application;

namespace ActivityPub.Misskey.Blazor.Client;

public static class MisskeyNoteCaptureUtilities
{
    public static ClientPostView ApplyStreamUpdate(ClientPostView note, string type, JsonElement body, Guid? viewerId)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (body.ValueKind != JsonValueKind.Object)
        {
            return note;
        }

        return type switch
        {
            "reacted" => ApplyReaction(note, body, viewerId, +1),
            "unreacted" => ApplyReaction(note, body, viewerId, -1),
            "pollVoted" => ApplyPollVote(note, body, viewerId),
            _ => note
        };
    }

    public static bool IsDeletedEvent(string type) =>
        string.Equals(type, "deleted", StringComparison.Ordinal);

    private static ClientPostView ApplyReaction(ClientPostView note, JsonElement body, Guid? viewerId, int delta)
    {
        string reaction = body.TryGetProperty("reaction", out JsonElement value) &&
                          value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
        if (reaction.Length == 0)
        {
            return note;
        }

        Dictionary<string, long> counts = note.Emojis.ToDictionary(
            emoji => emoji.Shortcode,
            _ => 0L,
            StringComparer.Ordinal);
        _ = counts;
        _ = viewerId;
        _ = delta;
        return note;
    }

    private static ClientPostView ApplyPollVote(ClientPostView note, JsonElement body, Guid? viewerId)
    {
        if (note.Poll is null ||
            !body.TryGetProperty("choice", out JsonElement choice) ||
            choice.ValueKind != JsonValueKind.Number ||
            !choice.TryGetInt32(out int index) ||
            index < 0 ||
            index >= note.Poll.Options.Count)
        {
            return note;
        }

        List<ClientPollOptionView> options = note.Poll.Options.ToList();
        ClientPollOptionView selected = options[index];
        options[index] = selected with { VotesCount = selected.VotesCount + 1 };
        ClientPollView poll = note.Poll with
        {
            Options = new ReadOnlyCollection<ClientPollOptionView>(options),
            VotesCount = note.Poll.VotesCount + 1
        };
        _ = viewerId;
        return note with { Poll = poll };
    }
}
