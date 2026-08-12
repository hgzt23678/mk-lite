# Keycloak-hosted MkSignin theme (historical, not the active local-auth path)

> この文書は過去のTailnet OIDC構成を追跡するために保持している。現在の`deploy/pasture` local account構成ではKeycloakへcredentialを委譲せず、Blazorの`MkSignin`が`POST /api/signin`へ同一originで送信する。新規実装・試験・互換性宣言の根拠にこの文書を使用しない。

旧Tailnet構成ではOIDC credential entryをKeycloak `26.7.0`が所有していた。現在のlocal account構成ではこの経路を使用しない。
現在のBlazor `MkSignin`は`/api/signin`へローカルpassword/TOTPを送信し、WebAuthnは既存の`/auth/passkey/*`境界を使う。IdP credentialをこのAPIへ渡さない。
`deploy/pasture/keycloak/themes/activitypub-misskey-v12` は認証処理を置き換えず、Misskey `12.119.2` commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2` の `MkSignin.vue`、`MkSigninDialog.vue`、生成済み theme token／CSS／Font Awesome asset を Keycloak FreeMarker DOM へ移した presentation layer である。

旧Tailnetでは`/app/auth/login`からOIDC providerを表示した。これは履歴であり、現行`LocalAccounts:Enabled=true`構成では`MkSignin`のusername/password formと`/api/signin`を表示する。

`login.ftl`、`login-username.ftl`、`login-password.ftl` は username/password を `${url.loginAction}` へ POST する。
`login-otp.ftl` は `otp` を同じ Keycloak authentication session へ POST する。
`webauthn-authenticate.ftl` は Keycloak `26.7.0` の同一 origin module と hidden assertion field を維持する。
realm import は `loginTheme=activitypub-misskey-v12` を指定し、Compose は固定 Keycloak base に theme と既存の生成済み Misskey CSS／asset だけを COPY した image を build する。

## 意図的に残る差

| Misskey `MkSignin` | Keycloak-hosted page |
| --- | --- |
| Vue modal の背後に実 app が見える | auth server document なので同じ modal shell を不透明な theme background 上へ表示する |
| app の `users/show` で avatar と 2FA capability を事前取得 | username enumeration を増やさず neutral avatar のままにする |
| password、TOTP、security key を一つの Vue component state で切替 | Keycloak authentication flow の独立した password／OTP／WebAuthn step を同じ class contract で描画する |
| modal close／Escape が app overlay を閉じる | OAuth authorization request に安全な任意 cancel URI がないため close control を捏造しない。Keycloak の restart／try-another-way link だけを使う |
| security key と TOTP を同じ DOM に同時表示できる | Keycloak が policy に従って authenticator selection と各 challenge を分離する |
| user password を Vue state が API request へ渡す | password は browser から Keycloak `login-actions` へ直接送られ、Blazor/.NET API は受け取らない |

この差を埋めるために direct grant、auth bypass、password relay、独自 session cookie を追加してはならない。
reset password、registration、social provider は realm/backend で有効な場合だけ表示し、未実装機能の見せかけの control は描画しない。

## 検証

```bash
node eng/check-keycloak-login-theme.mjs
bash eng/pasture.sh config
bash eng/pasture-oidc.sh verify
docker build --build-arg KEYCLOAK_VERSION=26.7.0 \
  --tag activitypub-pasture-keycloak-misskey-v12:26.7.0 \
  --file deploy/pasture/keycloak/Dockerfile .
PLAYWRIGHT_BASE_URL="$AP_TAILSCALE_ORIGIN" \
  npm --prefix tests/frontend-blazor-e2e exec -- \
    playwright test tailnet-signin-contract.spec.ts --project=chromium --reporter=line
bash eng/pasture-tailscale.sh test
```

browser test はBlazor dialogの上流DOM、mobile幅、panel背景alpha 255、初期focus、`/api/signin` action、credential field、Keycloak/Vue/Vite script不在を確認する。
上流`--windowHeader`の0.85 acrylicは意図した表現であり、透明化回帰の判定は表示面であるmodal bodyのalphaに対して行う。
browser test は password、token、cookie を入力・出力・trace・screenshot artifact に保存しない。
`eng/pasture-oidc.sh verify` は実 test account で authorization-code redirect まで確認し、code exchange は行わず、cookie／HTML／header を終了時に削除する。
token を明示的に必要とする運用だけが `issue` を使い、repository 外の mode `0700` session directory に mode `0600` で保存する。
