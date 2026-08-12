import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    const NativeDate = Date;
    Object.defineProperty(globalThis, '__misskeyAnalogNow', {
      configurable: true,
      writable: true,
      value: NativeDate.UTC(2026, 7, 4, 15, 30, 20),
    });
    globalThis.Date = class extends NativeDate {
      constructor(...args: ConstructorParameters<typeof NativeDate>) {
        super(...(args.length === 0 ? [globalThis.__misskeyAnalogNow] : args));
      }

      static now() {
        return globalThis.__misskeyAnalogNow;
      }
    } as DateConstructor;
  });
});

test('MkAnalogClock preserves pinned SVG, graduations, theme colors, and responsive-safe geometry', async ({ page }, testInfo) => {
  await page.goto('/__test/components/analog-clock');
  const root = page.locator('svg.mbcofsoe.fixture-clock');
  await expect(root).toHaveAttribute('viewBox', '0 0 10 10');
  await expect(root).toHaveAttribute('preserveAspectRatio', 'none');
  await expect(root.locator(':scope > circle')).toHaveCount(12);
  await expect(root.locator(':scope > line')).toHaveCount(3);
  await expect(root.locator(':scope > line.s')).toHaveClass('s animate elastic');
  await expect(root.locator(':scope > line.s')).toHaveAttribute('stroke-width', '0.05');
  await expect(root.locator(':scope > circle').nth(3)).toHaveAttribute('fill', '#86b300');
  await expect(root.locator(':scope > line').nth(1)).toHaveAttribute('stroke', '#676767');
  await expect(root.locator(':scope > line').nth(2)).toHaveAttribute('stroke', '#86b300');
  expect(await root.evaluate(element => getComputedStyle(element).display)).toBe('block');

  const clock24 = page.locator('svg.mbcofsoe.fixture-clock-24');
  await expect(clock24.locator(':scope > text')).toHaveCount(24);
  await expect(clock24.locator(':scope > text').first()).toHaveText('24');
  await expect(clock24.locator(':scope > text').nth(15)).toHaveAttribute('font-weight', 'bold');
  await expect(clock24.locator(':scope > text').nth(15)).toHaveAttribute('fill', '#86b300');
  await expect(clock24.locator(':scope > line.s')).toHaveClass('s animate easeOut');
  expect(await clock24.locator(':scope > text').evaluateAll(nodes => nodes.every(node => node.getAttribute('opacity') === '1'))).toBe(true);

  await page.evaluate(async () => {
    const theme = await import('/_content/ActivityPub.Misskey.Blazor/js/theme.js');
    if (!theme.applyTheme({
      bg: 'rgb(20, 20, 20)',
      panel: 'rgb(30, 30, 30)',
      popup: 'rgb(40, 40, 40)',
      fg: 'rgb(230, 230, 230)',
      accent: 'rgb(255, 100, 40)',
    }, 'dark', false, 'analog-dark')) throw new Error('theme rejected');
  });
  await expect(root.locator(':scope > line.s')).toHaveAttribute('stroke', 'rgba(255, 255, 255, 0.5)');
  await expect(root.locator(':scope > line').nth(1)).toHaveAttribute('stroke', '#e6e6e6');
  await expect(root.locator(':scope > line').nth(2)).toHaveAttribute('stroke', '#ff6428');
  await expect(root.locator(':scope > circle').nth(3)).toHaveAttribute('fill', '#ff6428');
  await root.screenshot({ path: `artifacts/analog-clock-parity-${testInfo.project.name}.png` });
});

test('MkAnalogClock replaces its exact branch and disposes timers on enhanced navigation', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/__test/components/analog-clock');
  const root = page.locator('svg.mbcofsoe.fixture-clock');
  await expect(root.locator(':scope > circle')).toHaveCount(12);
  await page.locator('#toggle-clock').click();
  await expect(root.locator(':scope > circle')).toHaveCount(0);
  await expect(root.locator(':scope > text')).toHaveCount(24);
  await expect(root.locator(':scope > line.s')).toHaveClass('s');
  await page.locator('#leave-clock').click();
  await expect(page).toHaveURL(/\/__test\/components\/time$/);
  await page.waitForTimeout(1200);
  expect(errors).toEqual([]);
});

