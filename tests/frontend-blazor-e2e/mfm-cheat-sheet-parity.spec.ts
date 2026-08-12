import { expect, test } from '@playwright/test';

test('MFM cheat sheet preserves the v12 feature order and editable previews', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => { if (message.type() === 'error') failures.push(`console:${message.text()}`); });
  page.on('pageerror', error => failures.push(`page:${error.message}`));
  page.on('response', response => { if (response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`); });

  await page.goto('/mfm-cheat-sheet');
  await expect(page.locator('[data-mfm-feature]')).toHaveCount(29);
  await expect(page.locator('[data-mfm-feature]').first()).toHaveAttribute('data-mfm-feature', 'mention');
  await expect(page.locator('[data-mfm-feature]').last()).toHaveAttribute('data-mfm-feature', 'plain');
  await expect(page.locator('[data-mfm-feature] textarea')).toHaveCount(29);
  await expect(page.locator('.mwysmxbg')).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  expect(failures).toEqual([]);
});
