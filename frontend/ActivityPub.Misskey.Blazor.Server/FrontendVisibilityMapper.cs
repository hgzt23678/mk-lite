using ActivityPub.Misskey.Blazor.Presentation;

namespace ActivityPub.Misskey.Blazor.Server;

internal static class FrontendVisibilityMapper
{
    public static ActivityPub.Domain.Visibility ToDomain(Visibility visibility) => visibility switch
    {
        Visibility.Public => ActivityPub.Domain.Visibility.Public,
        Visibility.Unlisted => ActivityPub.Domain.Visibility.Unlisted,
        Visibility.FollowersOnly => ActivityPub.Domain.Visibility.FollowersOnly,
        Visibility.MentionedOnly => ActivityPub.Domain.Visibility.MentionedOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
    };

    public static Visibility FromDomain(ActivityPub.Domain.Visibility visibility) => visibility switch
    {
        ActivityPub.Domain.Visibility.Public => Visibility.Public,
        ActivityPub.Domain.Visibility.Unlisted => Visibility.Unlisted,
        ActivityPub.Domain.Visibility.FollowersOnly => Visibility.FollowersOnly,
        ActivityPub.Domain.Visibility.MentionedOnly => Visibility.MentionedOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
    };
}
