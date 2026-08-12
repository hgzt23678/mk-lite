import { expect, test } from '@playwright/test';

test('settings sounds preserves v12 rows and durable device keys', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => { if (message.type() === 'error') failures.push(`console:${message.text()}`); });
  page.on('pageerror', error => failures.push(`page:${error.message}`));

  await page.goto('/__test/sign-in');
  await page.goto('/settings/sounds');
  const root = page.locator('[data-testid="settings-sounds"]');
  await expect(root).toBeVisible();
  await expect(root.locator('[data-setting="sound_masterVolume"]')).toHaveCount(1);
  await expect(root.locator('[data-sound]')).toHaveCount(7);
  await root.locator('button').last().click();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('pizzax::base') ?? '')).toContain('sound_masterVolume');
  await expect(root).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  expect(failures).toEqual([]);
});
