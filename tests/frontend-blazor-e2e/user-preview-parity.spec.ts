import { expect, test } from '@playwright/test';

test.use({ serviceWorkers: 'block', reducedMotion: 'no-preference' });

type PreviewState = {
  readCalls: number;
  followCalls: number;
  unfollowCalls: number;
  lastQuery: string | null;
  isFollowing: boolean;
  hasPendingFollowRequestFromYou: boolean;
  activeSubscriptions: number;
  disposedSubscriptions: number;
};

test.beforeEach(async ({ page }) => {
  expect((await page.request.post('/__test/reset-user-preview')).status()).toBe(204);
  expect((await page.request.post('/__test/reset-diagnostics')).status()).toBe(204);
  await page.goto('/__test/components/user-preview');
  await expect(page.locator('[data-contract="mk-user-preview"]')).toBeVisible();
  await expect(page.locator('[data-preview-source="alice"]')).toHaveAttribute(
    'data-user-preview-ready',
    'true',
  );
});

test('preserves the 500ms directive, detailed DOM, opaque theme, placement, movement and cancellable 300ms motion', async ({ page }) => {
  const failures = captureFailures(page);
  const source = page.locator('[data-preview-source="alice"]');
  const popup = page.locator('[data-user-preview-popup]');

  await source.hover();
  await page.waitForTimeout(350);
  expect(await popup.count()).toBe(0);
  await expect(popup).toBeVisible({ timeout: 2_000 });
  await expect(popup).toHaveClass(/popup-enter-active/);
  const motionStyle = await popup.evaluate((element) => ({
    transitionDuration: getComputedStyle(element).transitionDuration,
    transformOrigin: getComputedStyle(element).transformOrigin,
  }));
  expect(motionStyle.transitionDuration.split(',').map(value => value.trim())).toEqual(['0.3s', '0.3s']);
  expect(motionStyle.transformOrigin).toContain('150px 0px');
  await expect(popup).toHaveAttribute('data-preview-load-state', 'loaded');
  await expect(popup.locator(':scope > .info > .banner > .followed')).toHaveText('フォローされています');
  await expect(popup.locator('.title > .name')).toContainText('Alice');
  await expect(popup.locator('.title .mk-acct')).toHaveText('@alice@bücher.example');
  await expect(popup.locator('.status > div:nth-child(1) > span')).toHaveText('73');
  await expect(popup.locator('.status > div:nth-child(2) > span')).toHaveText('19');
  await expect(popup.locator('.status > div:nth-child(3) > span')).toHaveText('31');
  await expect(popup.locator('.avatar > img.inner')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(popup.locator('.banner')).toHaveCSS('background-image', /favicon\.png/);
  await expect(popup).toHaveAttribute('data-preview-state', 'shown');
  await expect.poll(() => popup.evaluate(element => getComputedStyle(element).transform)).toBe('none');

  const sourceBox = await source.boundingBox();
  const popupBox = await popup.boundingBox();
  expect(sourceBox).not.toBeNull();
  expect(popupBox).not.toBeNull();
  expect(Math.abs(popupBox!.x - (sourceBox!.x + sourceBox!.width / 2 - 150))).toBeLessThan(2);
  expect(Math.abs(popupBox!.y - (sourceBox!.y + sourceBox!.height))).toBeLessThan(2);
  const style = await popup.evaluate((element) => ({
    backgroundColor: getComputedStyle(element).backgroundColor,
    width: getComputedStyle(element).width,
    zIndex: Number.parseInt(getComputedStyle(element).zIndex, 10),
  }));
  expect(style.backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
  expect(style.backgroundColor).not.toBe('transparent');
  expect(style.width).toBe('300px');
  expect(style.zIndex).toBeGreaterThan(2_000_000);
  const avatarStyle = await popup.locator('.avatar').evaluate(element => ({
    position: getComputedStyle(element).position,
    top: getComputedStyle(element).top,
    left: getComputedStyle(element).left,
    width: getComputedStyle(element).width,
    height: getComputedStyle(element).height,
  }));
  expect(avatarStyle).toEqual({ position: 'absolute', top: '62px', left: '13px', width: '58px', height: '58px' });
  const followStyle = await popup.locator('.koudoku-button').evaluate(element => ({
    position: getComputedStyle(element).position,
    top: getComputedStyle(element).top,
    right: getComputedStyle(element).right,
  }));
  expect(followStyle).toEqual({ position: 'absolute', top: '8px', right: '8px' });

  await popup.hover();
  const beforeMove = await popup.boundingBox();
  await source.evaluate((element) => {
    (element as HTMLElement).style.left = '340px';
  });
  await expect.poll(async () => (await popup.boundingBox())?.x ?? 0).toBeGreaterThan(beforeMove!.x + 200);
  await popup.hover();
  await page.waitForTimeout(550);
  await expect(popup).toBeVisible();

  await popup.dispatchEvent('mouseleave');
  await page.waitForTimeout(520);
  await expect(popup).toHaveAttribute('data-preview-state', 'leaving');
  await expect(popup).toHaveClass(/popup-leave-active/);
  await popup.dispatchEvent('mouseover');
  await expect(popup).toHaveAttribute('data-preview-state', 'shown');
  await expect(popup).not.toHaveClass(/popup-leave-to/);

  await page.mouse.move(0, 0);
  await page.waitForTimeout(520);
  await expect(popup).toHaveAttribute('data-preview-state', 'leaving');
  await expect(popup).toHaveCount(0, { timeout: 1_000 });
  // The durable relationship subscription takes its cursor before refreshing the
  // snapshot, so a commit racing preview creation cannot be missed.
  expect(await readState(page)).toMatchObject({ readCalls: 2, lastQuery: 'alice-id' });
  await assertClean(page, failures);
});

test('keeps one preview during competing sources and closes on source removal and click', async ({ page }) => {
  const failures = captureFailures(page);
  const alice = page.locator('[data-preview-source="alice"]');
  const bob = page.locator('[data-preview-source="bob"]');
  const popup = page.locator('[data-user-preview-popup]');

  await alice.hover();
  await page.waitForTimeout(250);
  await bob.hover();
  await expect(popup).toBeVisible({ timeout: 2_000 });
  await expect(popup).toHaveAttribute('data-preview-query', 'bob-id');
  await expect(popup).toHaveCount(1);
  await expect(popup.locator('.avatar > img.inner')).toHaveAttribute('src', '/static-assets/user-unknown.png');
  await expect(popup.locator('.banner')).not.toHaveAttribute('style', /tracker\.invalid/);
  expect(await popup.textContent()).not.toContain('tracker.invalid');

  await alice.hover();
  await expect(popup).toHaveAttribute('data-preview-query', 'alice-id', { timeout: 2_000 });
  await expect(popup).toHaveCount(1);
  await page.locator('[data-action="remove"]').click();
  await expect(alice).toHaveCount(0);
  await expect(popup).toHaveCount(0, { timeout: 1_500 });

  await page.locator('[data-action="restore"]').click();
  const restored = page.locator('[data-preview-source="alice"]');
  await expect(restored).toHaveAttribute('data-user-preview-ready', 'true');
  await restored.hover();
  await expect(popup).toBeVisible({ timeout: 2_000 });
  await restored.click();
  await expect(popup).toHaveCount(0, { timeout: 1_000 });
  await assertClean(page, failures);
});

test('supports focus, Escape, touch and the real follow/unfollow presentation boundary', async ({ page }) => {
  const source = page.locator('[data-preview-source="alice"]');
  const popup = page.locator('[data-user-preview-popup]');

  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/u);
  await page.goto('/__test/components/user-preview');
  await expect(source).toHaveAttribute('data-user-preview-ready', 'true');
  await page.waitForTimeout(100);
  expect((await page.request.post('/__test/reset-diagnostics')).status()).toBe(204);
  const failures = captureFailures(page);

  await source.focus();
  await expect(popup).toBeVisible({ timeout: 2_000 });
  await page.keyboard.press('Escape');
  await expect(popup).toHaveCount(0, { timeout: 1_000 });
  await source.evaluate((element: HTMLElement) => element.blur());

  await source.evaluate((element) => {
    element.dispatchEvent(new Event('touchstart', { bubbles: true, cancelable: true }));
  });
  await expect(popup).toBeVisible({ timeout: 2_000 });
  await source.evaluate((element) => {
    element.dispatchEvent(new Event('touchend', { bubbles: true, cancelable: true }));
  });
  await expect(popup).toHaveCount(0, { timeout: 1_500 });

  await page.mouse.move(0, 0);
  await page.waitForTimeout(800);
  await source.hover();
  await expect(popup).toBeVisible({ timeout: 2_000 });
  const follow = popup.locator('button.kpoogebi.koudoku-button');
  await follow.click();
  await expect(follow).toHaveClass(/active/);
  await expect(follow.locator('i.fa-minus')).toHaveCount(1);
  expect(await readState(page)).toMatchObject({ followCalls: 1, isFollowing: true });

  await follow.click();
  const dialog = page.locator('.qzhlnise.dialog .mk-dialog');
  await expect(dialog).toBeVisible();
  const dialogBackground = await dialog.evaluate(element => getComputedStyle(element).backgroundColor);
  expect(dialogBackground).not.toBe('rgba(0, 0, 0, 0)');
  expect(dialogBackground).not.toBe('transparent');
  await dialog.locator('.buttons > button.primary').click();
  await expect(dialog).toHaveCount(0, { timeout: 1_000 });
  await expect(follow).not.toHaveClass(/active/);
  expect(await readState(page)).toMatchObject({ unfollowCalls: 1, isFollowing: false });
  await assertClean(page, failures);
});

test('updates an open follow button from another UI signal without losing focus and disposes its stream', async ({ page, context }) => {
  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/u);
  await page.goto('/__test/components/user-preview');
  const source = page.locator('[data-preview-source="alice"]');
  const popup = page.locator('[data-user-preview-popup]');
  await expect(source).toHaveAttribute('data-user-preview-ready', 'true');
  await page.waitForTimeout(100);
  expect((await page.request.post('/__test/reset-diagnostics')).status()).toBe(204);
  const failures = captureFailures(page);
  await source.hover();
  await expect(popup).toBeVisible({ timeout: 2_000 });
  const follow = popup.locator('button.kpoogebi.koudoku-button');
  await expect(follow).not.toHaveClass(/active/);
  await follow.focus();
  await expect(follow).toBeFocused();
  await expect.poll(async () => (await readState(page)).activeSubscriptions).toBeGreaterThan(0);

  expect((await context.request.post('/__test/user-preview-external/follow')).status()).toBe(204);

  await expect(follow).toHaveClass(/active/);
  await expect(follow.locator('i.fa-minus')).toHaveCount(1);
  await expect(follow).toBeEnabled();
  await expect(follow).toBeFocused();

  await follow.evaluate(element => (element as HTMLElement).blur());
  await page.mouse.move(0, 0);
  await expect(popup).toHaveCount(0, { timeout: 2_000 });
  await expect.poll(async () => (await readState(page)).disposedSubscriptions).toBeGreaterThan(0);
  await assertClean(page, failures);
});

