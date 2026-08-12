import { expect, test } from '@playwright/test';

test('reaction settings preserve v12 picker controls and durable pinned order', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  await page.goto('/settings/reaction');
  await page.waitForSelector('.vvcocwet ._formRoot', { timeout: 30000 });

  const root = page.locator('.vvcocwet ._formRoot');
  const items = root.locator('.zoaiodol .item');
  await expect(items).toHaveCount(10);
  await items.first().dragTo(items.last());
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').reactions)).toEqual([
    '❤️', '😆', '🤔', '😮', '🎉', '💢', '😥', '😇', '🍮', '👍'
  ]);

  // The width radio group is the second radio group; selecting a global
  // `.novjtctn` index is ambiguous because the settings page also renders
  // the size and height groups before/after it.
  await root.locator('.novjtcto').nth(1).locator('.novjtctn').nth(1).click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').reactionPickerWidth)).toBe(2);
  await root.locator('.ziffeomt .button').click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').reactionPickerUseDrawerForMobile)).toBe(false);
  await expect(page.locator('.zoaiodol')).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});
