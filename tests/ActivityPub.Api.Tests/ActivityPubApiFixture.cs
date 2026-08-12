using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Federation.Signatures;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using Testcontainers.PostgreSql;

namespace ActivityPub.Api.Tests;

public sealed class ActivityPubApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    internal const string FixtureAlicePassword = "fixture-alice-password";

    private readonly RSA recipientKey = RSA.Create(2_048);
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("activitypub_api_tests")
        .WithUsername("activitypub")
        .WithPassword("test-only-password")
        .Build();

    public Guid PrivateObjectId { get; private set; }
    public Guid PublicMediaObjectId { get; private set; }
    public Guid LocalActorId { get; private set; }
    public Guid RecipientActorId { get; private set; }
    public Guid LocalMediaId { get; private set; }
    public string MastodonLocalActorId { get; private set; } = string.Empty;
    public string MastodonPublicPostId { get; private set; } = string.Empty;
    public string MastodonLocalMediaId { get; private set; } = string.Empty;
    public string MisskeyLocalActorId { get; private set; } = string.Empty;
    public string MisskeyPublicPostId { get; private set; } = string.Empty;
    public string MisskeyPrivatePostId { get; private set; } = string.Empty;
    public string MisskeyRemotePublisherId { get; private set; } = string.Empty;
    public string MisskeyRecipientActorId { get; private set; } = string.Empty;

    public const string OutboundFollowActivityIri = "https://local.example/activities/follow-bob";

    public const string RecipientActorIri = "https://remote.example/users/bob";

    public const string RecipientKeyIri = RecipientActorIri + "#main-key";

    public const string RecipientAvatarIri = "https://remote.example/media/bob.png";

    public const string RecipientBannerIri = "https://cdn.remote.example/media/bob-banner.webp";

    internal FixtureIdentityEmailSender IdentityEmailSender =>
        Services.GetRequiredService<FixtureIdentityEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Linked upstream client assets are copied beside the test application at build time.
        // Using that web root makes WebApplicationFactory exercise the same published asset tree.
        builder.UseWebRoot(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ActivityPub", container.GetConnectionString());
        builder.UseSetting("Federation:PublicBaseUri", "https://local.example");
        builder.UseSetting("Federation:ClientToServerEnabled", "true");
        builder.UseSetting("Workers:InboxEnabled", "false");
        builder.UseSetting("Workers:DeliveryEnabled", "false");
        builder.UseSetting("KeyManagement:Enabled", "false");
        builder.UseSetting("Media:Enabled", "false");
        builder.UseSetting("Authentication:Authority", "https://identity.local.example");
        builder.UseSetting("Authentication:Audience", "activitypub-api");
        builder.UseSetting("OAuth:Enabled", "true");
        builder.UseSetting("OAuth:InteractiveClientId", "activitypub-api-tests");
        builder.UseSetting("Frontend:Enabled", "true");
        builder.UseSetting("Frontend:ClientId", "activitypub-web-test");
        builder.UseSetting("Frontend:PublicBaseUri", "https://client.local.example");
        builder.UseSetting("Frontend:Authority", "https://client.local.example/oidc/realms/test");
        builder.UseSetting("Frontend:SourceUrl", "https://source.local.example/activitypub-web");
        builder.UseSetting("LocalAccounts:Enabled", "true");
        builder.UseSetting("LocalAccounts:RegistrationEnabled", "false");
        builder.UseSetting("PasswordReset:Enabled", "true");
        builder.UseSetting("PasswordReset:SenderAddress", "no-reply@client.local.example");
        builder.UseSetting("PasswordReset:SenderName", "ActivityPub API tests");
        builder.UseSetting("PasswordReset:SmtpHost", "smtp.client.local.example");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ActivityPub"] = container.GetConnectionString(),
            ["Federation:PublicBaseUri"] = "https://local.example",
            ["Federation:ClientToServerEnabled"] = "true",
            ["Federation:RequireHttps"] = "true",
            ["Workers:InboxEnabled"] = "false",
            ["Workers:DeliveryEnabled"] = "false",
            ["KeyManagement:Enabled"] = "false",
            ["Media:Enabled"] = "false",
            ["Authentication:Authority"] = "https://identity.local.example",
            ["Authentication:Audience"] = "activitypub-api",
            ["OAuth:Enabled"] = "true",
            ["OAuth:InteractiveClientId"] = "activitypub-api-tests",
            ["Frontend:Enabled"] = "true",
            ["Frontend:ClientId"] = "activitypub-web-test",
            ["Frontend:PublicBaseUri"] = "https://client.local.example",
            ["Frontend:Authority"] = "https://client.local.example/oidc/realms/test",
            ["Frontend:SourceUrl"] = "https://source.local.example/activitypub-web",
            ["LocalAccounts:Enabled"] = "true",
            ["LocalAccounts:RegistrationEnabled"] = "false",
            ["PasswordReset:Enabled"] = "true",
            ["PasswordReset:SenderAddress"] = "no-reply@client.local.example",
            ["PasswordReset:SenderName"] = "ActivityPub API tests",
            ["PasswordReset:SmtpHost"] = "smtp.client.local.example",
            ["Http:AllowedCorsOrigins:0"] = "https://app.local.example"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "FixtureComposite";
                options.DefaultChallengeScheme = "FixtureComposite";
                options.DefaultForbidScheme = "FixtureComposite";
            })
                .AddPolicyScheme("FixtureComposite", "FixtureComposite", policy =>
                {
                    policy.ForwardDefaultSelector = context =>
                    {
                        string authorization = context.Request.Headers.Authorization.ToString();
                        if (string.Equals(authorization, "Bearer fixture-alice", StringComparison.Ordinal) ||
                            string.Equals(authorization, "Bearer fixture-admin", StringComparison.Ordinal))
                        {
                            return FixtureAuthenticationHandler.SchemeName;
                        }

                        if (authorization.StartsWith("Bearer mk_", StringComparison.Ordinal) ||
                            string.IsNullOrEmpty(authorization) &&
                            (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/streaming")))
                        {
                            return MisskeyTokenAuthenticationHandler.SchemeName;
                        }

                        return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                    };
                })
                .AddScheme<AuthenticationSchemeOptions, FixtureAuthenticationHandler>(
                    FixtureAuthenticationHandler.SchemeName,
                    _ => { });
            services.RemoveAll<IRemoteKeyResolver>();
            services.AddScoped<IRemoteKeyResolver>(_ => new FixtureRemoteKeyResolver(
                RecipientActorIri,
                recipientKey.ExportSubjectPublicKeyInfoPem()));
            // Registration is disabled in this fixture. Supply the actor boundary so the
            // local-account service can be validated while identity endpoint tests exercise
            // only antiforgery and routing; actor provisioning is covered by PostgreSQL tests.
            services.AddSingleton<ILocalActorAdministration, DisabledFixtureLocalActorAdministration>();
            services.RemoveAll<IPasswordResetEmailSender>();
            services.RemoveAll<IEmailConfirmationSender>();
            services.RemoveAll<IUrlPreviewFetcher>();
            services.AddSingleton<IUrlPreviewFetcher, FixtureUrlPreviewFetcher>();
            services.RemoveAll<IMediaService>();
            services.AddSingleton<IMediaService, FixtureMediaService>();
            services.AddSingleton<FixtureIdentityEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender>(provider => provider.GetRequiredService<FixtureIdentityEmailSender>());
            services.AddSingleton<IEmailConfirmationSender>(provider => provider.GetRequiredService<FixtureIdentityEmailSender>());
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await container.StartAsync();
        _ = Services;
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IDbContextFactory<FederationDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        IDbContextFactory<LocalIdentityDbContext> identityFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        await identity.Database.MigrateAsync();

        DateTimeOffset now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var key = ActorKey.CreateLocal(
            "https://local.example/users/alice#key-fixture",
            "https://local.example/users/alice",
            "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAwfixture\n-----END PUBLIC KEY-----",
            "fixture-not-used",
            now);
        key.Activate(now);
        LocalActor actor = LocalActor.Create("https://local.example/users/alice", "alice", ActorKind.Person, now);
        LocalActorId = actor.Id;
        actor.UpdateProfile("Alice", "<p>Profile</p>", false, true, true, now);
        actor.SetActiveKey(key.Id, now);
        db.ActorKeys.Add(key);
        db.LocalActors.Add(actor);
        MediaResource localMedia = MediaResource.Create(
            actor.Iri,
            "quarantine/fixture/source.png",
            new string('a', 64),
            "image/png",
            "fixture.png",
            1_024,
            Visibility.MentionedOnly,
            now);
        localMedia.MarkAvailable(
            "media/fixture/original.png",
            new string('b', 64),
            "image/png",
            1_024,
            64,
            32,
            null,
            "media/fixture/thumbnail.jpg",
            now);
        LocalMediaId = localMedia.Id;
        db.Media.Add(localMedia);
        string privateJson = "{\"type\":\"Note\",\"content\":\"private-fixture-secret\"}";
        PrivateObjectId = Guid.NewGuid();
        FederatedObject privateObject = FederatedObject.Create(
            $"https://local.example/objects/{PrivateObjectId}",
            actor.Iri,
            "Note",
            Visibility.MentionedOnly,
            privateJson,
            PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(privateJson)),
            now,
            now);
        db.Objects.Add(privateObject);
        string activityIri = $"https://local.example/activities/{Guid.NewGuid()}";
        string activityJson = $"{{\"id\":\"{activityIri}\",\"type\":\"Create\",\"actor\":\"{actor.Iri}\",\"object\":\"{privateObject.Iri}\",\"to\":\"{RecipientActorIri}\"}}";
        ActivityRecord activity = ActivityRecord.Create(
            activityIri,
            actor.Iri,
            "Create",
            privateObject.Iri,
            ActivityDirection.Outbound,
            Visibility.MentionedOnly,
            activityJson,
            PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(activityJson)),
            false,
            now,
            now);
        db.Activities.Add(activity);
        db.ActivityRecipients.Add(ActivityRecipient.Create(activity.Id, RecipientActorIri, AudienceField.To));
        db.ActivityRecipients.Add(ActivityRecipient.Create(activity.Id, actor.Iri, AudienceField.Bcc));
        RemoteActor recipientActor = RemoteActor.Create(
            RecipientActorIri,
            "Person",
            "bob",
            $"{{\"id\":\"{RecipientActorIri}\",\"type\":\"Person\",\"preferredUsername\":\"bob\",\"name\":\"Bob\",\"icon\":[{{\"type\":\"Image\",\"url\":\"javascript:alert(1)\"}},{{\"type\":\"Image\",\"url\":\"{RecipientAvatarIri}\"}}],\"image\":{{\"type\":\"Image\",\"url\":{{\"type\":\"Link\",\"href\":\"{RecipientBannerIri}\"}}}}}}",
            now);
        RecipientActorId = recipientActor.Id;
        db.RemoteActors.Add(recipientActor);
        FollowRelation outboundFollow = FollowRelation.Request(
            actor.Iri,
            RecipientActorIri,
            OutboundFollowActivityIri,
            now);
        outboundFollow.Accept(
            RecipientActorIri,
            "https://remote.example/activities/accept-follow-bob",
            now);
        db.FollowRelations.Add(outboundFollow);

        const string silencedActorIri = "https://silenced.example/users/noisy";
        const string mediaActorIri = "https://media-blocked.example/users/publisher";
        db.RemoteActors.Add(RemoteActor.Create(
            silencedActorIri,
            "Person",
            "noisy",
            $"{{\"id\":\"{silencedActorIri}\",\"type\":\"Person\",\"preferredUsername\":\"noisy\"}}",
            now));
        db.RemoteEndpoints.Add(RemoteEndpoint.Create(
            mediaActorIri,
            EndpointKind.Inbox,
            "https://media-blocked.example/inbox",
            now));
        db.RemoteEndpoints.Add(RemoteEndpoint.Create(
            mediaActorIri,
            EndpointKind.SharedInbox,
            "https://media-blocked.example/inbox/shared",
            now));
        RemoteActor mediaRemoteActor = RemoteActor.Create(
            mediaActorIri,
            "Person",
            "publisher",
            $"{{\"id\":\"{mediaActorIri}\",\"type\":\"Person\",\"preferredUsername\":\"publisher\"}}",
            now);
        db.RemoteActors.Add(mediaRemoteActor);
        const string silencedJson = "{\"type\":\"Note\",\"content\":\"silenced-public-secret\"}";
        const string mediaJson = "{\"type\":\"Note\",\"content\":\"media-policy-visible-text\",\"attachment\":{\"type\":\"Image\",\"url\":\"https://media-blocked.example/files/tracker.png\"}}";
        db.Objects.Add(FederatedObject.Create(
            "https://silenced.example/objects/1",
            silencedActorIri,
            "Note",
            Visibility.Public,
            silencedJson,
            PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(silencedJson)),
            now,
            now));
        FederatedObject mediaObject = FederatedObject.Create(
            "https://media-blocked.example/objects/1",
            mediaActorIri,
            "Note",
            Visibility.Public,
            mediaJson,
            PayloadDigest.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(mediaJson)),
            now,
            now);
        PublicMediaObjectId = mediaObject.Id;
        db.Objects.Add(mediaObject);
        db.LikeRelations.Add(LikeRelation.Create(
            silencedActorIri,
            mediaObject.Iri,
            "https://silenced.example/activities/reaction-1",
            FederatedReaction.Create(
                ":party:",
                silencedActorIri,
                "https://silenced.example/emojis/party",
                ":party:",
                "https://cdn.silenced.example/party.png",
                "image/png"),
            now));
        db.DomainPolicies.Add(DomainPolicy.Create(
            "silenced.example",
            FederationPolicyKind.Silence,
            "test silence",
            "fixture",
            now,
            null));
        db.DomainPolicies.Add(DomainPolicy.Create(
            "media-blocked.example",
            FederationPolicyKind.RejectMedia,
            "test media rejection",
            "fixture",
            now,
            null));
        await db.SaveChangesAsync();

        UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        if (await users.FindByNameAsync("alice") is null)
        {
            LocalIdentityUser identityUser = LocalIdentityUser.Create("alice", null, now);
            identityUser.Activate(actor.Id, actor.Iri, now);
            IdentityResult created = await users.CreateAsync(identityUser, FixtureAlicePassword);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException("The API fixture could not provision its local sign-in account.");
            }
        }

        IExternalEntityIdService externalIds = scope.ServiceProvider.GetRequiredService<IExternalEntityIdService>();
        MastodonLocalActorId = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Actor,
            LocalActorId,
            actor.CreatedAt,
            CancellationToken.None);
        MastodonPublicPostId = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Post,
            PublicMediaObjectId,
            mediaObject.PublishedAt,
            CancellationToken.None);
        MastodonLocalMediaId = await externalIds.GetOrCreateAsync(
            ApiDialect.Mastodon,
            ExternalEntityType.Media,
            LocalMediaId,
            localMedia.CreatedAt,
            CancellationToken.None);
        MisskeyLocalActorId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            LocalActorId,
            actor.CreatedAt,
            CancellationToken.None);
        MisskeyPublicPostId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            PublicMediaObjectId,
            mediaObject.PublishedAt,
            CancellationToken.None);
        MisskeyPrivatePostId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Post,
            privateObject.Id,
            privateObject.PublishedAt,
            CancellationToken.None);
        MisskeyRemotePublisherId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            mediaRemoteActor.Id,
            mediaRemoteActor.FetchedAt,
            CancellationToken.None);
        MisskeyRecipientActorId = await externalIds.GetOrCreateAsync(
            ApiDialect.Misskey,
            ExternalEntityType.Actor,
            recipientActor.Id,
            recipientActor.FetchedAt,
            CancellationToken.None);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        recipientKey.Dispose();
        await container.DisposeAsync();
    }

    public byte[] SignForRecipient(ReadOnlySpan<byte> data) =>
        recipientKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    private sealed class FixtureRemoteKeyResolver(string ownerIri, string publicKeyPem) : IRemoteKeyResolver
    {
        public Task<RemotePublicKey> ResolveAsync(
            string keyIri,
            bool forceRefresh,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RemotePublicKey(
                keyIri,
                ownerIri,
                publicKeyPem,
                "rsa-v1_5-sha256",
                DateTimeOffset.UtcNow.AddHours(1)));
    }

    private sealed class FixtureAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FixtureBearer";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues authorization) ||
                !string.Equals(authorization.ToString(), "Bearer fixture-alice", StringComparison.Ordinal) &&
                !string.Equals(authorization.ToString(), "Bearer fixture-admin", StringComparison.Ordinal))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            bool administrator = string.Equals(
                Request.Headers.Authorization.ToString(),
                "Bearer fixture-admin",
                StringComparison.Ordinal);
            var claims = new List<Claim>
            {
                new("sub", administrator ? "fixture-admin" : "fixture-alice"),
                new("preferred_username", "alice"),
                new("actor", "https://local.example/users/alice"),
                new("scope", administrator
                    ? "openid profile activitypub.read activitypub.write activitypub.admin read write"
                    : "openid profile activitypub.read activitypub.write read write")
            };
            if (administrator)
            {
                claims.Add(new Claim("role", "activitypub-admin"));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, "preferred_username", "role"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

internal sealed class DisabledFixtureLocalActorAdministration : ILocalActorAdministration
{
    public Task<LocalActorAdministrationResult> CreateAsync(
        string username,
        ActorKind kind,
        string displayName,
        string summaryHtml,
        bool manuallyApprovesFollowers,
        bool discoverable,
        bool indexable,
        string operatorId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Actor registration is disabled in the API fixture.");

    public Task<LocalActorAdministrationResult?> RotateKeyAsync(
        string username,
        TimeSpan overlap,
        string operatorId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Actor key rotation is disabled in the API fixture.");
}

internal sealed class FixtureIdentityEmailSender : IPasswordResetEmailSender, IEmailConfirmationSender
{
    private readonly ConcurrentQueue<PasswordResetEmail> passwordResets = new();
    private readonly ConcurrentQueue<EmailConfirmationEmail> confirmations = new();

    public IReadOnlyList<PasswordResetEmail> PasswordResets => passwordResets.ToArray();

    public IReadOnlyList<EmailConfirmationEmail> Confirmations => confirmations.ToArray();

    public Task SendAsync(PasswordResetEmail email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        passwordResets.Enqueue(email);
        return Task.CompletedTask;
    }

    public Task SendAsync(EmailConfirmationEmail email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        confirmations.Enqueue(email);
        return Task.CompletedTask;
    }
}

internal sealed class FixtureMediaService(
    IDbContextFactory<FederationDbContext> contextFactory) : IMediaService
{
    public async Task<MediaUploadResult> UploadAsync(MediaUploadCommand command, CancellationToken cancellationToken)
    {
        byte[] content;
        using (var buffer = new MemoryStream())
        {
            await command.Content.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        MediaResource media = MediaResource.Create(
            command.OwnerActorIri,
            "fixture/" + Guid.NewGuid().ToString("N") + ".bin",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)),
            command.DeclaredMediaType ?? "application/octet-stream",
            command.OriginalFileName,
            content.Length,
            Visibility.Public,
            now);
        media.MarkAvailable(
            media.StorageKey,
            media.ContentHash,
            media.DetectedMediaType,
            media.Length,
            null,
            null,
            null,
            null,
            now);
        await using FederationDbContext db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Media.Add(media);
        await db.SaveChangesAsync(cancellationToken);
        return new MediaUploadResult(media.Id, media.DetectedMediaType, media.Length, null, null, null);
    }

    public Task<MediaDownload?> OpenReadAsync(Guid id, string? requesterActorIri, CancellationToken cancellationToken) =>
        Task.FromResult<MediaDownload?>(null);
}

internal sealed class FixtureUrlPreviewFetcher : IUrlPreviewFetcher
{
    public int FetchCount { get; private set; }

    public string? LastUrl { get; private set; }

    public Task<UrlPreviewResult?> FetchAsync(string url, string? lang, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FetchCount += 1;
        LastUrl = url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
            !string.Equals(parsed.IdnHost, "known.example", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<UrlPreviewResult?>(null);
        }

        return Task.FromResult<UrlPreviewResult?>(new UrlPreviewResult(
            "Known Title",
            "Known description",
            "https://images.example/known.png",
            "https://images.example/favicon.ico",
            "KnownSite",
            "https://player.example/video.mp4",
            640,
            360));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ActivityPubApiFixtureDefinition : ICollectionFixture<ActivityPubApiFixture>
{
    public const string Name = "activitypub-api";
}
