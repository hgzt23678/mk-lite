import { expect, test } from '@playwright/test';

test('MkNote preserves the pinned v12 hierarchy, responsive layout, hotkeys, and real actions', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  await page.goto('/__test/sign-in');
  await page.goto('/__test/components/note-view');

  const root = page.locator('[data-contract="note-view-host"] > .tkcbzcuz');
  await expect(root).toHaveClass(/contract-note/);
  await expect(root).toHaveAttribute('data-fallthrough', 'note-view');
  await expect(root.locator(':scope > .reply-to.wrpstxzv')).toHaveCount(1);
  await expect(root.locator(':scope > .info > .fa-thumbtack')).toHaveCount(1);
  await expect(root.locator(':scope > .article > .avatar.eiwwqkts')).toHaveCount(1);
  await expect(root.locator(':scope > .article > .main > .header.kkwtjztg')).toHaveCount(1);
  await expect(root.locator(':scope > .article > .main > .body > .content')).toBeHidden();

  await root.locator(':scope > .article .cw > .nrvgflfu').click();
  const content = root.locator(':scope > .article > .main > .body > .content');
  await expect(content).toBeVisible();
  await expect(content.locator(':scope > .text > .reply > .fa-reply')).toHaveCount(1);
  await expect(content.locator(':scope > .text > .rp')).toHaveText('RN:');
  await expect(content.locator(':scope > .files .hoawjimk')).toHaveCount(1);
  await expect(content.locator(':scope > .poll')).toHaveCount(1);
  await expect(content.locator(':scope > .renote > .yohlumlk')).toContainText('quoted note');
  await expect(root.locator(':scope > .article > .main > .footer > .button')).toHaveCount(4);
  await expect(root.locator(':scope > .article > .main > .footer > .button > .fa-reply-all')).toHaveCount(1);

  const narrowRenote = page.locator('[data-contract="renote-host"] > .tkcbzcuz');
  await expect(narrowRenote).toHaveClass(/renote/);
  await expect(narrowRenote).not.toHaveClass(/max-width_/);
  expect(await page.locator('[data-contract="renote-host"]').evaluate(element =>
    getComputedStyle(element).containerType)).toBe('inline-size');
  expect(await narrowRenote.evaluate(element => getComputedStyle(element).fontSize)).toBe('12.6px');
  expect(await narrowRenote.locator(':scope > .article').evaluate(element =>
    getComputedStyle(element).padding)).toBe('14px 16px 9px');
  await expect(narrowRenote.locator(':scope > .renote > .avatar.eiwwqkts')).toHaveCount(1);
  await expect(narrowRenote.locator(':scope > .renote > .info > .time')).toHaveCount(1);

  const narrowHost = page.locator('[data-contract="renote-host"]');
  await narrowHost.evaluate(element => { (element as HTMLElement).style.width = '340px'; });
  expect(await narrowRenote.locator(':scope > .article > .main > .footer > .button').first().evaluate(element =>
    getComputedStyle(element).marginRight)).toBe('18px');
  await narrowHost.evaluate(element => { (element as HTMLElement).style.width = '290px'; });
  expect(await narrowRenote.locator(':scope > .article > .avatar').evaluate(element =>
    getComputedStyle(element).width)).toBe('44px');
  expect(await narrowRenote.locator(':scope > .article > .main > .footer > .button').first().evaluate(element =>
    getComputedStyle(element).marginRight)).toBe('12px');

  await root.focus();
  await page.keyboard.press('r');
  const postForm = page.locator('.gafaadew.modal');
  await expect(postForm).toBeVisible();
  await expect(postForm.locator(':scope > .form > .preview.yohlumlk')).toContainText('main note');
  await postForm.locator(':scope > header > .cancel').click();
  await expect(postForm).toHaveCount(0);

  await root.focus();
  await page.keyboard.press('q');
  const renoteMenu = page.locator('.popup-menu .rrevdjwt, .popup-menu .mkh-menu').first();
  await expect(page.getByText('Renote', { exact: true }).last()).toBeVisible();
  await page.getByText('Renote', { exact: true }).last().click();
  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/renote-state')).json() as { renoteCalls: number };
    return state.renoteCalls;
  }).toBeGreaterThan(0);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(failures).toEqual([]);
  void renoteMenu;
});
