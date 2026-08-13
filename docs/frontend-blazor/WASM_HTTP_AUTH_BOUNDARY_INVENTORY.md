# WASM HTTP・認証境界 inventory

## 目的と調査時点

この文書は、Interactive Server の Presentation 層を Blazor WebAssembly から利用できる同一 origin HTTP 境界へ置換するために実施した監査結果である。以下のinventoryと「必要」の記述は移行前の判断記録として保持する。

調査対象は 2026-08-13 UTC、commit `3619793` の次の実装である。

- `frontend/ActivityPub.Misskey.Blazor/Presentation/`
- `frontend/ActivityPub.Misskey.Blazor/Identity/`
- `frontend/ActivityPub.Misskey.Blazor/Pages/V12/MiauthSession.razor`
- `src/ActivityPub.MisskeyApi/MisskeyEndpoints.cs`
- `src/ActivityPub.Identity/ApiAuthenticationExtensions.cs`
- `src/ActivityPub.Api/FrontendEndpoints.cs`
- `src/ActivityPub.Api/MediaEndpoints.cs`

## 2026-08-13 実装checkpoint

- `ActivityPub.Misskey.Blazor.Client`が全UI Presentation interfaceを同一originの型付きHTTP／WebSocket adapterとして登録し、server実装を参照しない。
- `/api/frontend/config`と`/api/frontend/session`を実装し、PublicBaseUri、Authority、viewer、memory-only antiforgery contractを明示的にbootstrapする。
- HttpOnly session Cookieはbrowser marker付きの許可endpointだけに適用する。Bearerとnative Misskey `i` tokenは従来の認証経路を維持する。
- browser sign-in responseはraw Misskey tokenを発行・返却せず、native sign-in responseだけがhash-backed tokenを返す。
- Cookie認証されたunsafe Misskey API requestはsame-origin frontend markerとantiforgery headerを要求する。直接fetchを行う認証／logout moduleも同じmemory-only token sourceを使う。
- endpoint不足だったrenote一覧、token ID／expiry、invite expiry、discoverable投影を後方互換に追加し、Clientで必須値として検証する。
- Release全テスト973件と実standalone WASM Chromium smokeで、Cookie、CSRF、initial account／timeline、admin gating、秘密値非露出を再検証した。

したがって、この文書で列挙した最初のHTTP／認証blockerは解消済みである。Dolphinに契約がない機能は引き続きcapability exclusionであり、空responseによる代替は行わない。

## 結論

`instance meta -> current account -> initial timeline` の最初の vertical slice に必要な製品データ endpoint は既に存在する。

- instance meta: `POST /api/meta`
- current account: `POST /api/i`
- home timeline: `POST /api/notes/timeline`
- local timeline: `POST /api/notes/local-timeline`
- global timeline: `POST /api/notes/global-timeline`
- hybrid timeline: `POST /api/notes/hybrid-timeline`

ただし、現状の HttpOnly session Cookie だけでは protected Misskey API を呼べない。

`ApiAuthenticationExtensions` は `Authorization` がない `/api` と `/streaming` を `MisskeyTokenAuthenticationHandler` へ転送する。

session Cookie の `ExternalSessionScheme` は `FrontendPathBaseRequiredMetadata` を持つ endpoint だけに選択されるため、`/api/i`、home timeline、mutation、`/streaming` は Cookie を認証に使わず 401 になる。

したがって最初の slice の本当の blocker は endpoint 不足ではなく、次の browser-session adapter である。

1. memory-only CSRF token と viewer を返す bootstrap endpoint。
2. 明示された browser request だけ `ExternalSessionScheme` へ送る scheme selection。
3. Cookie 認証された unsafe request だけ CSRF を検証する境界。
4. Cookie を使う WebSocket の scheme selection と既存 Origin 検証。
5. browser sign-in response から raw Misskey token を除く分岐。

WASM client は `ActivityPub.MisskeyApi` を参照してはならない。

同 project は `ActivityPub.Persistence`、EF Core、Identity、Npgsql、Redisまで推移参照するためである。

wire DTO は新しい browser-safe contracts project か WASM Client 内へ分離する。

