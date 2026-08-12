import { expect, test } from '@playwright/test';

test('user information uses the real preview projection and exposes backend capability gaps', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await page.goto('/user-info/alice-id');

  const root = page.locator('[data-testid="user-info-page"]');
  await expect(root).toBeVisible();
  await expect(root.locator('.user-info-summary .vjnjpkug')).toHaveCount(1);
  await expect(root.locator('.user-info-summary a.name')).toContainText('Alice');
  await expect(root.locator('.user-info-values')).toContainText('alice-id');
  await expect(root.locator('[data-capability-state="false"]')).toContainText('Moderation flags');
  await expect(root).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  await expect(page.locator('[data-user-info-state="error"]')).toHaveCount(0);
});
