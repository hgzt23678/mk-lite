namespace ActivityPub.Application;

public sealed class MisskeyAuthenticationOptions
{
    public const string SectionName = "MisskeyAuthentication";

    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromDays(90);

    public void Validate()
    {
        if (SessionLifetime < TimeSpan.FromMinutes(1) || SessionLifetime > TimeSpan.FromHours(1) ||
            AccessTokenLifetime < TimeSpan.FromHours(1) || AccessTokenLifetime > TimeSpan.FromDays(365))
        {
            throw new InvalidOperationException("Misskey authentication lifetimes are outside the supported range.");
        }
    }
}
