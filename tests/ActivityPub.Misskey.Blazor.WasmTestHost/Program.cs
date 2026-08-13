using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
Uri publicBaseUri = ReadExplicitPublicBaseUri();
var diagnostics = new WasmSmokeDiagnostics();
byte[] cookieBytes = RandomNumberGenerator.GetBytes(32);
byte[] csrfBytes = RandomNumberGenerator.GetBytes(32);
string cookieValue = Convert.ToHexString(cookieBytes);
string csrfToken = Convert.ToHexString(csrfBytes);

builder.Services.AddSingleton(diagnostics);

WebApplication app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
        "script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self'; connect-src 'self' ws:; " +
        "worker-src 'self'; manifest-src 'self'; form-action 'self'";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    await next(context).ConfigureAwait(false);
});
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(
            "/app/_content",
            out PathString remaining))
    {
        context.Request.Path = new PathString("/_content").Add(remaining);
    }

    await next(context).ConfigureAwait(false);
});

string publicAssets = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "../../frontend/misskey-v12/public"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(publicAssets, "static-assets")),
    RequestPath = "/static-assets"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(publicAssets, "client-assets")),
    RequestPath = "/client-assets"
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.UseRouting();

app.MapGet("/__wasm/bootstrap", (HttpContext context) =>
{
    context.Response.Cookies.Append("wasm-smoke-session", cookieValue, new CookieOptions
    {
        HttpOnly = true,
        IsEssential = true,
        Path = "/",
        SameSite = SameSiteMode.Strict,
        Secure = false
    });
    return Results.Redirect("/app/");
});

app.MapGet("/__wasm/bootstrap-failure", (HttpContext context) =>
{
    context.Response.Cookies.Append("wasm-smoke-invalid-config", "1", new CookieOptions
    {
        HttpOnly = true,
        IsEssential = true,
        Path = "/",
        SameSite = SameSiteMode.Strict,
        Secure = false
    });
    return Results.Redirect("/app/");
});

app.MapGet("/api/frontend/config", (HttpContext context) => NoStoreJson(new
{
    enabled = !context.Request.Cookies.ContainsKey("wasm-smoke-invalid-config"),
    publicBaseUri = publicBaseUri.AbsoluteUri,
    apiBaseUri = new Uri(publicBaseUri, "api/").AbsoluteUri,
    authority = publicBaseUri.AbsoluteUri,
    sourceUrl = "https://github.com/hgzt23678/mk-lite",
    localAccountsEnabled = true
}));

app.MapGet("/api/frontend/session", (HttpContext context) =>
{
    bool cookieObserved = HasFixtureCookie(context, cookieValue);
    bool markerObserved = HasFrontendMarker(context);
    diagnostics.RecordSession(cookieObserved, markerObserved);
    return NoStoreJson(new
    {
        authenticated = false,
        viewer = (object?)null,
        csrf = new
        {
            headerName = "X-CSRF-TOKEN",
            requestToken = csrfToken
        }
    });
});

app.MapPost("/api/meta", (HttpContext context) =>
{
    if (!AcceptProtectedFixtureRequest(context, diagnostics, cookieValue, csrfToken))
    {
        return Results.BadRequest();
    }

    return NoStoreJson(new
    {
        name = "WASM Smoke",
        description = "Standalone WebAssembly browser verification",
        version = "12.119.2-wasm-smoke",
        iconUrl = "/static-assets/favicon.png",
        backgroundImageUrl = (string?)null,
        logoImageUrl = (string?)null,
        disableRegistration = false,
        emailRequiredForSignup = false,
        enableEmail = false,
        tosUrl = (string?)null,
        enableHcaptcha = false,
        hcaptchaSiteKey = (string?)null,
        enableRecaptcha = false,
        recaptchaSiteKey = (string?)null,
        enableTurnstile = false,
        turnstileSiteKey = (string?)null,
        turnstileAction = (string?)null,
        turnstileCdata = (string?)null,
        maintainerName = "WASM smoke",
        maintainerEmail = (string?)null,
        requireSetup = false
    });
});

