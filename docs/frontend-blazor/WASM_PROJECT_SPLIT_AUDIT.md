# Blazor WebAssembly project split audit

Date: 2026-08-13 UTC

Scope: `frontend/ActivityPub.Misskey.Blazor`

This audit originally classified the pre-migration source by browser boundary. The inventory below is retained as the decision record for the split; the implementation status that follows records the completed 2026-08-13 checkpoint.

## Implementation status

- `ActivityPub.Misskey.Blazor` is now a browser-safe Razor class library with no server project reference or `Microsoft.AspNetCore.App` framework reference.
- `ActivityPub.Misskey.Blazor.Server` owns the server-only presentation implementations used by the comparison TestHost.
- `ActivityPub.Misskey.Blazor.Client` is the standalone `Microsoft.NET.Sdk.BlazorWebAssembly` entry point and references only the browser-safe RCL.
- The production ASP.NET Core host serves the Client at `/app/`; Interactive Server, `blazor.web.js`, and `/_blazor` are not part of that route.
- The session-cookie, antiforgery, HTTP adapter, durable streaming, PWA, and safe bootstrap-failure boundaries described by the gates below are implemented and covered by Release tests and a real Chromium WASM smoke.
- The historical “required” and “before the new Client” sections below describe the acceptance criteria used during the migration, not current missing work.

## Reproduce the inventory

Run:

```bash
bash tools/frontend-wasm-audit.sh | awk -F '\t' '{ count[$1]++ } END { for (category in count) print category, count[category] }' | sort
```

The 2026-08-13 result is:

| Classification | Count | Meaning |
| --- | ---: | --- |
| `browser-safe-source` | 375 | C# or Razor source has no known server boundary and can remain in the browser-safe RCL once its referenced contracts are extracted. |
| `shared-ui-contract` | 25 | A whole source file contains browser-safe UI models, records, enums, or component contracts. |
| `mixed-contract-and-server` | 25 | A file contains UI-facing interfaces or view models and a server-side implementation. Split the types before compiling the UI for WASM. |
| `browser-refactor-required` | 8 | UI source must remain, but a server-only type or prerender assumption must be replaced. |
| `host-only` | 6 | Existing Interactive Server document, middleware, request localization, CSP, or server DI registration. Do not compile it into WASM. |
| `browser-safe-asset` | 2,021 | Existing CSS, JavaScript, fonts, icons, locale/theme JSON, and other static assets. |
| `browser-asset-refactor-required` | 4 | Manifest and service-worker files contain root-scope or Server-specific paths and cache names. |

The source total is 439 C#/Razor files. The asset total is 2,025 files: 1,949 files under `wwwroot` and 76 scoped `.razor.css` files. The scoped styles remain browser-safe with their components.

This is a lexical inventory with curated exceptions, not a substitute for the WASM compiler and publish artifact gate. `browser-safe-source` means that the file body can run in a browser; it can still refer to an interface currently declared in a mixed file.

## Host-only files

The following existing files must not be compiled into `ActivityPub.Misskey.Blazor.Client`:

- `App.razor`: full HTML document, `IHttpContextAccessor`, CSP nonce, `InteractiveServer`, circuit reconnect UI, and `blazor.web.js`.
- `MisskeyFrontendServiceCollectionExtensions.cs`: registers `IHttpContextAccessor` and every current server presentation/streaming implementation.
- `Localization/MisskeyFrontendLocalizationMiddleware.cs`: ASP.NET Core middleware.
- `Localization/MisskeyLocaleRequestResolver.cs`: selects locale from `HttpContext`.
- `Localization/MisskeyLocalizer.cs`: initializes locale from `HttpContext`.
- `Security/FrontendCspNonce.cs`: reads an ASP.NET Core request item.

`MisskeyLocaleCatalog.cs` itself is browser-safe. It reads an embedded assembly resource, which is supported by WASM. Only request-based locale selection and mutation must be replaced by a browser implementation.

## Mixed contract and server files

These files cannot move whole. Public records, enums, exceptions, and interfaces used by Razor belong in the browser-safe RCL. Their implementations must be replaced by typed HTTP or WebSocket clients in the WASM entry project.

### Identity and client state

