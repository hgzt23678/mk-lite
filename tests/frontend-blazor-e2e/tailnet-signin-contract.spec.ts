import { expect, test } from '@playwright/test';

const externalBaseURL = process.env.PLAYWRIGHT_BASE_URL?.replace(/\/$/, '');
test.skip(externalBaseURL === undefined, 'PLAYWRIGHT_BASE_URL must identify the Tailnet HTTPS origin.');
test.setTimeout(60_000);

test('Tailnet exposes the Blazor Misskey v12 credential contract', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 390, height: 844 });

  const response = await page.goto('/auth/login?returnUrl=%2Fapp%2F');
  expect(response?.status()).toBe(200);
  const dialog = page.locator('body > .qzhlnise.dialog[role="dialog"]');
  await expect.poll(async () => dialog.getAttribute('data-motion-state')).toBe('entered');
  const window = dialog.locator(':scope > .content > .ebkgoccj._narrow_');
  await expect(window).toBeVisible();
  await expect(window).toHaveCSS('width', '358px');
  await expect(window).toHaveCSS('height', '400px');

  const form = dialog.locator('form.eppvobhk._monolithic_');
  await expect(form).toHaveAttribute('data-auth-mode', 'local');
  await expect(form).toHaveAttribute('action', '/api/signin');
  await expect(form.locator('input[name="username"]')).toHaveCount(1);
  await expect(form.locator('input[name="password"]')).toHaveCount(1);
  await expect(form.locator('input[name="__RequestVerificationToken"]')).toHaveCount(0);
  await expect(form.locator('input[name="username"]')).toBeFocused();

  const surfaces = await window.evaluate(element => {
    const body = element.querySelector(':scope > .body');
    if (!(body instanceof HTMLElement)) throw new Error('MkSignin modal body is incomplete');
    return getComputedStyle(body).backgroundColor;
  });
  expect(surfaces, 'MkSignin panel must not regress to a transparent background').toBe('rgb(255, 255, 255)');

  const scripts = await page.locator('script[src]').evaluateAll(elements =>
    elements.map(element => new URL((element as HTMLScriptElement).src).pathname));
  expect(scripts.some(path => /(?:vue|vite|keycloak)/i.test(path))).toBe(false);
});
