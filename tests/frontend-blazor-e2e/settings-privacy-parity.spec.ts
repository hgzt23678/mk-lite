import { expect, test } from '@playwright/test';

test('privacy settings persist supported Dolphin profile controls without fake fields', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  await page.goto('/settings/privacy');
  await page.waitForSelector('[data-testid="settings-privacy"] ._formRoot, [data-testid="settings-privacy"]', { timeout: 30000 });

  const root = page.locator('[data-testid="settings-privacy"]');
  await expect(root.locator('[data-setting="isLocked"]')).toBeVisible();
  await expect(root.locator('[data-setting="isExplorable"]')).toBeVisible();
  await expect(root.locator('[data-capability-state="false"]')).toContainText('additional backend fields');

  await root.locator('[data-setting="isLocked"] .button').click();
  await expect(root.locator('[data-setting="isLocked"]')).toHaveClass(/checked/);
  await expect(root.locator('[data-testid="settings-privacy-status"]')).toContainText('保存しました');

  await root.locator('[data-setting="isExplorable"] .button').click();
  await expect(root.locator('[data-setting="isExplorable"]')).not.toHaveClass(/checked/);
  await expect(root).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});
