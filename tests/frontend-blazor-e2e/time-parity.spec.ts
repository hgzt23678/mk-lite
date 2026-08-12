import { expect, test } from '@playwright/test';

test('MkTime preserves upstream relative, absolute and detail DOM contracts', async ({ page }) => {
  await page.goto('/__test/components/time');

  const relative = page.locator('#relative time');
  const absolute = page.locator('#absolute time');
  const detail = page.locator('#detail time');
  await expect(relative).toHaveAttribute('data-mode', 'relative');
  await expect(relative).not.toHaveAttribute('datetime', /.*/);
  await expect(relative).toHaveText('1分前');

  const expectedAbsolute = await page.evaluate(() => new Date('2026-08-04T12:34:56Z').toLocaleString());
  await expect(absolute).toHaveAttribute('title', expectedAbsolute);
  await expect(absolute).toHaveText(expectedAbsolute);
  await expect(detail).toHaveText(/.+ \(1分前\)$/);
  await expect(detail).toHaveAttribute('title', /.+/);
});

test('MkTime disposal removes its live updater without terminating the circuit', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/__test/components/time');
  await expect(page.locator('#disposable time')).toBeVisible();

  await page.locator('#remove-time').click();
  await expect(page.locator('#disposable')).toHaveCount(0);
  await expect(page.locator('#remove-time')).toBeEnabled();
  await page.waitForTimeout(100);

  expect(errors).toEqual([]);
});
