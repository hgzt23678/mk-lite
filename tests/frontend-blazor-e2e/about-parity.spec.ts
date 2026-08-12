import { expect, test } from '@playwright/test';

test('about page uses the supported Dolphin-backed instance projection', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
  });

  await page.goto('/about');
  await expect(page.locator('body > .mk-app')).toHaveCount(1);
  await expect.poll(async () => page.locator('body > .mk-app').evaluate(element =>
    !(element as HTMLElement).inert)).toBe(true);

  const header = page.locator('.fdidabkb').first();
  await expect(header.locator(':scope > .titleContainer > .title > .title')).toHaveText('インスタンス情報');
  await expect(header.locator(':scope > .tabs > button.tab')).toHaveCount(2);
  await expect(page.locator('.fwhjspax > .content .name b')).toHaveText('Browser test instance');
  await expect(page.locator('.fwhjspax')).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  await expect(page.locator('text=12,345')).toBeVisible();
  await expect(page.locator('text=67,890')).toBeVisible();
  await expect(page.locator('a[href="http://127.0.0.1:5099/.well-known/nodeinfo"]')).toHaveCount(1);

  await header.getByRole('tab', { name: '連合' }).click();
  await expect(page.locator('.taeiyria > .query')).toBeVisible();
  await expect(page.locator('.dqokceoi > a.instance')).toHaveCount(10);
  await expect(page).toHaveURL(/\/about#federation$/);

  expect(failures).toEqual([]);
  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
});