- `Identity/AuthenticatedActorContext.cs`: extract `AuthenticatedActor`, `FrontendAuthenticationException`, and `IAuthenticatedActorContext`; replace `AuthenticatedActorContext` with a cookie-backed `/api/i` implementation and a browser `AuthenticationStateProvider`.
- `Client/MisskeyClientModuleUtilities.cs`: retain the pure snapshots/utilities, but replace `MisskeyAccountState`, which directly injects `IClientApiQueryService`. Do not retain `AddAccountAsync(token)` browser storage behavior for the session-cookie frontend.

### Presentation contracts

- `Presentation/AboutPresentationService.cs`
- `Presentation/AdminPresentationService.cs`
- `Presentation/AnnouncementPagePresentationService.cs`
- `Presentation/AnnouncementPresentationService.cs`
- `Presentation/AutocompletePresentationService.cs`
- `Presentation/AvatarsPresentationService.cs`
- `Presentation/ComposerMediaService.cs`
- `Presentation/CurrentAccountPresentationService.cs`
- `Presentation/HashtagTrendPresentationService.cs`
- `Presentation/InstancePresentationService.cs`
- `Presentation/NoteDeletionPresentationService.cs`
- `Presentation/NotificationPresentationService.cs`
- `Presentation/ReactionDetailsPresentationService.cs`
- `Presentation/RenoteDetailsPresentationService.cs`
- `Presentation/SettingsPresentationService.cs`
- `Presentation/TimelinePresentationService.cs`
- `Presentation/UserFollowRelationsPresentationService.cs`
- `Presentation/UserPreviewPresentationService.cs`
- `Presentation/UserSearchPresentationService.cs`
- `Presentation/VisibleUsersPresentationService.cs`

The UI-facing interfaces and view models stay unchanged initially so that the 264 Razor files do not need a redesign. Each concrete implementation becomes an HTTP adapter over existing same-origin Misskey routes such as `/api/meta`, `/api/i`, `/api/notes/*`, `/api/users/*`, `/api/i/notifications`, `/api/announcements`, and `/api/admin/*`.

### Streaming contracts

- `Streaming/ServerTimelineStream.cs`
- `Streaming/NotificationSubscriptionService.cs`
- `Streaming/RelationshipSubscriptionService.cs`

Extract the three interfaces and mutation records. Replace the implementations with one shared browser WebSocket connection to `/streaming`; multiplex Misskey `connect`/`disconnect` channel messages and fan out typed events. The server endpoint already accepts a durable `cursor`, defaults a missing cursor to the latest retained event, rechecks viewer visibility, and projects timeline, notification, relationship, reaction, poll, update, and deletion messages.

## Browser refactors that preserve the UI

- `_Imports.razor`: remove `ActivityPub.Domain`, `Microsoft.AspNetCore.Http`, and the static server render-mode import. Keep component, authorization, forms, routing, logging, and JS interop imports.
- `Presentation/TimelineModels.cs`: replace `ActivityPub.Domain.Visibility` with a browser contract enum that serializes to Misskey values `public`, `home`, `followers`, and `specified`.
- `Overlays/IMisskeyOverlayService.cs`: use the same browser visibility contract instead of the Domain enum. The overlay service itself is browser-safe state.
- `Client/MisskeyClientScriptUtilities.cs`: return the browser visibility contract.
- `Presentation/MentionNotePaginationSource.cs`: compare against the browser visibility contract.
- `Components/MkPoll.razor`: remove `ActivityPub.Application` exceptions/enums from the component boundary. The HTTP timeline adapter must translate API errors into frontend exceptions.
- `Pages/V12/MiauthSession.razor`: replace direct `IMisskeyAuthenticationService.IssueAsync` with `/api/miauth/gen-token`; keep the existing page, permission display, callback checks, and state machine.
- `Components/TimelineView.razor`: replace `PersistentComponentState` and `RegisterOnPersisting` with bounded browser state. Preserve the pagination seed/cursor behavior and do not turn this into a stateless or reduced timeline.

`Home.razor` and `FollowPage.razor` use `Microsoft.AspNetCore.WebUtilities.QueryHelpers`, but not a server request object. They are browser-capable if that package is retained; replacing these calls with `NavigationManager` URI helpers can reduce the client package surface.

## Whole-file shared UI contracts

The following files can remain whole in the browser-safe RCL:

