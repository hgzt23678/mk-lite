import { expect, test, type Request } from '@playwright/test';

const canvasAlpha = async (locator: ReturnType<import('@playwright/test').Page['locator']>) =>
  locator.evaluate(element => {
    const context = document.createElement('canvas').getContext('2d');
    if (!context) throw new Error('Canvas 2D context unavailable');
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });

const multipartField = (request: Request, name: string) => {
  const contentType = request.headers()['content-type'] ?? '';
  const boundaryMatch = /boundary=(?:"([^"]+)"|([^;]+))/i.exec(contentType);
  const boundary = boundaryMatch?.[1] ?? boundaryMatch?.[2];
  if (!boundary) throw new Error('Multipart boundary was not present');
  const part = (request.postData() ?? '')
    .split(`--${boundary}`)
    .find(candidate => candidate.includes(`name="${name}"`));
  if (!part) throw new Error(`Multipart field ${name} was not present`);
  const separator = part.includes('\r\n\r\n') ? '\r\n\r\n' : '\n\n';
  const valueStart = part.indexOf(separator);
  if (valueStart < 0) throw new Error(`Multipart field ${name} had no body`);
  return part.slice(valueStart + separator.length).replace(/\r?\n$/, '');
};

async function agreeToTerms(form: ReturnType<import('@playwright/test').Page['locator']>) {
  const switchRoot = form.locator('.ziffeomt.tou');
  if (await switchRoot.count() > 0 && !(await switchRoot.evaluate(element => element.classList.contains('checked')))) {
    await switchRoot.locator(':scope > .button').click();
    await expect(switchRoot).toHaveClass(/\bchecked\b/);
  }
}

test.beforeEach(async ({ page }) => {
  await page.request.post('/__test/registration-protection/none');
  await page.goto('/');
  await expect(page.locator('.rsqzvsbo > .top > .main')).toBeVisible();
});

test('signin reproduces the pinned MkSignin DOM, focus, background, 2FA state, and modal motion', async ({ page }) => {
  const userHintRequests: string[] = [];
  page.on('request', request => {
    if (request.url().includes('/auth/user-hint')) userHintRequests.push(request.url());
  });
  await page.locator('[data-cy-signin]').click();

  const modal = page.locator('body > .qzhlnise.dialog');
  const window = modal.locator(':scope > .content > .ebkgoccj._narrow_');
  const form = window.locator('.body > form.eppvobhk._monolithic_');
  await expect(modal).toHaveCount(1);
  await expect(modal).toHaveAttribute('role', 'dialog');
  await expect(window.locator(':scope > .header > .title')).toHaveText('ログイン');
  await expect(form.locator(':scope > .auth._section._formRoot > .normal-signin')).toHaveCount(1);
  await expect(form.locator(':scope > .social._section')).toHaveCount(1);
  await expect(form.locator('input[name="username"]')).toBeFocused();
  // MkModalWindow's rounded wrapper is intentionally transparent upstream. The visible body
  // is the panel surface that must remain opaque; asserting the wrapper would reject the oracle.
  expect(await canvasAlpha(window.locator(':scope > .body'))).toBe(255);
  await expect.poll(async () => modal.getAttribute('data-motion-state')).toBe('entered');

  await form.locator('input[name="username"]').fill('alice');
  await expect(form.locator('.avatar')).not.toHaveAttribute('style', /background-image/);
  expect(userHintRequests).toEqual([]);
  await form.locator('input[name="password"]').fill('not-a-real-secret');
  await form.locator('[data-auth-submit]').click();
  await expect(form.locator('.normal-signin')).toHaveCount(0);
  await expect(form.locator('[class~="2fa-signin"] > .totp-group input[name="token"]')).toHaveCount(1);

  await modal.locator(':scope > .content > .ebkgoccj > .header > button[aria-label="閉じる"]').click();
  await expect(modal).toHaveCount(0);
});

