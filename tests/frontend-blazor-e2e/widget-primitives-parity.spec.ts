import { expect, test } from '@playwright/test';

test('memo and unix clock widgets preserve the v12 DOM, storage and opaque panel surfaces', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      animation: false,
      memo: 'initial memo',
    }));
  });

  await page.goto('/__test/components/widget-primitives');
  const memo = page.locator('[data-contract="widget-primitives"] .mkw-memo');
  await expect(memo).toBeVisible();
  await expect(memo.locator('textarea')).toHaveValue('initial memo');
  await expect(memo.locator('textarea')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');

  await memo.locator('textarea').fill('saved memo');
  await memo.locator('button').click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').memo)).toBe('saved memo');
  await expect(memo.locator('button')).toBeDisabled();

  const clock = page.locator('[data-contract="widget-primitives"] .mkw-unixClock');
  await expect(clock).toBeVisible();
  await expect(clock).toHaveClass(/_monospace/);
  await expect(clock.locator(':scope > .label')).toHaveCount(2);
  await expect(clock.locator(':scope > .time > span')).toHaveCount(3);
  const clockBackground = await clock.evaluate(element => getComputedStyle(element).backgroundColor);
  expect(clockBackground).not.toBe('rgba(0, 0, 0, 0)');
  expect(clockBackground).not.toBe('transparent');

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