- All 20 top-level `Components/*.cs` model/context files: `CalendarWidgetSnapshot`, `EmojiPickerChosenEvent`, `InstanceTickerViewModel`, media/dialog/form/widget/clock/modal/page/tab/window models, `NotificationSettingResult`, and `StickyOffsetContext`.
- `Presentation/ComposerModels.cs`
- `Presentation/EmojiPickerModels.cs`
- `Presentation/MisskeyFrontendRuntimeConfiguration.cs`
- `Presentation/MisskeyPaginationModels.cs`
- `Presentation/VisitorAnnouncementViewModel.cs`

Also retain the browser-safe state/catalog code in `State/`, `Localization/MisskeyLocaleCatalog.cs`, all 77 browser interop files, and the pure utilities under `Client/`. Those are runtime implementations rather than DTO-only files, so the inventory reports them as browser-safe source instead of shared contracts.

The mixed presentation files additionally contain records that must be extracted without changing their public shape. In particular: timeline/note/author/media/poll/draft models; notification models and query; user preview/follow relations/page models; instance/federation/about models; settings token/profile models; admin models; autocomplete models; and composer media models.

## Minimal buildable project split

The smallest clean split is two frontend projects, not a WASM project that references the current server RCL unchanged:

```text
ActivityPub.Misskey.Blazor              browser-safe Razor class library
  Components, Pages, Layouts, Routes
  BrowserInterop, Client utilities, State, Overlays
  UI contracts/view models/interfaces
  CSS, JavaScript, fonts, icons, locale/theme/emoji assets
  no ActivityPub.MisskeyApi/Application/Domain reference

ActivityPub.Misskey.Blazor.Client       Microsoft.NET.Sdk.BlazorWebAssembly
  Program.cs / WebAssemblyHostBuilder
  App.razor / wwwroot/index.html
  client DI registration
  same-origin typed Misskey HTTP adapters
  cookie authentication-state provider
  /streaming WebSocket client
  PWA manifest and service worker
  project reference: ActivityPub.Misskey.Blazor only
```

A third Contracts project is not required for the minimum split. The UI RCL can own the frontend contracts because the backend already exposes Misskey JSON at HTTP/WebSocket boundaries. Introduce a separate `ActivityPub.Misskey.Blazor.Contracts` project only if server endpoint serialization is deliberately changed from its current response types/anonymous projections to shared explicit DTOs.

The browser-safe RCL should keep the assembly name and static asset base `ActivityPub.Misskey.Blazor` so the existing `_content/ActivityPub.Misskey.Blazor/...` URLs, CSS isolation bundle name, and 82 JavaScript module paths do not all change at once.

The current RCL has one direct project reference, `ActivityPub.MisskeyApi`. That project references `ActivityPub.Application`, `ActivityPub.Domain`, and `ActivityPub.Persistence`; Persistence then brings Identity, EF Core, Npgsql, OpenIddict, and Redis dependencies. Removing only direct service injection while retaining that project reference is therefore insufficient: the browser-safe RCL must have no project reference to `ActivityPub.MisskeyApi`.

### Required Client project shape

The eventual Client project needs the following structure:

```text
frontend/ActivityPub.Misskey.Blazor.Client/
  ActivityPub.Misskey.Blazor.Client.csproj
  Program.cs
  App.razor
  _Imports.razor
  Authentication/CookieAuthenticationStateProvider.cs
  Http/MisskeyApiClient.cs
  Http/*PresentationService.cs
  Localization/BrowserMisskeyLocalizer.cs
  Streaming/MisskeyStreamingClient.cs
  wwwroot/index.html
  wwwroot/manifest.webmanifest
  wwwroot/service-worker.js
  wwwroot/service-worker.published.js
```

`Program.cs` should use `WebAssemblyHostBuilder.CreateDefault(args)`, register `App` at `#app` and `HeadOutlet` at `head::after`, create a same-origin `HttpClient` from `builder.HostEnvironment.BaseAddress`, call `AddAuthorizationCore` and `AddCascadingAuthenticationState`, and register the browser services. This follows Iceshrimp.NET's useful process boundary: standalone WASM bootstrap, same-origin HTTP client, browser state/auth provider, and a browser streaming client. It intentionally does not copy Iceshrimp.NET's bearer-token-in-browser-storage authentication because this repository requires an HttpOnly session cookie.

