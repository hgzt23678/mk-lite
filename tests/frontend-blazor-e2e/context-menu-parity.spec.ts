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
  await expect(menu).not.toHaveClass(/fade-enter|fade-leave/);

  await menu.locator('button.item').click();
  await expect.poll(async () => await page.locator('[data-contract="state"]').getAttribute('data-action-count')).toBe('1');
  await expect(menu).toHaveCount(0);
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-closed-count', '1');

  await source.click({ button: 'right', position: { x: 8, y: 8 } });
  await expect(page.locator('.nvlagfpb')).toBeVisible();
  await page.mouse.click(400, 400);
  await expect(page.locator('.nvlagfpb')).toHaveClass(/fade-leave-active/);
  await page.waitForTimeout(120);
  const closing = await page.locator('.nvlagfpb').evaluate(element => ({
    opacity: Number.parseFloat(getComputedStyle(element).opacity),
    transform: getComputedStyle(element).transform,
  }));
  expect(closing.opacity).toBeLessThan(1);
  expect(closing.transform).not.toBe('none');
  await expect(page.locator('.nvlagfpb')).toHaveCount(1);
  await expect(page.locator('.nvlagfpb')).toHaveCount(0);
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-closed-count', '2');

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});

test('MkContextMenu honors the animation setting and reduced-motion preference', async ({ page }) => {
  await page.goto('/__test/components/context-menu');
  await page.evaluate(() => localStorage.setItem('pizzax::base', JSON.stringify({ animation: false })));
  await page.reload();

  const source = page.locator('[data-contract="source"]');
  const menu = page.locator('.nvlagfpb');
  await source.click({ button: 'right', position: { x: 8, y: 8 } });
  await expect(menu).toBeVisible();
  await expect(menu).not.toHaveClass(/fade-enter|fade-leave/);
  await expect(menu).toHaveCSS('opacity', '1');
  await expect(menu).toHaveCSS('transform', 'none');
  await page.mouse.click(400, 400);
  await expect(menu).toHaveCount(0);

  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.evaluate(() => localStorage.removeItem('pizzax::base'));
  await page.reload();
  await source.click({ button: 'right', position: { x: 8, y: 8 } });
  await expect(menu).toBeVisible();
  await expect(menu).not.toHaveClass(/fade-enter|fade-leave/);
  await page.mouse.click(400, 400);
  await expect(menu).toHaveCount(0);
});