## Presentation service の直接依存 inventory

`browser-safe` はそのまま client DI へ残せる依存、`HTTP` は既存 endpoint へ置換する依存、`server-only` は WASM 成果物から除外する依存を表す。

| Presentation service | 現在の直接依存 | 分類 | HTTP 置換 |
|---|---|---|---|
| `AboutPresentationService` | `IFederationQueryStore`, `MisskeyQueryService` | server-only | `/api/stats`, `/api/federation/instances` |
| `AdminPresentationService` | `MisskeyAnnouncementService`, `IRelayCommandService`, `IRegistrationInvitationService`, `IAuthenticatedActorContext` | server-only / auth | `/api/admin/announcements/*`, `/api/admin/relays/*`, `/api/admin/invite` |
| `AnnouncementPagePresentationService` | `MisskeyAnnouncementService`, `IAuthenticatedActorContext` | server-only / auth | `/api/announcements`, `/api/i/read-announcement` |
| `AnnouncementPresentationService` | `MisskeyAnnouncementService` | server-only | `/api/announcements` |
| `AutocompletePresentationService` | `IClientApiQueryService`, `IHashtagRepository`, `IEmojiCatalog`, runtime config | HTTP + browser-safe catalog/config | `/api/users/search`, `/api/hashtags/search`; emoji/MFM検索はbrowser-local |
| `AvatarsPresentationService` | `IClientApiQueryService`, `IExternalEntityIdService`, runtime config | server-only | `/api/users/show` の `userIds` |
| `ComposerMediaService` | `IServiceProvider -> IMediaService`, `IAuthenticatedActorContext` | server-only / auth | `/api/drive/files/create` を優先 |
| `CurrentAccountPresentationService` | `IClientApiQueryService`, `IAuthenticatedActorContext` | server-only / auth | `/api/i` と session bootstrap |
| `HashtagTrendPresentationService` | `IHashtagRepository` | server-only | `/api/hashtags/trend` |
| `InstancePresentationService` | `MisskeyMetadataService`, `MisskeyQueryService` | server-only | `/api/meta`, `/api/federation/instances` |
| `NoteDeletionPresentationService` | `IAuthenticatedActorContext`, `IClientApiCommandService` | server-only / auth | `/api/notes/delete` |
| `NotificationPresentationService` | `IClientNotificationService`, `IClientApiQueryService`, `IExternalEntityIdService`, `IAuthenticatedActorContext`, `IUserPreviewPresentationService` | server-only / auth | `/api/i/notifications`, `/api/notifications/read`, `/api/notifications/mark-all-as-read`; live notification は `/streaming` の完全な `body` を直接map |
| `ReactionDetailsPresentationService` | `IClientApiQueryService`, `IExternalEntityIdService`, `IAuthenticatedActorContext` | server-only / auth | `/api/notes/reactions` |
| `RenoteDetailsPresentationService` | `IClientApiQueryService`, `IExternalEntityIdService`, `IAuthenticatedActorContext` | server-only / auth | endpoint欠落 |
| `SettingsPresentationService` | `IClientApiQueryService`, `IProfileUpdateService`, `IAuthenticatedActorContext`, `IMisskeyAuthenticationService`, `IExternalEntityIdService` | server-only / auth | `/api/i`, `/api/i/update`, `/api/i/apps`, `/api/miauth/gen-token`, `/api/i/revoke-token` |
| `TimelinePresentationService` | `IClientApiQueryService`, `IClientApiCommandService`, `IExternalEntityIdService`, `IAuthenticatedActorContext`, runtime config | server-only / auth | 4 timeline route、`/api/notes/show`, `/api/notes/create`, reaction、poll、delete route |
| `UserFollowRelationsPresentationService` | `IClientApiQueryService`, `IExternalEntityIdService`, `IUserPreviewPresentationService`, runtime config | server-only | `/api/users/followers`, `/api/users/following` |
| `UserPagePresentationService` | `IUserPreviewPresentationService`, concrete `TimelinePresentationService` | browser-safe composition after HTTP port | `/api/users/show` + `/api/users/notes` |
| `UserPreviewPresentationService` | `IClientApiQueryService`, `IClientApiCommandService`, `IExternalEntityIdService`, `IAuthenticatedActorContext`, runtime config | server-only / auth | `/api/users/show`, `/api/users/relation`, `/api/following/create`, `/api/following/delete` |
| `UserSearchPresentationService` | `IClientApiQueryService`, `IExternalEntityIdService`, `IUserPreviewPresentationService`, runtime config | server-only | `/api/users/search` |
| `VisibleUsersPresentationService` | `IClientApiQueryService`, `IExternalEntityIdService`, runtime config | server-only | `/api/users/show` の `userIds` |