test('recovers a pointer already over the SSR source when the directive module hydrates', async ({ page }) => {
  const failures = captureFailures(page);
  let releaseModule!: () => void;
  const moduleGate = new Promise<void>((resolve) => {
    releaseModule = resolve;
  });
  let intercepted = false;
  await page.route('**/*', async (route) => {
    if (/\/js\/user-preview(?:\.[a-z0-9]+)?\.js$/u.test(new URL(route.request().url()).pathname)) {
      intercepted = true;
      await moduleGate;
    }
    await route.continue();
  });

  await page.reload();
  const source = page.locator('[data-preview-source="alice"]');
  await expect(source).toBeVisible();
  await source.hover();
  await expect.poll(() => intercepted).toBe(true);
  await expect(source).not.toHaveAttribute('data-user-preview-ready', 'true');
  releaseModule();
  await expect(source).toHaveAttribute('data-user-preview-ready', 'true');
  await expect(page.locator('[data-user-preview-popup]')).toBeVisible({ timeout: 2_000 });
  await assertClean(page, failures);
});

function captureFailures(page: import('@playwright/test').Page): string[] {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}:${error.message}`));
  return failures;
}

async function readState(page: import('@playwright/test').Page): Promise<PreviewState> {
  const response = await page.request.get('/__test/user-preview-state');
  expect(response.ok()).toBeTruthy();
  return await response.json() as PreviewState;
}

async function assertClean(page: import('@playwright/test').Page, failures: string[]): Promise<void> {
  const response = await page.request.get('/__test/diagnostics');
  expect(response.ok()).toBeTruthy();
  expect((await response.json()).unhandledExceptions).toEqual([]);
  expect(failures).toEqual([]);
}
