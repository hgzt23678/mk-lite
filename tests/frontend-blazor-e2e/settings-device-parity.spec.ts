import { expect, test } from '@playwright/test';

async function signedIn(page: import('@playwright/test').Page) {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
}

test('navbar and deck settings preserve v12 controls and pizzax persistence', async ({ page }) => {
  await signedIn(page);
  await page.goto('/settings/navbar');
  await page.waitForSelector('.vvcocwet ._formRoot', { timeout: 30000 });
  const shell = page.locator('.vvcocwet');
  await expect(shell.locator('.novjtcto')).toHaveCount(1);
  const menu = shell.locator('textarea').first();
  await expect(menu).toBeVisible();
  await menu.fill('notifications\nexplore\n-');
  await shell.locator('button.save').click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').menu)).toEqual([
    'notifications', 'explore', '-'
  ]);
  await shell.locator('.novjtctn').nth(1).click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').menuDisplay)).toBe('sideIcon');

  await page.goto('/settings/deck');
  await page.waitForSelector('.vvcocwet ._formRoot', { timeout: 30000 });
  await page.locator('.vvcocwet .ziffeomt .button').first().click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').navWindow)).toBe(false);
  await page.locator('.vvcocwet .novjtctn').last().click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').columnAlign)).toBe('center');
});

test('custom CSS uses the v12 warning and applies only validated textContent', async ({ page }) => {
  await signedIn(page);
  await page.goto('/settings/custom-css');
  await page.waitForSelector('.vvcocwet ._formRoot', { timeout: 30000 });
  const root = page.locator('.vvcocwet ._formRoot');
  await expect(root.locator('.fpezltsf.warn')).toHaveCount(1);
  const css = ':root { --misskey-v12-contract: #123456; }';
  await root.locator('textarea').fill(css);
  await root.locator('button.save').click();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('customCss'))).toBe(css);
  await expect.poll(() => page.locator('#misskey-v12-custom-css').evaluate(element => element.textContent)).toBe(css);
  await root.locator('textarea').fill('@import url(https://invalid.example/style.css);');
  await root.locator('button.save').click();
  await expect(root.locator('.fpezltsf.warn').last()).toContainText('Custom CSS may not load');
  await expect.poll(() => page.evaluate(() => localStorage.getItem('customCss'))).toBe(css);
});
