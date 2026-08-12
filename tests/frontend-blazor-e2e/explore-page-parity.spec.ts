import { expect, test } from '@playwright/test';

test('explore preserves v12 tabs and searches real user preview projections', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
  });

  await page.goto('/explore', { waitUntil: 'networkidle' });
  await expect(page.locator('body > .mk-app')).toHaveCount(1);
  await expect(page.locator('.fdidabkb .tabs button.tab')).toHaveCount(3);
  await expect(page.locator('[data-capability-state="false"][data-capability="explore/featured"]')).toHaveCount(1);

  await page.locator('.fdidabkb .tabs button.tab').nth(2).click();
  const input = page.locator('input[name="explore-user-search"]');
  await expect(input).toBeVisible();
  await input.fill('ali');

  const results = page.locator('[data-explore-search-results="true"]');
  await expect(results).toBeVisible({ timeout: 15_000 });
  await expect(results.locator('.user')).toHaveCount(1);
  await expect(results.locator('.user')).toContainText('Alice');
  await expect(results.locator('.user')).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');

  await page.locator('.fdidabkb .tabs button.tab').nth(1).click();
  await expect(page.locator('[data-capability-state="false"][data-capability="explore/users"]')).toHaveCount(1);
  expect(failures).toEqual([]);
  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
});
