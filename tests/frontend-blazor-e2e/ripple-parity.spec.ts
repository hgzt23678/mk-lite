import { expect, test } from '@playwright/test';

test('MkRipple preserves SVG motion, high stacking and the 1100ms lifetime', async ({ page }) => {
  await page.goto('/__test/components/ripple');
  await page.locator('#show-ripple').click();

  const ripple = page.locator('.vswabwbm');
  await expect(ripple).toHaveCount(1);
  await expect(ripple).toHaveAttribute('aria-hidden', 'true');
  await expect(ripple.locator(':scope > svg > circle')).toHaveCount(1);
  await expect(ripple.locator(':scope > svg > g > circle')).toHaveCount(12);
  await expect(ripple.locator('animate')).toHaveCount(38);

  const contract = await ripple.evaluate(element => {
    const style = getComputedStyle(element);
    const ringAnimations = [...element.querySelectorAll(':scope > svg > circle > animate')]
      .map(animation => ({
        name: animation.getAttribute('attributeName'),
        duration: animation.getAttribute('dur'),
        values: animation.getAttribute('values'),
        splines: animation.getAttribute('keySplines'),
      }));
    const particle = element.querySelector(':scope > svg > g > circle')!;
    return {
      top: style.top,
      left: style.left,
      width: style.width,
      height: style.height,
      position: style.position,
      pointerEvents: style.pointerEvents,
      zIndex: Number.parseInt(style.zIndex, 10),
      stroke: getComputedStyle(element.querySelector(':scope > svg > circle')!).stroke,
      ringAnimations,
      particleFill: particle.getAttribute('fill'),
    };
  });

  expect(contract).toMatchObject({
    top: '56px',
    left: '96px',
    width: '128px',
    height: '128px',
    position: 'fixed',
    pointerEvents: 'none',
    ringAnimations: [
      { name: 'r', duration: '0.5s', values: '4; 32', splines: '0.165, 0.84, 0.44, 1' },
      { name: 'stroke-width', duration: '0.5s', values: '16; 0', splines: '0.3, 0.61, 0.355, 1' },
    ],
  });
  expect(contract.zIndex).toBeGreaterThan(3_000_000);
  expect(['#FF1493', '#00FFFF', '#FFE202']).toContain(contract.particleFill);

  await page.waitForTimeout(850);
  await expect(ripple).toHaveCount(1);
  await expect(ripple).toHaveCount(0, { timeout: 700 });
  await expect(page.locator('#ripple-end-count')).toHaveText('1');
});

test('MkRipple suppresses decorative motion for reduced-motion users', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/__test/components/ripple');
  await page.locator('#show-ripple').click();

  await expect(page.locator('#ripple-end-count')).toHaveText('1');
  await expect(page.locator('.vswabwbm')).toHaveCount(0);
});
