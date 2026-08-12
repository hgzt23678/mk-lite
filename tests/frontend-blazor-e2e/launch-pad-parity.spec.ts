import { expect, test } from '@playwright/test';

test('MkLaunchPad preserves the pinned grid popup, opaque surface and close behavior', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/launch-pad');
  const source = page.locator('[data-contract="open"]');
  await source.click();
  const modal = page.locator('.qzhlnise');
  const surface = modal.locator('.szkkfdyq._popup._shadow');
  await expect(modal).toHaveClass(/\bpopup\b/);
  await expect(surface).toBeVisible();
  await expect(surface.locator(':scope > .main > *')).toHaveCount(2);
  await expect(surface.locator('button > .text')).toHaveText('Reload');
  await expect(surface.locator('a')).toHaveAttribute('href', '/');
  await expect(surface.locator('.indicator > i')).toHaveClass('fas fa-circle');
  expect(await surface.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');

  await surface.locator('button').click();
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-action-count', '1');
  await expect(modal).toHaveCount(0);
  await expect(source).toBeFocused();

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
