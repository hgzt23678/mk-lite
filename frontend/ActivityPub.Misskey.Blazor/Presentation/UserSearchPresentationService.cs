using ActivityPub.Application;
using ActivityPub.Domain;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IUserSearchPresentationService
{
    Task<IReadOnlyList<UserPreviewViewModel>> SearchAsync(
        string query,
        string origin,
        int limit,
        CancellationToken cancellationToken);
}

public sealed class UserSearchPresentationService(
    IClientApiQueryService clientQuery,
    IExternalEntityIdService externalIds,
    IUserPreviewPresentationService previews,
    MisskeyFrontendRuntimeConfiguration runtime) : IUserSearchPresentationService
{
    public async Task<IReadOnlyList<UserPreviewViewModel>> SearchAsync(
        string query,
        string origin,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Length > 2_048 || query.Any(char.IsControl))
        {
            throw new UserPreviewPresentationException("USER_SEARCH_QUERY_INVALID");
        }

        string normalizedOrigin = string.IsNullOrWhiteSpace(origin)
            ? "combined"
            : origin.Trim().ToLowerInvariant();
        if (normalizedOrigin is not ("combined" or "local" or "remote"))
        {
            throw new UserPreviewPresentationException("USER_SEARCH_ORIGIN_INVALID");
        }

        int safeLimit = Math.Clamp(limit, 1, 100);
        Uri publicBaseUri = runtime.PublicBaseUri
            ?? throw new UserPreviewPresentationException("USER_SEARCH_PUBLIC_BASE_URI_MISSING");
        IReadOnlyList<ClientAccountView> accounts = await clientQuery.SearchAccountsByUsernameAsync(
            query.Trim().TrimStart('@'),
            null,
            safeLimit,
            publicBaseUri.IdnHost,
            cancellationToken).ConfigureAwait(false);

        var result = new List<UserPreviewViewModel>(accounts.Count);
        foreach (ClientAccountView account in accounts)
        {
            bool remote = account.Acct.Contains('@', StringComparison.Ordinal);
            if (normalizedOrigin == "local" && remote ||
                normalizedOrigin == "remote" && !remote)
            {
                continue;
            }

            string externalId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.Actor,
                account.Id,
                account.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            try
            {
                result.Add(await previews.ReadAsync(externalId, cancellationToken).ConfigureAwait(false));
            }
            catch (UserPreviewPresentationException exception) when (exception.ErrorCode == "USER_PREVIEW_NOT_FOUND")
            {
                // The search and preview projections share the same visibility boundary. A
                // row that disappears between the two reads is omitted rather than exposed
                // with an incomplete or fabricated card.
            }
        }

        return result;
    }
}

public sealed class UserSearchPaginationSource(
    IUserSearchPresentationService search,
    string query,
    string origin) : IMisskeyPaginationSource<UserPreviewViewModel>
{
    public MisskeyPaginationOptions Options { get; } = new(10, NoPaging: true);

    public ValueTask<IReadOnlyList<UserPreviewViewModel>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken) =>
        new(search.SearchAsync(query, origin, Options.EffectiveLimit, cancellationToken));

    public string GetId(UserPreviewViewModel item) => item.Id;
}
