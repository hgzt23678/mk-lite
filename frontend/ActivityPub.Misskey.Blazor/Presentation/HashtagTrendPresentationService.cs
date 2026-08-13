#if MISSKEY_BLAZOR_SERVER
using ActivityPub.Application;
#endif

namespace ActivityPub.Misskey.Blazor.Presentation;

#if !MISSKEY_BLAZOR_SERVER
public sealed record HashtagTrendViewModel(string Tag, long UsersCount, IReadOnlyList<long> Chart);

public interface IHashtagTrendPresentationService
{
    Task<IReadOnlyList<HashtagTrendViewModel>> ReadAsync(CancellationToken cancellationToken);
}

#endif

#if MISSKEY_BLAZOR_SERVER
public sealed class HashtagTrendPresentationService(IHashtagRepository hashtags)
    : IHashtagTrendPresentationService
{
    public async Task<IReadOnlyList<HashtagTrendViewModel>> ReadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<HashtagTrend> trends = await hashtags.TrendAsync(
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return trends.Select(trend => new HashtagTrendViewModel(
            trend.Tag,
            trend.UsersCount,
            trend.Chart)).ToArray();
    }
}
#endif
