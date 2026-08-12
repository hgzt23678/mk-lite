import { expect, test } from '@playwright/test';

test('notifications page preserves the pinned v12 tabs, filters, note projections and read state', async ({ page }) => {
  test.setTimeout(60_000);
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));

  await page.goto('/__test/sign-in');
  await page.waitForURL('/');
  await page.goto('/my/notifications');

  const header = page.locator('.fdidabkb').first();
  await expect(header.locator(':scope > .titleContainer > i.icon.fas.fa-bell')).toHaveCount(1);
  await expect(header.locator(':scope > .titleContainer > .title > .title')).toHaveText('通知');
  const tabs = header.locator(':scope > .tabs > button.tab');
  await expect(tabs).toHaveCount(4);
  await expect(tabs).toHaveText(['全て', '未読', 'あなた宛て', 'ダイレクト投稿']);
  await expect(header.locator(':scope > .buttons.right > button')).toHaveCount(2);
  await expect(page.locator('._content_b6w6v_6').first()).toHaveCSS('max-width', '800px');

  let notifications = page.locator('.notifications:has(> .elsfgstc)');
  await expect(notifications).toBeVisible();
  await expect(notifications.locator('.elsfgstc > .tkcbzcuz')).toHaveCount(2);
  await expect(notifications.locator('.elsfgstc > .qglefbjs._panel.notification')).toHaveCount(1);
  await expect(notifications).toContainText('Mention notification fixture');
  await expect(notifications).toContainText('Direct notification fixture');
  await expect.poll(() => notifications.locator('.elsfgstc').evaluate(
    element => getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');
  await expect.poll(() => notifications.locator('.elsfgstc').evaluate(
    element => getComputedStyle(element).backgroundColor)).not.toBe('transparent');

  const filterButton = header.locator("button[aria-label='フィルタ']");
  await filterButton.click();
  let popup = page.locator('body > .qzhlnise.popup');
  await expect(popup).toHaveCount(1);
  await expect(popup.locator('.rrevdjwt > button.item')).toHaveCount(11);
  await popup.locator('.rrevdjwt > button.item', { hasText: 'リアクション' }).click();
  await expect(filterButton).toHaveClass(/highlighted/);
  notifications = page.locator('.notifications:has(> .elsfgstc)');
  await expect(notifications.locator('.elsfgstc > .qglefbjs._panel.notification')).toHaveCount(1);
  await expect(notifications.locator('.elsfgstc > .tkcbzcuz')).toHaveCount(0);

  await filterButton.click();
  popup = page.locator('body > .qzhlnise.popup');
  await popup.locator('.rrevdjwt > button.item', { hasText: 'クリア' }).click();
  await expect(filterButton).not.toHaveClass(/highlighted/);
  await expect(page.locator('.notifications:has(> .elsfgstc) > .elsfgstc > .tkcbzcuz')).toHaveCount(2);

  await header.locator(".tabs > button[title='あなた宛て']").click();
  const mentionNotes = page.locator('.giivymft > .notes');
  await expect(mentionNotes.locator(':scope > .tkcbzcuz')).toHaveCount(2);
  await expect(mentionNotes).toContainText('Mention notification fixture');
  await expect(mentionNotes).toContainText('Direct notification fixture');
  await expect(header.locator(':scope > .buttons.right > button')).toHaveCount(0);

  await header.locator(".tabs > button[title='ダイレクト投稿']").click();
  const directNotes = page.locator('.giivymft > .notes');
  await expect(directNotes.locator(':scope > .tkcbzcuz')).toHaveCount(1);
  await expect(directNotes).toContainText('Direct notification fixture');
  await expect(directNotes).not.toContainText('Mention notification fixture');

  await header.locator(".tabs > button[title='全て']").click();
  await header.locator("button[aria-label='全て既読にする']").click();
  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/notification-state')).json() as {
      markAllReadCalls: number;
    };
    return state.markAllReadCalls;
  }).toBe(1);
  await header.locator(".tabs > button[title='未読']").click();
  // MkPagination replaces the list root with its v12 empty-state node when the unread
  // query has no entries, so this assertion must not require the former list wrapper.
  await expect(page.getByText('通知はありません', { exact: true })).toBeVisible();

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