app.MapPost("/api/announcements", (HttpContext context) =>
    AcceptProtectedFixtureRequest(context, diagnostics, cookieValue, csrfToken)
        ? NoStoreJson(Array.Empty<object>())
        : Results.BadRequest());
app.MapPost("/api/federation/instances", (HttpContext context) =>
    AcceptProtectedFixtureRequest(context, diagnostics, cookieValue, csrfToken)
        ? NoStoreJson(Array.Empty<object>())
        : Results.BadRequest());
app.MapPost("/api/notes/global-timeline", (HttpContext context) =>
    AcceptProtectedFixtureRequest(context, diagnostics, cookieValue, csrfToken)
        ? NoStoreJson(Array.Empty<object>())
        : Results.BadRequest());

app.MapPost("/api/streaming/cursor", (HttpContext context) =>
{
    bool valid = AcceptProtectedFixtureRequest(context, diagnostics, cookieValue, csrfToken);
    diagnostics.RecordCursorRequest(valid);
    return valid ? NoStoreJson(new { cursor = 42L }) : Results.BadRequest();
});

app.MapPost("/__wasm/csrf-probe", (HttpContext context) =>
{
    bool valid = AcceptProtectedFixtureRequest(context, diagnostics, cookieValue, csrfToken);
    diagnostics.RecordCsrfProbe(valid);
    return valid ? Results.NoContent() : Results.BadRequest();
});

app.MapGet("/__wasm/diagnostics", () => Results.Json(diagnostics.Snapshot()));

app.Map("/streaming", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest ||
        !HasFixtureCookie(context, cookieValue) ||
        context.Request.Query["resume"] != "v1" ||
        context.Request.Query["cursor"] != "42")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
    byte[] receiveBuffer = new byte[8_192];
    WebSocketReceiveResult received = await socket.ReceiveAsync(
        new ArraySegment<byte>(receiveBuffer),
        context.RequestAborted).ConfigureAwait(false);
    if (received.MessageType != WebSocketMessageType.Text || received.EndOfMessage is false)
    {
        await socket.CloseAsync(
            WebSocketCloseStatus.InvalidPayloadData,
            "single text frame required",
            context.RequestAborted).ConfigureAwait(false);
        return;
    }

    string frame = Encoding.UTF8.GetString(receiveBuffer, 0, received.Count);
    if (!TryReadStreamSubscription(frame, out string? subscriptionId, out string? channel))
    {
        await socket.CloseAsync(
            WebSocketCloseStatus.InvalidPayloadData,
            "invalid subscription",
            context.RequestAborted).ConfigureAwait(false);
        return;
    }

    diagnostics.RecordWebSocket(channel!);
    await SendJsonAsync(
        socket,
        new { type = "connected", body = new { id = subscriptionId }, cursor = 42L },
        context.RequestAborted).ConfigureAwait(false);
    await SendJsonAsync(
        socket,
        new { type = "checkpoint", body = new { cursor = 43L }, cursor = 43L },
        context.RequestAborted).ConfigureAwait(false);

    WebSocketReceiveResult close = await socket.ReceiveAsync(
        new ArraySegment<byte>(receiveBuffer),
        context.RequestAborted).ConfigureAwait(false);
    if (close.MessageType == WebSocketMessageType.Close)
    {
        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            "smoke complete",
            context.RequestAborted).ConfigureAwait(false);
    }
});

app.MapGet("/", () => Results.Redirect("/app/"));
app.MapStaticAssets();
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

await app.RunAsync().ConfigureAwait(false);

static Uri ReadExplicitPublicBaseUri()
{
    string? configured = Environment.GetEnvironmentVariable("WASM_TEST_PUBLIC_BASE_URI");
    if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? uri) ||
        uri.UserInfo.Length != 0 ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment) ||
        !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal) ||
        uri.Scheme is not ("http" or "https"))
    {
        throw new InvalidOperationException(
            "WASM_TEST_PUBLIC_BASE_URI must be an explicit absolute HTTP(S) origin ending in '/'.");
    }

    return uri;
}

static IResult NoStoreJson<T>(T value)
{
    return new NoStoreJsonResult<T>(value);
}