test('signup reproduces MkSignup hierarchy and live Vue-equivalent validation states', async ({ page }) => {
  let registrationRequests = 0;
  const emailAvailabilityRequests: string[] = [];
  page.on('request', request => {
    if (request.url().includes('/auth/email-address-available')) emailAvailabilityRequests.push(request.url());
  });
  await page.route('**/auth/register', async route => {
    registrationRequests += 1;
    const request = route.request();
    expect(request.method()).toBe('POST');
    expect(multipartField(request, 'username')).toBe('available_user');
    expect(multipartField(request, 'email')).toBe('available@example.test');
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'signup-email-pending' }),
    });
  });
  await page.locator('[data-cy-signup]').click();

  const modal = page.locator('body > .qzhlnise.dialog');
  const window = modal.locator(':scope > .content > .ebkgoccj._narrow_');
  const form = window.locator('.body > ._monolithic_ > ._section > form.qlvuhzng._formRoot');
  await expect(window.locator(':scope > .header > .title')).toHaveText('新規登録');
  await expect(form).toHaveCount(1);
  await expect(form.locator('[data-cy-signup-username]')).toHaveCount(1);
  await expect(form.locator('[data-cy-signup-password]')).toHaveCount(1);
  await expect(form.locator('[data-cy-signup-password-retype]')).toHaveCount(1);
  expect(await canvasAlpha(window.locator(':scope > .body'))).toBe(255);

  const username = form.locator('input[name="username"]');
  await username.fill('not-valid!');
  const usernameCaption = form.locator('[data-cy-signup-username] > .caption');
  await expect(usernameCaption).toContainText('a~z、A~Z、0~9、_が使えます');
  await username.fill('available_user');
  await expect(usernameCaption).toContainText('利用できます');

  const email = form.locator('input[name="email"]');
  const emailCaption = form.locator('[data-cy-signup-email] > .caption');
  await email.fill('invalid-email');
  await expect(emailCaption).toContainText('形式が正しくありません');
  await email.fill('used@example.test');
  await expect(emailCaption).toHaveText('');
  await email.fill('available@example.test');
  await expect(emailCaption).toHaveText('');
  expect(emailAvailabilityRequests).toEqual([]);

  const password = form.locator('input[name="password"]');
  const retyped = form.locator('input[name="retypedPassword"]');
  await password.fill('weak');
  const passwordCaption = form.locator('[data-cy-signup-password] > .caption');
  const retypedCaption = form.locator('[data-cy-signup-password-retype] > .caption');
  await expect(passwordCaption).toContainText('弱いパスワード');
  await retyped.fill('different');
  await expect(retypedCaption).toContainText('一致していません');
  await expect(form.locator('[data-cy-signup-submit]')).toBeDisabled();
  await retyped.fill('weak');
  await expect(retypedCaption).toContainText('一致しました');
  await expect(form.locator('[data-cy-signup-submit]')).toBeDisabled();
  await agreeToTerms(form);
  await expect(form.locator('[data-cy-signup-submit]')).toBeEnabled();

  await form.locator('[data-cy-signup-submit]').click();
  const alert = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(alert.locator('.mk-dialog')).toBeVisible();
  await expect.poll(async () => alert.getAttribute('data-motion-state')).toBe('entered');
  await expect(alert.locator('.mk-dialog > header')).toHaveText('ほとんど完了です');
  await expect(alert.locator('.mk-dialog > .body')).toContainText('available@example.test');
  await expect(alert.locator('.mk-dialog > .buttons button .content')).toHaveText('わかった');
  await expect(alert.locator('.mk-dialog > .buttons button')).toBeFocused();
  expect(await canvasAlpha(alert.locator('.mk-dialog'))).toBe(255);
  await expect.poll(async () => alert.evaluate(element => (element as HTMLElement).style.zIndex)).toBe('3000100');
  await expect(page.locator('body > .qzhlnise.dialog[role="dialog"]')).toHaveCount(0);
  expect(registrationRequests).toBe(1);

  await alert.locator('.mk-dialog > .buttons button').click();
  await expect(alert).toHaveCount(0);
});

