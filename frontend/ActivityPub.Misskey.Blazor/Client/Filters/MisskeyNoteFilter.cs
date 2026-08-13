namespace ActivityPub.Misskey.Blazor.Client.Filters;

public static class MisskeyNoteFilter
{
    public static string Page(string noteId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noteId);
        if (noteId.Length > 256 || noteId.Any(char.IsControl) || noteId.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("The Misskey note ID is invalid.", nameof(noteId));
        }

        return $"notes/{noteId}";
    }
}
