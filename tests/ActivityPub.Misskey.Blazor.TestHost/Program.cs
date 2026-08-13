using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.Routing;
using ActivityPub.Misskey.Blazor.Security;
using ActivityPub.Misskey.Blazor.Streaming;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
var browserDiagnostics = new BrowserTestDiagnostics();
var browserCircuitDiagnostics = new BrowserTestCircuitDiagnostics();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddProvider(new BrowserTestLoggerProvider(browserDiagnostics));
builder.Services.AddSingleton(browserDiagnostics);
builder.Services.AddSingleton(browserCircuitDiagnostics);
builder.Services.AddSingleton<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(browserCircuitDiagnostics);
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddAuthentication("browser-tests").AddCookie("browser-tests", options =>
{
    options.Cookie.Name = "misskey-browser-tests";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthorization();
builder.Services.AddRazorComponents().AddInteractiveServerComponents(options =>
{
    options.DetailedErrors = false;
    // Browser lifecycle tests need disconnected circuits to reach component disposal without
    // retaining every short-lived Playwright context for the production default interval.
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(1);
});
MisskeyFrontendRouteAssemblies frontendRouteAssemblies =
    MisskeyFrontendRouteAssemblies.FromRouteComponents(typeof(ActivityPub.Misskey.Blazor.TestHost.KeyValueContractPage));
builder.Services.AddMisskeyBlazorFrontend(new MisskeyFrontendRuntimeConfiguration(
    MisskeyFrontendRuntimeConfiguration.PortVersion,
    new Uri("https://source.example.test/activitypub-server", UriKind.Absolute),
    new Uri("http://127.0.0.1:5099", UriKind.Absolute),
    LocalAccountsEnabled: true), frontendRouteAssemblies);
builder.Services.AddScoped<IAuthenticatedActorContext, BrowserTestAuthenticatedActorContext>();
var browserInstance = new BrowserTestInstancePresentationService();
builder.Services.AddSingleton<IInstancePresentationService>(browserInstance);
var browserAbout = new BrowserTestAboutPresentationService();
builder.Services.AddSingleton<IAboutPresentationService>(browserAbout);
var browserAnnouncements = new BrowserTestAnnouncementPresentationService();
builder.Services.AddSingleton<IAnnouncementPresentationService>(browserAnnouncements);
builder.Services.AddSingleton<IAnnouncementPagePresentationService>(browserAnnouncements);
var browserTimeline = new BrowserTestTimelinePresentationService();
builder.Services.AddSingleton<ITimelinePresentationService>(browserTimeline);
builder.Services.AddSingleton<INotePagePresentationService>(browserTimeline);
builder.Services.AddSingleton<INoteDeletionPresentationService>(new BrowserTestNoteDeletionPresentationService());
var visibleUsers = new BrowserTestVisibleUsersPresentationService();
builder.Services.AddSingleton<IVisibleUsersPresentationService>(visibleUsers);
builder.Services.AddSingleton<IAvatarsPresentationService>(new BrowserTestAvatarsPresentationService());
builder.Services.AddSingleton<IAutocompletePresentationService>(new BrowserTestAutocompletePresentationService());
builder.Services.AddSingleton<IHashtagTrendPresentationService>(new BrowserTestHashtagTrendPresentationService());
builder.Services.AddSingleton<ISettingsPresentationService>(new BrowserTestSettingsPresentationService());
builder.Services.AddSingleton<IAdminPresentationService>(new BrowserTestAdminPresentationService());
builder.Services.AddSingleton<IReactionDetailsPresentationService>(new BrowserTestReactionDetailsPresentationService());
builder.Services.AddSingleton<IRenoteDetailsPresentationService>(new BrowserTestRenoteDetailsPresentationService());
var browserNotifications = new BrowserTestNotificationPresentationService();
builder.Services.AddSingleton<INotificationPresentationService>(browserNotifications);
var browserNotificationStream = new BrowserTestNotificationSubscriptionService();
builder.Services.AddSingleton<INotificationSubscriptionService>(browserNotificationStream);
var browserTimelineStream = new BrowserTestTimelineSubscriptionService();
builder.Services.AddSingleton<ITimelineSubscriptionService>(browserTimelineStream);
builder.Services.AddSingleton<ICurrentAccountPresentationService>(new BrowserTestCurrentAccountPresentationService());
builder.Services.AddSingleton<IComposerMediaService>(new BrowserTestComposerMediaService());
var browserUserPreview = new BrowserTestUserPreviewPresentationService();
builder.Services.AddSingleton<IUserPreviewPresentationService>(browserUserPreview);
builder.Services.AddSingleton<IUserFollowRelationsPresentationService>(
    new BrowserTestUserFollowRelationsPresentationService(browserUserPreview));
builder.Services.AddSingleton<IUserSearchPresentationService>(new BrowserTestUserSearchPresentationService(browserUserPreview));
builder.Services.AddSingleton<IUserPagePresentationService>(new BrowserTestUserPagePresentationService(
    browserUserPreview,
    browserTimeline));
var browserRelationships = new BrowserTestRelationshipSubscriptionService();
builder.Services.AddSingleton<IRelationshipSubscriptionService>(browserRelationships);

WebApplication app = builder.Build();
app.UsePathBase("/");
app.UseMisskeyFrontendLocalization();
app.Use(async (context, next) =>
{
    context.Items[FrontendCspNonce.HttpContextItemKey] = FrontendCspNonce.Create();
    if (context.Request.Path.Equals("/_content/ActivityPub.Misskey.Blazor/service-worker.js"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Service-Worker-Allowed"] = "/";
            context.Response.Headers.CacheControl = "no-cache,no-store";
            return Task.CompletedTask;
        });
    }

    await next().ConfigureAwait(false);
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapGet("/__test/sign-in", async context =>
{
    Claim[] claims =
    [
        new(ClaimTypes.NameIdentifier, "browser-alice"),
        new(ClaimTypes.Name, "alice"),
        new("preferred_username", "alice"),
        new(ClaimTypes.Role, "activitypub-admin")
    ];
    await context.SignInAsync(
        "browser-tests",
        new ClaimsPrincipal(new ClaimsIdentity(claims, "browser-tests")));
    context.Response.Redirect("/");
});
app.MapPost("/auth/logout", async (
    HttpContext context,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
    await context.SignOutAsync("browser-tests").ConfigureAwait(false);
    return Results.Redirect("/");
});
app.MapGet("/auth/username-available", (string? username) => Results.Json(new
{
    available = string.Equals(username, "available_user", StringComparison.Ordinal)
}));
app.MapGet("/avatar/{account}", () => Results.Redirect("/static-assets/favicon.png"));
app.MapPost("/auth/credentials", () => Results.Json(
    new { status = "two-factor-required", errorCode = "TWO_FACTOR_REQUIRED" },
    statusCode: StatusCodes.Status401Unauthorized));
// MkSignin deliberately posts to the Misskey v12-compatible endpoint. Keep the browser
// fixture on that same wire contract instead of testing the retired frontend-only alias.
app.MapPost("/api/signin", () => Results.Json(
    new { status = "two-factor-required", errorCode = "TWO_FACTOR_REQUIRED" },
    statusCode: StatusCodes.Status401Unauthorized));
app.MapPost("/__test/initial-setup/{state}", (string state) =>
{
    if (state is not ("required" or "complete"))
    {
        return Results.BadRequest();
    }

    browserInstance.SetSetupRequired(state == "required");
    return Results.NoContent();
}).DisableAntiforgery();
app.MapGet("/__test/initial-setup-state", () => Results.Json(new
{
    browserInstance.SetupRequired,
    browserInstance.SetupCalls,
    browserInstance.LastSetupUsername
}));
app.MapPost("/api/admin/accounts/create", async (HttpContext context) =>
{
    JsonElement body = await context.Request.ReadFromJsonAsync<JsonElement>();
    string username = body.TryGetProperty("username", out JsonElement usernameValue)
        ? usernameValue.GetString() ?? string.Empty
        : string.Empty;
    string password = body.TryGetProperty("password", out JsonElement passwordValue)
        ? passwordValue.GetString() ?? string.Empty
        : string.Empty;
    if (!browserInstance.TryCompleteSetup(username, password))
    {
        return Results.Json(
            new { error = new { code = "INITIAL_SETUP_FAILED" } },
            statusCode: StatusCodes.Status400BadRequest);
    }

    Claim[] claims =
    [
        new(ClaimTypes.NameIdentifier, "browser-initial-admin"),
        new(ClaimTypes.Name, username),
        new("preferred_username", username),
        new(ClaimTypes.Role, "activitypub-admin")
    ];
    await context.SignInAsync(
        "browser-tests",
        new ClaimsPrincipal(new ClaimsIdentity(claims, "browser-tests")));
    return Results.Json(new { id = "browser-initial-admin", username, token = "browser-test-token" });
}).DisableAntiforgery();
app.MapGet("/__test/reaction-state", () => Results.Json(new
{
    browserTimeline.CurrentNote.ViewerReaction,
    browserTimeline.CurrentNote.Reactions,
    browserTimeline.ReactionCalls,
    browserTimeline.LastRemove
}));
app.MapGet("/__test/renote-state", () => Results.Json(new
{
    browserTimeline.RenoteCalls,
    browserTimeline.LastRenotedId
}));
app.MapPost("/__test/reset-renote", () =>
{
    browserTimeline.ResetRenote();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapGet("/__test/notification-state", () => Results.Json(new
{
    browserNotifications.MarkReadCalls,
    browserNotifications.MarkAllReadCalls,
    browserNotifications.LastMarkedId,
    browserNotificationStream.ActiveSubscriptions,
    browserNotificationStream.Cursor
}));
app.MapGet("/__test/announcement-state", () => Results.Json(new
{
    browserAnnouncements.MarkReadCalls,
    browserAnnouncements.LastMarkedId
}));
app.MapGet("/__test/about-state", () => Results.Json(new
{
    browserAbout.StatisticsCalls,
    browserAbout.FederationCalls,
    browserAbout.LastQuery
}));
app.MapPost("/__test/reset-announcements", () =>
{
    browserAnnouncements.Reset();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapPost("/__test/notification-stream/{variant}", (string variant) =>
{
    NotificationViewModel? notification = variant switch
    {
        "duplicate" => BrowserTestNotificationPresentationService.MentionFixture,
        "new" => BrowserTestNotificationPresentationService.StreamFixture,
        _ => null
    };
    if (notification is null)
    {
        return Results.BadRequest(new { errorCode = "UNKNOWN_NOTIFICATION_FIXTURE" });
    }

    browserNotificationStream.Publish(notification);
    return Results.NoContent();
}).DisableAntiforgery();
app.MapPost("/__test/registration-protection/{provider}", (string provider) =>
{
    if (provider is not ("none" or "hcaptcha" or "recaptcha" or "turnstile"))
    {
        return Results.BadRequest(new { errorCode = "UNKNOWN_CAPTCHA_PROVIDER" });
    }

    browserInstance.SetProtection(provider);
    return Results.NoContent();
}).DisableAntiforgery();
app.MapGet("/__test/poll-state", () => Results.Json(new
{
    browserTimeline.VoteCalls,
    browserTimeline.LastVoteChoice,
    browserTimeline.CurrentNote.Poll
}));
app.MapGet("/__test/compose-state", () => Results.Json(new
{
    browserTimeline.CreateCalls,
    browserTimeline.LastCreatedText
}));
app.MapPost("/__test/timeline-stream-note", () =>
{
    browserTimelineStream.Publish(browserTimeline.CurrentNote);
    return Results.NoContent();
}).DisableAntiforgery();
app.MapGet("/__test/timeline-stream-state", () => Results.Json(new
{
    browserTimelineStream.ActiveSubscriptions,
    browserTimelineStream.Cursor
}));
app.MapPost("/__test/reset-compose", () =>
{
    browserTimeline.ResetCompose();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapPost("/__test/reset-reaction", () =>
{
    browserTimeline.ResetReaction();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapPost("/__test/poll-note/{variant}", (string variant) =>
{
    return browserTimeline.UsePollFixture(variant)
        ? Results.NoContent()
        : Results.BadRequest(new { errorCode = "UNKNOWN_POLL_FIXTURE" });
}).DisableAntiforgery();
app.MapPost("/__test/cw-note/{variant}", (string variant) =>
{
    return browserTimeline.UseContentWarningFixture(variant)
        ? Results.NoContent()
        : Results.BadRequest(new { errorCode = "UNKNOWN_CW_FIXTURE" });
}).DisableAntiforgery();
app.MapPost("/__test/visibility-note/{variant}", (string variant) =>
{
    visibleUsers.Reset();
    return browserTimeline.UseVisibilityFixture(variant)
        ? Results.NoContent()
        : Results.BadRequest(new { errorCode = "UNKNOWN_VISIBILITY_FIXTURE" });
}).DisableAntiforgery();
app.MapGet("/__test/visibility-state", () => Results.Json(new
{
    visibleUsers.ReadCalls,
    visibleUsers.LastRequestedIds
}));
app.MapGet("/__test/diagnostics", () => Results.Json(new
{
    unhandledExceptions = browserDiagnostics.Read()
}));
app.MapGet("/__test/transport-diagnostics", () => Results.Json(new
{
    applicationNeverCompleted = browserDiagnostics.ReadTransportFailures()
}));
app.MapGet("/__test/circuit-diagnostics", () => Results.Json(new
{
    browserCircuitDiagnostics.ActiveCircuits,
    browserCircuitDiagnostics.ClosedCircuits
}));
app.MapPost("/__test/collect-garbage", () =>
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapGet("/__test/localization-state", (IMisskeyLocalizer localizer, IMisskeyLocaleCatalog catalog) => Results.Json(new
{
    currentLocale = localizer.CurrentLocale,
    direction = localizer.Direction,
    culture = localizer.Culture.Name,
    showMore = localizer.Translate("showMore"),
    supportedLocaleCount = localizer.SupportedLocales.Count,
    completeLocaleCount = localizer.SupportedLocales.Count(locale => catalog.GetTranslationCount(locale.Locale) == 1632)
}));
app.MapGet("/__test/user-preview-state", () => Results.Json(new
{
    browserUserPreview.ReadCalls,
    browserUserPreview.FollowCalls,
    browserUserPreview.UnfollowCalls,
    browserUserPreview.LastQuery,
    browserUserPreview.Alice.IsFollowing,
    browserUserPreview.Alice.HasPendingFollowRequestFromYou,
    browserRelationships.ActiveSubscriptions,
    browserRelationships.DisposedSubscriptions
}));
app.MapPost("/__test/user-preview-external/{state}", (string state) =>
{
    if (state is not ("follow" or "unfollow"))
    {
        return Results.BadRequest(new { errorCode = "UNKNOWN_RELATIONSHIP_STATE" });
    }

    browserUserPreview.SetAliceRelationship(state == "follow");
    browserRelationships.Publish();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapPost("/__test/reset-user-preview", () =>
{
    browserUserPreview.Reset();
    browserRelationships.ResetCounters();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapPost("/__test/reset-diagnostics", () =>
{
    browserDiagnostics.Reset();
    return Results.NoContent();
}).DisableAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<ActivityPub.Misskey.Blazor.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(frontendRouteAssemblies.Assemblies.ToArray());
app.Run();

internal sealed class BrowserTestInstancePresentationService : IInstancePresentationService
{
    private string provider = "none";
    private bool setupRequired;
    private int setupCalls;
    private string? lastSetupUsername;

    public void SetProtection(string value) => Volatile.Write(ref provider, value);

    public bool SetupRequired => Volatile.Read(ref setupRequired);

    public int SetupCalls => Volatile.Read(ref setupCalls);

    public string? LastSetupUsername => Volatile.Read(ref lastSetupUsername);

    public void SetSetupRequired(bool value)
    {
        Volatile.Write(ref setupRequired, value);
        if (value)
        {
            Interlocked.Exchange(ref setupCalls, 0);
            Volatile.Write(ref lastSetupUsername, null);
        }
    }

    public bool TryCompleteSetup(string username, string password)
    {
        if (!SetupRequired || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        Volatile.Write(ref lastSetupUsername, username);
        Interlocked.Increment(ref setupCalls);
        Volatile.Write(ref setupRequired, false);
        return true;
    }

    public Task<InstanceSummaryViewModel> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CreateViewModel(Volatile.Read(ref provider), SetupRequired));

    private static InstanceSummaryViewModel CreateViewModel(string captchaProvider, bool requireSetup) => new(
            "Browser test instance",
            "Opaque background regression host",
            "12.119.2-test",
            "/static-assets/favicon.png",
            BackgroundImageUrl: "/static-assets/favicon.png",
            LogoImageUrl: null,
            DisableRegistration: captchaProvider != "none",
            EmailRequiredForSignup: true,
            EnableEmail: true,
            TosUrl: "https://terms.example.test/",
            EnableHcaptcha: captchaProvider == "hcaptcha",
            HcaptchaSiteKey: captchaProvider == "hcaptcha" ? "10000000-ffff-ffff-ffff-000000000001" : null,
            EnableRecaptcha: captchaProvider == "recaptcha",
            RecaptchaSiteKey: captchaProvider == "recaptcha" ? "test-recaptcha-site-key" : null,
            EnableTurnstile: captchaProvider == "turnstile",
            TurnstileSiteKey: captchaProvider == "turnstile" ? "1x00000000000000000000AA" : null,
            TurnstileAction: captchaProvider == "turnstile" ? "signup" : null,
            TurnstileCdata: captchaProvider == "turnstile" ? "activitypub_signup" : null,
            MaintainerName: "Browser Maintainer",
            MaintainerEmail: "maintainer@example.test",
            RequireSetup: requireSetup);

    public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>(
        [
            new("9federation1", "mastodon.example", null),
            new("9federation2", "misskey.example", null),
            new("9federation3", "pleroma.example", null)
        ]);
}

internal sealed class BrowserTestAboutPresentationService : IAboutPresentationService
{
    private readonly object sync = new();
    private AboutFederationQuery? lastQuery;
    private int statisticsCalls;
    private int federationCalls;

    public int StatisticsCalls => Volatile.Read(ref statisticsCalls);
    public int FederationCalls => Volatile.Read(ref federationCalls);

    public AboutFederationQuery? LastQuery
    {
        get
        {
            lock (sync)
            {
                return lastQuery;
            }
        }
    }

    public Task<AboutStatisticsViewModel> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref statisticsCalls);
        return Task.FromResult(new AboutStatisticsViewModel(12_345, 67_890));
    }

    public Task<IReadOnlyList<FederationInstanceViewModel>> ReadFederationInstancesAsync(
        AboutFederationQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            lastQuery = query;
        }
        Interlocked.Increment(ref federationCalls);

        IEnumerable<FederationInstanceViewModel> values = Fixtures();
        if (!string.IsNullOrWhiteSpace(query.Host))
        {
            values = values.Where(value => value.Host.Contains(query.Host, StringComparison.OrdinalIgnoreCase));
        }

        values = query.State switch
        {
            "blocked" => values.Where(value => value.IsBlocked),
            "suspended" => values.Where(value => value.IsSuspended),
            "notResponding" => values.Where(value => value.IsNotResponding),
            "federating" => values.Where(value => !value.IsBlocked && !value.IsSuspended && !value.IsNotResponding),
            _ => values
        };

        values = query.Sort switch
        {
            "+notes" => values.OrderByDescending(value => value.NotesCount),
            "-notes" => values.OrderBy(value => value.NotesCount),
            "+users" => values.OrderByDescending(value => value.UsersCount),
            "-users" => values.OrderBy(value => value.UsersCount),
            "-pubSub" => values.OrderBy(value => value.FollowersCount),
            _ => values.OrderByDescending(value => value.FollowersCount)
        };

        return Task.FromResult<IReadOnlyList<FederationInstanceViewModel>>(
            values.Skip(query.Offset).Take(query.Limit).ToArray());
    }

    private static FederationInstanceViewModel[] Fixtures() => Enumerable.Range(1, 46)
        .Select(index => new FederationInstanceViewModel(
            $"9about{index:D3}",
            index switch
            {
                1 => "mastodon.browser.test",
                2 => "misskey.browser.test",
                3 => "pleroma.browser.test",
                _ => $"node-{index:D2}.browser.test"
            },
            "/static-assets/favicon.png",
            IsNotResponding: index % 13 == 0,
            IsBlocked: index % 11 == 0,
            IsSuspended: index % 17 == 0,
            SoftwareName: index % 2 == 0 ? "Misskey" : "Mastodon",
            SoftwareVersion: $"{index}.0",
            Name: $"Federation node {index}",
            CaughtAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index),
            UsersCount: index * 100,
            NotesCount: index * 1_000,
            FollowingCount: index * 3,
            FollowersCount: index * 5,
            LatestRequestSentAt: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero).AddHours(index),
            LastCommunicatedAt: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index)))
        .ToArray();
}

internal sealed class BrowserTestAnnouncementPresentationService :
    IAnnouncementPresentationService,
    IAnnouncementPagePresentationService
{
    private readonly object sync = new();
    private readonly HashSet<string> readIds = new(StringComparer.Ordinal);

    public int MarkReadCalls { get; private set; }
    public string? LastMarkedId { get; private set; }

    public void Reset()
    {
        lock (sync)
        {
            readIds.Clear();
            MarkReadCalls = 0;
            LastMarkedId = null;
        }
    }

    public Task<IReadOnlyList<VisitorAnnouncementViewModel>> ReadPublicAsync(
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VisitorAnnouncementViewModel>>(
        [
            new(
                "9browserannouncement",
                "Scheduled maintenance",
                "Browser regression announcement.",
                "/static-assets/favicon.png")
        ]);

    public Task<IReadOnlyList<AnnouncementPageViewModel>> ReadAsync(
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (untilId is not null)
        {
            return Task.FromResult<IReadOnlyList<AnnouncementPageViewModel>>([]);
        }

        lock (sync)
        {
            AnnouncementPageViewModel[] items =
            [
                new(
                    "9browserannouncement-unread",
                    new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
                    "Browser unread announcement",
                    "I $[jelly ❤] Misskey",
                    "/static-assets/favicon.png",
                    readIds.Contains("9browserannouncement-unread")),
                new(
                    "9browserannouncement-read",
                    new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
                    "Browser read announcement",
                    "Already read announcement",
                    "https://remote.example/image.png",
                    IsRead: true)
            ];
            return Task.FromResult<IReadOnlyList<AnnouncementPageViewModel>>(items.Take(limit).ToArray());
        }
    }

    public Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!string.Equals(id, "9browserannouncement-unread", StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            readIds.Add(id);
            MarkReadCalls++;
            LastMarkedId = id;
            return Task.FromResult(true);
        }
    }
}

internal sealed class BrowserTestCurrentAccountPresentationService : ICurrentAccountPresentationService
{
    public Task<NoteAuthorViewModel> GetAsync(CancellationToken cancellationToken) => Task.FromResult(
        new NoteAuthorViewModel(
            "9duke7z2w3",
            "alice",
            "alice",
            "Alice",
            "/static-assets/favicon.png",
            IsBot: false));
}

internal sealed class BrowserTestRenoteDetailsPresentationService : IRenoteDetailsPresentationService
{
    private static readonly IReadOnlyList<NoteAuthorViewModel> Users = Enumerable.Range(0, 11)
        .Select(index => new NoteAuthorViewModel(
            $"9renoteuser{index}",
            $"renoteuser{index}",
            $"renoteuser{index}@remote.example",
            $"Renote user {index}",
            "/static-assets/favicon.png",
            IsBot: false))
        .ToArray();

    public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>(Users.Take(limit).ToArray());
    }
}

internal sealed class BrowserTestUserPreviewPresentationService : IUserPreviewPresentationService
{
    private readonly object sync = new();
    private UserPreviewViewModel alice = CreateAlice();
    private UserPreviewViewModel bob = CreateBob();

    public UserPreviewViewModel Alice
    {
        get
        {
            lock (sync)
            {
                return alice;
            }
        }
    }

    public UserPreviewViewModel Bob
    {
        get
        {
            lock (sync)
            {
                return bob;
            }
        }
    }

    public int ReadCalls { get; private set; }
    public int FollowCalls { get; private set; }
    public int UnfollowCalls { get; private set; }
    public string? LastQuery { get; private set; }

    public void Reset()
    {
        lock (sync)
        {
            alice = CreateAlice();
            bob = CreateBob();
            ReadCalls = 0;
            FollowCalls = 0;
            UnfollowCalls = 0;
            LastQuery = null;
        }
    }

    public void SetAliceRelationship(bool following)
    {
        lock (sync)
        {
            alice = alice with
            {
                IsFollowing = following,
                HasPendingFollowRequestFromYou = false
            };
        }
    }

    public Task<UserPreviewViewModel> ReadAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            ReadCalls++;
            LastQuery = query;
            string normalized = query.Trim().TrimStart('@');
            return Task.FromResult(normalized switch
            {
                "alice" or "alice-id" => alice,
                "bob" or "bob-id" => bob,
                _ => throw new UserPreviewPresentationException("USER_PREVIEW_NOT_FOUND")
            });
        }
    }

    public Task<UserPreviewViewModel> FollowAsync(
        UserPreviewViewModel user,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            FollowCalls++;
            UserPreviewViewModel updated = user with
            {
                IsFollowing = true,
                HasPendingFollowRequestFromYou = false
            };
            Store(updated);
            return Task.FromResult(updated);
        }
    }

    public Task<UserPreviewViewModel> UnfollowAsync(
        UserPreviewViewModel user,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            UnfollowCalls++;
            UserPreviewViewModel updated = user with
            {
                IsFollowing = false,
                HasPendingFollowRequestFromYou = false
            };
            Store(updated);
            return Task.FromResult(updated);
        }
    }

    private void Store(UserPreviewViewModel updated)
    {
        if (string.Equals(updated.Id, alice.Id, StringComparison.Ordinal))
        {
            alice = updated;
        }
        else if (string.Equals(updated.Id, bob.Id, StringComparison.Ordinal))
        {
            bob = updated;
        }
    }

    private static UserPreviewViewModel CreateAlice() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "alice-id",
        new NoteAuthorViewModel(
            "alice-id",
            "alice",
            "alice@xn--bcher-kva.example",
            "Alice :wave:",
            "/static-assets/favicon.png",
            IsBot: false,
            IsCat: true,
            OnlineStatus: "active",
            Emojis: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["wave"] = "/static-assets/favicon.png"
            }),
        "Hello @world #fediverse :wave:",
        "/static-assets/favicon.png",
        NotesCount: 73,
        FollowingCount: 19,
        FollowersCount: 31,
        IsLocked: false,
        CanFollow: true,
        IsFollowing: false,
        HasPendingFollowRequestFromYou: false,
        IsFollowed: true);

    private static UserPreviewViewModel CreateBob() => new(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        "bob-id",
        new NoteAuthorViewModel(
            "bob-id",
            "bob",
            "bob@remote.example",
            "Bob",
            "https://tracker.invalid/bob.png",
            IsBot: false),
        "Remote preview fixture",
        "https://tracker.invalid/banner.png",
        NotesCount: 5,
        FollowingCount: 2,
        FollowersCount: 3,
        IsLocked: true,
        CanFollow: true,
        IsFollowing: false,
        HasPendingFollowRequestFromYou: false,
        IsFollowed: false);
}

internal sealed class BrowserTestUserSearchPresentationService(
    BrowserTestUserPreviewPresentationService users) : IUserSearchPresentationService
{
    public Task<IReadOnlyList<UserPreviewViewModel>> SearchAsync(
        string query,
        string origin,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = query.Trim().TrimStart('@');
        IEnumerable<UserPreviewViewModel> matches = normalized.ToLowerInvariant() switch
        {
            "alice" or "ali" => [users.Alice],
            "bob" => [users.Bob],
            _ => []
        };
        matches = origin switch
        {
            "local" => matches.Where(user => !user.User.Acct.Contains('@', StringComparison.Ordinal)),
            "remote" => matches.Where(user => user.User.Acct.Contains('@', StringComparison.Ordinal)),
            _ => matches
        };
        return Task.FromResult<IReadOnlyList<UserPreviewViewModel>>(
            matches.Take(Math.Clamp(limit, 1, 100)).ToArray());
    }
}

internal sealed class BrowserTestUserPagePresentationService(
    BrowserTestUserPreviewPresentationService users,
    BrowserTestTimelinePresentationService timeline) : IUserPagePresentationService
{
    public async Task<UserPageViewModel> ReadAsync(
        string acct,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        UserPreviewViewModel user = await users.ReadAsync(acct, cancellationToken);
        if (untilId is not null)
        {
            return new(user, new TimelinePageViewModel([], null));
        }

        return new(user, new TimelinePageViewModel([timeline.CurrentNote], null));
    }
}

internal sealed class BrowserTestUserFollowRelationsPresentationService(
    BrowserTestUserPreviewPresentationService users) : IUserFollowRelationsPresentationService
{
    public Task<UserFollowRelationsPageViewModel?> ReadAsync(
        string acct,
        bool followers,
        string? untilId,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedAcct = acct.Trim().TrimStart('@');
        int hostSeparator = normalizedAcct.IndexOf('@');
        if (hostSeparator >= 0)
        {
            normalizedAcct = normalizedAcct[..hostSeparator];
        }

        if (!string.Equals(normalizedAcct, "alice", StringComparison.Ordinal))
        {
            return Task.FromResult<UserFollowRelationsPageViewModel?>(null);
        }

        IReadOnlyList<UserFollowRelationListItem> items = followers || untilId is not null
            ? []
            : [new("follow-relation-id", users.Bob)];
        return Task.FromResult<UserFollowRelationsPageViewModel?>(new(items.Take(Math.Clamp(limit, 1, 100)).ToArray()));
    }
}

internal sealed class BrowserTestAuthenticatedActorContext(
    AuthenticationStateProvider authenticationStateProvider) : IAuthenticatedActorContext
{
    public async Task<AuthenticatedActor?> FindAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthenticationState state = await authenticationStateProvider.GetAuthenticationStateAsync().ConfigureAwait(false);
        ClaimsPrincipal principal = state.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string username = principal.FindFirstValue("preferred_username")
            ?? throw new FrontendAuthenticationException("AUTH_USERNAME_INVALID");
        return new AuthenticatedActor(username, $"http://127.0.0.1:5099/users/{username}");
    }

    public async Task<AuthenticatedActor> RequireAsync(CancellationToken cancellationToken) =>
        await FindAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new FrontendAuthenticationException("AUTH_REQUIRED");

    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

internal sealed class BrowserTestComposerMediaService : IComposerMediaService
{
    public Task<ComposerMediaViewModel> UploadAsync(
        string fileName,
        string? declaredMediaType,
        Stream content,
        CancellationToken cancellationToken) => throw new ComposerMediaUnavailableException();
}

internal sealed class BrowserTestVisibleUsersPresentationService : IVisibleUsersPresentationService
{
    public int ReadCalls { get; private set; }
    public IReadOnlyList<string> LastRequestedIds { get; private set; } = [];

    public void Reset()
    {
        ReadCalls = 0;
        LastRequestedIds = [];
    }

    public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        ReadCalls++;
        LastRequestedIds = userIds.ToArray();
        return Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>(userIds
            .Take(VisibleUsersPresentationService.MaximumUsers)
            .Select((id, index) => new NoteAuthorViewModel(
                id,
                $"recipient{index}",
                $"recipient{index}@remote.example",
                $"Recipient {index}",
                "/static-assets/favicon.png",
                IsBot: false))
            .ToArray());
    }
}

internal sealed class BrowserTestAvatarsPresentationService : IAvatarsPresentationService
{
    public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        IReadOnlyList<string> userIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>(userIds
            .Select((id, index) => new NoteAuthorViewModel(
                id,
                index == 0 ? "alice" : "bob",
                index == 0 ? "alice" : "bob@remote.example",
                index == 0 ? "Alice" : "Bob",
                "/static-assets/favicon.png",
                IsBot: false,
                OnlineStatus: index == 0 ? "online" : "offline"))
            .ToArray());
    }
}

internal sealed class BrowserTestReactionDetailsPresentationService : IReactionDetailsPresentationService
{
    public Task<IReadOnlyList<NoteAuthorViewModel>> ReadAsync(
        Guid postId,
        string reaction,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = string.Equals(reaction, "👍", StringComparison.Ordinal) ? 4 : 1;
        return Task.FromResult<IReadOnlyList<NoteAuthorViewModel>>(Enumerable.Range(0, Math.Min(count, limit))
            .Select(index => new NoteAuthorViewModel(
                $"9reactor{index}",
                $"reactor{index}",
                $"reactor{index}@remote.example",
                $"Reactor {index}",
                "/static-assets/favicon.png",
                IsBot: false))
            .ToArray());
    }
}

internal sealed class BrowserTestNotificationPresentationService : INotificationPresentationService
{
    private bool allRead;

    private static readonly NoteAuthorViewModel Alice = new(
        "9notification-user",
        "alice",
        "alice@remote.example",
        "Alice",
        "/static-assets/favicon.png",
        IsBot: false);

    private static readonly NotificationNoteViewModel Note = new(
        Guid.Parse("25252525-2525-2525-2525-252525252525"),
        "9notification-note",
        new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero),
        Alice,
        "Browser notification fixture",
        null,
        HasReply: false,
        MediaCount: 1,
        HasPoll: true,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["party"] = "/static-assets/favicon.png"
        },
        Renote: null);

    private static readonly NoteViewModel FullNote = new(
        Note.InternalId,
        Note.Id,
        Note.CreatedAt,
        Alice,
        Note.Text,
        Note.ContentWarning,
        ActivityPub.Misskey.Blazor.Presentation.Visibility.Public,
        ReplyId: null,
        RepliesCount: 0,
        RenotesCount: 0,
        ReactionsCount: 1,
        ReactedByViewer: false,
        new Dictionary<string, long>(StringComparer.Ordinal) { [":party:"] = 1 },
        ViewerReaction: null,
        Media: [],
        Mentions: [],
        Hashtags: [],
        Note.Emojis,
        Poll: null,
        Renote: null);

    public static NotificationViewModel Fixture { get; } = new(
        Guid.Parse("15151515-1515-1515-1515-151515151515"),
        "9notification",
        new DateTimeOffset(2026, 8, 4, 10, 5, 0, TimeSpan.Zero),
        MisskeyNotificationType.Reaction,
        IsRead: false,
        Alice,
        Note,
        ":party:",
        FullNote: FullNote);

    public static NotificationViewModel MentionFixture { get; } = Fixture with
    {
        InternalId = Guid.Parse("17171717-1717-1717-1717-171717171717"),
        Id = "9notification-mention",
        Type = MisskeyNotificationType.Mention,
        Reaction = null,
        FullNote = FullNote with
        {
            InternalId = Guid.Parse("27272727-2727-2727-2727-272727272727"),
            Id = "9notification-mention-note",
            Text = "Mention notification fixture"
        }
    };

    public static NotificationViewModel DirectFixture { get; } = MentionFixture with
    {
        InternalId = Guid.Parse("19191919-1919-1919-1919-191919191919"),
        Id = "9notification-direct",
        FullNote = MentionFixture.FullNote! with
        {
            InternalId = Guid.Parse("29292929-2929-2929-2929-292929292929"),
            Id = "9notification-direct-note",
            Text = "Direct notification fixture",
            Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility.MentionedOnly
        }
    };

    public static NotificationViewModel StreamFixture { get; } = MentionFixture with
    {
        InternalId = Guid.Parse("18181818-1818-1818-1818-181818181818"),
        Id = "9notification-stream",
        FullNote = MentionFixture.FullNote! with
        {
            InternalId = Guid.Parse("28282828-2828-2828-2828-282828282828"),
            Id = "9notification-stream-note",
            Text = "Stream notification fixture"
        }
    };

    public static NotificationViewModel ReadFixture => Fixture with
    {
        InternalId = Guid.Parse("16161616-1616-1616-1616-161616161616"),
        Id = "9notification-toast",
        IsRead = true
    };

    public int MarkReadCalls { get; private set; }

    public int MarkAllReadCalls { get; private set; }

    public Guid? LastMarkedId { get; private set; }

    public Task<IReadOnlyList<NotificationViewModel>> ReadAsync(
        NotificationPresentationQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<NotificationViewModel> result = new[] { DirectFixture, MentionFixture, Fixture }
            .Select(notification => allRead ? notification with { IsRead = true } : notification);
        if (request.IncludeTypes is not null)
        {
            result = result.Where(notification => request.IncludeTypes.Contains(notification.Type));
        }

        if (request.ExcludeTypes is not null)
        {
            result = result.Where(notification => !request.ExcludeTypes.Contains(notification.Type));
        }

        if (request.UnreadOnly)
        {
            result = result.Where(notification => !notification.IsRead);
        }

        if (request.UntilId is not null)
        {
            result = result.SkipWhile(notification => notification.Id != request.UntilId).Skip(1);
        }

        return Task.FromResult<IReadOnlyList<NotificationViewModel>>(
            result.Take(Math.Clamp(request.Limit, 1, 100)).ToArray());
    }

    public Task<NotificationViewModel?> FindAsync(Guid notificationId, CancellationToken cancellationToken) =>
        Task.FromResult<NotificationViewModel?>(new[] { Fixture, MentionFixture, DirectFixture, StreamFixture }
            .SingleOrDefault(notification => notification.InternalId == notificationId));

    public Task<bool> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MarkReadCalls++;
        LastMarkedId = notificationId;
        return Task.FromResult(new[] { Fixture, MentionFixture, DirectFixture, StreamFixture }
            .Any(notification => notification.InternalId == notificationId));
    }

    public Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MarkAllReadCalls++;
        allRead = true;
        return Task.FromResult(3);
    }
}

