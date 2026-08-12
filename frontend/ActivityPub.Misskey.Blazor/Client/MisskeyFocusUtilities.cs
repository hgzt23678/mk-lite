namespace ActivityPub.Misskey.Blazor.Client;

public sealed record MisskeyFocusableNode(bool HasTabIndex, string Id);

public static class MisskeyFocusUtilities
{
    public static MisskeyFocusableNode? Previous(
        IReadOnlyList<MisskeyFocusableNode> siblings,
        int currentIndex,
        bool self = false)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        int index = self ? currentIndex : currentIndex - 1;
        for (; index >= 0; index--)
        {
            if (siblings[index].HasTabIndex)
            {
                return siblings[index];
            }
        }

        return null;
    }

    public static MisskeyFocusableNode? Next(
        IReadOnlyList<MisskeyFocusableNode> siblings,
        int currentIndex,
        bool self = false)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        int index = self ? currentIndex : currentIndex + 1;
        for (; index < siblings.Count; index++)
        {
            if (siblings[index].HasTabIndex)
            {
                return siblings[index];
            }
        }

        return null;
    }
}