test('invite-only hCaptcha signup preserves MkCaptcha DOM, callback, payload, and reset boundary', async ({ page }) => {
  await page.request.post('/__test/registration-protection/hcaptcha');
  await page.route('https://js.hcaptcha.com/**', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/javascript',
      body: `globalThis.hcaptcha = {
        render: (container, options) => {
          const marker = document.createElement('div');
          marker.dataset.testHcaptcha = 'rendered';
          container.appendChild(marker);
          setTimeout(() => options.callback('browser-captcha-token'), 0);
          return 'widget-1';
        },
        reset: () => {},
        remove: () => {}
      };`,
    });
  });
  let registrationRequests = 0;
  await page.route('**/auth/register', async route => {
    registrationRequests += 1;
    const request = route.request();
    expect(multipartField(request, 'invitationCode')).toBe('23456789ABCDEFGHJKLMNPQRST');
    expect(multipartField(request, 'hcaptcha-response')).toBe('browser-captcha-token');
    expect(request.postData()).not.toContain('provider-secret');
    // Keep the synthetic response behind the originating pointer sequence. A zero-latency
    // fulfillment can open the upstream error alert before Playwright finishes mouseup/click,
    // causing an artificial retry against the now-covered submit button.
    await new Promise(resolve => setTimeout(resolve, 500));
    await route.fulfill({
      status: 400,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'failed', errorCode: 'INVALID_INVITATION_CODE' }),
    });
  });

  await page.locator('[data-cy-signup]').click();
  const form = page.locator('body > .qzhlnise.dialog form.qlvuhzng');
  const invitation = form.locator('input[name="invitationCode"]');
  await expect(invitation).toHaveCount(1);
  await expect(invitation).toHaveAttribute('maxlength', '26');
  const orderedNames = await form.locator('input').evaluateAll(inputs =>
    inputs.map(input => input.getAttribute('name')));
  expect(orderedNames.indexOf('invitationCode')).toBeLessThan(orderedNames.indexOf('username'));
  await expect(form.locator('[data-test-hcaptcha="rendered"]')).toHaveCount(1);
  await expect(form.locator('input[name="hcaptcha-response"]')).toHaveValue('browser-captcha-token');

  await invitation.fill('23456789ABCDEFGHJKLMNPQRST');
  await form.locator('input[name="username"]').fill('available_user');
  await form.locator('input[name="email"]').fill('available@example.test');
  await form.locator('input[name="password"]').fill('strong-enough-password');
  await form.locator('input[name="retypedPassword"]').fill('strong-enough-password');
  await agreeToTerms(form);
  await expect(form.locator('[data-cy-signup-submit]')).toBeEnabled();
  expect(registrationRequests).toBe(0);
  await expect(page.locator('body > .qzhlnise.dialog[role="alertdialog"]')).toHaveCount(0);
  await form.locator('[data-cy-signup-submit]').click();
  const failure = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(failure.locator('.mk-dialog > .body')).toHaveText('問題が発生しました');
  await expect(failure.locator('.mk-dialog > .buttons button')).toBeFocused();
  await expect(form.locator('input[name="hcaptcha-response"]')).toHaveValue('');
  await expect(form.locator('[data-cy-signup-submit]')).toBeDisabled();
  expect(registrationRequests).toBe(1);
  await failure.locator('.mk-dialog > .buttons button').click();
  await expect(failure).toHaveCount(0);
  await page.request.post('/__test/registration-protection/none');
});

