#if MISSKEY_BLAZOR_SERVER
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
#endif

namespace ActivityPub.Misskey.Blazor.Presentation;

#if !MISSKEY_BLAZOR_SERVER
public sealed record SettingsProfileViewModel(
    string Username,
    string Name,
    string Description,
    bool IsLocked,
    bool Discoverable,
    string AvatarUrl,
    string HeaderUrl);

public sealed record SettingsApiTokenViewModel(
    string Id,
    string Name,
    string? Description,
    string? IconUri,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt);

public sealed record SettingsApiTokenIssuedViewModel(
    string Token,
    string Id,
    DateTimeOffset ExpiresAt);

public interface ISettingsPresentationService
{
    Task<SettingsProfileViewModel> ReadProfileAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SettingsApiTokenViewModel>> ReadApiTokensAsync(CancellationToken cancellationToken);

    Task<SettingsApiTokenIssuedViewModel> GenerateApiTokenAsync(
        string? name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken);

    Task<bool> RevokeApiTokenAsync(string externalId, CancellationToken cancellationToken);

    Task UpdateProfileAsync(
        string? name,
        string? description,
        bool? isLocked,
        bool? discoverable,
        CancellationToken cancellationToken);
}

#endif

#if MISSKEY_BLAZOR_SERVER
public sealed class SettingsPresentationService(
    IClientApiQueryService query,
    IProfileUpdateService profiles,
    IAuthenticatedActorContext actorContext,
    IMisskeyAuthenticationService misskeyAuthentication,
    IExternalEntityIdService externalIds) : ISettingsPresentationService
{
    public async Task<SettingsProfileViewModel> ReadProfileAsync(CancellationToken cancellationToken)
    {
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        ClientAccountView? account = await query.FindAccountByLookupAsync(
            actor.Username,
            "local.example",
            cancellationToken).ConfigureAwait(false);
        return new SettingsProfileViewModel(
            actor.Username,
            account?.DisplayName ?? string.Empty,
            account?.SummaryHtml ?? string.Empty,
            account?.Locked ?? false,
            account?.Discoverable ?? true,
            account?.AvatarUrl ?? string.Empty,
            account?.HeaderUrl ?? string.Empty);
    }

    public async Task<IReadOnlyList<SettingsApiTokenViewModel>> ReadApiTokensAsync(
        CancellationToken cancellationToken)
    {
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MisskeyTokenSummary> tokens = await misskeyAuthentication
            .ListAsync(actor.ActorIri, cancellationToken).ConfigureAwait(false);
        var result = new List<SettingsApiTokenViewModel>(tokens.Count);
        foreach (MisskeyTokenSummary token in tokens)
        {
            string externalId = await externalIds.GetOrCreateAsync(
                ApiDialect.Misskey,
                ExternalEntityType.AccessToken,
                token.Id,
                token.CreatedAt,
                cancellationToken).ConfigureAwait(false);
            result.Add(new(
                externalId,
                token.Name,
                token.Description,
                token.IconUri,
                token.Permissions,
                token.CreatedAt,
                token.ExpiresAt,
                token.LastUsedAt));
        }

        return result;
    }

    public async Task<SettingsApiTokenIssuedViewModel> GenerateApiTokenAsync(
        string? name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        MisskeyIssuedToken issued = await misskeyAuthentication.IssueDirectAsync(
            actor.Username,
            string.IsNullOrWhiteSpace(name) ? "Misskey v12 web client" : name.Trim(),
            "Generated from the Misskey API settings page.",
            iconUri: null,
            permissions,
            cancellationToken).ConfigureAwait(false);
        string externalId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.AccessToken,
            issued.TokenId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return new(issued.Token, externalId, issued.ExpiresAt);
    }

    public async Task<bool> RevokeApiTokenAsync(string externalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Length > 128)
        {
            return false;
        }

        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        Guid? tokenId = await externalIds.ResolveAsync(
            ApiDialect.Misskey,
            ExternalEntityType.AccessToken,
            externalId,
            cancellationToken).ConfigureAwait(false);
        return tokenId is Guid id && await misskeyAuthentication
            .RevokeAsync(actor.ActorIri, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateProfileAsync(
        string? name,
        string? description,
        bool? isLocked,
        bool? discoverable,
        CancellationToken cancellationToken)
    {
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);
        await profiles.UpdateAsync(
            actor.Username,
            new ProfileUpdateCommand(name, description, isLocked, discoverable, Indexable: null),
            cancellationToken).ConfigureAwait(false);
    }
}
#endif
