using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ActivityPub.Application;
using ActivityPub.Identity;
using ActivityPub.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ActivityPub.Persistence.Tests;

[Collection(PostgreSqlFixtureDefinition.Name)]
public sealed class LocalIdentityIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task RegistrationInvitationIsHashedExpiringSingleUseAndConcurrencySafe()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IRegistrationInvitationStore store = scope.ServiceProvider.GetRequiredService<IRegistrationInvitationStore>();
        TestClock clock = scope.ServiceProvider.GetRequiredService<TestClock>();
        var options = new RegistrationProtectionOptions
        {
            InvitationRequired = true,
            InvitationLifetime = TimeSpan.FromDays(7),
            InvitationReservationLifetime = TimeSpan.FromMinutes(5)
        };
        var issuer = new RegistrationInvitationService(store, clock, options);
        var protection = new RegistrationProtectionService(store, new AcceptingCaptchaVerifier(), clock, options);

        RegistrationInvitationIssueResult issued = await issuer.IssueAsync("admin:test", CancellationToken.None);
        Assert.Matches("^[2-9A-HJ-NP-Z]{26}$", issued.Code);

        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using (LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync())
        {
            LocalRegistrationInvitation persisted = await identity.Set<LocalRegistrationInvitation>()
                .AsNoTracking()
                .SingleAsync(invitation => invitation.CreatedBy == "admin:test");
            Assert.Equal(32, persisted.CodeHash.Length);
            Assert.False(persisted.CodeHash.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(issued.Code)));
            Assert.InRange(
                (issued.ExpiresAt - persisted.ExpiresAt).Duration(),
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1));
        }

        RegistrationProtectionResult[] concurrent = await Task.WhenAll(
            protection.AuthorizeAsync(new LocalRegistrationProtection(issued.Code, null, null), CancellationToken.None),
            protection.AuthorizeAsync(new LocalRegistrationProtection(issued.Code, null, null), CancellationToken.None));
        RegistrationProtectionResult winner = Assert.Single(
            concurrent,
            result => result.Status == RegistrationProtectionStatus.Accepted);
        Assert.Single(concurrent, result => result.Status == RegistrationProtectionStatus.InvitationInvalid);
        Assert.NotNull(winner.InvitationReservation);

        await protection.ReleaseInvitationAsync(winner.InvitationReservation!, CancellationToken.None);
        RegistrationProtectionResult reservedAgain = await protection.AuthorizeAsync(
            new LocalRegistrationProtection(issued.Code, null, null),
            CancellationToken.None);
        Assert.Equal(RegistrationProtectionStatus.Accepted, reservedAgain.Status);
        Assert.True(await protection.ConsumeInvitationAsync(
            Assert.IsType<RegistrationInvitationReservation>(reservedAgain.InvitationReservation),
            "inviteuser",
            CancellationToken.None));
        await using (LocalIdentityDbContext consumedIdentity = await identityFactory.CreateDbContextAsync())
        {
            LocalRegistrationInvitation consumed = await consumedIdentity.Set<LocalRegistrationInvitation>()
                .AsNoTracking()
                .SingleAsync(invitation => invitation.CreatedBy == "admin:test" && invitation.ConsumedAt != null);
            Assert.Equal("inviteuser", consumed.ConsumedByUsername);
            Assert.Null(consumed.ReservationId);
            Assert.Null(consumed.ReservedAt);
            Assert.Null(consumed.ReservationExpiresAt);
        }

        Assert.Equal(
            RegistrationProtectionStatus.InvitationInvalid,
            (await protection.AuthorizeAsync(
                new LocalRegistrationProtection(issued.Code, null, null),
                CancellationToken.None)).Status);

        RegistrationInvitationIssueResult expiring = await issuer.IssueAsync("admin:test", CancellationToken.None);
        clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(
            RegistrationProtectionStatus.InvitationInvalid,
            (await protection.AuthorizeAsync(
                new LocalRegistrationProtection(expiring.Code, null, null),
                CancellationToken.None)).Status);

        IDbContextFactory<FederationDbContext> federationFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext federation = await federationFactory.CreateDbContextAsync();
        Assert.True(await federation.AuditEvents.AnyAsync(audit =>
            audit.Action == "registration-invitation-issued" && audit.Actor == "admin:test"));
        Assert.True(await federation.AuditEvents.AnyAsync(audit =>
            audit.Action == "registration-invitation-consumed" && audit.Actor == "registration:inviteuser"));
    }

    [Fact]
    public async Task RegistrationInvitationMigrationRejectsMalformedRowsInPostgreSql()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<LocalIdentityDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await factory.CreateDbContextAsync();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;

        PostgresException shortHash = await Assert.ThrowsAsync<PostgresException>(() =>
            identity.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.registration_invitations
                    (id, code_hash, created_by, created_at, expires_at)
                VALUES
                    ({Guid.NewGuid()}, {RandomNumberGenerator.GetBytes(31)}, {'a' + Guid.NewGuid().ToString("N")},
                     {createdAt}, {createdAt.AddMinutes(1)});
                """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, shortHash.SqlState);
        Assert.Equal("ck_identity_registration_invitations_code_hash_length", shortHash.ConstraintName);

        PostgresException invalidExpiry = await Assert.ThrowsAsync<PostgresException>(() =>
            identity.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.registration_invitations
                    (id, code_hash, created_by, created_at, expires_at)
                VALUES
                    ({Guid.NewGuid()}, {RandomNumberGenerator.GetBytes(32)}, {'a' + Guid.NewGuid().ToString("N")},
                     {createdAt}, {createdAt});
                """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidExpiry.SqlState);
        Assert.Equal("ck_identity_registration_invitations_expiry", invalidExpiry.ConstraintName);

        PostgresException partialReservation = await Assert.ThrowsAsync<PostgresException>(() =>
            identity.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.registration_invitations
                    (id, code_hash, created_by, created_at, expires_at, reservation_id)
                VALUES
                    ({Guid.NewGuid()}, {RandomNumberGenerator.GetBytes(32)}, {'a' + Guid.NewGuid().ToString("N")},
                     {createdAt}, {createdAt.AddMinutes(1)}, {Guid.NewGuid()});
                """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, partialReservation.SqlState);
        Assert.Equal("ck_identity_registration_invitations_reservation", partialReservation.ConstraintName);

        PostgresException partialConsumption = await Assert.ThrowsAsync<PostgresException>(() =>
            identity.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.registration_invitations
                    (id, code_hash, created_by, created_at, expires_at, consumed_at)
                VALUES
                    ({Guid.NewGuid()}, {RandomNumberGenerator.GetBytes(32)}, {'a' + Guid.NewGuid().ToString("N")},
                     {createdAt}, {createdAt.AddMinutes(1)}, {createdAt});
                """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, partialConsumption.SqlState);
        Assert.Equal("ck_identity_registration_invitations_consumption", partialConsumption.ConstraintName);
    }

    [Fact]
    public async Task LocalAccountRegistrationCannotBypassInvitationOrCaptchaProtection()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IRegistrationInvitationStore store = scope.ServiceProvider.GetRequiredService<IRegistrationInvitationStore>();
        TestClock clock = scope.ServiceProvider.GetRequiredService<TestClock>();
        var accountOptions = new LocalAccountOptions
        {
            Enabled = true,
            RegistrationEnabled = false,
            RequireConfirmedEmail = false,
            RequiredPasswordLength = 8
        };
        var protectionOptions = new RegistrationProtectionOptions
        {
            InvitationRequired = true,
            InvitationLifetime = TimeSpan.FromDays(7),
            InvitationReservationLifetime = TimeSpan.FromMinutes(5),
            CaptchaProvider = RegistrationCaptchaProvider.Hcaptcha,
            CaptchaSiteKey = "test-site-key",
            CaptchaSecretFile = "/test-only/not-read-by-fake-verifier",
            CaptchaExpectedHostname = "identity-tests.example",
            CaptchaVerificationTimeout = TimeSpan.FromSeconds(2)
        };
        var captcha = new ConditionalCaptchaVerifier("valid-captcha-response");
        var registrationProtection = new RegistrationProtectionService(store, captcha, clock, protectionOptions);
        var issuer = new RegistrationInvitationService(store, clock, protectionOptions);
        LocalAccountService accounts = new(
            scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(),
            scope.ServiceProvider.GetRequiredService<SignInManager<LocalIdentityUser>>(),
            scope.ServiceProvider.GetRequiredService<ActivityPub.Application.IFederationQueryStore>(),
            scope.ServiceProvider.GetRequiredService<ActivityPub.Application.ILocalActorAdministration>(),
            scope.ServiceProvider.GetRequiredService<ActivityPub.Application.IAuditLog>(),
            clock,
            accountOptions,
            protectionOptions,
            registrationProtection,
            scope.ServiceProvider.GetRequiredService<IAtomicLocalAccountRegistration>(),
            scope.ServiceProvider.GetRequiredService<IPasswordVerificationTimingEqualizer>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<LocalIdentityRole>>(),
            scope.ServiceProvider.GetRequiredService<ILogger<LocalAccountService>>());
        string username = UniqueUsername("protected");
        RegistrationInvitationIssueResult issued = await issuer.IssueAsync("admin:protected-test", CancellationToken.None);

        LocalAccountRegistrationResult bypass = await accounts.RegisterAsync(
            username,
            email: null,
            "a sufficiently long password",
            CancellationToken.None);
        Assert.Equal(LocalAccountRegistrationStatus.CaptchaInvalid, bypass.Status);

        LocalAccountRegistrationResult invalidCaptcha = await accounts.RegisterAsync(
            username,
            email: null,
            "a sufficiently long password",
            new LocalRegistrationProtection(issued.Code, "invalid", null),
            CancellationToken.None);
        Assert.Equal(LocalAccountRegistrationStatus.CaptchaInvalid, invalidCaptcha.Status);

        LocalAccountRegistrationResult weakPassword = await accounts.RegisterAsync(
            username,
            email: null,
            "short",
            new LocalRegistrationProtection(issued.Code, "valid-captcha-response", null),
            CancellationToken.None);
        Assert.Equal(LocalAccountRegistrationStatus.InvalidPassword, weakPassword.Status);
        Assert.Contains("PASSWORD_TOO_SHORT", weakPassword.SafeErrorCodes, StringComparer.Ordinal);

        LocalAccountRegistrationResult created = await accounts.RegisterAsync(
            username,
            email: null,
            "a sufficiently long password",
            new LocalRegistrationProtection(issued.Code, "valid-captcha-response", null),
            CancellationToken.None);
        Assert.Equal(LocalAccountRegistrationStatus.Created, created.Status);
        Assert.Equal(LocalAccountProvisioningState.Active, created.User?.ProvisioningState);

        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        LocalRegistrationInvitation consumedInvitation = await identity.Set<LocalRegistrationInvitation>()
            .AsNoTracking()
            .SingleAsync(invitation => invitation.CreatedBy == "admin:protected-test");
        Assert.NotNull(consumedInvitation.ConsumedAt);
        Assert.Equal(username, consumedInvitation.ConsumedByUsername);
        Assert.Null(consumedInvitation.ReservationId);
        Assert.Null(consumedInvitation.ReservedAt);
        Assert.Null(consumedInvitation.ReservationExpiresAt);

        LocalAccountRegistrationResult replay = await accounts.RegisterAsync(
            UniqueUsername("replay"),
            email: null,
            "a sufficiently long password",
            new LocalRegistrationProtection(issued.Code, "valid-captcha-response", null),
            CancellationToken.None);
        Assert.Equal(LocalAccountRegistrationStatus.InvitationInvalid, replay.Status);
    }

    [Fact]
    public async Task AtomicInvitationConsumeRollsBackOnIdentityValidationAndDatabaseCollision()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        IRegistrationInvitationStore store = scope.ServiceProvider.GetRequiredService<IRegistrationInvitationStore>();
        IAtomicLocalAccountRegistration atomic = scope.ServiceProvider.GetRequiredService<IAtomicLocalAccountRegistration>();
        UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        TestClock clock = scope.ServiceProvider.GetRequiredService<TestClock>();
        var options = new RegistrationProtectionOptions
        {
            InvitationRequired = true,
            InvitationLifetime = TimeSpan.FromDays(7),
            InvitationReservationLifetime = TimeSpan.FromMinutes(5)
        };
        var issuer = new RegistrationInvitationService(store, clock, options);
        var protection = new RegistrationProtectionService(store, new AcceptingCaptchaVerifier(), clock, options);

        RegistrationInvitationIssueResult weakInvitation = await issuer.IssueAsync(
            "admin:atomic-validation",
            CancellationToken.None);
        RegistrationProtectionResult weakAdmission = await protection.AuthorizeAsync(
            new LocalRegistrationProtection(weakInvitation.Code, null, null),
            CancellationToken.None);
        RegistrationInvitationReservation weakReservation = Assert.IsType<RegistrationInvitationReservation>(
            weakAdmission.InvitationReservation);
        LocalIdentityUser weakUser = LocalIdentityUser.Create(
            UniqueUsername("atomicweak"),
            email: null,
            clock.UtcNow);

        AtomicLocalAccountCreationResult weakResult = await atomic.CreateAsync(
            weakUser,
            "short",
            weakReservation,
            clock.UtcNow,
            CancellationToken.None);

        Assert.True(weakResult.InvitationAccepted);
        Assert.False(weakResult.InvitationConsumed);
        Assert.False(weakResult.IdentityResult.Succeeded);
        Assert.Null(await users.FindByNameAsync(weakUser.UserName!));
        await protection.ReleaseInvitationAsync(weakReservation, CancellationToken.None);
        RegistrationProtectionResult weakRetry = await protection.AuthorizeAsync(
            new LocalRegistrationProtection(weakInvitation.Code, null, null),
            CancellationToken.None);
        Assert.Equal(RegistrationProtectionStatus.Accepted, weakRetry.Status);
        await protection.ReleaseInvitationAsync(
            Assert.IsType<RegistrationInvitationReservation>(weakRetry.InvitationReservation),
            CancellationToken.None);

        string duplicateUsername = UniqueUsername("atomicrace");
        LocalIdentityUser existing = LocalIdentityUser.Create(duplicateUsername, email: null, clock.UtcNow);
        Assert.True((await users.CreateAsync(existing, "a sufficiently long existing password")).Succeeded);
        RegistrationInvitationIssueResult collisionInvitation = await issuer.IssueAsync(
            "admin:atomic-collision",
            CancellationToken.None);
        RegistrationProtectionResult collisionAdmission = await protection.AuthorizeAsync(
            new LocalRegistrationProtection(collisionInvitation.Code, null, null),
            CancellationToken.None);
        RegistrationInvitationReservation collisionReservation = Assert.IsType<RegistrationInvitationReservation>(
            collisionAdmission.InvitationReservation);

        users.UserValidators.Clear();
        LocalIdentityUser duplicate = LocalIdentityUser.Create(duplicateUsername, email: null, clock.UtcNow);
        _ = await Assert.ThrowsAsync<DbUpdateException>(() => atomic.CreateAsync(
            duplicate,
            "a sufficiently long duplicate password",
            collisionReservation,
            clock.UtcNow,
            CancellationToken.None));

        await protection.ReleaseInvitationAsync(collisionReservation, CancellationToken.None);
        RegistrationProtectionResult collisionRetry = await protection.AuthorizeAsync(
            new LocalRegistrationProtection(collisionInvitation.Code, null, null),
            CancellationToken.None);
        Assert.Equal(RegistrationProtectionStatus.Accepted, collisionRetry.Status);
        await protection.ReleaseInvitationAsync(
            Assert.IsType<RegistrationInvitationReservation>(collisionRetry.InvitationReservation),
            CancellationToken.None);
    }

    [Fact]
    public async Task CaptchaVerificationUsesFixedOriginFailsClosedAndDoesNotLogSecrets()
    {
        string secretPath = Path.Combine(Path.GetTempPath(), $"captcha-{Guid.NewGuid():N}.secret");
        const string secret = "provider-secret-do-not-log";
        const string responseToken = "browser-token-do-not-log";
        await File.WriteAllTextAsync(secretPath, secret);
        try
        {
            var logger = new CapturingLogger<RegistrationCaptchaVerifier>();
            var handler = new CaptchaHandler(HttpStatusCode.ServiceUnavailable, "{}");
            var options = new RegistrationProtectionOptions
            {
                CaptchaProvider = RegistrationCaptchaProvider.Hcaptcha,
                CaptchaSiteKey = "site-key",
                CaptchaSecretFile = secretPath,
                CaptchaExpectedHostname = "identity-tests.example",
                CaptchaVerificationTimeout = TimeSpan.FromSeconds(2)
            };
            var verifier = new RegistrationCaptchaVerifier(new HttpClient(handler), options, logger);

            Assert.Equal(
                RegistrationCaptchaVerificationResult.Unavailable,
                await verifier.VerifyAsync(
                    RegistrationCaptchaProvider.Hcaptcha,
                    responseToken,
                    "203.0.113.7",
                    CancellationToken.None));
            Assert.Equal(new Uri("https://api.hcaptcha.com/siteverify"), handler.RequestUri);
            Assert.Contains("secret=", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("response=", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("sitekey=", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("remoteip=203.0.113.7", handler.RequestBody, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, string.Join('\n', logger.Messages), StringComparison.Ordinal);
            Assert.DoesNotContain(responseToken, string.Join('\n', logger.Messages), StringComparison.Ordinal);

            handler.StatusCode = HttpStatusCode.OK;
            handler.ResponseBody = "{\"success\":true,\"hostname\":\"identity-tests.example\",\"sitekey\":\"site-key\"}";
            Assert.Equal(
                RegistrationCaptchaVerificationResult.Valid,
                await verifier.VerifyAsync(
                    RegistrationCaptchaProvider.Hcaptcha,
                    responseToken,
                    "203.0.113.7",
                    CancellationToken.None));
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [Fact]
    public async Task TurnstileVerificationBindsHostnameActionCdataRemoteIpAndHandlesProviderFailures()
    {
        string secretPath = Path.Combine(Path.GetTempPath(), $"turnstile-{Guid.NewGuid():N}.secret");
        const string secret = "turnstile-secret-do-not-log";
        const string responseToken = "turnstile-token-do-not-log";
        await File.WriteAllTextAsync(secretPath, secret);
        try
        {
            var logger = new CapturingLogger<RegistrationCaptchaVerifier>();
            var handler = new CaptchaHandler(
                HttpStatusCode.OK,
                "{\"success\":true,\"hostname\":\"identity-tests.example\",\"action\":\"signup\",\"cdata\":\"activitypub_signup\"}");
            var options = new RegistrationProtectionOptions
            {
                CaptchaProvider = RegistrationCaptchaProvider.Turnstile,
                CaptchaSiteKey = "site-key",
                CaptchaSecretFile = secretPath,
                CaptchaExpectedHostname = "identity-tests.example",
                CaptchaExpectedAction = "signup",
                CaptchaExpectedCdata = "activitypub_signup",
                CaptchaVerificationTimeout = TimeSpan.FromSeconds(2)
            };
            var verifier = new RegistrationCaptchaVerifier(new HttpClient(handler), options, logger);

            Assert.Equal(
                RegistrationCaptchaVerificationResult.Valid,
                await verifier.VerifyAsync(
                    RegistrationCaptchaProvider.Turnstile,
                    responseToken,
                    "203.0.113.7",
                    CancellationToken.None));
            Assert.Equal(new Uri("https://challenges.cloudflare.com/turnstile/v0/siteverify"), handler.RequestUri);
            Assert.Contains("remoteip=203.0.113.7", handler.RequestBody, StringComparison.Ordinal);
            Assert.True(TryReadFormField(handler.RequestBody, "idempotency_key", out string? idempotencyKey));
            Assert.True(Guid.TryParse(idempotencyKey, out _));

            handler.ResponseBody =
                "{\"success\":true,\"hostname\":\"other.example\",\"action\":\"signup\",\"cdata\":\"activitypub_signup\"}";
            Assert.Equal(RegistrationCaptchaVerificationResult.Invalid, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, responseToken, "203.0.113.7", CancellationToken.None));

            handler.ResponseBody =
                "{\"success\":true,\"hostname\":\"identity-tests.example\",\"action\":\"login\",\"cdata\":\"activitypub_signup\"}";
            Assert.Equal(RegistrationCaptchaVerificationResult.Invalid, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, responseToken, "203.0.113.7", CancellationToken.None));

            handler.ResponseBody =
                "{\"success\":true,\"hostname\":\"identity-tests.example\",\"action\":\"signup\",\"cdata\":\"wrong\"}";
            Assert.Equal(RegistrationCaptchaVerificationResult.Invalid, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, responseToken, "203.0.113.7", CancellationToken.None));

            handler.ResponseBody = "{\"success\":false,\"error-codes\":[\"timeout-or-duplicate\"]}";
            Assert.Equal(RegistrationCaptchaVerificationResult.Invalid, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, responseToken, "203.0.113.7", CancellationToken.None));

            handler.StatusCode = HttpStatusCode.ServiceUnavailable;
            Assert.Equal(RegistrationCaptchaVerificationResult.Unavailable, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, responseToken, "203.0.113.7", CancellationToken.None));

            handler.StatusCode = HttpStatusCode.OK;
            handler.TransientFailuresRemaining = 1;
            handler.ResponseBody =
                "{\"success\":true,\"hostname\":\"identity-tests.example\",\"action\":\"signup\",\"cdata\":\"activitypub_signup\"}";
            Assert.Equal(RegistrationCaptchaVerificationResult.Valid, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, responseToken, "203.0.113.7", CancellationToken.None));
            string[] retriedBodies = handler.RequestBodies.TakeLast(2).ToArray();
            Assert.Equal(2, retriedBodies.Length);
            Assert.True(TryReadFormField(retriedBodies[0], "idempotency_key", out string? firstRetryId));
            Assert.True(TryReadFormField(retriedBodies[1], "idempotency_key", out string? secondRetryId));
            Assert.Equal(firstRetryId, secondRetryId);
            Assert.DoesNotContain(secret, string.Join('\n', logger.Messages), StringComparison.Ordinal);
            Assert.DoesNotContain(responseToken, string.Join('\n', logger.Messages), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [Fact]
    public async Task TurnstileRejectsOversizedTokenWithoutCallingProviderAndTimesOutClosed()
    {
        string secretPath = Path.Combine(Path.GetTempPath(), $"turnstile-{Guid.NewGuid():N}.secret");
        await File.WriteAllTextAsync(secretPath, "test-secret");
        try
        {
            var handler = new CaptchaHandler(HttpStatusCode.OK, "{\"success\":true}");
            var options = new RegistrationProtectionOptions
            {
                CaptchaProvider = RegistrationCaptchaProvider.Turnstile,
                CaptchaSiteKey = "site-key",
                CaptchaSecretFile = secretPath,
                CaptchaExpectedHostname = "identity-tests.example",
                CaptchaVerificationTimeout = TimeSpan.FromMilliseconds(10)
            };
            var verifier = new RegistrationCaptchaVerifier(
                new HttpClient(handler),
                options,
                new CapturingLogger<RegistrationCaptchaVerifier>());

            Assert.Equal(RegistrationCaptchaVerificationResult.Invalid, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, new string('x', 2_049), null, CancellationToken.None));
            Assert.Null(handler.RequestUri);

            handler.Delay = TimeSpan.FromSeconds(5);
            Assert.Equal(RegistrationCaptchaVerificationResult.Unavailable, await verifier.VerifyAsync(
                RegistrationCaptchaProvider.Turnstile, "bounded-token", null, CancellationToken.None));
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    private static bool TryReadFormField(string body, string name, out string? value)
    {
        foreach (string field in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = field.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.Ordinal))
            {
                value = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
                return true;
            }
        }

        value = null;
        return false;
    }

    [Fact]
    public async Task RegistrationHashesPasswordAndCreatesActorAndKeyBeforeActivation()
    {
        string username = UniqueUsername("register");
        const string password = "correct horse battery staple";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();

        LocalAccountRegistrationResult result = await accounts.RegisterAsync(
            username,
            email: null,
            password,
            CancellationToken.None);

        Assert.Equal(LocalAccountRegistrationStatus.Created, result.Status);
        LocalIdentityUser user = Assert.IsType<LocalIdentityUser>(result.User);
        Assert.Equal(LocalAccountProvisioningState.Active, user.ProvisioningState);
        Assert.Equal($"https://identity-tests.example/users/{username}", user.LocalActorIri);

        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        LocalIdentityUser persistedUser = await identity.Users.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.NotNull(persistedUser.PasswordHash);
        Assert.DoesNotContain(password, persistedUser.PasswordHash, StringComparison.Ordinal);
        PasswordVerificationResult verified = new PasswordHasher<LocalIdentityUser>().VerifyHashedPassword(
            persistedUser,
            persistedUser.PasswordHash,
            password);
        Assert.NotEqual(PasswordVerificationResult.Failed, verified);

        IDbContextFactory<FederationDbContext> federationFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext federation = await federationFactory.CreateDbContextAsync();
        var actor = await federation.LocalActors.SingleAsync(candidate => candidate.Id == user.LocalActorId);
        var key = await federation.ActorKeys.SingleAsync(candidate => candidate.OwnerIri == actor.Iri);
        Assert.Equal(username, actor.Username);
        Assert.StartsWith("ap-", key.PrivateKeyHandle, StringComparison.Ordinal);
        Assert.Contains("BEGIN PUBLIC KEY", key.PublicKeyPem, StringComparison.Ordinal);

        LocalAccountRegistrationResult duplicate = await accounts.RegisterAsync(
            username,
            email: null,
            password,
            CancellationToken.None);
        Assert.Equal(LocalAccountRegistrationStatus.UsernameUnavailable, duplicate.Status);
        Assert.Contains("USERNAME_UNAVAILABLE", duplicate.SafeErrorCodes);
    }

    [Fact]
    public async Task PasswordAuthenticationDistinguishesSecondFactorFailureAndPersistsSignIn()
    {
        string username = UniqueUsername("twofactor");
        const string password = "a sufficiently long password";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        LocalAccountRegistrationResult registration = await accounts.RegisterAsync(
            username,
            email: null,
            password,
            CancellationToken.None);
        LocalIdentityUser user = Assert.IsType<LocalIdentityUser>(registration.User);

        IdentityResult reset = await users.ResetAuthenticatorKeyAsync(user);
        Assert.True(reset.Succeeded);
        IdentityResult enabled = await users.SetTwoFactorEnabledAsync(user, enabled: true);
        Assert.True(enabled.Succeeded);

        LocalPasskeyChallengeResult noPasskey = await accounts.BeginPasskeyAuthenticationAsync(
            username,
            password,
            CancellationToken.None);
        Assert.Equal(LocalPasskeyChallengeStatus.PasskeyUnavailable, noPasskey.Status);
        Assert.Null(noPasskey.RequestOptionsJson);

        LocalAccountAuthenticationResult challenge = await accounts.AuthenticatePasswordAsync(
            username,
            password,
            authenticatorCode: null,
            CancellationToken.None);
        Assert.Equal(LocalAccountAuthenticationStatus.TwoFactorRequired, challenge.Status);

        LocalAccountAuthenticationResult invalid = await accounts.AuthenticatePasswordAsync(
            username,
            password,
            authenticatorCode: "000000",
            CancellationToken.None);
        Assert.Equal(LocalAccountAuthenticationStatus.InvalidSecondFactor, invalid.Status);

        string authenticatorKey = Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(user));
        string code = GenerateAuthenticatorCode(authenticatorKey, DateTimeOffset.UtcNow);
        Assert.True(await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code));
        LocalAccountAuthenticationResult valid = await accounts.AuthenticatePasswordAsync(
            username,
            password,
            code,
            CancellationToken.None);
        Assert.Equal(LocalAccountAuthenticationStatus.Succeeded, valid.Status);
        Assert.NotNull(valid.User?.LastSignedInAt);

        LocalPasskeyAuthenticationResult assertionWithoutChallenge = await accounts.AuthenticatePasskeyAsync(
            "{}",
            CancellationToken.None);
        Assert.Equal(LocalPasskeyAuthenticationStatus.InvalidAssertion, assertionWithoutChallenge.Status);
        Assert.Null(assertionWithoutChallenge.User);
    }

    [Fact]
    public async Task RepeatedWrongPasswordsLockTheAccountWithoutChangingActorState()
    {
        string username = UniqueUsername("lockout");
        const string password = "another sufficiently long password";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        LocalAccountRegistrationResult registration = await accounts.RegisterAsync(
            username,
            email: null,
            password,
            CancellationToken.None);
        LocalIdentityUser user = Assert.IsType<LocalIdentityUser>(registration.User);

        LocalAccountAuthenticationResult? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            last = await accounts.AuthenticatePasswordAsync(
                username,
                "definitely-wrong",
                authenticatorCode: null,
                CancellationToken.None);
        }

        Assert.Equal(LocalAccountAuthenticationStatus.LockedOut, last?.Status);
        LocalAccountLookup? lookup = await accounts.FindAsync(username, CancellationToken.None);
        Assert.Equal(LocalAccountProvisioningState.Active, lookup?.ProvisioningState);
        Assert.Equal(user.LocalActorIri, lookup?.ActorIri);
    }

    [Fact]
    public async Task PasswordResetIsHashedExpiringSingleUseAndPreservesTokenAfterPasswordValidationFailure()
    {
        string username = UniqueUsername("reset");
        string email = $"{username}@identity-tests.example";
        const string oldPassword = "the old sufficiently long password";
        const string newPassword = "the new sufficiently long password";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        IPasswordResetService resets = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        TestPasswordResetEmailSender sender = scope.ServiceProvider.GetRequiredService<TestPasswordResetEmailSender>();
        int messagesBefore = sender.Messages.Count;

        LocalAccountRegistrationResult registration = await accounts.RegisterAsync(
            username,
            email,
            oldPassword,
            CancellationToken.None);
        LocalIdentityUser user = Assert.IsType<LocalIdentityUser>(registration.User);
        string confirmationToken = await users.GenerateEmailConfirmationTokenAsync(user);
        Assert.True((await users.ConfirmEmailAsync(user, confirmationToken)).Succeeded);

        await resets.RequestAsync(
            username,
            email,
            new Uri("https://identity-tests.example"),
            CancellationToken.None);

        PasswordResetEmail message = Assert.Single(sender.Messages.Skip(messagesBefore));
        Assert.Equal(email, message.RecipientAddress);
        Assert.Equal("/reset-password", message.ResetUri.AbsolutePath);
        string externalToken = message.ResetUri.Fragment.TrimStart('#');
        Assert.NotEmpty(externalToken);

        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        LocalPasswordResetRequest persisted = await identity.Set<LocalPasswordResetRequest>()
            .SingleAsync(request => request.UserId == user.Id);
        Assert.Equal(32, persisted.TokenHash.Length);
        Assert.NotEqual(Encoding.ASCII.GetBytes(externalToken), persisted.TokenHash);
        Assert.True(CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.ASCII.GetBytes(externalToken)),
            persisted.TokenHash));

        PasswordResetCompletionResult weak = await resets.ResetAsync(
            externalToken,
            "short",
            CancellationToken.None);
        Assert.Equal(PasswordResetCompletionStatus.InvalidPassword, weak.Status);

        PasswordResetCompletionResult completed = await resets.ResetAsync(
            externalToken,
            newPassword,
            CancellationToken.None);
        Assert.Equal(PasswordResetCompletionStatus.Succeeded, completed.Status);

        PasswordResetCompletionResult replay = await resets.ResetAsync(
            externalToken,
            "a third sufficiently long password",
            CancellationToken.None);
        Assert.Equal(PasswordResetCompletionStatus.InvalidOrExpiredToken, replay.Status);
        Assert.Equal(
            LocalAccountAuthenticationStatus.InvalidCredentials,
            (await accounts.AuthenticatePasswordAsync(username, oldPassword, null, CancellationToken.None)).Status);
        Assert.Equal(
            LocalAccountAuthenticationStatus.Succeeded,
            (await accounts.AuthenticatePasswordAsync(username, newPassword, null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task PasswordResetRequestCooldownAndExpiryArePersisted()
    {
        string username = UniqueUsername("expiry");
        string email = $"{username}@identity-tests.example";
        const string password = "a sufficiently long password";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        UserManager<LocalIdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        IPasswordResetService resets = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        TestPasswordResetEmailSender sender = scope.ServiceProvider.GetRequiredService<TestPasswordResetEmailSender>();
        TestClock clock = scope.ServiceProvider.GetRequiredService<TestClock>();
        int messagesBefore = sender.Messages.Count;

        LocalIdentityUser user = Assert.IsType<LocalIdentityUser>((await accounts.RegisterAsync(
            username,
            email,
            password,
            CancellationToken.None)).User);
        string confirmationToken = await users.GenerateEmailConfirmationTokenAsync(user);
        Assert.True((await users.ConfirmEmailAsync(user, confirmationToken)).Succeeded);

        await resets.RequestAsync(username, email, new Uri("https://identity-tests.example"), CancellationToken.None);
        await resets.RequestAsync(username, email, new Uri("https://identity-tests.example"), CancellationToken.None);
        PasswordResetEmail message = Assert.Single(sender.Messages.Skip(messagesBefore));

        clock.Advance(TimeSpan.FromMinutes(31));
        PasswordResetCompletionResult expired = await resets.ResetAsync(
            message.ResetUri.Fragment.TrimStart('#'),
            "another sufficiently long password",
            CancellationToken.None);
        Assert.Equal(PasswordResetCompletionStatus.InvalidOrExpiredToken, expired.Status);
    }

    [Fact]
    public async Task ConcurrentPasswordResetConsumesTokenExactlyOnce()
    {
        string username = UniqueUsername("race");
        string email = $"{username}@identity-tests.example";
        const string password = "a sufficiently long password";
        await using (AsyncServiceScope setup = fixture.Services.CreateAsyncScope())
        {
            ILocalAccountService accounts = setup.ServiceProvider.GetRequiredService<ILocalAccountService>();
            UserManager<LocalIdentityUser> users = setup.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = Assert.IsType<LocalIdentityUser>((await accounts.RegisterAsync(
                username,
                email,
                password,
                CancellationToken.None)).User);
            string confirmationToken = await users.GenerateEmailConfirmationTokenAsync(user);
            Assert.True((await users.ConfirmEmailAsync(user, confirmationToken)).Succeeded);
        }

        TestPasswordResetEmailSender sender = fixture.Services.GetRequiredService<TestPasswordResetEmailSender>();
        int messagesBefore = sender.Messages.Count;
        await RequestResetAsync(fixture.Services, username, email);
        string token = Assert.Single(sender.Messages.Skip(messagesBefore)).ResetUri.Fragment.TrimStart('#');

        PasswordResetCompletionResult[] results = await Task.WhenAll(
            CompleteResetAsync(fixture.Services, token, "one sufficiently long password"),
            CompleteResetAsync(fixture.Services, token, "two sufficiently long password"));
        Assert.Single(results, result => result.Status == PasswordResetCompletionStatus.Succeeded);
        Assert.Single(results, result => result.Status == PasswordResetCompletionStatus.InvalidOrExpiredToken);
    }

    [Fact]
    public async Task EmailConfirmationIsHashedAndSingleUseAndMarksIdentityVerified()
    {
        string username = UniqueUsername("confirm");
        string email = $"{username}@identity-tests.example";
        const string password = "a sufficiently long password";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        IEmailConfirmationService confirmations = scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>();
        TestPasswordResetEmailSender sender = scope.ServiceProvider.GetRequiredService<TestPasswordResetEmailSender>();
        int messagesBefore = sender.Confirmations.Count;
        LocalIdentityUser user = Assert.IsType<LocalIdentityUser>((await accounts.RegisterAsync(
            username,
            email,
            password,
            CancellationToken.None)).User);

        Assert.False(user.EmailConfirmed);
        await confirmations.RequestForUserAsync(
            user,
            new Uri("https://identity-tests.example"),
            CancellationToken.None);
        EmailConfirmationEmail message = Assert.Single(sender.Confirmations.Skip(messagesBefore));
        Assert.Equal("/signup-complete", message.ConfirmationUri.AbsolutePath);
        string externalToken = message.ConfirmationUri.Fragment.TrimStart('#');

        IDbContextFactory<LocalIdentityDbContext> identityFactory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        LocalEmailConfirmationRequest persisted = await identity.Set<LocalEmailConfirmationRequest>()
            .SingleAsync(request => request.UserId == user.Id);
        Assert.Equal(32, persisted.TokenHash.Length);
        Assert.True(CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.ASCII.GetBytes(externalToken)),
            persisted.TokenHash));
        EmailConfirmationResult completed = await confirmations.ConfirmAsync(externalToken, CancellationToken.None);
        Assert.Equal(EmailConfirmationStatus.Succeeded, completed.Status);
        Assert.True(completed.User?.EmailConfirmed);
        Assert.Equal(
            EmailConfirmationStatus.InvalidOrExpiredToken,
            (await confirmations.ConfirmAsync(externalToken, CancellationToken.None)).Status);
        Assert.Equal(
            LocalAccountAuthenticationStatus.Succeeded,
            (await accounts.AuthenticatePasswordAsync(username, password, null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task EmailConfirmationExpiryAndDeliveryFailureDoNotLeaveUsableReservations()
    {
        string failedUsername = UniqueUsername("cffail");
        string failedEmail = $"{failedUsername}@identity-tests.example";
        string expiredUsername = UniqueUsername("cfexpiry");
        string expiredEmail = $"{expiredUsername}@identity-tests.example";
        const string password = "a sufficiently long password";
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        IEmailConfirmationService confirmations = scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>();
        TestPasswordResetEmailSender sender = scope.ServiceProvider.GetRequiredService<TestPasswordResetEmailSender>();
        TestClock clock = scope.ServiceProvider.GetRequiredService<TestClock>();

        LocalIdentityUser failedUser = Assert.IsType<LocalIdentityUser>((await accounts.RegisterAsync(
            failedUsername,
            failedEmail,
            password,
            CancellationToken.None)).User);
        sender.FailNextConfirmation = true;
        await confirmations.RequestForUserAsync(failedUser, new Uri("https://identity-tests.example"), CancellationToken.None);
        IDbContextFactory<LocalIdentityDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using (LocalIdentityDbContext verification = await factory.CreateDbContextAsync())
        {
            Assert.False(await verification.Set<LocalEmailConfirmationRequest>()
                .AnyAsync(request => request.UserId == failedUser.Id));
        }

        LocalIdentityUser expiredUser = Assert.IsType<LocalIdentityUser>((await accounts.RegisterAsync(
            expiredUsername,
            expiredEmail,
            password,
            CancellationToken.None)).User);
        int before = sender.Confirmations.Count;
        await confirmations.RequestForUserAsync(expiredUser, new Uri("https://identity-tests.example"), CancellationToken.None);
        string token = Assert.Single(sender.Confirmations.Skip(before)).ConfirmationUri.Fragment.TrimStart('#');
        clock.Advance(TimeSpan.FromHours(25));

        Assert.Equal(
            EmailConfirmationStatus.InvalidOrExpiredToken,
            (await confirmations.ConfirmAsync(token, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task EmailConfirmationResendCooldownAndConcurrentClaimAreDurable()
    {
        string username = UniqueUsername("cfrace");
        string email = $"{username}@identity-tests.example";
        const string password = "a sufficiently long password";
        await using (AsyncServiceScope setup = fixture.Services.CreateAsyncScope())
        {
            ILocalAccountService accounts = setup.ServiceProvider.GetRequiredService<ILocalAccountService>();
            _ = Assert.IsType<LocalIdentityUser>((await accounts.RegisterAsync(
                username,
                email,
                password,
                CancellationToken.None)).User);
        }

        TestPasswordResetEmailSender sender = fixture.Services.GetRequiredService<TestPasswordResetEmailSender>();
        int messagesBefore = sender.Confirmations.Count;
        await RequestConfirmationAsync(fixture.Services, username, email);
        await RequestConfirmationAsync(fixture.Services, username, email);
        string token = Assert.Single(sender.Confirmations.Skip(messagesBefore)).ConfirmationUri.Fragment.TrimStart('#');

        EmailConfirmationResult[] results = await Task.WhenAll(
            CompleteConfirmationAsync(fixture.Services, token),
            CompleteConfirmationAsync(fixture.Services, token));
        Assert.Single(results, result => result.Status == EmailConfirmationStatus.Succeeded);
        Assert.Single(results, result => result.Status == EmailConfirmationStatus.InvalidOrExpiredToken);
    }

    private static async Task RequestResetAsync(IServiceProvider services, string username, string email)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPasswordResetService>().RequestAsync(
            username,
            email,
            new Uri("https://identity-tests.example"),
            CancellationToken.None);
    }

    private sealed class AcceptingCaptchaVerifier : IRegistrationCaptchaVerifier
    {
        public Task<RegistrationCaptchaVerificationResult> VerifyAsync(
            RegistrationCaptchaProvider provider,
            string response,
            string? remoteIpAddress,
            CancellationToken cancellationToken) =>
            Task.FromResult(RegistrationCaptchaVerificationResult.Valid);
    }

    private sealed class ConditionalCaptchaVerifier(string acceptedResponse) : IRegistrationCaptchaVerifier
    {
        public Task<RegistrationCaptchaVerificationResult> VerifyAsync(
            RegistrationCaptchaProvider provider,
            string response,
            string? remoteIpAddress,
            CancellationToken cancellationToken) => Task.FromResult(
                string.Equals(response, acceptedResponse, StringComparison.Ordinal)
                    ? RegistrationCaptchaVerificationResult.Valid
                    : RegistrationCaptchaVerificationResult.Invalid);
    }

    private sealed class CaptchaHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = statusCode;

        public string ResponseBody { get; set; } = responseBody;

        public Uri? RequestUri { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        public List<string> RequestBodies { get; } = [];

        public TimeSpan Delay { get; set; }

        public int TransientFailuresRemaining { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(RequestBody);
            HttpStatusCode effectiveStatus = TransientFailuresRemaining > 0
                ? HttpStatusCode.ServiceUnavailable
                : StatusCode;
            if (TransientFailuresRemaining > 0)
            {
                TransientFailuresRemaining--;
            }

            return new HttpResponseMessage(effectiveStatus)
            {
                Content = new StringContent(ResponseBody)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
                }
            };
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private static async Task<PasswordResetCompletionResult> CompleteResetAsync(
        IServiceProvider services,
        string token,
        string password)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPasswordResetService>()
            .ResetAsync(token, password, CancellationToken.None);
    }

    private static async Task RequestConfirmationAsync(IServiceProvider services, string username, string email)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>().RequestAsync(
            username,
            email,
            new Uri("https://identity-tests.example"),
            CancellationToken.None);
    }

    private static async Task<EmailConfirmationResult> CompleteConfirmationAsync(
        IServiceProvider services,
        string token)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>()
            .ConfirmAsync(token, CancellationToken.None);
    }

    [Fact]
    public async Task ConcurrentRegistrationCreatesExactlyOneAccountAndOneActor()
    {
        string username = UniqueUsername("concurrent");
        const string password = "a sufficiently long concurrent password";

        static async Task<LocalAccountRegistrationResult> RegisterAsync(
            IServiceProvider services,
            string username,
            string password)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
            return await accounts.RegisterAsync(username, email: null, password, CancellationToken.None);
        }

        LocalAccountRegistrationResult[] results = await Task.WhenAll(
            RegisterAsync(fixture.Services, username, password),
            RegisterAsync(fixture.Services, username, password));

        Assert.Single(results, result => result.Status == LocalAccountRegistrationStatus.Created);
        Assert.Single(results, result => result.Status == LocalAccountRegistrationStatus.UsernameUnavailable);

        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<LocalIdentityDbContext> identityFactory = verificationScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        string normalizedUsername = username.ToUpperInvariant();
        Assert.Single(await identity.Users.Where(user => user.NormalizedUserName == normalizedUsername).ToListAsync());

        IDbContextFactory<FederationDbContext> federationFactory = verificationScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext federation = await federationFactory.CreateDbContextAsync();
        Assert.Single(await federation.LocalActors.Where(actor => actor.Username == username).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRegistrationWithSameEmailRejectsOneBeforeActorProvisioning()
    {
        string suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..10];
        string firstUsername = "emaila" + suffix;
        string secondUsername = "emailb" + suffix;
        string email = $"parallel-{suffix}@example.test";
        const string password = "a sufficiently long concurrent password";

        static async Task<LocalAccountRegistrationResult> RegisterAsync(
            IServiceProvider services,
            string username,
            string email,
            string password)
        {
            await using AsyncServiceScope scope = services.CreateAsyncScope();
            ILocalAccountService accounts = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
            return await accounts.RegisterAsync(username, email, password, CancellationToken.None);
        }

        LocalAccountRegistrationResult[] results = await Task.WhenAll(
            RegisterAsync(fixture.Services, firstUsername, email, password),
            RegisterAsync(fixture.Services, secondUsername, email, password));

        Assert.Single(results, result => result.Status == LocalAccountRegistrationStatus.Created);
        Assert.Single(results, result => result.Status == LocalAccountRegistrationStatus.EmailUnavailable);

        await using AsyncServiceScope verificationScope = fixture.Services.CreateAsyncScope();
        IDbContextFactory<LocalIdentityDbContext> identityFactory = verificationScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<LocalIdentityDbContext>>();
        await using LocalIdentityDbContext identity = await identityFactory.CreateDbContextAsync();
        string normalizedEmail = email.ToUpperInvariant();
        Assert.Single(await identity.Users.Where(user => user.NormalizedEmail == normalizedEmail).ToListAsync());

        IDbContextFactory<FederationDbContext> federationFactory = verificationScope.ServiceProvider
            .GetRequiredService<IDbContextFactory<FederationDbContext>>();
        await using FederationDbContext federation = await federationFactory.CreateDbContextAsync();
        Assert.Single(await federation.LocalActors
            .Where(actor => actor.Username == firstUsername || actor.Username == secondUsername)
            .ToListAsync());
    }

    private static string UniqueUsername(string prefix) =>
        prefix + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)[..10];

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "This test must reproduce the HMAC-SHA1 TOTP profile used by ASP.NET Core Identity authenticators; it is not used for password hashing or general cryptography.")]
    private static string GenerateAuthenticatorCode(string base32Key, DateTimeOffset now)
    {
        byte[] key = DecodeBase32(base32Key);
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);
        byte[] hash = HMACSHA1.HashData(key, counter);
        int offset = hash[^1] & 0x0f;
        int binaryCode = ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(hash);
        return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>((value.Length * 5 + 7) / 8);
        int buffer = 0;
        int bits = 0;
        foreach (char character in value)
        {
            int digit = alphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);
            Assert.InRange(digit, 0, alphabet.Length - 1);
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits < 8)
            {
                continue;
            }

            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        return [.. bytes];
    }
}
