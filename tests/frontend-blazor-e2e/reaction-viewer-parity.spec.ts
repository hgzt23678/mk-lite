import { expect, test } from '@playwright/test';

test('reaction viewer preserves v12 display tooltip and toggle behavior', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  const reset = await page.request.post('/__test/reset-reaction');
  expect(reset.status()).toBe(204);
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');

  const viewer = page.locator('.tkcbzcuz.qtqtichx footer.footer > .tdflqwzn');
  await expect(viewer).toHaveClass(/isMe/);
  const reactions = viewer.locator(':scope > button.hkzvhatu._button');
  await expect(reactions).toHaveCount(2);
  await expect(reactions.nth(0)).toHaveClass(/canToggle/);
  await expect(reactions.nth(0).locator(':scope > .count')).toHaveText('4');
  await expect(reactions.nth(1).locator(':scope > img.icon.mk-emoji.custom.normal')).toHaveAttribute(
    'src',
    '/static-assets/favicon.png');

  await reactions.nth(0).hover();
  const details = page.locator('body .buebdbiu > .bqxuuuey');
  await expect(details).toBeVisible();
  await expect(details.locator(':scope > .reaction > .icon')).toHaveCount(1);
  await expect(details.locator(':scope > .reaction > .name')).toHaveText('👍');
  await expect(details.locator(':scope > .users > .user')).toHaveCount(4);
  await expect(details.locator(':scope > .users > .user > .avatar')).toHaveCount(4);
  await expect(details.locator(':scope > .users > .user > .name')).toHaveCount(4);

  await reactions.nth(0).click();
  await expect(page.locator('.vswabwbm')).toHaveCount(1);
  await expect(details).toHaveCount(0);
  await expect(reactions.nth(0)).toHaveClass(/reacted/);
  const created = await (await page.request.get('/__test/reaction-state')).json() as {
    viewerReaction: string | null;
    reactionCalls: number;
    lastRemove: boolean | null;
  };
  expect(created).toMatchObject({ viewerReaction: '👍', reactionCalls: 1, lastRemove: false });

  await reactions.nth(0).click();
  await expect(reactions.nth(0)).not.toHaveClass(/reacted/);
  const removed = await (await page.request.get('/__test/reaction-state')).json() as {
    viewerReaction: string | null;
    reactionCalls: number;
    lastRemove: boolean | null;
  };
  expect(removed).toMatchObject({ viewerReaction: null, reactionCalls: 2, lastRemove: true });
  expect(failures).toEqual([]);
});