internal sealed class BrowserTestNotificationSubscriptionService : INotificationSubscriptionService
{
    private readonly Channel<NotificationMutation> channel = Channel.CreateUnbounded<NotificationMutation>();
    private long cursor = 50;
    private int activeSubscriptions;

    public long Cursor => Interlocked.Read(ref cursor);

    public int ActiveSubscriptions => Volatile.Read(ref activeSubscriptions);

    public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Cursor);
    }

    public async IAsyncEnumerable<NotificationMutation> SubscribeAsync(
        long afterCursor,
        IReadOnlySet<MisskeyNotificationType>? includeTypes,
        IReadOnlySet<MisskeyNotificationType>? excludeTypes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref activeSubscriptions);
        try
        {
            await foreach (NotificationMutation mutation in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (mutation.Notification is not { } notification ||
                    includeTypes is { Count: > 0 } && !includeTypes.Contains(notification.Type) ||
                    excludeTypes?.Contains(notification.Type) == true)
                {
                    yield return mutation with
                    {
                        Kind = NotificationMutationKind.Checkpoint,
                        Notification = null
                    };
                    continue;
                }

                yield return mutation;
            }
        }
        finally
        {
            Interlocked.Decrement(ref activeSubscriptions);
        }
    }

    public void Publish(NotificationViewModel notification)
    {
        long next = Interlocked.Increment(ref cursor);
        channel.Writer.TryWrite(new(next, NotificationMutationKind.Created, notification));
    }
}

