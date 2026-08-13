import { expect, test } from '@playwright/test';

test('MkDateSeparatedList preserves date, advertisement and list motion behavior', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });
  await page.emulateMedia({ reducedMotion: 'no-preference' });

  await page.goto('/__test/components/date-separated-list');
  await expect(page.locator('.mk-app')).not.toHaveAttribute('inert', '');

  const root = page.locator('.sqadhkmv.contract-list');
  await expect(root).toHaveClass('sqadhkmv noGap contract-list');
  await expect(root).toHaveAttribute('data-direction', 'down');
  await expect(root).toHaveAttribute('data-reversed', 'false');
  await expect(root).toHaveAttribute('aria-live', 'polite');
  await expect.poll(() => root.evaluate(element => Array.from(element.children).map(child => {
    if (child.hasAttribute('data-date-item')) return `item:${child.getAttribute('data-date-item')}`;
    if (child.hasAttribute('data-ad-item')) return `ad:${child.getAttribute('data-ad-item')}`;
    return `separator:${Array.from(child.querySelectorAll('span')).map(value => value.textContent?.trim()).join('|')}`;
  }))).toEqual(['item:a', 'separator:4月 3日|4月 2日', 'ad:b', 'item:b', 'item:c']);

  await page.locator('[data-contract="prepend"]').click();
  await expect(root.locator('[data-date-item="live"]')).toHaveCount(1);
  await expect(root.locator('[data-date-item="live"]')).toHaveClass(/list-enter-active/);

  await page.evaluate(() => {
    const descriptor = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetHeight');
    if (!descriptor?.get) throw new Error('offsetHeight getter is unavailable');
    const nativeGetter = descriptor.get;
    const counts = { root: 0, child: 0 };
    Object.defineProperty(HTMLElement.prototype, 'offsetHeight', {
      configurable: true,
      get() {
        if (this.matches('.sqadhkmv.contract-list')) counts.root += 1;
        else if (this.matches('.sqadhkmv.contract-list > *')) counts.child += 1;
        return nativeGetter.call(this);
      },
    });
    (window as Window & { __dateListLayoutReads?: typeof counts }).__dateListLayoutReads = counts;
  });

  await page.locator('[data-contract="reverse"]').click();
  await expect(root.locator('[data-date-item]').first()).toHaveAttribute('data-date-item', 'c');
  await expect.poll(() => root.locator('.list-move').count()).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() =>
    (window as Window & { __dateListLayoutReads?: { root: number } }).__dateListLayoutReads?.root)).toBe(1);
  expect(await page.evaluate(() =>
    (window as Window & { __dateListLayoutReads?: { child: number } }).__dateListLayoutReads?.child)).toBe(0);
  await expect(root.locator('.list-move, .list-enter-active')).toHaveCount(0, { timeout: 2_000 });

  expect(browserFailures).toEqual([]);
});
