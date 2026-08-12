import { expect, test, type Page } from '@playwright/test';

const attachFailureGuards = (page: Page) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.message}`));
  return failures;
};

const openSignIn = async (page: Page) => {
  await page.goto('/');
  const source = page.locator('[data-cy-signin]');
  await source.click();
  const modal = page.locator('body > .qzhlnise.dialog[role="dialog"]');
  await expect(modal).toHaveCount(1);
  await expect.poll(async () => modal.getAttribute('data-motion-state')).toBe('entered');
  return { source, modal, form: modal.locator('form.eppvobhk._monolithic_') };
};

test.beforeEach(async ({ page }) => {
  await page.request.post('/__test/reset-diagnostics');
  await page.request.post('/__test/registration-protection/none');
});

test('MkSignin preserves the pinned visual contract, native fields, password controls, and close focus', async ({ page }) => {
  const failures = attachFailureGuards(page);
  let releaseCredentialResponse!: () => void;
  const credentialResponseGate = new Promise<void>(resolve => {
    releaseCredentialResponse = resolve;
  });
  await page.route('**/api/signin', async route => {
    await credentialResponseGate;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'failed', errorCode: 'RATE_LIMIT_EXCEEDED' }),
    });
  });
  const { source, modal, form } = await openSignIn(page);
  const window = modal.locator(':scope > .content > .ebkgoccj._narrow_');
  const body = window.locator(':scope > .body');
  const avatar = form.locator(':scope > .auth._section._formRoot > .avatar');
  const username = form.locator('input[name="username"]');
  const password = form.locator('input[name="password"]');
  const toggle = form.locator('[data-password-toggle]');
  const capsWarning = form.locator('[data-caps-lock-warning]');

  await expect(window.locator(':scope > .header > .title')).toHaveText('ログイン');
  await expect(username).toBeFocused();
  await expect(username).toHaveAttribute('pattern', '^[a-zA-Z0-9_]+$');
  await expect(username).not.toHaveAttribute('maxlength', /.+/);
  await expect(password).not.toHaveAttribute('autocomplete', /.+/);
  await expect(password).not.toHaveAttribute('maxlength', /.+/);
  await expect(form.locator(':scope > .social._section')).toHaveCount(1);

  const avatarVisual = await avatar.evaluate(element => ({
    width: element.getBoundingClientRect().width,
    height: element.getBoundingClientRect().height,
    radius: getComputedStyle(element).borderRadius,
    background: getComputedStyle(element).backgroundColor,
  }));
  const bodyBackground = await body.evaluate(element => getComputedStyle(element).backgroundColor);
  const windowBox = await window.boundingBox();
  const normalMotionMs = await modal.locator(':scope > .content').evaluate(element => {
    const style = getComputedStyle(element);
    const durations = style.transitionDuration.split(',');
    return durations.reduce((maximum, duration) => {
      const part = duration.trim();
      const parsed = Number.parseFloat(part);
      return Math.max(maximum, part.endsWith('ms') ? parsed : parsed * 1000);
    }, 0);
  });
  const visual = {
    avatarWidth: avatarVisual.width,
    avatarHeight: avatarVisual.height,
    avatarRadius: avatarVisual.radius,
    avatarBackground: avatarVisual.background,
    bodyBackground,
    windowWidth: windowBox?.width,
    windowHeight: windowBox?.height,
    normalMotionMs,
  };
  expect(visual).toEqual({
    avatarWidth: 64,
    avatarHeight: 64,
    avatarRadius: '100%',
    avatarBackground: 'rgb(221, 221, 221)',
    bodyBackground: 'rgb(255, 255, 255)',
    windowWidth: 370,
    windowHeight: 400,
    normalMotionMs: 200,
  });

  await password.fill('browser-only-secret');
  await toggle.click();
  await expect(password).toHaveAttribute('type', 'text');
  await expect(toggle).toHaveAttribute('aria-pressed', 'true');
  await expect(password).toHaveValue('browser-only-secret');
  await expect(password).toBeFocused();
  await toggle.click();
  await expect(password).toHaveAttribute('type', 'password');
  await expect(toggle).toHaveAttribute('aria-pressed', 'false');

  await password.evaluate(input => {
    const event = new KeyboardEvent('keydown', { bubbles: true, code: 'KeyA', key: 'A' });
    Object.defineProperty(event, 'getModifierState', {
      value: (key: string) => key === 'CapsLock',
    });
    input.dispatchEvent(event);
  });
  await expect(capsWarning).toBeVisible();
  await expect(password).toHaveAttribute('data-caps-lock', 'on');
  await username.focus();
  await expect(capsWarning).toBeHidden();

  await username.fill('alice');
  await password.fill('not-a-production-secret');
  await password.press('Enter');
  const submit = form.locator('[data-auth-submit]');
  await expect(form).toHaveClass(/\bsigning\b/);
  await expect(form).toHaveAttribute('aria-busy', 'true');
  await expect(submit).toBeDisabled();
  await expect(submit.locator('.content')).toHaveText('ログイン中');
  releaseCredentialResponse();

  const alert = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(alert.locator('.mk-dialog > header')).toHaveText('ログインに失敗しました');
  await expect(alert.locator('.mk-dialog > .body')).toContainText('レート制限を超えました');
  await expect(form).not.toHaveClass(/\bsigning\b/);
  await expect(form).toHaveAttribute('aria-busy', 'false');
  await expect(submit).toBeEnabled();
  await alert.locator('.mk-dialog > .buttons button').click();
  await expect(alert).toHaveCount(0);

  await modal.locator(':scope > .content > .ebkgoccj > .header > button[aria-label="閉じる"]').click();
  await expect(modal).toHaveCount(0);
  await expect(source).toBeFocused();
  expect(failures).toEqual([]);
  await expect.poll(async () => (await page.request.get('/__test/diagnostics')).json())
    .toEqual({ unhandledExceptions: [] });
});

test('MkSignin resets failed 2FA through the upstream alert while retaining the password only in browser state', async ({ page }) => {
  const failures = attachFailureGuards(page);
  let credentialRequests = 0;
  await page.route('**/api/signin', async route => {
    credentialRequests += 1;
    if (credentialRequests === 1) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ status: 'two-factor-required', errorCode: 'TWO_FACTOR_REQUIRED' }),
      });
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'failed', errorCode: 'INVALID_TWO_FACTOR_CODE' }),
    });
  });

  const { modal, form } = await openSignIn(page);
  await form.locator('input[name="username"]').fill('alice');
  await form.locator('input[name="password"]').fill('retained-browser-secret');
  await form.locator('[data-auth-submit]').click();
  const token = form.locator('.totp-group input[name="token"]');
  await expect(token).toBeVisible();
  await expect(token).toHaveAttribute('autocomplete', 'off');
  await expect(token).not.toHaveAttribute('maxlength', /.+/);
  await token.fill('123456');
  await form.locator('[data-auth-submit]').click();

  const alert = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(alert.locator('.mk-dialog')).toBeVisible();
  await expect(alert.locator('.mk-dialog > header')).toHaveText('ログインに失敗しました');
  await expect(form.locator('.normal-signin')).toHaveCount(1);
  await expect(form.locator('input[name="password"]')).toHaveValue('retained-browser-secret');
  expect(await form.locator('input[name="password"]').evaluate(input => input.outerHTML))
    .not.toContain('retained-browser-secret');
  expect(await page.locator('body').textContent()).not.toContain('retained-browser-secret');
  expect(credentialRequests).toBe(2);

  await alert.locator('.mk-dialog > .buttons button').click();
  await expect(alert).toHaveCount(0);
  await expect(modal).toHaveCount(1);
  expect(failures).toEqual([]);
});

test('MkSignin maps native Misskey error ids to the local safe presentation state', async ({ page }) => {
  const failures = attachFailureGuards(page);
  await page.route('**/api/signin', async route => {
    await route.fulfill({
      // Keep the browser smoke console clean; the API contract's 403 status is
      // covered by the API fixture while this test exercises the native error
      // body-to-presentation mapping.
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        error: { id: 'e03a5f46-d309-4865-9b69-56282d94e1eb' },
      }),
    });
  });

  const { modal, form } = await openSignIn(page);
  await form.locator('input[name="username"]').fill('alice');
  await form.locator('input[name="password"]').fill('browser-only-secret');
  await form.locator('[data-auth-submit]').click();

  const alert = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(alert.locator('.mk-dialog > header')).toHaveText('アカウントが凍結されています');
  await alert.locator('.mk-dialog > .buttons button').click();
  await expect(alert).toHaveCount(0);
  await expect(modal).toHaveCount(1);
  expect(failures).toEqual([]);
});

test('MkSignin reduced-motion Escape closes only the top dialog and restores the launch focus', async ({ page }) => {
  const failures = attachFailureGuards(page);
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const { source, modal } = await openSignIn(page);
  const motion = await modal.evaluate(element => {
    const milliseconds = (value: string) => value.split(',').reduce((maximum, item) => {
      const part = item.trim();
      const parsed = Number.parseFloat(part);
      if (!Number.isFinite(parsed)) return maximum;
      return Math.max(maximum, part.endsWith('ms') ? parsed : parsed * 1000);
    }, 0);
    const content = element.querySelector(':scope > .content');
    const background = element.querySelector(':scope > .bg');
    if (!(content instanceof HTMLElement) || !(background instanceof HTMLElement)) return null;
    return {
      contentTransitionMs: milliseconds(getComputedStyle(content).transitionDuration),
      backgroundTransitionMs: milliseconds(getComputedStyle(background).transitionDuration),
    };
  });
  expect(motion).not.toBeNull();
  expect(motion!.contentTransitionMs).toBeLessThanOrEqual(0.01);
  expect(motion!.backgroundTransitionMs).toBeLessThanOrEqual(0.01);

  await page.keyboard.press('Escape');
  await expect(modal).toHaveCount(0);
  await expect(source).toBeFocused();
  expect(failures).toEqual([]);
});
