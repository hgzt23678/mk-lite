namespace ActivityPub.Federation.Protocol;

public static class ActivityStreamsConstants
{
    public const string ActivityStreamsContext = "https://www.w3.org/ns/activitystreams";
    public const string PublicAudience = "https://www.w3.org/ns/activitystreams#Public";
    public const string ActivityJson = "application/activity+json";
    public const string ActivityStreamsJsonLd = "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"";

    public static IReadOnlySet<string> SupportedActivities { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Accept", "Add", "Announce", "Block", "Create", "Delete", "Flag", "Follow", "Like", "EmojiReaction", "EmojiReact",
        "Move", "Reject", "Remove", "Undo", "Update"
    };

    public static IReadOnlySet<string> SupportedObjects { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Person", "Service", "Application", "Group", "Note", "Article", "Page", "Question", "Tombstone",
        "Document", "Image", "Audio", "Video", "Mention", "Hashtag", "Emoji", "PropertyValue"
    };
}