internal sealed class BrowserTestNoteDeletionPresentationService : INoteDeletionPresentationService
{
    public Task DeleteAsync(NoteViewModel note, string idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

internal sealed class BrowserTestTimelinePresentationService : ITimelinePresentationService, INotePagePresentationService
{
    private readonly object sync = new();
    private NoteViewModel sampleNote = InitialNote;

    public NoteViewModel CurrentNote
    {
        get
        {
            lock (sync)
            {
                return sampleNote;
            }
        }
    }

    public int ReactionCalls { get; private set; }
    public bool? LastRemove { get; private set; }
    public int VoteCalls { get; private set; }
    public int? LastVoteChoice { get; private set; }
    public int CreateCalls { get; private set; }
    public string? LastCreatedText { get; private set; }
    public int RenoteCalls { get; private set; }
    public string? LastRenotedId { get; private set; }

    public Task<NoteViewModel?> FindAsync(string noteId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult<NoteViewModel?>(
                string.Equals(noteId, sampleNote.Id, StringComparison.Ordinal)
                    ? sampleNote
                    : null);
        }
    }

    public void ResetReaction()
    {
        lock (sync)
        {
            sampleNote = InitialNote;
            ReactionCalls = 0;
            LastRemove = null;
        }
    }

    public void ResetRenote()
    {
        lock (sync)
        {
            RenoteCalls = 0;
            LastRenotedId = null;
        }
    }

    public void ResetCompose()
    {
        lock (sync)
        {
            CreateCalls = 0;
            LastCreatedText = null;
        }
    }

    public bool UsePollFixture(string variant)
    {
        lock (sync)
        {
            sampleNote = variant switch
            {
                "single" => CreatePollFixture(multiple: false, expired: false),
                "multiple" => CreatePollFixture(multiple: true, expired: false),
                "expired" => CreatePollFixture(multiple: false, expired: true),
                "reset" => InitialNote,
                _ => sampleNote
            };
            VoteCalls = 0;
            LastVoteChoice = null;
            return variant is "single" or "multiple" or "expired" or "reset";
        }
    }

    public bool UseContentWarningFixture(string variant)
    {
        lock (sync)
        {
            sampleNote = variant switch
            {
                "all" => CreateContentWarningFixture(
                    "A👨‍👩‍👧‍👦é",
                    mediaCount: 2,
                    includePoll: true),
                "files-only" => CreateContentWarningFixture(
                    string.Empty,
                    mediaCount: 2,
                    includePoll: false),
                "text-poll" => CreateContentWarningFixture(
                    "**abc**",
                    mediaCount: 0,
                    includePoll: true),
                "reset" => InitialNote,
                _ => sampleNote
            };
            return variant is "all" or "files-only" or "text-poll" or "reset";
        }
    }

    public bool UseVisibilityFixture(string variant)
    {
        lock (sync)
        {
            sampleNote = variant switch
            {
                "specified" => InitialNote with
                {
                    Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility.MentionedOnly,
                    LocalOnly = false,
                    VisibleUserIds = Enumerable.Range(0, 12).Select(index => $"9recipient{index}").ToArray()
                },
                "local-only" => InitialNote with
                {
                    Visibility = ActivityPub.Misskey.Blazor.Presentation.Visibility.Unlisted,
                    LocalOnly = true,
                    VisibleUserIds = []
                },
                "reset" => InitialNote,
                _ => sampleNote
            };
            return variant is "specified" or "local-only" or "reset";
        }
    }

    public Task<TimelinePageViewModel> ReadAsync(
        TimelineKind kind,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken) => Task.FromResult(new TimelinePageViewModel([CurrentNote], null));

    private static NoteViewModel InitialNote { get; } = new(
        Guid.Parse("017f22e2-79b0-7b1d-b2b4-3a89e9af0380"),
        "9dummqy0w3",
        new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
        new(
            "9duke7z2w3",
            "alice",
            "alice",
            "Alice",
            "/static-assets/favicon.png",
            IsBot: false),
        "Misskey **v12** のMFM :party_parrot: #fediverse",
        ContentWarning: null,
        ActivityPub.Misskey.Blazor.Presentation.Visibility.Public,
        ReplyId: null,
        RepliesCount: 2,
        RenotesCount: 3,
        ReactionsCount: 5,
        ReactedByViewer: false,
        new Dictionary<string, long>(StringComparer.Ordinal) { ["👍"] = 4, [":party_parrot:"] = 1 },
        ViewerReaction: null,
        Media: [],
        Mentions: [],
        Hashtags: ["fediverse"],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["party_parrot"] = "/static-assets/favicon.png"
        },
        Poll: null,
        Renote: null);