test('invite-only Turnstile signup preserves upstream captcha position and Cloudflare bindings', async ({ page }) => {
  await page.request.post('/__test/registration-protection/turnstile');
  await page.route('https://challenges.cloudflare.com/turnstile/**', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/javascript',
      body: `globalThis.turnstile = {
        render: (container, options) => {
          const marker = document.createElement('div');
          marker.dataset.testTurnstile = 'rendered';
          marker.dataset.action = options.action;
          marker.dataset.cdata = options.cData;
          marker.dataset.responseField = String(options['response-field']);
          container.appendChild(marker);
          setTimeout(() => options.callback('browser-turnstile-token'), 0);
          return 'turnstile-widget-1';
        },
        reset: () => {},
        remove: () => {}
      };`,
    });
  });
  let registrationRequests = 0;
  await page.route('**/auth/register', async route => {
    registrationRequests += 1;
    const request = route.request();
    expect(multipartField(request, 'cf-turnstile-response')).toBe('browser-turnstile-token');
    expect(request.postData()).not.toContain('provider-secret');
    await new Promise(resolve => setTimeout(resolve, 500));
    await route.fulfill({
      status: 400,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'failed', errorCode: 'INVALID_CAPTCHA' }),
    });
  });

  await page.locator('[data-cy-signup]').click();
  const form = page.locator('body > .qzhlnise.dialog form.qlvuhzng');
  const widget = form.locator('[data-test-turnstile="rendered"]');
  await expect(widget).toHaveCount(1);
  await expect(widget).toHaveAttribute('data-action', 'signup');
  await expect(widget).toHaveAttribute('data-cdata', 'activitypub_signup');
  await expect(widget).toHaveAttribute('data-response-field', 'false');
  await expect(form.locator('input[name="cf-turnstile-response"]')).toHaveCount(1);
  await expect(form.locator('input[name="cf-turnstile-response"]')).toHaveValue('browser-turnstile-token');
  const orderedNames = await form.locator('input').evaluateAll(inputs =>
    inputs.map(input => input.getAttribute('name')));
  expect(orderedNames.indexOf('retypedPassword')).toBeLessThan(orderedNames.indexOf('cf-turnstile-response'));

  await form.locator('input[name="invitationCode"]').fill('23456789ABCDEFGHJKLMNPQRST');
  await form.locator('input[name="username"]').fill('available_user');
  await form.locator('input[name="email"]').fill('available@example.test');
  await form.locator('input[name="password"]').fill('strong-enough-password');
  await form.locator('input[name="retypedPassword"]').fill('strong-enough-password');
  await agreeToTerms(form);
  await expect(form.locator('[data-cy-signup-submit]')).toBeEnabled();
  await form.locator('[data-cy-signup-submit]').click();
  await expect(page.locator('body > .qzhlnise.dialog[role="alertdialog"] .mk-dialog > .body'))
    .toHaveText('問題が発生しました');
  await expect(form.locator('input[name="cf-turnstile-response"]')).toHaveValue('');
  await expect(form.locator('[data-cy-signup-submit]')).toBeDisabled();
  expect(registrationRequests).toBe(1);
  await page.request.post('/__test/registration-protection/none');
});

test('signup retries a transient Turnstile script failure after the dialog is reopened', async ({ page }) => {
  await page.request.post('/__test/reset-diagnostics');
  await page.request.post('/__test/registration-protection/turnstile');
  let scriptRequests = 0;
  await page.route('https://challenges.cloudflare.com/turnstile/**', async route => {
    scriptRequests += 1;
    if (scriptRequests === 1) {
      await route.abort('failed');
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/javascript',
      body: `globalThis.turnstile = {
        render: (container, options) => {
          const marker = document.createElement('div');
          marker.dataset.testTurnstileRetry = 'rendered';
          container.appendChild(marker);
          setTimeout(() => options.callback('retry-turnstile-token'), 0);
          return 'turnstile-widget-retry';
        },
        reset: () => {},
        remove: () => {}
      };`,
    });
  });

  await page.locator('[data-cy-signup]').click();
  let modal = page.locator('body > .qzhlnise.dialog');
  let form = modal.locator('form.qlvuhzng');
  await expect(form.locator('[data-test-turnstile-retry]')).toHaveCount(0);
  await expect(form.locator('[data-captcha-container]').locator('xpath=preceding-sibling::span[1]')).toContainText(/waiting/i);
  await modal.locator(':scope > .content > .ebkgoccj > .header > button[aria-label="閉じる"]').click();
  await expect(modal).toHaveCount(0);

  await page.locator('[data-cy-signup]').click();
  modal = page.locator('body > .qzhlnise.dialog');
  form = modal.locator('form.qlvuhzng');
  await expect(form.locator('[data-test-turnstile-retry="rendered"]')).toHaveCount(1);
  await expect(form.locator('input[name="cf-turnstile-response"]')).toHaveValue('retry-turnstile-token');
  expect(await canvasAlpha(modal.locator(':scope > .content > .ebkgoccj > .body'))).toBe(255);
  expect(scriptRequests).toBe(2);
  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);

  await page.request.post('/__test/registration-protection/none');
});

