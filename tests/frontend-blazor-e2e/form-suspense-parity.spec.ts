import { expect, test } from '@playwright/test';

test('FormSuspense preserves retry, result slot and fade out-in ordering', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });
  await page.emulateMedia({ reducedMotion: 'no-preference' });

  await page.goto('/__test/components/form-suspense');
  await expect(page.locator('.mk-app')).not.toHaveAttribute('inert', '');
  const host = page.locator('[data-contract="host"]');
  await expect(host.locator('._root_13vug_9')).toBeVisible();

  await host.evaluate(element => {
    const snapshots: Array<{ loading: boolean; error: boolean; result: boolean; classes: string }> = [];
    const capture = () => {
      const branch = element.querySelector(':scope > [data-suspense-branch]');
      snapshots.push({
        loading: branch?.querySelector('._root_13vug_9') !== null,
        error: branch?.querySelector('.wszdbhzo') !== null,
        result: branch?.querySelector('[data-contract="result"]') !== null,
        classes: branch?.className ?? '',
      });
    };
    new MutationObserver(capture).observe(element, {
      attributes: true,
      attributeFilter: ['class'],
      childList: true,
      subtree: true,
    });
    (window as Window & { formSuspenseSnapshots?: typeof snapshots }).formSuspenseSnapshots = snapshots;
    capture();
  });

  await page.locator('[data-contract="reject"]').click();
  await expect(host.locator('.wszdbhzo')).toBeVisible();
  await expect(host.locator('.wszdbhzo > div')).toContainText('問題が発生しました');
  await expect(host.locator('.fa-exclamation-triangle')).toHaveCount(1);
  await expect(host.locator('button.retry.inline')).toContainText('再試行');
  await expect(host.locator('.fa-redo-alt')).toHaveCount(1);
  await expect(host.locator('.fade-enter-active, .fade-leave-active')).toHaveCount(0, { timeout: 2_000 });

  const rejectionSnapshots = await page.evaluate(() =>
    (window as Window & { formSuspenseSnapshots?: Array<{
      loading: boolean;
      error: boolean;
      result: boolean;
      classes: string;
    }> }).formSuspenseSnapshots ?? []);
  expect(rejectionSnapshots.some(snapshot => snapshot.loading && snapshot.classes.includes('fade-leave-active'))).toBe(true);
  expect(rejectionSnapshots.some(snapshot => snapshot.error && snapshot.classes.includes('fade-enter-active'))).toBe(true);
  expect(rejectionSnapshots.some(snapshot => snapshot.loading && snapshot.error)).toBe(false);

  await host.locator('button.retry').click();
  await expect(host.locator('._root_13vug_9')).toBeVisible();
  await expect(host.locator('.fade-enter-active, .fade-leave-active')).toHaveCount(0, { timeout: 2_000 });
  await page.locator('[data-contract="resolve"]').click();
  await expect(host.locator('[data-contract="result"]')).toHaveText('resolved-2');
  await expect(host.locator('.fade-enter-active, .fade-leave-active')).toHaveCount(0, { timeout: 2_000 });

  const allSnapshots = await page.evaluate(() =>
    (window as Window & { formSuspenseSnapshots?: Array<{
      loading: boolean;
      error: boolean;
      result: boolean;
      classes: string;
    }> }).formSuspenseSnapshots ?? []);
  expect(allSnapshots.some(snapshot => snapshot.loading && snapshot.classes.includes('fade-leave-active'))).toBe(true);
  expect(allSnapshots.some(snapshot => snapshot.result && snapshot.classes.includes('fade-enter-active'))).toBe(true);
  expect(allSnapshots.some(snapshot => snapshot.loading && snapshot.result)).toBe(false);
  expect(browserFailures).toEqual([]);
});
