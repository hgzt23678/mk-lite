import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.goto('/__test/components/number-diff');
  const fixture = page.locator('[data-contract="mk-number-diff"]');
  await expect(fixture).toBeVisible();
  // Interactive Server replaces the static SSR nodes when its circuit attaches.  Firefox can
  // expose the old, already-detached nodes to an evaluateAll callback during that exact frame;
  // getComputedStyle correctly returns empty strings for those nodes.  Require a connected DOM
  // and resolved theme variables, so the parity assertion still fails for missing CSS.
  await expect.poll(() => page.evaluate(() => {
    const root = getComputedStyle(document.documentElement);
    const candidate = document.querySelector<HTMLElement>('[data-contract="mk-number-diff"]');
    if (candidate === null || !candidate.isConnected || candidate.children.length !== 3) return false;
    const children = Array.from(candidate.children);
    return root.getPropertyValue('--success').trim().length > 0 &&
      root.getPropertyValue('--error').trim().length > 0 &&
      children.every(element => element.isConnected &&
        getComputedStyle(element).color.length > 0 &&
        getComputedStyle(element).opacity.length > 0);
  })).toBe(true);
});

test('MkNumberDiff preserves the pinned Vue DOM, locale formatting, slots, and state colors', async ({ page }) => {
  const localeValues = await page.evaluate(() => [1234, -1234.5678, 0].map(value => value.toLocaleString()));
  const snapshot = await page.evaluate(() => {
    const root = getComputedStyle(document.documentElement);
    const fixture = document.querySelector<HTMLElement>('[data-contract="mk-number-diff"]');
    if (fixture === null) throw new Error('Number diff fixture is missing');
    return {
      values: Array.from(fixture.querySelectorAll(':scope > span')).map(element => ({
        tag: element.tagName,
        className: element.className,
        text: element.textContent,
        color: getComputedStyle(element).color,
        opacity: getComputedStyle(element).opacity,
      })),
      colors: {
        success: root.getPropertyValue('--success').trim(),
        error: root.getPropertyValue('--error').trim(),
        inherited: getComputedStyle(fixture).color,
      },
    };
  });
  expect(snapshot.values).toEqual([
    {
      tag: 'SPAN',
      className: 'ceaaebcd isPlus fixture-positive',
      text: `(+${localeValues[0]})`,
      color: snapshot.colors.success,
      opacity: '1',
    },
    {
      tag: 'SPAN',
      className: 'ceaaebcd isMinus fixture-negative',
      text: localeValues[1],
      color: snapshot.colors.error,
      opacity: '1',
    },
    {
      tag: 'SPAN',
      className: 'ceaaebcd isZero fixture-zero',
      text: localeValues[2],
      color: snapshot.colors.inherited,
      opacity: '0.5',
    },
  ]);
});

test('MkNumberDiff state changes do not add wrappers or lose the external class', async ({ page }) => {
  const positive = page.locator('.fixture-positive');
  expect(await positive.locator(':scope > *').count()).toBe(0);
  await expect(positive).toHaveAttribute('class', 'ceaaebcd isPlus fixture-positive');
});
