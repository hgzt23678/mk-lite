namespace ActivityPub.Application;

public sealed class PublicIriFactory
{
    private readonly Uri _baseUri;

    public PublicIriFactory(FederationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _baseUri = new Uri(options.PublicBaseUri.AbsoluteUri.TrimEnd('/') + '/', UriKind.Absolute);
    }

    public string Actor(string username) => Build("users", Escape(username));
    public string ActorInbox(string username) => Build("users", Escape(username), "inbox");
    public string ActorOutbox(string username) => Build("users", Escape(username), "outbox");
    public string ActorFollowers(string username) => Build("users", Escape(username), "followers");
    public string ActorFollowing(string username) => Build("users", Escape(username), "following");
    public string ActorLiked(string username) => Build("users", Escape(username), "liked");
    public string ActorFeatured(string username) => Build("users", Escape(username), "featured");
    public string ObjectIri(Guid id) => Build("objects", id.ToString("D"));
    public string ActivityIri(Guid id) => Build("activities", id.ToString("D"));
    public string RelayFollow(Guid id) => Build("activities", "follow-relay", id.ToString("D"));
    public string CollectionIri(Guid id) => Build("collections", id.ToString("D"));
    public string MediaIri(Guid id) => Build("media", id.ToString("D"));
    public string Key(string username, Guid id) => Actor(username) + "#key-" + id.ToString("N");

    private string Build(params string[] segments) =>
        new Uri(_baseUri, string.Join('/', segments)).AbsoluteUri;

    private static string Escape(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        return Uri.EscapeDataString(segment);
    }
}
