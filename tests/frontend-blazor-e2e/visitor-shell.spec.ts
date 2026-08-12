import { expect, test, type Page } from '@playwright/test';

const browserFailures = new WeakMap<Page, string[]>();

test.beforeEach(async ({ page }) => {
  const failures: string[] = [];
  browserFailures.set(page, failures);
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}:${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
  });
});

test.afterEach(async ({ page }) => {
  expect(browserFailures.get(page) ?? [], 'visitor shell emitted a browser or HTTP failure').toEqual([]);
});

async function waitForInteractiveVisitor(page: Page): Promise<void> {
  const shell = page.locator('body > .mk-app');
  await expect(shell).toHaveCount(1);
  await expect.poll(async () => shell.evaluate(element => !(element as HTMLElement).inert)).toBe(true);
  await expect.poll(async () => shell.locator(':scope > .main > .contents > .header').evaluate(
    element => element.children.length)).toBeGreaterThan(0);
}

test('visitor keeps the pinned 1280px shell and 1300px header breakpoints', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 800 });
  await page.goto('/about-misskey');
  await waitForInteractiveVisitor(page);

  const shell = page.locator('body > .mk-app');
  await expect(shell.locator(':scope > .side')).toHaveCount(0);
  await expect(shell.locator(':scope > .main > .banner.rwqkcmrc')).toHaveCount(1);
  await expect(shell.locator(':scope > .main > .banner .wrapper > h1.full')).toHaveCount(0);
  await expect(shell.locator(':scope > .main > .contents > .header.sqxihjet > .narrow')).toHaveCount(1);
  await expect(shell.locator('.header .narrow > .title')).toHaveText('Misskeyについて');

  await page.setViewportSize({ width: 1440, height: 900 });
  await expect(shell.locator(':scope > .side > .kanban.rwqkcmrc')).toHaveCount(1);
  await expect(shell.locator(':scope > .main > .banner')).toHaveCount(0);
  await expect(shell.locator('.side .wrapper > h1.full')).toHaveCount(1);
  await expect(shell.locator('.side .wrapper > .about > .desc')).toHaveText('Opaque background regression host');
  await expect(shell.locator('.side .wrapper > .action > button')).toHaveCount(2);
  await expect(shell.locator('.side .announcements > .list > .item')).toHaveCount(1);
  await expect(shell.locator('.side .announcements .item > .title')).toHaveText('Scheduled maintenance');
  await expect(shell.locator('.side .announcements .item > .content > img'))
    .toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(shell.locator('.header.sqxihjet > .narrow')).toHaveCount(1);
  const sideGeometry = await shell.locator(':scope > .side > .kanban').evaluate(element => {
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return { position: style.position, width: rect.width, height: rect.height, left: rect.left, top: rect.top };
  });
  expect(sideGeometry).toEqual({ position: 'fixed', width: 500, height: 900, left: 0, top: 0 });

  await page.setViewportSize({ width: 1920, height: 1080 });
  await expect(shell.locator('.header.sqxihjet > .wide > .content')).toHaveCount(1);
  await expect(shell.locator('.header .wide > .content > a.link')).toHaveCount(4);
  await expect(shell.locator('.header .wide > .content > .page.active.link')).toContainText('Misskeyについて');
  await expect(shell.locator('.contents > .powered-by > b')).toHaveText('127.0.0.1');

  const rootAlpha = await shell.evaluate(element => {
    const context = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
    if (context === null) return null;
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(rootAlpha, 'visitor shell must keep the theme background opaque').toBe(255);
});

test('narrow visitor tray reproduces Vue enter/leave cancellation, focus, Escape and touch', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/about-misskey');
  await waitForInteractiveVisitor(page);

  const shell = page.locator('body > .mk-app');
  const source = shell.locator('.header.sqxihjet > .narrow > button.menu');
  const tray = shell.locator(':scope > .menu');
  const background = shell.locator(':scope > .menu-back');

  await source.click();
  await expect(tray).toHaveCount(1);
  await expect(background).toHaveCount(1);
  await expect(tray).toHaveAttribute('data-motion-state', 'entered');
  await expect(tray.locator(':scope > a.link').first()).toBeFocused();
  await expect(source).toHaveAttribute('aria-expanded', 'true');
  const traySurface = await tray.evaluate(element => {
    const context = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
    if (context === null) return { alpha: null, width: 0, height: 0, zIndex: '' };
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    const rect = element.getBoundingClientRect();
    return {
      alpha: context.getImageData(0, 0, 1, 1).data[3],
      width: rect.width,
      height: rect.height,
      zIndex: getComputedStyle(element).zIndex,
    };
  });
  expect(traySurface.alpha).toBe(255);
  expect(traySurface.width).toBeCloseTo(240, 1);
  // WebKit exposes device-pixel rounding here (843.984375 CSS px at a nominal 844px viewport).
  expect(traySurface.height).toBeCloseTo(844, 1);
  expect(traySurface.zIndex).toBe('1001');

  await page.keyboard.press('Escape');
  await expect(tray).toHaveCount(0);
  await expect(background).toHaveCount(0);
  await expect(source).toBeFocused();
  await expect(source).toHaveAttribute('aria-expanded', 'false');

  // Cancel the two-rAF enter phase immediately. A stale enter callback must not leave a drawer
  // behind or remove a later drawer generation.
  await source.click();
  await page.keyboard.press('Escape');
  await expect(tray).toHaveCount(0);
  await expect(source).toBeFocused();
  await source.click();
  await expect(tray).toHaveAttribute('data-motion-state', 'entered');

  // Firefox headless does not expose the TouchEvent constructor, but dispatching the native
  // event type still exercises the passive touchstart listener used by the shell.
  await background.evaluate(element => element.dispatchEvent(new Event('touchstart', { bubbles: true })));
  await expect(tray).toHaveCount(0);
  await expect(source).toBeFocused();
});

test('visitor root retains DesignB root structure without non-root chrome', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  const shell = page.locator('body > .mk-app');
  await expect(shell.locator(':scope > .github-corner')).toHaveCount(1);
  await expect(shell.locator(':scope > .side')).toHaveCount(0);
  await expect(shell.locator(':scope > .main > .banner')).toHaveCount(0);
  await expect(shell.locator(':scope > .main > .contents > .header')).toHaveCount(0);
  await expect(shell.locator(':scope > .main > .contents > main > .rsqzvsbo')).toHaveCount(1);
});
