import { expect, test } from '@playwright/test';

async function signedIn(page: import('@playwright/test').Page) {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
}

test('theme settings preserve v12 controls, built-in catalog selection and browser-local persistence', async ({ page }) => {
  await signedIn(page);
  await page.goto('/settings/theme');
  const root = page.locator('.vvcocwet .rsljpzjq');
  await expect(root).toBeVisible({ timeout: 30000 });
  await expect(root.locator('#dn')).toHaveCount(1);
  await expect(root.locator('.toggle__handler')).toHaveCount(1);
  await expect(root.locator('select')).toHaveCount(2);
  await expect(root.locator('select').first().locator('option')).toHaveCount(8);
  await expect(root.locator('select').last().locator('option')).toHaveCount(10);

  // The upstream toggle intentionally positions the checkbox off-screen;
  // interact through its visible label just as a keyboard/pointer user does.
  await root.locator('label[for="dn"]').click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').darkMode)).toBe(true);
  await expect.poll(() => page.evaluate(() => document.documentElement.dataset.theme)).toBe('dark');

  await root.locator('select').first().selectOption({ label: 'Mi Apricot Light' });
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('miux:lightTheme') ?? '{}').id)).toBe('0ff48d43-aab3-46e7-ab12-8492110d2e2b');
  await root.locator('label[for="dn"]').click();
  await expect.poll(() => page.evaluate(() => document.documentElement.dataset.theme)).toBe('light');

  await expect(root.locator('[data-capability-state="false"][data-capability="settings/theme/registry"]')).toContainText('registry');
  await expect(root.locator('[data-capability-state="false"][data-capability="settings/theme/wallpaper"]')).toHaveCount(1);
  await expect(root.locator('.rfqxtzch')).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});
