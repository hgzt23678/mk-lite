import { expect, test } from '@playwright/test';

test('announcements page preserves the v12 cards, safe media and persistent read action', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));

  await page.request.post('/__test/reset-announcements');
  await page.goto('/__test/sign-in');
  await page.waitForURL('/');
  await page.goto('/announcements');

  const header = page.locator('.fdidabkb').first();
  await expect(header.locator(':scope > .titleContainer > i.fas.fa-broadcast-tower')).toHaveCount(1);
  await expect(header.locator(':scope > .titleContainer > .title > .title')).toHaveText('お知らせ');

  const cards = page.locator('.ruryvtyk > section._card.announcement');
  await expect(cards).toHaveCount(2);
  await expect(cards.nth(0).locator(':scope > ._title')).toHaveText('🆕 Browser unread announcement');
  await expect(cards.nth(1).locator(':scope > ._title')).toHaveText('Browser read announcement');
  await expect(cards.nth(0).locator(':scope > ._content')).toContainText('I');
  await expect(cards.nth(0).locator(':scope > ._content')).toContainText('Misskey');
  await expect(cards.nth(0).locator(':scope > ._content > img')).toHaveAttribute(
    'src',
    '/static-assets/favicon.png');
  await expect(cards.nth(1).locator(':scope > ._content > img')).toHaveCount(0);

  const background = await cards.nth(0).evaluate(element => getComputedStyle(element).backgroundColor);
  expect(background).not.toBe('rgba(0, 0, 0, 0)');
  expect(background).not.toBe('transparent');

  const readButton = cards.nth(0).locator(':scope > ._footer button');
  await expect(readButton).toHaveText(/わかった/);
  await readButton.click();
  await expect(cards.nth(0).locator(':scope > ._footer')).toHaveCount(0);
  await expect(cards.nth(0).locator(':scope > ._title')).toHaveText('Browser unread announcement');

  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/announcement-state')).json() as {
      markReadCalls: number;
      lastMarkedId: string | null;
    };
    return `${state.markReadCalls}:${state.lastMarkedId}`;
  }).toBe('1:9browserannouncement-unread');

  await page.reload();
  await expect(page.locator('.ruryvtyk > section._card.announcement').nth(0).locator(':scope > ._footer')).toHaveCount(0);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