    private static NoteViewModel CreateContentWarningFixture(
        string text,
        int mediaCount,
        bool includePoll)
    {
        IReadOnlyList<NoteMediaViewModel> media = Enumerable.Range(0, mediaCount)
            .Select(index => new NoteMediaViewModel(
                $"cw-media-{index}",
                "image/png",
                "/static-assets/favicon.png",
                "/static-assets/favicon.png",
                $"CW fixture {index + 1}",
                null,
                64,
                64,
                Sensitive: false))
            .ToArray();
        NotePollViewModel? poll = includePoll
            ? new NotePollViewModel(
                "cw-poll",
                null,
                Expired: false,
                Multiple: false,
                VotedByViewer: false,
                OwnVotes: [],
                Options:
                [
                    new NotePollOptionViewModel("はい", 2),
                    new NotePollOptionViewModel("いいえ", 1)
                ])
            : null;

        return InitialNote with
        {
            Text = text,
            ContentWarning = "閲覧注意",
            Media = media,
            Poll = poll
        };
    }

    private static NoteViewModel CreatePollFixture(bool multiple, bool expired) => InitialNote with
    {
        Text = "Misskey v12 poll fixture",
        Poll = new NotePollViewModel(
            "browser-poll",
            expired ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddHours(1),
            Expired: expired,
            Multiple: multiple,
            VotedByViewer: false,
            OwnVotes: [],
            Options:
            [
                new NotePollOptionViewModel("alpha", 2),
                new NotePollOptionViewModel("beta", 1)
            ])
    };

