import { expect, test } from '@playwright/test';

test('notification settings preserve v12 actions and execute the durable mark-all command', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  await page.goto('/settings/notifications');

  const root = page.locator('[data-testid="settings-notifications"]');
  await expect(root).toBeVisible();
  await expect(root.locator('[data-action="configure"][data-capability-state="false"]')).toHaveCount(1);
  await expect(root.locator('[data-action="mark-all-unread-notes"][disabled]')).toHaveCount(1);
  await expect(root.locator('[data-action="mark-all-messaging"][disabled]')).toHaveCount(1);

  const markAll = root.locator('[data-action="mark-all-notifications"]');
  await expect(markAll).toBeEnabled();
  await markAll.click();
  await expect(root.locator('[data-testid="settings-notifications-status"]')).toContainText(/通知|notifications/i);
  await expect(root).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});
