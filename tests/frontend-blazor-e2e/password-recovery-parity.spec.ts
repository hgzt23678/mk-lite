import { expect, test, type Page } from '@playwright/test';

const resetToken = 'fixture_reset_token_for_browser_contract_0000000000000000';
const confirmationToken = 'fixture_confirmation_token_for_browser_contract_0000000000';

const alpha = async (locator: ReturnType<Page['locator']>) => locator.evaluate(element => {
  const context = document.createElement('canvas').getContext('2d');
  if (!context) throw new Error('Canvas 2D context unavailable');
  context.fillStyle = getComputedStyle(element).backgroundColor;
  context.fillRect(0, 0, 1, 1);
  return context.getImageData(0, 0, 1, 1).data[3];
});

const startDiagnostics = (page: Page, acceptedFailurePaths: string[] = []) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() !== 'error') return;
    const text = message.text();
    // Chromium and WebKit report an expected, handled fetch 4xx as a console resource error.
    // The response listener still classifies every URL/status, so this does not hide an
    // unclassified failed request.
    if (acceptedFailurePaths.length > 0 && text.startsWith('Failed to load resource: the server responded with a status of ')) return;
    failures.push(`console:${text}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}:${error.message}`));
  page.on('response', response => {
    if (response.status() < 400) return;
    const path = new URL(response.url()).pathname;
    if (!acceptedFailurePaths.includes(path)) failures.push(`http:${response.status()}:${path}`);
  });
  return failures;
};

const assertCircuitHealthy = async (page: Page, failures: string[]) => {
  expect(failures).toEqual([]);
  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
};

const openForgotPassword = async (page: Page) => {
  const dialogs = page.locator('body > .qzhlnise.dialog');
  if (await dialogs.count() === 0) await page.locator('[data-cy-signin]').click();
  await dialogs.first().locator('button._textButton').click();
  await expect(dialogs).toHaveCount(2);
  return page.locator('body > .qzhlnise.dialog[aria-label="パスワードを忘れた"]');
};

const waitForHomeCircuit = async (page: Page) => {
  await page.evaluate(() => document.fonts.ready);
  await page.locator('[data-cy-signin]').click();
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(1);
  await page.keyboard.press('Escape');
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(0);
};

test.beforeEach(async ({ page }) => {
  await page.request.post('/__test/reset-diagnostics');
});

test('forgot-password reproduces the pinned form, localization, responsive surface, side-effect states, and motion', async ({ page }) => {
  const failures = startDiagnostics(page, ['/auth/password-reset/request']);
  let attempts = 0;
  let submitted = '';
  await page.route('**/auth/password-reset/request', async route => {
    attempts += 1;
    submitted = route.request().postData() ?? '';
    await route.fulfill({
      status: attempts === 1 ? 429 : 202,
      contentType: 'application/json',
      body: JSON.stringify(attempts === 1
        ? { status: 'failed', errorCode: 'RATE_LIMIT_EXCEEDED' }
        : { status: 'accepted' }),
    });
  });

  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto('/');
  const forgot = await openForgotPassword(page);
  const window = forgot.locator(':scope > .content > .ebkgoccj._narrow_');
  const form = window.locator(':scope > .body > form.bafeceda');
  await expect(window.locator(':scope > .header > .title')).toHaveText('パスワードを忘れた');
  await expect(form.locator(':scope > .main._formRoot > .matxzzsk._formBlock')).toHaveCount(2);
  await expect(form.locator('input[name="username"]')).toHaveAttribute('pattern', '^[a-zA-Z0-9_]+$');
  await expect(form.locator('input[name="username"]')).toBeFocused();
  await expect(form.locator('input[name="email"]')).toHaveAttribute('type', 'email');
  await expect(form.locator('.matxzzsk:has(input[name="email"]) > .input > .prefix')).toBeEmpty();
  await expect(form.locator(':scope > .main > button._formBlock.primary')).toHaveText('送信');
  await expect(form.locator(':scope > .sub > a._link')).toHaveText(/管理者までお問い合わせください/);
  expect(await alpha(window.locator(':scope > .body'))).toBe(255);
  const box = await window.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.width).toBeLessThanOrEqual(360);
  expect(await form.locator(':scope > .main').evaluate(element => getComputedStyle(element).padding)).toBe('24px');
  expect(await form.locator(':scope > .sub').evaluate(element => getComputedStyle(element).borderTopStyle)).toBe('solid');
  await expect.poll(() => forgot.getAttribute('data-motion-state')).toBe('entered');
  const motion = await forgot.locator(':scope > .content').evaluate(element => ({
    duration: getComputedStyle(element).transitionDuration,
    properties: getComputedStyle(element).transitionProperty,
  }));
  expect(motion.duration).toContain('0.2s');
  expect(motion.properties).toContain('opacity');
  expect(motion.properties).toContain('transform');

  await form.locator('input[name="username"]').fill('alice');
  await form.locator('input[name="email"]').fill('alice@example.test');
  await form.locator('input[name="email"]').press('Enter');
  await expect(form.locator('.fpezltsf.warn')).toHaveText(/レート制限を超えました/);
  expect(attempts).toBe(1);
  expect(submitted).toContain('alice');
  expect(submitted).toContain('alice@example.test');

  await form.locator('button[data-password-reset-submit]').click();
  await expect.poll(() => forgot.getAttribute('data-motion-state')).toBe('leaving');
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(1);
  expect(attempts).toBe(2);

  const cancelled = await openForgotPassword(page);
  await page.keyboard.press('Escape');
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(1);
  await expect(cancelled).toHaveCount(0);
  const reopened = await openForgotPassword(page);
  await expect.poll(() => reopened.getAttribute('data-motion-state')).toBe('entered');
  await page.keyboard.press('Escape');
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(1);
  await assertCircuitHealthy(page, failures);
});