    public Task<NoteViewModel> CreateAsync(
        NoteDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        lock (sync)
        {
            CreateCalls++;
            LastCreatedText = draft.Text;
        }
        return Task.FromResult(CurrentNote with
        {
            InternalId = Guid.NewGuid(),
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            Text = draft.Text,
            ContentWarning = draft.ContentWarning,
            Visibility = draft.Visibility,
            Media = []
        });
    }

    public Task<NoteViewModel> RenoteAsync(
        string noteId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!string.Equals(noteId, sampleNote.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unknown browser-test note.");
            }

            RenoteCalls++;
            LastRenotedId = noteId;
            sampleNote = sampleNote with { RenotesCount = sampleNote.RenotesCount + 1 };
            return Task.FromResult(sampleNote);
        }
    }

    public Task<NoteViewModel> ReactAsync(
        string noteId,
        string reaction,
        bool remove,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!string.Equals(noteId, sampleNote.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unknown browser-test note.");
            }

            var reactions = new Dictionary<string, long>(sampleNote.Reactions, StringComparer.Ordinal);
            if (sampleNote.ViewerReaction is { } previous && reactions.TryGetValue(previous, out long previousCount))
            {
                if (previousCount <= 1)
                {
                    reactions.Remove(previous);
                }
                else
                {
                    reactions[previous] = previousCount - 1;
                }
            }

            if (!remove)
            {
                reactions[reaction] = reactions.GetValueOrDefault(reaction) + 1;
            }

            ReactionCalls++;
            LastRemove = remove;
            sampleNote = sampleNote with
            {
                Reactions = reactions,
                ReactionsCount = reactions.Values.Sum(),
                ReactedByViewer = !remove,
                ViewerReaction = remove ? null : reaction
            };
            return Task.FromResult(sampleNote);
        }
    }

    public Task<NoteViewModel> VotePollAsync(
        string noteId,
        int choiceIndex,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!string.Equals(noteId, sampleNote.Id, StringComparison.Ordinal) || sampleNote.Poll is not { } poll)
            {
                throw new InvalidOperationException("Unknown browser-test poll note.");
            }

            if (poll.Expired || poll.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException("The browser-test poll is expired.");
            }

            if (choiceIndex < 0 || choiceIndex >= poll.Options.Count ||
                poll.OwnVotes.Contains(choiceIndex) || !poll.Multiple && poll.OwnVotes.Count > 0)
            {
                throw new InvalidOperationException("The browser-test poll vote is invalid or duplicated.");
            }

            NotePollOptionViewModel[] options = poll.Options
                .Select((option, index) => index == choiceIndex
                    ? option with { VotesCount = option.VotesCount + 1 }
                    : option)
                .ToArray();
            int[] ownVotes = [.. poll.OwnVotes, choiceIndex];
            sampleNote = sampleNote with
            {
                Poll = poll with
                {
                    VotedByViewer = true,
                    OwnVotes = ownVotes,
                    Options = options
                }
            };
            VoteCalls++;
            LastVoteChoice = choiceIndex;
            return Task.FromResult(sampleNote);
        }
    }

    public Task<NoteViewModel?> FindForStreamAsync(
        Guid id,
        TimelineKind kind,
        CancellationToken cancellationToken) => Task.FromResult<NoteViewModel?>(null);

    public Task<string> MapNoteIdAsync(
        Guid id,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) => Task.FromResult(id.ToString("N"));
}

