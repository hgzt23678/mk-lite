import { expect, test } from '@playwright/test';

test('MkSpacer preserves the pinned responsive margins on the real about page', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/about-misskey');

  const spacer = page.locator('._root_b6w6v_1').first();
  const content = spacer.locator(':scope > ._content_b6w6v_6');
  await expect(spacer).toBeVisible();
  await expect.poll(() => spacer.evaluate(element => getComputedStyle(element).paddingTop)).toBe('24px');
  await expect.poll(() => content.evaluate(element => getComputedStyle(element).maxWidth)).toBe('600px');
  expect(await content.evaluate(element => element.getBoundingClientRect().width)).toBeLessThanOrEqual(600);

  await page.setViewportSize({ width: 390, height: 844 });
  await expect.poll(() => spacer.evaluate(element => getComputedStyle(element).paddingTop)).toBe('20px');
  await expect(spacer).toHaveClass(/_root_b6w6v_1/);
  await expect(content).toHaveClass(/_content_b6w6v_6/);
});

test('MkSpacer uses the pinned mobile user-agent and explicit device override rules', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'userAgent', {
      configurable: true,
      get: () => 'Mozilla/5.0 (Linux; Android 13; Mobile) AppleWebKit/537.36',
    });
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');

  const result = await page.evaluate(async () => {
    const module = await import('/_content/ActivityPub.Misskey.Blazor/js/spacer.js');
    const element = document.createElement('div');
    element.style.width = '1000px';
    document.body.append(element);

    const observeOnce = (overriddenDeviceKind: string | null) => new Promise<string>((resolve, reject) => {
      let handle: { dispose(): void } | undefined;
      const receiver = {
        invokeMethodAsync(_method: string, _width: number, _viewportWidth: number, deviceKind: string) {
          resolve(deviceKind);
          queueMicrotask(() => handle?.dispose());
          return Promise.resolve();
        },
      };
      try {
        handle = module.observe(element, { overriddenDeviceKind }, receiver);
      } catch (error) {
        reject(error);
      }
    });

    const detected = await observeOnce(null);
    const overridden = await observeOnce('desktop');
    element.remove();
    return { detected, overridden };
  });

  expect(result).toEqual({ detected: 'smartphone', overridden: 'desktop' });
});
