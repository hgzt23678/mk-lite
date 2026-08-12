import { expect, test } from '@playwright/test';

test('MkNotifications preserves the v12 list, full-note branch and durable stream dedupe', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));

  await page.goto('/__test/sign-in');
  await page.waitForURL('/');
  await page.goto('/__test/components/notifications');

  const list = page.locator("[data-fixture='notifications'] > .sqadhkmv.elsfgstc.noGap");
  await expect(list).toBeVisible();
  // The pinned fixture intentionally has both direct and mention full-note projections;
  // the remaining reaction is rendered through the standard notification branch.
  await expect(list.locator(':scope > .tkcbzcuz')).toHaveCount(2);
  await expect(list.locator(':scope > .qglefbjs._panel.notification')).toHaveCount(1);
  await expect(list.locator(':scope > .tkcbzcuz')).toContainText([
    'Direct notification fixture',
    'Mention notification fixture'
  ]);
  const background = await list.evaluate(element => getComputedStyle(element).backgroundColor);
  expect(background).not.toBe('rgba(0, 0, 0, 0)');
  expect(background).not.toBe('transparent');

  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/notification-state')).json() as {
      activeSubscriptions: number;
    };
    return state.activeSubscriptions;
  }).toBe(1);
  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/notification-state')).json() as {
      markReadCalls: number;
    };
    return state.markReadCalls;
  }).toBeGreaterThanOrEqual(1);
  const before = await (await page.request.get('/__test/notification-state')).json() as {
    markReadCalls: number;
  };

  await page.request.post('/__test/notification-stream/duplicate');
  await expect(list.locator(':scope > .tkcbzcuz')).toHaveCount(2);
  await page.request.post('/__test/notification-stream/new');
  await expect(list.locator(':scope > .tkcbzcuz')).toHaveCount(3);
  await expect(list).toContainText('Stream notification fixture');
  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/notification-state')).json() as {
      markReadCalls: number;
    };
    return state.markReadCalls - before.markReadCalls;
  }).toBe(2);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
