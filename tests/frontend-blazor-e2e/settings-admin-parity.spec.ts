import { expect, test } from '@playwright/test';

test('settings shell renders the v12 menu and the profile form', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  await page.goto('/settings/profile');
  await page.waitForSelector('.vvcocwet', { timeout: 30000 });
  const shell = page.locator('.vvcocwet');
  const settingsLinks = shell.locator(':scope > .body > .nav .baaadecd .group .items a');
  await expect(settingsLinks).toHaveCount(12);
  await expect(settingsLinks.nth(0)).toHaveAttribute('href', '/settings/profile');
  await expect(settingsLinks.nth(1)).toHaveAttribute('href', '/settings/api');
  await expect(settingsLinks.nth(2)).toHaveAttribute('href', '/settings/apps');
  await expect(settingsLinks.nth(3)).toHaveAttribute('href', '/settings/navbar');
  await expect(settingsLinks.nth(4)).toHaveAttribute('href', '/settings/deck');
  await expect(settingsLinks.nth(5)).toHaveAttribute('href', '/settings/custom-css');
  await expect(settingsLinks.nth(6)).toHaveAttribute('href', '/settings/reaction');
  await expect(settingsLinks.nth(7)).toHaveAttribute('href', '/settings/privacy');
  await expect(settingsLinks.nth(8)).toHaveAttribute('href', '/settings/general');
  await expect(settingsLinks.nth(9)).toHaveAttribute('href', '/settings/theme');
  await expect(settingsLinks.nth(10)).toHaveAttribute('href', '/settings/notifications');
  await expect(settingsLinks.nth(11)).toHaveAttribute('href', '/settings/sounds');
  await expect(shell.locator(':scope > .body > .main .bkzroven')).toHaveCount(1);
  const form = shell.locator(':scope > .body > .main .bkzroven ._formRoot');
  await expect(form.locator('input[type="text"]')).toHaveCount(1);
  await expect(form.locator('textarea')).toHaveCount(1);
  await expect(form.locator('.llvierxe')).toHaveCount(1);
  await expect(form.locator('.llvierxe')).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});

test('admin shell renders the control panel nav and relay page', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  await page.goto('/admin/relays');
  await page.waitForSelector('.vvcocwet', { timeout: 30000 });
  const shell = page.locator('.vvcocwet');
  const links = shell.locator(':scope > .body > .nav .baaadecd .group .items a');
  await expect(links).toHaveCount(3);
  await expect(shell.locator(':scope > .body > .main .bkzroven .relaycxt')).toHaveCount(0);
  const addInput = shell.locator(':scope > .body > .main .bkzroven input[type="url"]');
  await expect(addInput).toHaveCount(1);
});

test('login dialog shows the local credential form without leaving the page', async ({ page }) => {
  await page.goto('/');
  await page.waitForTimeout(12000);
  const urlBefore = page.url();
  await page.getByRole('button', { name: 'ログイン' }).first().click();
  await page.waitForTimeout(3000);
  const dialog = page.locator('body > .qzhlnise.dialog');
  await expect(dialog).toHaveCount(1);
  await expect(dialog.locator('input[name="username"]')).toHaveCount(1);
  await expect(dialog.locator('input[name="password"]')).toHaveCount(1);
  await expect(page.url()).toBe(urlBefore);
});

test('API settings exposes token generation and the installed-apps link', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await page.goto('/settings/api');
  await page.waitForSelector('.vvcocwet ._formRoot', { timeout: 30000 });

  const root = page.locator('.vvcocwet ._formRoot');
  await expect(root.locator('button[type="button"], button[type="submit"]').first()).toBeVisible();
  await expect(root.locator('a[href="/settings/apps"]')).toHaveCount(1);
  await expect(root.locator('a[href="/api-console"]')).toHaveCount(1);

  await root.locator('button').first().click();
  const dialog = page.locator('.qzhlnise.dialog').filter({ has: page.locator('.ziffeomt') });
  await expect(dialog).toHaveCount(1);
  const tokenName = dialog.locator('input[type="text"]').first();
  await expect(tokenName).toBeVisible();
  await tokenName.fill('Browser contract token');
  await dialog.locator('.ziffeomt').first().click();
  await dialog.locator('.ebkgoccj > .header > button[aria-label="決定"]').click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('body > .qzhlnise.dialog').last()).toContainText('token');
});

test('installed-apps settings renders persisted token details', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await page.goto('/settings/apps');
  await page.waitForSelector('.vvcocwet ._formRoot', { timeout: 30000 });

  const card = page.locator('.vvcocwet ._formRoot .bfomjevm').filter({ hasText: 'Browser test application' });
  await expect(card).toHaveCount(1);
  await expect(card.locator('.name')).toHaveText('Browser test application');
  await expect(card.locator('.description')).toHaveText('Contract fixture token');
  await expect(card.locator('details summary')).toHaveCount(1);
});

test('privacy settings expose supported controls and explicit backend capability gaps', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await page.goto('/settings/privacy');
  await expect(page.locator('[data-testid="settings-privacy"] [data-setting="isLocked"]')).toHaveCount(1);
  await expect(page.locator('[data-testid="settings-privacy"] [data-capability-state="false"]')).toContainText('additional backend fields');
  await page.goto('/admin/users');
  const adminUnavailable = page.locator('[data-capability-state="false"][data-capability="admin/users"]');
  await expect(adminUnavailable).toHaveCount(1);
});