test('invite-only reCAPTCHA signup uses the official script, fails closed, submits, and resets', async ({ page }) => {
  await page.request.post('/__test/registration-protection/recaptcha');
  await page.route('https://www.google.com/recaptcha/**', async route => {
    await route.fulfill({
      status: 200,
      contentType: 'application/javascript',
      body: `globalThis.grecaptcha = {
        render: (container, options) => {
          const marker = document.createElement('button');
          marker.type = 'button';
          marker.dataset.testRecaptcha = 'rendered';
          marker.addEventListener('click', () => options.callback('browser-recaptcha-token'));
          container.appendChild(marker);
          return 'widget-2';
        },
        reset: () => {},
        remove: () => {}
      };`,
    });
  });
  let registrationRequests = 0;
  await page.route('**/auth/register', async route => {
    registrationRequests += 1;
    const request = route.request();
    expect(multipartField(request, 'invitationCode')).toBe('89ABCDEFGHJKLMNPQRSTUVWXYZ');
    expect(multipartField(request, 'g-recaptcha-response')).toBe('browser-recaptcha-token');
    await new Promise(resolve => setTimeout(resolve, 500));
    await route.fulfill({
      status: 400,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'failed', errorCode: 'INVALID_CAPTCHA' }),
    });
  });

  await page.locator('[data-cy-signup]').click();
  const form = page.locator('body > .qzhlnise.dialog form.qlvuhzng');
  const submit = form.locator('[data-cy-signup-submit]');
  await expect(form.locator('[data-test-recaptcha="rendered"]')).toHaveCount(1);
  await expect(form.locator('input[name="g-recaptcha-response"]')).toHaveValue('');
  await expect(submit).toBeDisabled();

  await form.locator('[data-test-recaptcha="rendered"]').click();
  await expect(form.locator('input[name="g-recaptcha-response"]')).toHaveValue('browser-recaptcha-token');
  await form.locator('input[name="invitationCode"]').fill('89ABCDEFGHJKLMNPQRSTUVWXYZ');
  await form.locator('input[name="username"]').fill('available_user');
  await form.locator('input[name="email"]').fill('available@example.test');
  await form.locator('input[name="password"]').fill('strong-enough-password');
  await form.locator('input[name="retypedPassword"]').fill('strong-enough-password');
  await agreeToTerms(form);
  await expect(submit).toBeEnabled();
  expect(registrationRequests).toBe(0);
  await expect(page.locator('body > .qzhlnise.dialog[role="alertdialog"]')).toHaveCount(0);
  await submit.click();
  const failure = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(failure.locator('.mk-dialog > .body')).toHaveText('問題が発生しました');
  await expect(failure.locator('.mk-dialog > .buttons button')).toBeFocused();
  await expect(form.locator('input[name="g-recaptcha-response"]')).toHaveValue('');
  await expect(submit).toBeDisabled();
  expect(registrationRequests).toBe(1);
  await failure.locator('.mk-dialog > .buttons button').click();
  await expect(failure).toHaveCount(0);
  await page.request.post('/__test/registration-protection/none');
});

test('authentication dialogs keep opaque surfaces and exact width constraints at mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 800 });
  await page.locator('[data-cy-signin]').click();
  const window = page.locator('body > .qzhlnise.dialog > .content > .ebkgoccj._narrow_');
  await expect(window).toBeVisible();
  const box = await window.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.width).toBeLessThanOrEqual(360);
  expect(await canvasAlpha(window.locator(':scope > .body'))).toBe(255);
});

