using System.Collections.Immutable;

namespace ActivityPub.Application;

public static class MisskeyPermissions
{
    public static ImmutableHashSet<string> All { get; } = new[]
    {
        "read:account", "write:account", "read:blocks", "write:blocks", "read:drive", "write:drive",
        "read:favorites", "write:favorites", "read:following", "write:following", "read:messaging",
        "write:messaging", "read:mutes", "write:mutes", "write:notes", "read:notifications",
        "write:notifications", "read:reactions", "write:reactions", "write:votes", "read:pages",
        "write:pages", "write:page-likes", "read:page-likes", "read:user-groups", "write:user-groups",
        "read:channels", "write:channels", "read:gallery", "write:gallery", "read:gallery-likes",
        "write:gallery-likes"
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public static bool GrantsRead(IReadOnlyCollection<string> permissions) =>
        permissions.Any(permission => permission.StartsWith("read:", StringComparison.Ordinal) ||
                                      permission.StartsWith("write:", StringComparison.Ordinal));

    public static bool GrantsWrite(IReadOnlyCollection<string> permissions) =>
        permissions.Any(permission => permission.StartsWith("write:", StringComparison.Ordinal));
}