internal sealed class BrowserTestTimelineSubscriptionService : ITimelineSubscriptionService
{
    private readonly object sync = new();
    private readonly List<TimelineMutation> history = [];
    private long cursor;
    private int activeSubscriptions;

    public int ActiveSubscriptions => Volatile.Read(ref activeSubscriptions);
    public long Cursor => Interlocked.Read(ref cursor);

    public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Interlocked.Read(ref cursor));

    public async IAsyncEnumerable<TimelineMutation> SubscribeAsync(
        TimelineKind kind,
        long afterCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = kind;
        long next = afterCursor;
        Interlocked.Increment(ref activeSubscriptions);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimelineMutation[] pending;
                lock (sync)
                {
                    pending = history.Where(item => item.Cursor > next).ToArray();
                }

                foreach (TimelineMutation mutation in pending)
                {
                    next = mutation.Cursor;
                    yield return mutation;
                }

                await Task.Delay(20, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref activeSubscriptions);
        }
    }

    public void Publish(NoteViewModel source)
    {
        long next = Interlocked.Increment(ref cursor);
        NoteViewModel note = source with
        {
            InternalId = Guid.NewGuid(),
            Id = $"9stream{next:D8}",
            CreatedAt = DateTimeOffset.UtcNow,
            Text = $"streamed timeline note {next}",
            RepliesCount = 0,
            RenotesCount = 0,
            ReactionsCount = 0,
            ReactedByViewer = false,
            Reactions = new Dictionary<string, long>(StringComparer.Ordinal),
            ViewerReaction = null,
            Poll = null,
            Renote = null
        };
        lock (sync)
        {
            history.Add(new(next, TimelineMutationKind.Upsert, note.Id, note));
        }
    }
}