test('security-key login follows the pinned challenge, assertion, close-motion, and session reload path', async ({ page }) => {
  let optionRequests = 0;
  let assertionRequests = 0;
  await page.route('**/api/signin', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      status: 'passkey-required',
      publicKey: {
        challenge: 'AQIDBA',
        timeout: 60_000,
        rpId: 'localhost',
        allowCredentials: [{ id: 'AQIDBA', type: 'public-key', transports: ['internal'] }],
        userVerification: 'required',
      },
    }),
  }));
  await page.route('**/auth/passkey/options', async route => {
    optionRequests += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        challenge: 'AQIDBA',
        timeout: 60_000,
        rpId: 'localhost',
        allowCredentials: [{ id: 'AQIDBA', type: 'public-key', transports: ['internal'] }],
        userVerification: 'required',
      }),
    });
  });
  await page.route('**/auth/passkey/assertion', async route => {
    assertionRequests += 1;
    const credential = JSON.parse(multipartField(route.request(), 'credential'));
    expect(credential.id).toBe('fixture-passkey');
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'succeeded', redirectUrl: '/?passkey-auth=verified' }),
    });
  });
  await page.evaluate(() => {
    class TestPublicKeyCredential {
      static parseRequestOptionsFromJSON(value: unknown) { return value; }
    }
    Object.defineProperty(globalThis, 'PublicKeyCredential', {
      configurable: true,
      value: TestPublicKeyCredential,
    });
    Object.defineProperty(navigator, 'credentials', {
      configurable: true,
      value: {
        get: async () => ({
          toJSON: () => ({
            id: 'fixture-passkey',
            rawId: 'AQIDBA',
            type: 'public-key',
            authenticatorAttachment: 'platform',
            clientExtensionResults: {},
            response: {
              authenticatorData: 'AQIDBA',
              clientDataJSON: 'AQIDBA',
              signature: 'AQIDBA',
              userHandle: null,
            },
          }),
        }),
      },
    });
  });

  await page.locator('[data-cy-signin]').click();
  const form = page.locator('form.eppvobhk');
  await form.locator('input[name=username]').fill('security_key');
  await form.locator('input[name=password]').fill('passkey-test-password');
  await form.locator('[data-auth-submit]').click();

  await expect(form.locator('[class~="2fa-signin"][class~="securityKeys"]')).toHaveCount(1);
  await expect(page).toHaveURL(/passkey-auth=verified/);
  expect(optionRequests).toBe(0);
  expect(assertionRequests).toBe(1);
});

test('cancelled security-key prompt keeps the TOTP fallback and retry behavior without a false error', async ({ page }) => {
  let credentialQueries = 0;
  await page.route('**/api/signin', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      status: 'passkey-required',
      publicKey: {
        challenge: 'AQIDBA',
        timeout: 60_000,
        rpId: 'localhost',
        allowCredentials: [{ id: 'AQIDBA', type: 'public-key' }],
        userVerification: 'required',
      },
    }),
  }));
  await page.route('**/auth/passkey/options', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      challenge: 'AQIDBA',
      timeout: 60_000,
      rpId: 'localhost',
      allowCredentials: [{ id: 'AQIDBA', type: 'public-key' }],
      userVerification: 'required',
    }),
  }));
  await page.evaluate(() => {
    class TestPublicKeyCredential {
      static parseRequestOptionsFromJSON(value: unknown) { return value; }
    }
    Object.defineProperty(globalThis, 'PublicKeyCredential', {
      configurable: true,
      value: TestPublicKeyCredential,
    });
    Object.defineProperty(navigator, 'credentials', {
      configurable: true,
      value: {
        get: async () => {
          (globalThis as typeof globalThis & { __passkeyQueries?: number }).__passkeyQueries =
            ((globalThis as typeof globalThis & { __passkeyQueries?: number }).__passkeyQueries ?? 0) + 1;
          throw new DOMException('cancelled by user', 'NotAllowedError');
        },
      },
    });
  });

  await page.locator('[data-cy-signin]').click();
  const form = page.locator('form.eppvobhk');
  await form.locator('input[name=username]').fill('security_key');
  await form.locator('input[name=password]').fill('passkey-test-password');
  await form.locator('[data-auth-submit]').click();

  await expect(form.locator('.tap-group button')).toBeVisible();
  await expect(form.locator('.totp-group input[name=token]')).toBeVisible();
  await expect(form.locator('.auth > .mk-info.warning')).toHaveCount(0);
  await form.locator('.tap-group button').click();
  await expect(form.locator('.tap-group button')).toBeVisible();
  await expect.poll(async () => page.evaluate(() =>
    (globalThis as typeof globalThis & { __passkeyQueries?: number }).__passkeyQueries ?? 0)).toBe(2);
});
