import { expect, test } from '@playwright/test';

test('MkNotification and toast preserve v12 layout, read persistence, tooltip and six-second motion', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));

  await page.goto('/__test/sign-in');
  await page.waitForURL('/');
  await page.goto('/__test/components/notification');
  const notification = page.locator(".qglefbjs[data-fixture='notification']");
  await expect(notification).toHaveClass(/reaction/);
  await expect(notification).toHaveClass(/max-width_500px/);
  await expect(notification).toHaveClass(/max-width_600px/);
  await expect(notification.locator(':scope > .head > .sub-icon.reaction > .mk-emoji')).toHaveAttribute('alt', ':party@.:');
  await expect(notification.locator(':scope > .head > .icon')).toHaveCSS('width', '42px');
  await expect(notification.locator(':scope > .head > .icon')).toHaveCSS('height', '42px');
  await expect(notification.locator(':scope > .head > .sub-icon.reaction > .mk-emoji')).toHaveCSS('width', '20px');
  await expect(notification.locator(':scope > .head > .sub-icon.reaction > .mk-emoji')).toHaveCSS('height', '20px');
  await expect(notification.locator(':scope > .tail > header > .name')).toHaveText('Alice');
  await expect(notification.locator(':scope > .tail > a.text')).toContainText('Browser notification fixture');

  await expect.poll(async () => {
    const response = await page.request.get('/__test/notification-state');
    return (await response.json()).markReadCalls as number;
  }).toBe(1);

  await notification.locator('.sub-icon.reaction > .mk-emoji').hover();
  const tooltip = page.locator('.beeadbfb');
  await expect(tooltip).toBeVisible({ timeout: 2_000 });
  await expect(tooltip.locator(':scope > .name')).toHaveText(':party:');

  await page.evaluate(() => {
    (window as any).__notificationToastPhases = [];
    const observer = new MutationObserver(() => {
      const body = document.querySelector(".mk-notification-toast[data-fixture='notification-toast'] > .notification") as HTMLElement | null;
      if (!body) return;
      (window as any).__notificationToastPhases.push(`${body.dataset.motionState ?? 'initial'}:${body.className}`);
    });
    observer.observe(document.body, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['class', 'data-motion-state'],
    });
    (window as any).__notificationToastObserver = observer;
  });
  const startedAt = Date.now();
  await page.locator('#show-notification-toast').click();
  const toast = page.locator(".mk-notification-toast[data-fixture='notification-toast']");
  const toastNotification = toast.locator(':scope > .notification._acrylic.qglefbjs');
  await expect(toastNotification).toHaveAttribute('data-motion-state', 'entered');
  const visual = await toast.evaluate(element => {
    const style = getComputedStyle(element);
    const notificationStyle = getComputedStyle(element.querySelector(':scope > .notification')!);
    return {
      position: style.position,
      left: style.left,
      width: style.width,
      top: style.top,
      pointerEvents: style.pointerEvents,
      zIndex: Number.parseInt(style.zIndex, 10),
      borderRadius: notificationStyle.borderRadius,
      overflow: notificationStyle.overflow,
    };
  });
  expect(visual).toMatchObject({
    position: 'fixed',
    left: '0px',
    width: '250px',
    top: '32px',
    pointerEvents: 'none',
    borderRadius: '8px',
    overflow: 'hidden',
  });
  expect(visual.zIndex).toBeGreaterThan(3_000_000);

  await expect(page.locator('#notification-toast-closed-count')).toHaveText('1', { timeout: 8_000 });
  expect(Date.now() - startedAt).toBeGreaterThanOrEqual(5_900);
  await expect(toast).toHaveCount(0);
  const phases = await page.evaluate(() => {
    (window as any).__notificationToastObserver.disconnect();
    return (window as any).__notificationToastPhases as string[];
  });
  expect(phases.some(value => value.includes('entering:') && value.includes('notification-toast-enter-active'))).toBeTruthy();
  expect(phases.some(value => value.startsWith('entered:'))).toBeTruthy();
  expect(phases.some(value => value.includes('leaving:') && value.includes('notification-toast-leave-to'))).toBeTruthy();
  expect(errors).toEqual([]);
});
