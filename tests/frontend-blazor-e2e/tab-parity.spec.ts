import { expect, test } from '@playwright/test';

test('MkTab preserves option DOM, active selection and native keyboard activation', async ({ page }) => {
  await page.goto('/__test/components/tab');
  const root = page.locator('.pxhvhrfw[data-fixture="tab"]');
  const buttons = root.locator(':scope > button._button');
  await expect(buttons).toHaveCount(3);
  expect(await buttons.evaluateAll(elements => elements.map(element => element.getAttribute('type'))))
    .toEqual([null, null, null]);
  await expect(buttons.nth(0)).toHaveText('Notes');
  await expect(buttons.nth(0)).toBeDisabled();
  await expect(buttons.nth(0)).toHaveClass(/active/);
  const wideFontRatio = await root.evaluate(element =>
    Number.parseFloat(getComputedStyle(element).fontSize) /
      Number.parseFloat(getComputedStyle(element.parentElement!).fontSize));
  expect(wideFontRatio).toBeCloseTo(0.9, 3);
  await expect(buttons.nth(0)).toHaveCSS('padding', '10px 8px');

  await buttons.nth(1).focus();
  await page.keyboard.press('Space');
  await expect(page.locator('#tab-value')).toHaveText('replies');
  await expect(buttons.nth(1)).toBeDisabled();
  await expect(buttons.nth(1)).toHaveClass(/active/);
  await expect(buttons.nth(0)).toBeEnabled();
});

test('MkTab applies the inclusive v-size 500px responsive class and spacing', async ({ page }) => {
  await page.goto('/__test/components/tab');
  const root = page.locator('.pxhvhrfw[data-fixture="tab"]');
  await expect(root).not.toHaveClass(/max-width_500px/);
  await page.locator('#resize-tab').click();
  await expect(root).toHaveClass(/max-width_500px/);
  const narrowFontRatio = await root.evaluate(element =>
    Number.parseFloat(getComputedStyle(element).fontSize) /
      Number.parseFloat(getComputedStyle(element.parentElement!).fontSize));
  expect(narrowFontRatio).toBeCloseTo(0.8, 3);
  await expect(root.locator(':scope > button').first()).toHaveCSS('padding', '11px 8px');
  await expect(root.locator(':scope > button').nth(1)).toHaveCSS('margin-left', '8px');
});

test('MkTab route disposal releases observers without a circuit error', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/__test/components/tab');
  await page.locator('#resize-tab').click();
  await expect(page.locator('.pxhvhrfw')).toHaveClass(/max-width_500px/);
  await page.locator('#leave-tab').click();
  await expect(page.locator('[data-contract="mk-time"]')).toBeVisible();
  expect(errors).toEqual([]);
});