internal sealed class BrowserTestRelationshipSubscriptionService : IRelationshipSubscriptionService
{
    private readonly object sync = new();
    private readonly List<RelationshipMutation> history = [];
    private long cursor;
    private int activeSubscriptions;
    private int disposedSubscriptions;

    public int ActiveSubscriptions => Volatile.Read(ref activeSubscriptions);
    public int DisposedSubscriptions => Volatile.Read(ref disposedSubscriptions);

    public Task<long> GetLatestCursorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Interlocked.Read(ref cursor));
    }

    public async IAsyncEnumerable<RelationshipMutation> SubscribeAsync(
        Guid targetActorId,
        long afterCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = targetActorId;
        Interlocked.Increment(ref activeSubscriptions);
        long next = afterCursor;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RelationshipMutation[] pending;
                lock (sync)
                {
                    pending = history.Where(value => value.Cursor > next).ToArray();
                }

                foreach (RelationshipMutation mutation in pending)
                {
                    next = mutation.Cursor;
                    yield return mutation;
                }

                await Task.Delay(20, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref activeSubscriptions);
            Interlocked.Increment(ref disposedSubscriptions);
        }
    }

    public void Publish()
    {
        long next = Interlocked.Increment(ref cursor);
        lock (sync)
        {
            history.Add(new(next, Changed: true));
        }
    }

    public void ResetCounters()
    {
        Volatile.Write(ref disposedSubscriptions, 0);
    }
}