test('MkAnalogClock preserves the 59-to-zero second-hand correction and reduced-motion override', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/__test/components/analog-clock');
  const root = page.locator('svg.mbcofsoe.fixture-clock');
  const secondHand = root.locator(':scope > line.s');
  await expect(secondHand).toHaveClass('s animate elastic');
  await expect(secondHand).toHaveAttribute('stroke', 'rgba(0, 0, 0, 0.3)');
  // The server render uses CSS variables for the minute hand; the normalized color proves
  // the pinned clock module is attached before controlled time is advanced.
  await expect(root.locator(':scope > line').nth(1)).toHaveAttribute('stroke', '#676767');
  await expect.poll(async () => Number.parseFloat(
    await secondHand.evaluate(element => getComputedStyle(element).transitionDuration),
  )).toBeLessThanOrEqual(0.000001);

  await page.evaluate(() => {
    globalThis.__misskeyAnalogNow = Date.UTC(2026, 7, 4, 15, 30, 59);
  });
  const rotation = () => secondHand.evaluate(element => {
    const match = element.getAttribute('style')?.match(/rotateZ\((-?[\d.]+)rad\)/);
    return match === undefined || match === null ? Number.NaN : Number(match[1]);
  });
  await expect.poll(
    rotation,
    { timeout: 3000 },
  ).toBeCloseTo(Math.PI * 59 / 30, 4);

  await secondHand.evaluate(element => {
    globalThis.__misskeyAnalogFrames = [];
    globalThis.__misskeyAnalogObserver = new MutationObserver(() => {
      const match = element.getAttribute('style')?.match(/rotateZ\((-?[\d.]+)rad\)/);
      globalThis.__misskeyAnalogFrames.push({
        className: element.getAttribute('class') ?? '',
        rotation: match === undefined || match === null ? Number.NaN : Number(match[1]),
      });
    });
    globalThis.__misskeyAnalogObserver.observe(element, {
      attributes: true,
      attributeFilter: ['class', 'style'],
    });
  });

  await page.evaluate(() => {
    globalThis.__misskeyAnalogNow = Date.UTC(2026, 7, 4, 15, 31, 0);
  });
  await page.waitForTimeout(3500);
  const frames = await page.evaluate(() => {
    globalThis.__misskeyAnalogObserver.disconnect();
    return globalThis.__misskeyAnalogFrames;
  });
  const roundIndex = frames.findIndex(frame => Math.abs(frame.rotation - (Math.PI * 2)) < 0.00005);
  // MutationObserver may coalesce the class and style mutations into one record delivery.
  // The observable contract is still exact: reach 2π, reset to zero with animation disabled,
  // then restore the configured animation class while remaining at zero.
  const zeroIndex = frames.findIndex((frame, index) =>
    index > roundIndex && frame.className === 's' && frame.rotation === 0);
  const enabledIndex = frames.findIndex((frame, index) =>
    index > zeroIndex && frame.className === 's animate elastic');
  const diagnostics = JSON.stringify(frames);
  expect(roundIndex, diagnostics).toBeGreaterThanOrEqual(0);
  expect(zeroIndex, diagnostics).toBeGreaterThan(roundIndex);
  expect(enabledIndex, diagnostics).toBeGreaterThan(zeroIndex);
});

declare global {
  // This controlled browser time changes only Date construction; real timers and the Blazor
  // transport remain untouched so lifecycle behavior stays representative.
  var __misskeyAnalogNow: number;
  var __misskeyAnalogFrames: Array<{ className: string; rotation: number }>;
  var __misskeyAnalogObserver: MutationObserver;
}
