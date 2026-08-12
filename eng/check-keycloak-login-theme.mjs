#!/usr/bin/env node
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = resolve(fileURLToPath(new URL('..', import.meta.url)));
const themeRoot = resolve(
  repositoryRoot,
  'deploy/pasture/keycloak/themes/activitypub-misskey-v12/login'
);

const read = relativePath => readFile(resolve(repositoryRoot, relativePath), 'utf8');
const readTheme = relativePath => readFile(resolve(themeRoot, relativePath), 'utf8');

const [realm, compose, dockerfile, properties, template, login, username, password, otp,
  webauthn, passkeyConditional, webauthnError, authenticatorSelection, adapterCss, adapterScript] =
  await Promise.all([
    read('deploy/pasture/keycloak-realm.template.json'),
    read('deploy/pasture/docker-compose.pasture.yml'),
    read('deploy/pasture/keycloak/Dockerfile'),
    readTheme('theme.properties'),
    readTheme('template.ftl'),
    readTheme('login.ftl'),
    readTheme('login-username.ftl'),
    readTheme('login-password.ftl'),
    readTheme('login-otp.ftl'),
    readTheme('webauthn-authenticate.ftl'),
    readTheme('login-passkeys-conditional-authenticate.ftl'),
    readTheme('webauthn-error.ftl'),
    readTheme('select-authenticator.ftl'),
    readTheme('resources/css/keycloak-misskey-v12.css'),
    readTheme('resources/js/misskey-signin.js')
  ]);

const realmConfiguration = JSON.parse(realm);
assert.equal(realmConfiguration.loginTheme, 'activitypub-misskey-v12');
assert.equal(realmConfiguration.defaultLocale, 'ja');
assert.match(compose, /dockerfile: deploy\/pasture\/keycloak\/Dockerfile/);
assert.match(dockerfile, /FROM quay\.io\/keycloak\/keycloak:\$\{KEYCLOAK_VERSION\}/);
assert.match(dockerfile, /misskey-v12-upstream\.css/);
assert.match(properties, /Misskey 12\.119\.2/);
assert.match(properties, /parent=base/);
assert.match(template, /data-misskey-version="12\.119\.2"/);

for (const [name, source] of Object.entries({
  login,
  username,
  password,
  otp,
  webauthn,
  passkeyConditional,
  webauthnError,
  authenticatorSelection
})) {
  assert.match(source, /\$\{url\.loginAction\}/, `${name} must submit to Keycloak`);
  assert.doesNotMatch(source, /\/auth\/credentials/, `${name} must not submit to Blazor`);
  assert.doesNotMatch(source, /console\.(?:log|debug|info|warn|error)/, `${name} must not log auth data`);
}

assert.match(login, /class="eppvobhk _monolithic_"/);
assert.match(login, /name="username"/);
assert.match(login, /name="password"/);
assert.match(otp, /class="eppvobhk _monolithic_ totpLogin"/);
assert.match(otp, /name="otp"/);
assert.match(webauthn, /webauthnAuthenticate\.js/);
assert.match(passkeyConditional, /passkeysConditionalAuth\.js/);
assert.match(authenticatorSelection, /name="authenticationExecution"/);
assert.match(adapterCss, /@media \(prefers-reduced-motion: reduce\)/);
assert.match(adapterCss, /background-color: var\(--panel/);
assert.match(adapterCss, /@keyframes mk-keycloak-dialog-in/);
assert.doesNotMatch(adapterScript, /console\.(?:log|debug|info|warn|error)/);
assert.doesNotMatch(adapterScript, /\.value\b/);
for (const field of ['clientDataJSON', 'authenticatorData', 'signature', 'credentialId', 'error']) {
  assert.match(webauthn, new RegExp(`name="${field}"`));
}

process.stdout.write('Keycloak-hosted MkSignin theme contract: ok\n');
