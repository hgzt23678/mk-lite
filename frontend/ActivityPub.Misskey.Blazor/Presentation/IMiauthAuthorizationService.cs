namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IMiauthAuthorizationService
{
    Task AuthorizeAsync(
        string username,
        string session,
        string name,
        Uri? iconUri,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken);
}
public static class MisskeyFrontendPermissions
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "read:account", "write:account", "read:blocks", "write:blocks", "read:drive", "write:drive",
        "read:favorites", "write:favorites", "read:following", "write:following", "read:messaging",
        "write:messaging", "read:mutes", "write:mutes", "write:notes", "read:notifications",
        "write:notifications", "read:reactions", "write:reactions", "write:votes", "read:pages",
        "write:pages", "write:page-likes", "read:page-likes", "read:user-groups", "write:user-groups",
        "read:channels", "write:channels", "read:gallery", "write:gallery", "read:gallery-likes",
        "write:gallery-likes"
    };
}