`App.razor` must render the existing `Routes` component. `wwwroot/index.html` must contain the current stylesheet/module order, `<base href="/app/">`, the Blazor WASM boot script, overlay/error roots, and the WebSocket/API connection status UI. Moving to WASM is not permission to replace the existing shells or pages.

## Why no bootstrap was added in this audit

Adding a Client project that references the current RCL would compile only by transitively carrying `ActivityPub.MisskeyApi`, `ActivityPub.Application`, `ActivityPub.Domain`, and their server dependency graph into the browser publish output. It would also retain `InteractiveServer`, `HttpContext`, circuit reconnect markup, and server presentation implementations. Such a project is not a valid migration checkpoint.

Adding an empty `App.razor` or a reduced placeholder shell would be buildable but would violate the requirement to preserve the current UI. Therefore the first buildable Client checkpoint must follow extraction of the 25 mixed files and the 8 browser refactors above. No production bootstrap was added here.

## Host and security integration gates

Before the new Client becomes the production path:

1. Add the WebAssembly package/version and Client project to central package/solution files.
2. Serve the Client publish assets at `/app/`, with SPA fallback for every current client route and no fallback for `/api`, `/streaming`, `/media`, ActivityPub, OAuth, or static asset misses.
3. Keep `/streaming` same-origin so the browser automatically carries the HttpOnly session cookie. Never add the session token to the WebSocket query string.
4. Add a browser-readable antiforgery request-token endpoint or same-origin mutation header contract and validate every cookie-authenticated mutation. `SameSite=Strict` is useful defense in depth, not the only CSRF check.
5. Make `/api/i` the authoritative viewer/auth-state query; 401 means anonymous. Do not expose the cookie value to JavaScript.
6. Split current account storage: preferences and non-secret account labels may use IndexedDB/local storage, while tokens, cookies, authorization codes, and MiAuth secrets must not.
7. Replace the circuit reconnect UI with API/WebSocket status. Preserve exponential backoff with jitter, a bounded event queue, durable cursor resume, deduplication, unsubscribe/disposal, and account-change reconnect.
8. Update CSP for external WASM scripts and `connect-src 'self'`; remove nonce-dependent inline bootstrap rather than weakening CSP with `unsafe-inline`.
9. Change PWA scope and registration from `/` to `/app/`; use the generated `service-worker-assets.js`, version caches by publish content, and never cache `/api`, `/streaming`, `/media`, authenticated navigation responses, or private content.

## Compile and artifact gates

The first accepted Client checkpoint should pass:

```bash
dotnet build frontend/ActivityPub.Misskey.Blazor.Client/ActivityPub.Misskey.Blazor.Client.csproj --configuration Release
dotnet publish frontend/ActivityPub.Misskey.Blazor.Client/ActivityPub.Misskey.Blazor.Client.csproj --configuration Release
```

Then inspect the publish boot manifest and compressed assemblies and fail if they contain any of:

- `ActivityPub.MisskeyApi`
- `ActivityPub.Application`
- `ActivityPub.Domain`
- `ActivityPub.Persistence`
- `ActivityPub.Identity`
- `Microsoft.EntityFrameworkCore`
- `Npgsql`

Also fail if the production HTML or network trace contains `InteractiveServer`, `blazor.server.js`, `blazor.web.js`, `/_blazor`, or the circuit reconnect element. Require `_framework/blazor.webassembly.js` and the WASM boot assets instead.

## Iceshrimp.NET comparison

The current Iceshrimp.NET frontend uses `Microsoft.NET.Sdk.BlazorWebAssembly`, a `WebAssemblyHostBuilder`, a same-origin `HttpClient`, browser authorization state, frontend stores, PWA service-worker assets, and a browser streaming service. Its API client calls backend HTTP endpoints, and its streaming service connects from the browser rather than injecting backend repositories. Those boundaries validate this split. Its local bearer-token/session storage and SignalR-specific transport are implementation choices and are not copied here; mkdotnet already has a native `/streaming` Misskey WebSocket and an HttpOnly cookie policy.

Primary references:

- <https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Frontend/Iceshrimp.Frontend.csproj>
- <https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Frontend/Startup.cs>
- <https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Frontend/Core/Services/ApiClient.cs>
- <https://iceshrimp.dev/iceshrimp/Iceshrimp.NET/src/branch/dev/Iceshrimp.Frontend/Core/Services/StreamingService.cs>