test('signup-complete keeps the code in the fragment and submits only after the upstream alert leave', async ({ page }) => {
  const failures = startDiagnostics(page, ['/auth/email-confirmation/complete']);
  let attempts = 0;
  let submitted = '';
  let alertCountAtRequest = -1;
  await page.route('**/auth/email-confirmation/complete', async route => {
    attempts += 1;
    submitted = route.request().postData() ?? '';
    alertCountAtRequest = await page.locator('.qzhlnise.dialog[role="alertdialog"]').count();
    await route.fulfill({
      status: attempts === 1 ? 400 : 200,
      contentType: 'application/json',
      body: JSON.stringify(attempts === 1
        ? { status: 'failed', errorCode: 'INVALID_OR_EXPIRED_TOKEN' }
        : { status: 'succeeded', redirectUrl: '/' }),
    });
  });

  await page.goto(`/signup-complete#${confirmationToken}`);
  await expect.poll(() => page.url()).not.toContain('#');
  await expect(page.getByText('処理中', { exact: true })).toBeVisible();
  const prompt = page.locator('.qzhlnise.dialog[role="alertdialog"]');
  await expect(prompt).toHaveAttribute('aria-label', 'メール');
  await expect(prompt.locator('.mk-dialog > .body')).toHaveText('[わかった]を押して、メールアドレスの確認を完了してください。');
  await expect(prompt.locator('.mk-dialog > .buttons button')).toHaveText('わかった');
  await expect(prompt.locator('.mk-dialog > .buttons button')).toBeFocused();
  expect(await alpha(prompt.locator('.mk-dialog'))).toBe(255);
  await expect.poll(() => prompt.getAttribute('data-motion-state')).toBe('entered');

  await prompt.locator('.mk-dialog > .buttons button').press('Enter');
  await expect.poll(() => prompt.getAttribute('data-motion-state')).toBe('leaving');
  await expect(page.locator('.qzhlnise.dialog[role="alertdialog"]')).toHaveAttribute('aria-label', 'エラー');
  await expect(page.locator('.qzhlnise.dialog[role="alertdialog"] .mk-dialog > .body')).toHaveText('有効な値ではありません。');
  expect(attempts).toBe(1);
  expect(alertCountAtRequest).toBe(0);
  expect(submitted).toContain(confirmationToken);
  expect(page.url()).not.toContain(confirmationToken);
  await page.locator('.qzhlnise.dialog[role="alertdialog"] .mk-dialog > .buttons button').click();
  await expect(page.locator('.qzhlnise.dialog[role="alertdialog"]')).toHaveCount(0);

  await page.goto(`/signup-complete#${confirmationToken}`);
  await expect.poll(() => page.url()).not.toContain('#');
  await page.locator('.qzhlnise.dialog[role="alertdialog"] .mk-dialog > .buttons button').click();
  await expect(page).toHaveURL(/\/$/);
  await waitForHomeCircuit(page);
  expect(attempts).toBe(2);
  await assertCircuitHealthy(page, failures);
});

test('reset-password preserves the sticky page, clears fragment history, localizes errors, and handles keyboard submit', async ({ page }) => {
  const failures = startDiagnostics(page, ['/auth/password-reset/complete']);
  let attempts = 0;
  const submissions: string[] = [];
  await page.route('**/auth/password-reset/complete', async route => {
    attempts += 1;
    submissions.push(route.request().postData() ?? '');
    await route.fulfill({
      status: attempts === 1 ? 400 : 200,
      contentType: 'application/json',
      body: JSON.stringify(attempts === 1
        ? { status: 'failed', errorCode: 'PASSWORD_TOO_SHORT' }
        : { status: 'succeeded', redirectUrl: '/' }),
    });
  });

  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto(`/reset-password#${resetToken}`);
  await page.evaluate(() => document.fonts.ready);
  await expect.poll(() => page.url()).not.toContain('#');
  await expect(page.locator('.fdidabkb .titleContainer .title .title')).toHaveText('パスワードをリセット');
  const form = page.locator('form._formRoot[action="/auth/password-reset/complete"]');
  const input = form.locator('input[name="password"]');
  const button = form.locator('button[data-password-reset-submit]');
  await expect(button).toBeEnabled();
  await expect(input).toHaveAttribute('autocomplete', 'new-password');
  expect(await alpha(input)).toBe(255);
  const formBox = await form.boundingBox();
  expect(formBox).not.toBeNull();
  expect(formBox!.width).toBeLessThanOrEqual(700);

  await input.fill('short');
  await input.press('Enter');
  await expect(form.locator('.fpezltsf.warn')).toHaveText(/短すぎます/);
  expect(attempts).toBe(1);
  expect(submissions[0]).toContain(resetToken);
  expect(page.url()).not.toContain(resetToken);

  await input.fill('a sufficiently strong browser fixture password 42!');
  await button.click();
  await expect(page).toHaveURL(/\/$/);
  await waitForHomeCircuit(page);
  expect(attempts).toBe(2);

  await page.goto('/reset-password');
  await expect(page).toHaveURL(/\/$/);
  const forgot = page.locator('body > .qzhlnise.dialog').last();
  await expect(forgot.locator('.ebkgoccj > .header > .title')).toHaveText('パスワードを忘れた');
  await expect(forgot.locator('input[name="username"]')).toBeFocused();
  await expect.poll(() => forgot.getAttribute('data-motion-state')).toBe('entered');
  await page.keyboard.press('Escape');
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(0);
  await assertCircuitHealthy(page, failures);
});