`IAuthenticatedActorContext` 自体はfrontend型だが、現実装は server-side `AuthenticationStateProvider` と `IClientApiQueryService` に依存する。

WASMでは session bootstrap response を正本とする browser実装へ交換する。

`IExternalEntityIdService` は clientへ移さない。

Misskey endpoint が既に外部Misskey IDをrequest/responseに使用するため、browserは内部 `Guid` を解決しない。

`TimelineModels.cs` の `ActivityPub.Domain.Visibility`、`NoteDraft` の内部 `Guid`、`_Imports.razor` のDomain参照も別途browser-safe enumと外部string IDへ変える必要がある。

## endpoint 置換分類

### そのまま利用できる

次は既存Misskey JSON契約をtyped clientから呼べる。

| 機能 | endpoint |
|---|---|
| instance | `/api/meta`, `/api/stats`, `/api/federation/instances` |
| viewer/account | `/api/i`, `/api/users/show`, `/api/users/search`, `/api/users/search-by-username-and-host`, `/api/users/relation` |
| timeline/note read | `/api/notes/timeline`, `/api/notes/local-timeline`, `/api/notes/global-timeline`, `/api/notes/hybrid-timeline`, `/api/notes/show`, `/api/users/notes` |
| note mutation | `/api/notes/create`, `/api/notes/delete`, `/api/notes/reactions/create`, `/api/notes/reactions/delete`, `/api/notes/polls/vote` |
| follow | `/api/following/create`, `/api/following/delete`, `/api/users/followers`, `/api/users/following` |
| notification | `/api/i/notifications`, `/api/notifications/read`, `/api/notifications/mark-all-as-read` |
| announcement | `/api/announcements`, `/api/i/read-announcement`, `/api/admin/announcements/*` |
| search/trend | `/api/hashtags/search`, `/api/hashtags/trend` |
| settings | `/api/i/update`, `/api/i/apps`, `/api/i/revoke-token` |
| admin | `/api/admin/relays/*`, `/api/admin/invite`, `/api/admin/announcements/*` |
| media | `/api/drive/files/create` |
| streaming | `/streaming` |

### client compositionまたはresponse拡張が必要

- renote作成は専用endpointではなく `/api/notes/create` の `renoteId` を使う。
- reaction mutation と poll vote は204を返す。現在のPresentation APIが要求する更新後 `NoteViewModel` は、同じ外部note IDで `/api/notes/show` を再取得すれば実現できる。単一round tripが必要なら既存responseを後方互換に拡張する。
- notification streamは完全なMisskey notificationを `body` に含む。現在の `FindAsync(Guid)` をbrowserへ移さず、stream bodyを直接mapする。
- `/api/i/apps` は `SettingsApiTokenViewModel` が必要とする `expiresAt` を返さない。
- `/api/miauth/gen-token` はsettingsで必要なtokenを返すが、`SettingsApiTokenIssuedViewModel` が必要とする外部IDと `expiresAt` を返さない。発行後に `/api/i/apps` を再読して推測せず、responseを `{ token, id, expiresAt }` へ後方互換に拡張する。
- `/api/admin/invite` はcodeだけを返し、`AdminInvitationViewModel` が必要とする `expiresAt` を返さない。
- `/api/drive/files/create` は必要なfile projectionを返すが、現在は明示的にantiforgeryを無効化している。Cookie frontend requestだけを対象にするCSRF filterを別途適用する。
- `/api/i` のMisskey projectionでは `isAdmin` が常にfalseである。WASMのadmin gatingはsession bootstrapが返すserver principalのroleを使い、`/api/i` の値を信用しない。

