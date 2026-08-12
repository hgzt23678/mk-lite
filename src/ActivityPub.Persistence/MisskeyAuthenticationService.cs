using System.Data;
using System.Security.Cryptography;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ActivityPub.Persistence;

public sealed class MisskeyAuthenticationService(
    IDbContextFactory<FederationDbContext> contextFactory,
    IDataProtectionProvider dataProtection,
    MisskeyAuthenticationOptions options,
    IAuditLog audit) : IMisskeyAuthenticationService
{
    private readonly IDataProtector tokenProtector = dataProtection.CreateProtector(
        "ActivityPub.MisskeyAuthentication.OneTimeToken.v1");

    public Task<MisskeyIssuedToken> IssueDirectAsync(
        string username,
        string clientName,
        string? description,
        string? iconUri,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken) =>
        // Keep the existing one-to-one source-session constraint while accepting the
        // Misskey v12 session:null contract. The generated key is internal only and
        // is never returned by the HTTP endpoint or exposed to the client.
        IssueAsync(
            username,
            Guid.NewGuid().ToString("D"),
            clientName,
            description,
            iconUri,
            callbackUri: null,
            permissions,
            cancellationToken);

    public async Task<MisskeyIssuedToken> IssueAsync(
        string username,
        string sessionKey,
        string clientName,
        string? description,
        string? iconUri,
        string? callbackUri,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        ValidateSessionKey(sessionKey);
        string[] granted = ValidatePermissions(permissions);
        string name = string.IsNullOrWhiteSpace(clientName) ? "Unnamed application" : clientName.Trim();
        string? validatedIconUri = ValidateOptionalHttpsUri(iconUri, nameof(iconUri));
        string? validatedDescription = ValidateOptionalHttpsUri(description, nameof(description), uriOnly: false);
        string? callback = ValidateCallbackUri(callbackUri);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string normalizedUsername = username.ToUpperInvariant();

        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        LocalActor actor = await db.LocalActors.SingleOrDefaultAsync(
            item => item.NormalizedUsername == normalizedUsername && !item.IsSuspended,
            cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Authenticated account has no active local actor.");
        MisskeyAuthSession? existing = await db.MisskeyAuthSessions.SingleOrDefaultAsync(
            item => item.SessionKey == sessionKey,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            bool sameRequest = existing.State == MisskeyAuthSessionState.Approved &&
                existing.ActorIri == actor.Iri &&
                existing.ClientName == name &&
                existing.Permissions == string.Join(' ', granted) &&
                existing.EncryptedToken is not null &&
                existing.IssuedTokenId is not null &&
                now < existing.ExpiresAt;
            if (!sameRequest)
            {
                throw new InvalidOperationException("The MiAuth session has already been used.");
            }

            Guid issuedTokenId = existing.IssuedTokenId.GetValueOrDefault();
            MisskeyAccessToken existingToken = await db.MisskeyAccessTokens.SingleAsync(
                item => item.Id == issuedTokenId,
                cancellationToken).ConfigureAwait(false);
            string existingRawToken = tokenProtector.Unprotect(existing.EncryptedToken!);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(
                existingRawToken,
                existingToken.Id,
                sessionKey,
                actor.Iri,
                actor.Username,
                existingToken.GetPermissions(),
                existingToken.ExpiresAt);
        }

        MisskeyAuthSession session = MisskeyAuthSession.Create(
            sessionKey,
            name,
            iconUri,
            null,
            callback,
            granted,
            now,
            now.Add(options.SessionLifetime));
        string rawToken = "mk_" + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        MisskeyAccessToken token = MisskeyAccessToken.Create(
            actor.Iri,
            name,
            validatedDescription,
            validatedIconUri,
            Hash(rawToken),
            granted,
            now,
            now.Add(options.AccessTokenLifetime),
            session.Id);
        session.Approve(actor.Iri, now);
        session.AttachIssuedToken(token.Id, tokenProtector.Protect(rawToken));
        db.MisskeyAuthSessions.Add(session);
        db.MisskeyAccessTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await audit.AppendAsync(
            "misskey-auth",
            "token-issued",
            actor.Iri,
            token.Id.ToString("N"),
            // sessionKey is the still-redeemable MiAuth bearer credential. Audit only the
            // internal record identifier so log readers cannot race the client for its token.
            System.Text.Json.JsonSerializer.Serialize(new { client = name, permissions = granted, sessionId = session.Id }),
            now,
            cancellationToken).ConfigureAwait(false);
        return new(rawToken, token.Id, sessionKey, actor.Iri, actor.Username, granted, token.ExpiresAt);
    }

    public async Task<MisskeyIssuedToken?> ConsumeSessionAsync(
        string sessionKey,
        CancellationToken cancellationToken)
    {
        ValidateSessionKey(sessionKey);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        MisskeyAuthSession? session = await db.MisskeyAuthSessions
            .FromSqlInterpolated($"SELECT * FROM activitypub.misskey_auth_sessions WHERE session_key = {sessionKey} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (session is null || session.State != MisskeyAuthSessionState.Approved || now > session.ExpiresAt ||
            session.IssuedTokenId is null || session.EncryptedToken is null || session.ActorIri is null)
        {
            if (session is not null && now > session.ExpiresAt)
            {
                session.Expire(now);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        string rawToken = tokenProtector.Unprotect(session.EncryptedToken);
        MisskeyAccessToken token = await db.MisskeyAccessTokens.SingleAsync(
            item => item.Id == session.IssuedTokenId.Value,
            cancellationToken).ConfigureAwait(false);
        LocalActor actor = await db.LocalActors.SingleAsync(
            item => item.Iri == session.ActorIri && !item.IsSuspended,
            cancellationToken).ConfigureAwait(false);
        _ = session.Consume(now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(rawToken, token.Id, sessionKey, actor.Iri, actor.Username, token.GetPermissions(), token.ExpiresAt);
    }

    public async Task<MisskeyTokenPrincipal?> ValidateAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256 || !token.StartsWith("mk_", StringComparison.Ordinal))
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string hash = Hash(token);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        MisskeyAccessToken? stored = await db.MisskeyAccessTokens.SingleOrDefaultAsync(
            item => item.TokenHash == hash && item.RevokedAt == null && item.ExpiresAt > now,
            cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return null;
        }

        LocalActor? actor = await db.LocalActors.SingleOrDefaultAsync(
            item => item.Iri == stored.ActorIri && !item.IsSuspended,
            cancellationToken).ConfigureAwait(false);
        if (actor is null)
        {
            return null;
        }

        if (stored.LastUsedAt is null || now - stored.LastUsedAt > TimeSpan.FromMinutes(5))
        {
            stored.MarkUsed(now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new(stored.Id, actor.Iri, actor.Username, stored.GetPermissions(), stored.ExpiresAt);
    }

    public async Task<IReadOnlyList<MisskeyTokenSummary>> ListAsync(
        string actorIri,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        MisskeyAccessToken[] tokens = await db.MisskeyAccessTokens
            .Where(item => item.ActorIri == actorIri && item.RevokedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .Take(200)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return tokens.Select(token => new MisskeyTokenSummary(
            token.Id,
            token.Name,
            token.Description,
            token.IconUri,
            token.GetPermissions(),
            token.CreatedAt,
            token.ExpiresAt,
            token.LastUsedAt,
            token.RevokedAt)).ToArray();
    }

    public async Task<bool> RevokeAsync(
        string actorIri,
        Guid tokenId,
        CancellationToken cancellationToken)
    {
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        MisskeyAccessToken? token = await db.MisskeyAccessTokens.SingleOrDefaultAsync(
            item => item.Id == tokenId && item.ActorIri == actorIri,
            cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return false;
        }

        token.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await audit.AppendAsync(
            "misskey-auth",
            "token-revoked",
            actorIri,
            tokenId.ToString("N"),
            "{}",
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static string[] ValidatePermissions(IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        string[] normalized = permissions
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Any(permission => !MisskeyPermissions.All.Contains(permission)))
        {
            throw new ArgumentException("The MiAuth permission set contains an unsupported permission.", nameof(permissions));
        }

        return normalized;
    }

    private static void ValidateSessionKey(string sessionKey)
    {
        if (!Guid.TryParseExact(sessionKey, "D", out _))
        {
            throw new ArgumentException("A MiAuth session must be a canonical UUID.", nameof(sessionKey));
        }
    }

    private static string? ValidateCallbackUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("The MiAuth callback URI is invalid.", nameof(value));
        }

        bool allowed = uri.Scheme == Uri.UriSchemeHttps ||
            uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback ||
            uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeFile;
        return allowed ? uri.AbsoluteUri : throw new ArgumentException("The MiAuth callback URI scheme is not allowed.", nameof(value));
    }

    private static string? ValidateOptionalHttpsUri(string? value, string name, bool uriOnly = true)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!uriOnly) return value.Length <= 2_000 ? value : throw new ArgumentException(name + " is too long.", name);
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo)
            ? uri.AbsoluteUri
            : throw new ArgumentException(name + " must be an absolute HTTPS URI without userinfo.", name);
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
