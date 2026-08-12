import { expect, test } from '@playwright/test';

test('MkPageWindow preserves the v12 nested navigation, back button and context menu contract', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));

  await page.goto('/__test/components/page-window');
  const windowRoot = page.locator('.ebkgocck');
  await expect(windowRoot).toBeVisible();
  await expect(windowRoot.locator(':scope > .body > .header .title > span')).toHaveText('First page');
  await expect(windowRoot.locator('[data-page="/first"]')).toBeVisible();
  await windowRoot.locator('[data-navigate]').click();
  await expect(windowRoot.locator(':scope > .body > .header .title > span')).toHaveText('Second page');
  await expect(windowRoot.locator('[data-page="/second"]')).toBeVisible();
  await windowRoot.locator(':scope > .body > .header .left > button').click();
  await expect(windowRoot.locator(':scope > .body > .header .title > span')).toHaveText('First page');

  await windowRoot.locator(':scope > .body > .header').click({ button: 'right' });
  const menu = page.locator('.rrevdjwt[role="menu"]');
  await expect(menu).toBeVisible();
  await expect(menu.locator(':scope > .label')).toHaveText('/first');
  await expect(menu.locator(':scope > .item')).toHaveCount(5);
  await menu.locator(':scope > .item').filter({ has: page.locator('i.fa-link') }).click();
  await expect(menu).toHaveCount(0);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
