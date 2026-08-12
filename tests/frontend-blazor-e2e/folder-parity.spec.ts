import { expect, test } from '@playwright/test';

test('MkFolder preserves pinned DOM persistence background size and motion contracts', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/folder');
  await page.evaluate(() => localStorage.removeItem('ui:folder:browser-contract'));
  await page.reload();

  const folder = page.locator('[data-contract="folder"]');
  await expect(folder).toHaveClass(/\bssazuxis\b/);
  await expect(folder).toHaveClass(/\bmax-width_500px\b/);
  await expect(folder.locator(':scope > header > .title')).toContainText('Folder');
  await expect(folder.locator(':scope > div:last-child')).toBeVisible();
  await expect(folder.locator(':scope > header')).toHaveCSS('background-color', 'rgba(20, 30, 40, 0.85)');

  await folder.locator(':scope > header').click();
  await expect(folder.locator(':scope > div:last-child')).toBeHidden();
  expect(await page.evaluate(() => localStorage.getItem('ui:folder:browser-contract'))).toBe('f');

  await page.reload();
  await expect(folder.locator(':scope > div:last-child')).toBeHidden();
  await folder.locator(':scope > header').click();
  await expect(folder.locator(':scope > div:last-child')).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem('ui:folder:browser-contract'))).toBe('t');
  expect(browserFailures).toEqual([]);
});
