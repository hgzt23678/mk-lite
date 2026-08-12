import { expect, test } from '@playwright/test';

test('MkContextMenu preserves the v12 placement, menu action and outside-close contract', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));

  await page.goto('/__test/components/context-menu');
  const source = page.locator('[data-contract="source"]');
  await source.click({ button: 'right', position: { x: 8, y: 8 } });

  const menu = page.locator('.nvlagfpb');
  await expect(menu).toBeVisible();
  await expect(menu.locator('.rrevdjwt[role="menu"] > button.item')).toHaveCount(1);
  const style = await menu.evaluate(element => ({
    top: Number.parseFloat(getComputedStyle(element).top),
    left: Number.parseFloat(getComputedStyle(element).left),
    zIndex: getComputedStyle(element).zIndex,
  }));
  expect(style.top).toBeGreaterThan(0);
  expect(style.left).toBeGreaterThan(0);
  expect(Number(style.zIndex)).toBeGreaterThan(0);

  await menu.locator('button.item').click();
  await expect.poll(async () => await page.locator('[data-contract="state"]').getAttribute('data-action-count')).toBe('1');
  await expect(menu).toHaveCount(0);
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-closed-count', '1');

  await source.click({ button: 'right', position: { x: 8, y: 8 } });
  await expect(page.locator('.nvlagfpb')).toBeVisible();
  await page.mouse.click(400, 400);
  await expect(page.locator('.nvlagfpb')).toHaveCount(0);
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-closed-count', '2');

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