### endpointまたはprotocolが欠落

- `RenoteDetailsPresentationService` に対応する `/api/notes/renotes` がない。`noteId`, `limit`, optional viewerを受け、visibilityを再検証したMisskey user配列を返すendpointが必要である。
- `/streaming?cursor=` はresume cursorを受けるが、outbound messageにcursorが含まれない。browserは最後に処理したdurable cursorを更新できない。`connected/checkpoint/channel/noteUpdated` messageへ単調増加cursorを含めるか、別のack/checkpoint protocolが必要である。
- initial queryとsubscription間の取りこぼしを機械的に閉じるREST cursor endpointはない。現protocolを使う場合は、WebSocket接続とchannel購読を先に完了し、messageをbounded bufferへ貯めてから初期timelineを取得し、外部note IDでdedupeする。

## HttpOnly session CookieのWASM設計

### session bootstrap

同一originの `GET /api/frontend/session` を追加する。

endpointは `IAntiforgery.GetAndStoreTokens` を呼び、次だけを `Cache-Control: no-store` と `Vary: Cookie` で返す。

```json
{
  "authenticated": true,
  "viewer": {
    "id": "misskey-external-user-id",
    "username": "alice",
    "name": "Alice",
    "avatarUrl": "/media/..."
  },
  "roles": ["admin"],
  "csrf": {
    "headerName": "X-CSRF-TOKEN",
    "requestToken": "..."
  }
}
```

- anonymousでもCSRF request tokenを返す。これによりsignin/signup/passkey開始前にtokenを得られる。
- responseへCookie、session ticket、Misskey token、actor IRI、security stamp、全claimを含めない。
- request tokenは認証credentialではないが、localStorage、sessionStorage、IndexedDB、URLへ保存せずWASM memoryだけに置く。
- `AuthenticationStateProvider` はこのresponseからUI用の最小principalを作る。server authorizationは必ずCookie principalを再評価し、client claimsを信用しない。
- signin、signup、passkey完了、email確認、logout、account switch後はbootstrapを再取得する。匿名時のantiforgery tokenはlogin後のidentityに結び付かない可能性があるため、使い回さない。

既存session cookieの属性は維持する。

- name: `__Host-activitypub-oauth-session`
- `HttpOnly=true`
- `Secure=Always`
- `SameSite=Lax`
- `Path=/`
- non-persistent
- sliding expirationなし
- `LocalAccounts:SessionLifetime`、既定8時間

`LocalAccountCookieEvents.ValidatePrincipal` はuserのActive状態とsecurity stampをrequestごとに再検証しているため、suspend、password/security-stamp変更、account削除時の失効境界として再利用できる。

### API authentication schemeの分離

従来のnative Misskey clientを壊さないため、Cookieの存在だけでschemeを切り替えない。

HTTP typed clientは全requestに `X-ActivityPub-Frontend: 1` を付け、serverはbrowser利用を許可したendpoint metadataと固定 `Frontend:PublicBaseUri` のsame-origin provenanceが一致する場合だけ `ExternalSessionScheme` を選ぶ。

scheme selectionの優先順位は次とする。

1. `Authorization: Bearer mk_...` はMisskey token。
2. `/streaming?i=...` はMisskey token。
3. browser marker + browser-session endpoint metadata はExternal session Cookie。
4. 従来の `/api` JSON body `i` はMisskey token。
5. その他のBearerは既存OpenIddict/JWT。

browser `WebSocket` は任意headerを付けられない。

`/streaming` 自体へbrowser-session metadataを付け、query `i` がなく、既存の固定origin WebSocket検証に成功した場合だけCookie schemeを選ぶ。

Cookie schemeのAPI challenge/forbidはlogin redirectではなく、`Cache-Control: no-store` の401/403 Misskey JSONを返す。

admin routeには追加のpolicy修正が必要である。

