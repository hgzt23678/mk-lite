import { expect, test } from '@playwright/test';

test('MkPagination preserves load, auto-more, queue, retry and reload behavior', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/pagination');
  await expect(page.locator('.mk-app')).not.toHaveAttribute('inert', '');

  const scroll = page.locator('[data-contract="scroll"]');
  const items = scroll.locator('[data-pagination-item]');
  await expect(items).toHaveCount(6);
  await expect(items.nth(3)).toHaveAttribute('data-ad', 'true');
  await expect(page.locator('[data-contract="request-count"]')).toHaveText('1');
  await expect(scroll.locator('[data-pagination-auto-load]')).toHaveCount(1);
  await expect.poll(() => scroll.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);

  await scroll.evaluate(element => { element.scrollTo({ top: element.scrollHeight, behavior: 'instant' }); });
  await expect(items).toHaveCount(9);
  await expect(page.locator('[data-contract="request-count"]')).toHaveText('2');

  await page.locator('[data-contract="prepend"]').click();
  await expect(page.locator('[data-contract="queue-count"]')).toHaveText('1');
  await expect(scroll.locator('[data-pagination-item="live-1"]')).toHaveCount(0);

  await scroll.evaluate(element => { element.scrollTop = 0; });
  await expect(page.locator('[data-contract="queue-count"]')).toHaveText('0');
  await expect(items.first()).toHaveAttribute('data-pagination-item', 'live-1');

  const errorPagination = page.locator('[data-contract="error-pagination"]');
  await expect(errorPagination.locator('.mjndxjcg')).toBeVisible();
  await errorPagination.locator('.mjndxjcg > .button').click();
  await expect(errorPagination.locator('.empty > ._fullinfo')).toContainText('ありません');

  await page.locator('[data-contract="reload"]').click();
  await expect(items).toHaveCount(6);
  await expect(page.locator('[data-contract="request-count"]')).toHaveText('3');
  expect(browserFailures).toEqual([]);
});
