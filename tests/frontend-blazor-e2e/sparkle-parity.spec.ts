import { expect, test } from '@playwright/test';

test('MkSparkle preserves the pinned SVG particle motion and resize contract', async ({ page }) => {
  await page.goto('/__test/components/sparkle');
  const root = page.locator('.mk-sparkle[data-fixture="sparkle"]');
  const svg = root.locator(':scope > svg').first();
  await expect(svg).toBeVisible();
  const initial = await svg.evaluate(element => {
    const path = element.querySelector('path')!;
    const animations = [...element.querySelectorAll('animateTransform')];
    return {
      rootPosition: getComputedStyle(element.parentElement!).position,
      position: getComputedStyle(element).position,
      top: getComputedStyle(element).top,
      left: getComputedStyle(element).left,
      pointerEvents: getComputedStyle(element).pointerEvents,
      width: Number(element.getAttribute('width')),
      height: Number(element.getAttribute('height')),
      transform: path.getAttribute('transform'),
      fill: path.getAttribute('fill'),
      types: animations.map(item => item.getAttribute('type')),
      durations: animations.map(item => item.getAttribute('dur')),
      repeat: animations.map(item => item.getAttribute('repeatCount')),
    };
  });
  expect(initial).toMatchObject({
    rootPosition: 'relative',
    position: 'absolute',
    top: '-32px',
    left: '-32px',
    pointerEvents: 'none',
    types: ['rotate', 'scale'],
    repeat: ['1', '1'],
  });
  expect(['#FF1493', '#00FFFF', '#FFE202']).toContain(initial.fill);
  expect(initial.width).toBeGreaterThan(64);
  expect(initial.height).toBeGreaterThan(64);
  expect(initial.durations.every(value => /^1\d{3}(?:\.\d+)?ms$|^2000ms$/.test(value ?? ''))).toBeTruthy();

  await page.locator('#expand-sparkle').click();
  await expect(root.locator(':scope > span')).toContainText('much longer title');
  await expect.poll(async () => Number(await root.locator(':scope > svg').first().getAttribute('width')))
    .toBeGreaterThan(initial.width);
});

test('MkSparkle suppresses particles for reduced motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/__test/components/sparkle');
  await expect(page.locator('.mk-sparkle > span')).toHaveText('Updated');
  await page.waitForTimeout(1_100);
  await expect(page.locator('.mk-sparkle > svg')).toHaveCount(0);
});

test('MkSparkle releases observers and timers on early removal', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/__test/components/sparkle');
  await expect(page.locator('.mk-sparkle > svg').first()).toBeVisible();
  await page.locator('#remove-sparkle').click();
  await expect(page.locator('.mk-sparkle')).toHaveCount(0);
  await page.waitForTimeout(1_100);
  await expect(page.locator('#remove-sparkle')).toBeEnabled();
  expect(errors).toEqual([]);
});
