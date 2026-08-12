import { expect, test, type Page } from '@playwright/test';

const attachFailureGuards = (page: Page) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.message}`));
  return failures;
};

const canvasAlpha = async (locator: ReturnType<Page['locator']>) =>
  locator.evaluate(element => {
    const context = document.createElement('canvas').getContext('2d');
    if (!context) throw new Error('Canvas 2D context unavailable');
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });

const openSignup = async (page: Page) => {
  const source = page.locator('[data-cy-signup]');
  await source.click();
  const modal = page.locator('body > .qzhlnise.dialog[role="dialog"]');
  await expect(modal).toHaveCount(1);
  await expect.poll(async () => modal.getAttribute('data-motion-state')).toBe('entered');
  return { source, modal };
};

test.beforeEach(async ({ page }) => {
  await page.request.post('/__test/reset-diagnostics');
  await page.request.post('/__test/registration-protection/none');
  await page.goto('/');
});

test('MkSignupDialog preserves pinned geometry, focus, close paths, and email-pending completion', async ({ page }) => {
  const failures = attachFailureGuards(page);
  let registrationRequests = 0;
  await page.route('**/auth/register', async route => {
    registrationRequests += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ status: 'signup-email-pending' }),
    });
  });

  const first = await openSignup(page);
  const firstWindow = first.modal.locator(':scope > .content > .ebkgoccj._narrow_');
  const firstBody = firstWindow.locator(':scope > .body');
  const firstForm = firstBody.locator(':scope > ._monolithic_ > ._section > form.qlvuhzng._formRoot');
  const firstBox = await firstWindow.boundingBox();
  expect(firstBox).not.toBeNull();
  expect(firstBox!.width).toBe(366);
  expect(firstBox!.height).toBe(500);
  expect(await canvasAlpha(firstBody)).toBe(255);
  await expect(firstWindow.locator(':scope > .header > .title')).toHaveText('新規登録');
  await expect(firstForm).toHaveAttribute('data-auto-set', 'false');
  await expect(first.source).toBeFocused();

  await first.modal.locator(':scope > .bg').evaluate(element =>
    element.dispatchEvent(new MouseEvent('click', { bubbles: true })));
  await expect(first.modal).toHaveCount(1);
  await page.keyboard.press('Tab');
  const closeButton = firstWindow.locator(':scope > .header > button[data-mk-dialog-close="true"]');
  await expect(closeButton).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(firstForm.locator('[data-cy-signup-username] > .label > div._help[role="button"]')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(firstForm.locator('input[name="username"]')).toBeFocused();
  await closeButton.click();
  await expect(first.modal).toHaveCount(0);
  await expect(first.source).toBeFocused();

  const escaped = await openSignup(page);
  await page.keyboard.press('Escape');
  await expect(escaped.modal).toHaveCount(0);
  await expect(escaped.source).toBeFocused();

  const pending = await openSignup(page);
  const pendingForm = pending.modal.locator(
    ':scope > .content > .ebkgoccj._narrow_ > .body > ._monolithic_ > ._section > form.qlvuhzng._formRoot');
  await pendingForm.locator('input[name="username"]').fill('available_user');
  await pendingForm.locator('input[name="email"]').fill('available@example.test');
  await pendingForm.locator('input[name="password"]').fill('test-password-123');
  await pendingForm.locator('input[name="retypedPassword"]').fill('test-password-123');
  await pendingForm.locator('.ziffeomt.tou > .button').click();
  await expect(pendingForm.locator('.ziffeomt.tou')).toHaveClass(/\bchecked\b/);
  await expect(pendingForm.locator('[data-cy-signup-submit]')).toBeEnabled();
  await pendingForm.locator('[data-cy-signup-submit]').click();

  const alert = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(alert.locator('.mk-dialog > header')).toHaveText('ほとんど完了です');
  await expect(alert.locator('.mk-dialog > .body')).toContainText('available@example.test');
  await expect(alert.locator('.mk-dialog > .buttons button')).toBeFocused();
  await expect(page.locator('body > .qzhlnise.dialog[role="dialog"]')).toHaveCount(0);
  expect(registrationRequests).toBe(1);
  expect(failures).toEqual([]);
  await expect.poll(async () => (await page.request.get('/__test/diagnostics')).json())
    .toEqual({ unhandledExceptions: [] });
});
