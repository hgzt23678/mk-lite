import { expect, test } from '@playwright/test';

test('MkToast preserves pinned geometry, opaque acrylic surface and high stacking', async ({ page }) => {
  await page.goto('/__test/components/toast');
  await page.locator('#show-toast').click();

  const root = page.locator('.mk-toast[data-fixture="toast"]');
  const body = root.locator(':scope > .body._acrylic');
  await expect(body).toHaveAttribute('data-motion-state', 'entered');
  await expect(body.locator(':scope > .message')).toHaveText('Welcome back, Alice');
  await expect(body).not.toHaveClass(/toast-enter-active|toast-enter-from|toast-enter-to/);
  const contract = await body.evaluate(element => {
    const style = getComputedStyle(element);
    const color = style.backgroundColor.match(/[\d.]+/g)?.map(Number) ?? [];
    return {
      position: style.position,
      top: style.top,
      marginTop: style.marginTop,
      minWidth: style.minWidth,
      maxWidth: style.maxWidth,
      pointerEvents: style.pointerEvents,
      zIndex: Number.parseInt(style.zIndex, 10),
      backgroundAlpha: color.length === 4 ? color[3] : 1,
      padding: getComputedStyle(element.querySelector(':scope > .message')!).padding,
    };
  });
  expect(contract).toMatchObject({
    position: 'fixed',
    top: '0px',
    marginTop: '16px',
    minWidth: '300px',
    pointerEvents: 'none',
    padding: '16px 24px',
  });
  expect(contract.zIndex).toBeGreaterThan(3_000_000);
  expect(contract.backgroundAlpha).toBeGreaterThan(0);
});

test('MkToast runs the enter and leave lifecycle before emitting closed', async ({ page }) => {
  await page.goto('/__test/components/toast');
  await page.evaluate(() => {
    (window as any).__toastPhases = [];
    const observer = new MutationObserver(() => {
      const body = document.querySelector('.mk-toast > .body') as HTMLElement | null;
      if (!body) return;
      (window as any).__toastPhases.push(`${body.dataset.motionState ?? 'initial'}:${body.className}`);
    });
    observer.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'data-motion-state'] });
    (window as any).__toastObserver = observer;
  });

  await page.locator('#show-toast').click();
  await expect(page.locator('.mk-toast > .body')).toHaveAttribute('data-motion-state', 'entered');
  await expect(page.locator('#toast-closed-count')).toHaveText('1', { timeout: 6_000 });
  await expect(page.locator('.mk-toast > .body')).toHaveCount(0);
  const phases = await page.evaluate(() => {
    (window as any).__toastObserver.disconnect();
    return (window as any).__toastPhases as string[];
  });
  expect(phases.some(value => value.includes('entering:') && value.includes('toast-enter-active'))).toBeTruthy();
  expect(phases.some(value => value.startsWith('entered:'))).toBeTruthy();
  expect(phases.some(value => value.includes('leaving:') && value.includes('toast-leave-to'))).toBeTruthy();
});

test('MkToast disposes safely when its parent removes it mid-enter', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));

  await page.goto('/__test/components/toast');
  await page.locator('#show-toast').click();
  const body = page.locator('.mk-toast > .body');
  await expect(body).toHaveAttribute('data-motion-state', 'entering');
  await page.locator('#remove-toast').click();
  await expect(body).toHaveCount(0);
  await expect(page.locator('#show-toast')).toBeEnabled();
  expect(errors).toEqual([]);
});

test('MkToast honors reduced motion and early disposal without a circuit error', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/__test/components/toast');
  await page.locator('#show-toast').click();
  const body = page.locator('.mk-toast > .body');
  await expect(body).toHaveAttribute('data-motion-state', 'entered');
  await expect(body).not.toHaveClass(/toast-enter-active|toast-enter-from/);

  await page.locator('#remove-toast').click();
  await expect(body).toHaveCount(0);
  await expect(page.locator('#show-toast')).toBeEnabled();
  expect(errors).toEqual([]);
});