現在の `activitypub.admin` policyはroleと `activitypub.admin` scopeの両方を要求するが、local Cookie principalはadmin roleを持ってもscopeが `openid profile activitypub.read activitypub.write` だけである。

native API tokenのrole+admin scope要件を維持しつつ、External session identityではadmin roleを十分条件にするfirst-party admin policyへ分岐する。

## CSRF設計

Misskey APIはnative token clientとbrowser Cookie clientを同じrouteで受けるため、全endpointへ無条件にantiforgery metadataを付けてはならない。

推奨境界は、routing/authentication後かつhandlerのbody読取り前に動くbrowser-session filter/middlewareである。

次のすべてを満たすrequestだけ `IAntiforgery.ValidateRequestAsync` する。

- endpointがbrowser-session mutation metadataを持つ。
- `X-ActivityPub-Frontend: 1` がある。
- authenticated identityのauthentication typeが `ExternalSessionScheme` である。
- methodがPOST/PUT/PATCH/DELETEである。

clientはbootstrapで受けた `X-CSRF-TOKEN` をmutation headerへ入れる。

明示的BearerまたはJSON body `i` のnative clientは従来どおりCSRF不要である。

frontend CORSは不要であり、Cookie credentialをcross-originで許可しない。

CSRF headerを理由に `AllowCredentials` や広いoriginを追加しない。

次もbrowser-session mutationとして保護する。

- `/api/i/notifications` の `markAsRead=true`
- `/api/miauth/gen-token`
- `/api/drive/files/create`
- `/api/signin` のbrowser frontend branch
- `/api/admin/accounts/create`
- `/auth/logout`
- signup、passkey、password reset、email確認

`/api/signin` のnative互換branchは現行のfixed-origin `Origin` / `Sec-Fetch-Site` 検証とrate limitを維持する。

browser frontend branchはbootstrap tokenも検証する。

CSRF token失効時にnon-idempotent commandを自動再送しない。

安定した `Idempotency-Key` を持つcommandだけ、bootstrap再取得後に同じkeyで一度再送できる。

## signin、signup、passkey、MiAuth

### signin

`POST /api/signin` のbrowser branchは次に変更する。

- password/TOTP/passkey、lockout、suspended判定は既存Identity境界を再利用する。
- `ExternalSessionScheme` のHttpOnly Cookieを発行する。
- responseは `{ status, redirectUrl }` とsafe errorだけにする。
- browser branchでは `IssueDirectAsync` を呼ばず、raw `i` tokenを発行・返却しない。
- native Misskey branchは専用hash-backed tokenと既存 `i` responseを維持する。

現行 `auth-form.js` はresponseの `i` を保存していないが、raw token自体はJavaScriptから読めるresponseへ含まれる。

WASM移行では「保存しない」だけでなくbrowser branchへ返さない。

成功後はsession bootstrapを再取得し、viewer IDが以前と異なる場合はuser-scoped stateを破棄し、WebSocketを切断して新accountで接続し直す。

### signup

WASMはnative `POST /api/signup` ではなく既存 `POST /auth/register` を使う。

後者はantiforgery、Identity登録、email確認、session Cookieを既に扱う。

`AntiforgeryToken` componentのSSR hidden inputには依存せず、bootstrapのheader tokenを送る。

email確認必須時は未認証のまま `signup-email-pending` を扱い、確認完了後にbootstrapを再取得する。

initial administrator setupは現行browser専用endpointを維持するが、CSRFを追加し、responseからraw tokenを除く。

### passkey

`POST /auth/passkey/options` と `POST /auth/passkey/assertion`、Identityのprotected passkey state、2分のStrict/HttpOnly/Secure state Cookieを再利用する。

WASM/JSは次だけを扱う。

1. bootstrap CSRF header付きでoptionsを要求する。
2. `navigator.credentials.get` を呼ぶ。
3. assertionを既存endpointへCSRF header付きで送る。
4. 成功後にsession bootstrapを再取得する。

challenge、password、assertion、credential JSONをC# component state、browser storage、telemetryへ渡さないという既存 `auth-form.js` の境界を維持する。

### MiAuth

