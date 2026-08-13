import { expect, test } from '@playwright/test';

const backgroundAlpha = async (locator: ReturnType<import('@playwright/test').Page['locator']>) =>
  locator.evaluate(element => {
    const context = document.createElement('canvas').getContext('2d');
    if (!context) throw new Error('Canvas 2D context unavailable');
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });

test('first-run welcome reproduces welcome.setup and signs in the initial administrator', async ({ page }) => {
  await page.request.post('/__test/initial-setup/required');
  await page.goto('/');

  const form = page.locator('form.mk-setup');
  await expect(form).toBeVisible();
  await expect(form).toHaveAttribute('data-setup-ready', 'true');
  await expect(form.locator(':scope > h1')).toHaveText('Welcome to Misskey!');
  await expect(form.locator(':scope > div._formRoot > p')).toBeVisible();
  await expect(page.locator('.rsqzvsbo')).toHaveCount(0);
  await expect(form.locator('input[name="username"]')).toHaveAttribute('pattern', '^[a-zA-Z0-9_]{1,20}$');
  await expect(form.locator('input[name="password"]')).toHaveAttribute('type', 'password');
  await expect(form.locator('[data-cy-admin-ok]')).toHaveText('完了');
  expect(await backgroundAlpha(form.locator(':scope > h1'))).toBe(255);
  expect(await backgroundAlpha(form.locator(':scope > div._formRoot'))).toBe(255);

  await form.locator('input[name="username"]').fill('initial_admin');
  await form.locator('input[name="password"]').fill('test-only-initial-password');
  await form.locator('[data-cy-admin-ok]').click();

  await expect(page).toHaveURL(/\/$/);
  await expect(page.locator('form.mk-setup')).toHaveCount(0);
  await expect(page.locator('.fdidabkb')).toBeVisible();
  const state = await page.request.get('/__test/initial-setup-state');
  expect(await state.json()).toEqual({
    setupRequired: false,
    setupCalls: 1,
    lastSetupUsername: 'initial_admin',
  });
});