internal sealed class BrowserTestAutocompletePresentationService : IAutocompletePresentationService
{
    public Task<IReadOnlyList<AutocompleteUserViewModel>> SearchUsersAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AutocompleteUserViewModel>>(
            string.IsNullOrWhiteSpace(query) || !"alice".StartsWith(query, StringComparison.OrdinalIgnoreCase)
                ? []
                : [new AutocompleteUserViewModel("11111111-1111-1111-1111-111111111111", "alice", null, "Alice", string.Empty)]);
    }

    public Task<IReadOnlyList<string>> SearchHashtagsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            string.IsNullOrWhiteSpace(query) ? [] : [query.ToLowerInvariant()]);
    }

    public IReadOnlyList<AutocompleteEmojiViewModel> SearchEmojis(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : [new AutocompleteEmojiViewModel("\U0001F600", "grinning", null, null, false)];

    public IReadOnlyList<string> SearchMfmTags(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? MfmTags
            : MfmTags.Where(tag => tag.StartsWith(query, StringComparison.Ordinal)).ToArray();

    private static readonly string[] MfmTags = ["spin", "tada", "rainbow"];

    public void RememberEmoji(string emoji)
    {
    }
}

internal sealed class BrowserTestSettingsPresentationService : ISettingsPresentationService
{
    private bool isLocked;
    private bool discoverable = true;

    private readonly List<SettingsApiTokenViewModel> tokens =
    [
        new(
            "test-api-token",
            "Browser test application",
            "Contract fixture token",
            null,
            ["read:account", "write:notes"],
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            null)
    ];

    public Task<SettingsProfileViewModel> ReadProfileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SettingsProfileViewModel(
            "alice",
            "Alice",
            "Profile description",
            IsLocked: isLocked,
            Discoverable: discoverable,
            "/static-assets/user-unknown.png",
            "/static-assets/user-unknown.png"));
    }

    public Task UpdateProfileAsync(
        string? name,
        string? description,
        bool? isLocked,
        bool? discoverable,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isLocked is not null)
        {
            this.isLocked = isLocked.Value;
        }

        if (discoverable is not null)
        {
            this.discoverable = discoverable.Value;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SettingsApiTokenViewModel>> ReadApiTokensAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SettingsApiTokenViewModel>>(tokens.ToArray());
    }

    public Task<SettingsApiTokenIssuedViewModel> GenerateApiTokenAsync(
        string? name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string id = "test-generated-token";
        tokens.Add(new(
            id,
            string.IsNullOrWhiteSpace(name) ? "Misskey v12 web client" : name,
            "Generated by browser test",
            null,
            permissions.ToArray(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            null));
        return Task.FromResult(new SettingsApiTokenIssuedViewModel(
            "mk_browser_test_token",
            id,
            DateTimeOffset.UtcNow.AddDays(30)));
    }

    public Task<bool> RevokeApiTokenAsync(string externalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int removed = tokens.RemoveAll(token => string.Equals(token.Id, externalId, StringComparison.Ordinal));
        return Task.FromResult(removed > 0);
    }
}

internal sealed class BrowserTestAdminPresentationService : IAdminPresentationService
{
    public Task<AdminOverviewViewModel> ReadOverviewAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AdminOverviewViewModel([], []));

    public Task<IReadOnlyList<AdminRelayViewModel>> ListRelaysAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdminRelayViewModel>>([]);

    public Task<AdminRelayViewModel> AddRelayAsync(string inbox, CancellationToken cancellationToken) =>
        Task.FromResult(new AdminRelayViewModel(Guid.NewGuid().ToString("D"), inbox, "requesting"));

    public Task RemoveRelayAsync(string inbox, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<AdminAnnouncementViewModel>> ListAnnouncementsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdminAnnouncementViewModel>>([]);

    public Task CreateAnnouncementAsync(string title, string text, string? imageUrl, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteAnnouncementAsync(string announcementId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<AdminInvitationViewModel> CreateInvitationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AdminInvitationViewModel(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddHours(1)));
    }
}

internal sealed class BrowserTestHashtagTrendPresentationService : IHashtagTrendPresentationService
{
    public Task<IReadOnlyList<HashtagTrendViewModel>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chart = Enumerable.Range(0, 20).Select(index => (long)(index % 4)).ToArray();
        return Task.FromResult<IReadOnlyList<HashtagTrendViewModel>>(
        [
            new HashtagTrendViewModel("Misskey", 42, chart),
            new HashtagTrendViewModel("activitypub", 17, chart)
        ]);
    }
}