static bool AcceptProtectedFixtureRequest(
    HttpContext context,
    WasmSmokeDiagnostics diagnostics,
    string cookieValue,
    string csrfToken)
{
    bool valid = HasFixtureCookie(context, cookieValue) &&
        HasFrontendMarker(context) &&
        context.Request.Headers["X-CSRF-TOKEN"] == csrfToken;
    diagnostics.RecordProtectedRequest(valid);
    return valid;
}

static bool HasFixtureCookie(HttpContext context, string cookieValue) =>
    context.Request.Cookies.TryGetValue("wasm-smoke-session", out string? value) &&
    CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(value),
        Encoding.UTF8.GetBytes(cookieValue));

static bool HasFrontendMarker(HttpContext context) =>
    context.Request.Headers["X-ActivityPub-Frontend"] == "1";

static bool TryReadStreamSubscription(
    string frame,
    out string? subscriptionId,
    out string? channel)
{
    subscriptionId = null;
    channel = null;
    try
    {
        using JsonDocument document = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 8 });
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("type", out JsonElement type) || type.GetString() != "connect" ||
            !root.TryGetProperty("body", out JsonElement body) ||
            !body.TryGetProperty("id", out JsonElement id) ||
            !body.TryGetProperty("channel", out JsonElement channelElement))
        {
            return false;
        }

        subscriptionId = id.GetString();
        channel = channelElement.GetString();
        return subscriptionId is { Length: > 0 and <= 128 } && channel == "globalTimeline";
    }
    catch (JsonException)
    {
        return false;
    }
}

static async Task SendJsonAsync(
    WebSocket socket,
    object value,
    CancellationToken cancellationToken)
{
    byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
    await socket.SendAsync(
        new ArraySegment<byte>(payload),
        WebSocketMessageType.Text,
        true,
        cancellationToken).ConfigureAwait(false);
}

internal sealed class NoStoreJsonResult<T>(T value) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Json(value).ExecuteAsync(httpContext);
    }
}

internal sealed class WasmSmokeDiagnostics
{
    private int sessionCookieObserved;
    private int sessionMarkerObserved;
    private int protectedRequestCount;
    private int invalidProtectedRequestCount;
    private int csrfProbeSucceeded;
    private int cursorRequestSucceeded;
    private int webSocketConnected;
    private string? webSocketChannel;

    public void RecordSession(bool cookieObserved, bool markerObserved)
    {
        if (cookieObserved) Interlocked.Exchange(ref sessionCookieObserved, 1);
        if (markerObserved) Interlocked.Exchange(ref sessionMarkerObserved, 1);
    }

    public void RecordProtectedRequest(bool valid)
    {
        Interlocked.Increment(ref protectedRequestCount);
        if (!valid) Interlocked.Increment(ref invalidProtectedRequestCount);
    }

    public void RecordCsrfProbe(bool valid)
    {
        if (valid) Interlocked.Exchange(ref csrfProbeSucceeded, 1);
    }

    public void RecordCursorRequest(bool valid)
    {
        if (valid) Interlocked.Exchange(ref cursorRequestSucceeded, 1);
    }

    public void RecordWebSocket(string channel)
    {
        Volatile.Write(ref webSocketChannel, channel);
        Interlocked.Exchange(ref webSocketConnected, 1);
    }

    public object Snapshot() => new
    {
        sessionCookieObserved = Volatile.Read(ref sessionCookieObserved) == 1,
        sessionMarkerObserved = Volatile.Read(ref sessionMarkerObserved) == 1,
        protectedRequestCount = Volatile.Read(ref protectedRequestCount),
        invalidProtectedRequestCount = Volatile.Read(ref invalidProtectedRequestCount),
        csrfProbeSucceeded = Volatile.Read(ref csrfProbeSucceeded) == 1,
        cursorRequestSucceeded = Volatile.Read(ref cursorRequestSucceeded) == 1,
        webSocketConnected = Volatile.Read(ref webSocketConnected) == 1,
        webSocketChannel = Volatile.Read(ref webSocketChannel)
    };
}
