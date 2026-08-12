import { expect, test } from '@playwright/test';

test('MkTagCloud preserves the v12 canvas, hidden tag slot and disposal lifecycle', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));

  await page.goto('/__test/components/tag-cloud');
  const cloud = page.locator('[data-contract="tag-cloud"] .meijqfqm');
  await expect(cloud).toBeVisible();
  await expect(cloud.locator(':scope > canvas.canvas')).toHaveAttribute('height', '300');
  await expect(cloud.locator(':scope > .tags')).toHaveCSS('top', '999px');
  await expect(cloud.locator(':scope > .tags li')).toHaveCount(2);
  await expect(cloud.locator(':scope > canvas.canvas')).toHaveAttribute('width', '420');

  await page.locator('[data-contract="toggle"]').click();
  await expect(cloud).toHaveCount(0);
  await page.locator('[data-contract="toggle"]').click();
  await expect(page.locator('[data-contract="tag-cloud"] .meijqfqm')).toBeVisible();

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
