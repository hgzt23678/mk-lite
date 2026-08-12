import { expect, test } from '@playwright/test';

test('general settings preserve v12 controls and browser/device storage contracts', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  await page.goto('/settings/general');
  await page.waitForSelector('[data-testid="settings-general"] .vblkjoeq', { timeout: 30000 });

  const root = page.locator('[data-testid="settings-general"]');
  await expect(root.locator('[data-setting="lang"]')).toHaveCount(1);
  await expect(root.locator('[data-setting="overridedDeviceKind"] .novjtctn')).toHaveCount(4);
  await expect(root.locator('[data-setting="serverDisconnectedBehavior"] option')).toHaveCount(3);
  await expect(root.locator('[data-setting="instanceTicker"] option')).toHaveCount(3);
  await expect(root.locator('[data-setting="nsfw"] option')).toHaveCount(3);
  await expect(root.locator('[data-setting="numberOfPageCache"]')).toHaveCount(1);

  await root.locator('[data-setting="overridedDeviceKind"] .novjtctn').nth(3).click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').overridedDeviceKind))
    .toBe('desktop');

  await root.locator('[data-setting="reduceAnimation"] .button').click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').animation))
    .toBe(false);

  await root.locator('[data-setting="fontSize"] .novjtctn').nth(2).click();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('fontSize'))).toBe('2');

  await root.locator('[data-setting="useSystemFont"] .button').click();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('useSystemFont'))).toBe('t');
  await expect.poll(() => page.evaluate(() => document.documentElement.classList.contains('useSystemFont'))).toBe(true);

  await expect(root).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});
