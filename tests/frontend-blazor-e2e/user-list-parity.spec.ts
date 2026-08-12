import { expect, test } from '@playwright/test';

test('MkUserList preserves the pinned grid and real pagination load and reload paths', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));

  // The v12 pagination control auto-loads as soon as its sentinel is visible. This
  // contract exercises the complementary manual-load path through the persisted
  // user setting, rather than racing IntersectionObserver.
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({ enableInfiniteScroll: false }));
  });

  await page.goto('/__test/components/user-list');
  const list = page.locator('[data-contract="user-list"]');
  const grid = list.locator(':scope > .efvhhmdq');
  await expect(grid.locator(':scope > ._panel.vjnjpkug.user')).toHaveCount(2);
  await expect(grid.locator(':scope > .user > .title > .name')).toHaveText(['Alice', 'Bob']);
  await expect(list.locator(':scope > .cxiknjgy > button')).toHaveCount(1);
  await expect(list).not.toHaveClass(/noGap/);
  const gridContract = await grid.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      display: style.display,
      columns: style.gridTemplateColumns,
      gap: style.gap,
    };
  });
  expect(gridContract.display).toBe('grid');
  expect(gridContract.columns.split(' ').length).toBeGreaterThanOrEqual(2);
  expect(gridContract.gap).not.toBe('normal');

  await list.locator(':scope > .cxiknjgy > button').click();
  await expect(grid.locator(':scope > ._panel.vjnjpkug.user')).toHaveCount(3);
  await expect(grid.locator(':scope > .user > .title > .name')).toHaveText(['Alice', 'Bob', 'Carol']);

  await page.locator('[data-contract="reload"]').click();
  await expect(grid.locator(':scope > ._panel.vjnjpkug.user')).toHaveCount(1);
  await expect(grid.locator(':scope > .user > .title > .name')).toHaveText(['Bob']);
  expect(errors).toEqual([]);
});
