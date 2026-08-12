import { expect, test } from '@playwright/test';

test('MkDigitalClock preserves the pinned span DOM and UTC offset behavior', async ({ page }, testInfo) => {
  await page.goto('/__test/components/digital-clock');
  const root = page.locator('.zjobosdg.fixture-clock[data-fixture="digital-clock"]');
  await expect(root).toBeVisible();
  await expect(root.locator(':scope > span')).toHaveCount(5);
  // The pinned Vue component intentionally adds showColon for 30ms on its first tick.  Read
  // the base DOM after that real transition frame, rather than racing the valid pulse.
  await expect.poll(async () => root.locator(':scope > span').evaluateAll(
    nodes => nodes.map(node => node.className)))
    .toEqual(['', 'colon', '', 'colon', '']);

  const observed = await root.locator(':scope > span:not(.colon)').allTextContents().then(parts => parts.join(':'));
  expect(observed).toMatch(/^\d{2}:\d{2}:\d{2}$/);
  const observedSeconds = Date.parse(`1970-01-01T${observed}Z`) / 1000;
  const now = new Date();
  const expectedSeconds = Date.parse(`1970-01-01T${now.toISOString().slice(11, 19)}Z`) / 1000;
  const circularDifference = Math.min(
    Math.abs(observedSeconds - expectedSeconds),
    86400 - Math.abs(observedSeconds - expectedSeconds));
  expect(circularDifference).toBeLessThanOrEqual(2);

  await expect(root.locator(':scope > .colon').first()).toHaveCSS('transition-duration', '1s');
  const pixel = await page.screenshot({
    path: `artifacts/digital-clock-parity-${testInfo.project.name}.png`
  });
  expect(pixel.byteLength).toBeGreaterThan(1_000);
  const backgroundAlpha = await page.evaluate(() => {
    const color = getComputedStyle(document.body).backgroundColor;
    const match = color.match(/rgba?\(([^)]+)\)/);
    if (!match) return 0;
    const values = match[1].split(',').map(value => Number.parseFloat(value.trim()));
    return values.length === 4 ? values[3] : 1;
  });
  expect(backgroundAlpha).toBe(1);
});

test('MkDigitalClock uses the upstream 10ms ticker and 30ms colon flash lifecycle', async ({ page }) => {
  await page.goto('/__test/components/digital-clock');
  await page.evaluate(() => {
    const main = document.querySelector('main[data-contract="mk-digital-clock"]');
    if (!main) throw new Error('clock fixture missing');
    const state = window as typeof window & {
      clockColonClasses?: string[];
      clockColonObserver?: MutationObserver;
    };
    state.clockColonClasses = [];
    state.clockColonObserver?.disconnect();
    state.clockColonObserver = new MutationObserver(records => {
      for (const record of records) {
        if (record.type !== 'attributes' || !(record.target instanceof HTMLElement)) continue;
        if (!record.target.matches('.colon') ||
            !record.target.parentElement?.matches('.zjobosdg.fixture-clock')) continue;
        (window as typeof window & { clockColonClasses: string[] }).clockColonClasses.push(record.target.className);
      }
    });
    state.clockColonObserver.observe(document.documentElement, {
      subtree: true,
      attributes: true,
      attributeFilter: ['class']
    });
  });

  await page.locator('#toggle-ms').click();
  const root = page.locator('.zjobosdg.fixture-clock');
  await expect(root.locator(':scope > span').last()).toHaveText(/^\d{2}$/);
  const first = await root.locator(':scope > span').last().textContent();
  await expect.poll(async () => root.locator(':scope > span').last().textContent())
    .not.toBe(first);
  await expect.poll(() => page.evaluate(() =>
    (window as typeof window & { clockColonClasses?: string[] }).clockColonClasses ?? []))
    .toEqual(expect.arrayContaining([expect.stringContaining('showColon'), 'colon']));
});

test('MkDigitalClock route disposal releases its ticker without a circuit error', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/__test/components/digital-clock');
  await page.locator('#toggle-ms').click();
  await page.goto('/__test/components/time');
  await expect(page.locator('[data-contract="mk-time"]')).toBeVisible();
  expect(errors).toEqual([]);
});