authorizing pageの `IMisskeyAuthenticationService.IssueAsync` 直接呼出しは `/api/miauth/gen-token` へ置換できるが、そのままではraw tokenがauthorizing browserへ返る。

sessionがnon-nullのbrowser approvalではresponseを `{ ok: true }` にし、raw tokenは返さない。

tokenは既存のencrypted one-time sessionにだけ保持し、外部appが `/api/miauth/{session}/check` を単回consumeした時だけ返す。

settingsの `session: null` token生成は利用者へ一度だけcopyさせる機能なので別response contractとして維持できるが、memory表示だけに限定する。

callbackはbrowserだけで検証せずserverへ渡し、既存 `ValidateCallbackUri` と同等のpolicyで検証したsafe redirectだけをresponseに返す。

`http` はloopbackだけ、`https` と明示的なcustom schemeはuserinfoなし、`file`は禁止する。

## 最初のvertical slice

### Slice 1: session + instance + current account + initial timeline

1. browser-safe contracts projectを作り、session、meta、user、note、Misskey error DTOだけを置く。Application、Domain、MisskeyApi、Persistenceを参照しない。
2. `GET /api/frontend/session`、browser-session endpoint metadata、scheme selection、Cookie API 401/403、conditional CSRFを実装する。
3. WASM `HttpClient` はroot-relative `/api/...`、same-origin credentials、`X-ActivityPub-Frontend: 1`、memory-only CSRF handlerを使う。
4. `IInstancePresentationService.GetAsync` を `POST /api/meta` へ移す。
5. client `AuthenticationStateProvider` と `ICurrentAccountPresentationService` をsession bootstrap + `POST /api/i` へ移す。
6. `ITimelinePresentationService.ReadAsync` のread-only部分を4既存timeline routeへ移す。初回は `limit=10..40` と外部 `untilId` を使う。
7. anonymousはmeta + local/global、authenticatedはmeta + `/api/i` + home/hybridを検証する。
8. このsliceではmutationとStreamingを同時に入れない。HTTP/auth境界を固定した次sliceでWebSocketを追加する。

initial timeline responseは既にMisskey note projectionなので、clientで `IExternalEntityIdService` や内部 `Guid` を使って再mapしない。

表示用 `NoteViewModel` は外部string IDを正本にする。

### focused acceptance tests

- anonymous bootstrapがHttpOnly/Secure/SameSite antiforgery Cookie、memory用request token、`authenticated=false`、`no-store`を返す。
- valid session bootstrapがviewerを返し、Cookieやraw tokenやsecurity stampを返さない。
- `/api/i` とhome timelineがbrowser marker + session Cookieで成功する。
- 同じrouteのnative `i` body/Bearer token contractが変わらない。
- Cookie mutationはCSRF欠落・不一致を403で拒否し、valid tokenを受理する。
- explicit token mutationはCSRFなしで従来どおり動く。
- signin responseのbrowser branchに `i`、`token`、authorization codeが存在せず、HttpOnly sessionだけが確立する。
- signin後の古いanonymous CSRF tokenを使わずbootstrapを更新する。
- suspend/security-stamp変更後のsessionが401になり、WASM auth stateがanonymousへ戻る。
- admin Cookie principalはroleでfirst-party admin policyを通るが、通常userは403になる。
- `/app/` deep linkとrefresh後にもbootstrap、meta、viewer、timelineが成功する。
- Service Workerが `/api/`、`/auth/`、`/streaming`、private `/media/` をcacheしない。
- browser storageに `mk_`、Cookie、CSRF token、passkey assertionが存在しない。

## 次slice以降の順序

1. `/streaming` のCookie認証、cursor付きprotocol、bounded buffer、dedupe、account switch再接続。
2. compose/reply/renote/reaction/poll/deleteとCSRF/idempotency。
3. notification REST + stream body map。
4. signup/passkey/email確認のWASM component化。
5. MiAuth approvalとsettings tokenのresponse分離。
6. remaining Presentation serviceを上表のendpointへ順次port。

全serviceを一度にHTTP化せず、各sliceでserver-only assemblyがWASM publish outputへ混入していないことを検査する。
