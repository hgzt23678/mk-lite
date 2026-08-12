import { expect, test } from '@playwright/test';

test('MkNoteDetailed preserves the v12 detail hierarchy, thread, actions, and opaque panel', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  await page.goto('/__test/sign-in');
  await page.goto('/__test/components/note-detailed');

  const root = page.locator('[data-contract="note-detailed-host"] > .lxwezrsl');
  await expect(root).toHaveClass(/_block/);
  await expect(root).toHaveClass(/contract-note-detailed/);
  await expect(root).toHaveAttribute('data-fallthrough', 'note-detailed');
  await expect(root.locator(':scope > .reply-to-more.wrpstxzv')).toHaveCount(1);
  await expect(root.locator(':scope > .reply-to.wrpstxzv')).toHaveCount(1);
  await expect(root.locator(':scope > .reply.wrpstxzv')).toHaveCount(1);
  await expect(root.locator(':scope > .article > .header > .avatar.eiwwqkts')).toHaveCount(1);
  await expect(root.locator(':scope > .article > .header > .body > .top > .name')).toContainText('Alice');
  await expect(root.locator(':scope > .article > .main > .footer > .info > .created-at time')).toHaveCount(1);
  await expect(root.locator(':scope > .article > .main > .footer > .tdflqwzn')).toHaveCount(1);
  await expect(root.locator(':scope > .article > .main > .footer > .button')).toHaveCount(4);
  await expect(root.locator(':scope > .article > .main > .footer > .button > .fa-reply-all')).toHaveCount(1);

  const visual = await root.evaluate(element => {
    const style = getComputedStyle(element);
    const article = element.querySelector(':scope > .article');
    const avatar = article?.querySelector(':scope > .header > .avatar');
    return {
      backgroundColor: style.backgroundColor,
      articlePadding: article ? getComputedStyle(article).padding : '',
      avatarWidth: avatar ? getComputedStyle(avatar).width : '',
      avatarHeight: avatar ? getComputedStyle(avatar).height : ''
    };
  });
  expect(visual.backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
  expect(visual.backgroundColor).not.toBe('transparent');
  expect(visual.articlePadding).toBe('32px');
  expect(visual.avatarWidth).toBe('58px');
  expect(visual.avatarHeight).toBe('58px');

  await root.focus();
  await page.keyboard.press('r');
  const postForm = page.locator('.gafaadew.modal');
  await expect(postForm).toBeVisible();
  await expect(postForm.locator(':scope > .form > .preview.yohlumlk')).toContainText('Misskey v12');
  await postForm.locator(':scope > header > .cancel').click();

  const renote = page.locator('[data-contract="note-detailed-renote-host"] > .lxwezrsl');
  await expect(renote).toHaveClass(/renote/);
  await expect(renote).toHaveClass(/max-width_450px/);
  await expect(renote.locator(':scope > .renote > .avatar.eiwwqkts')).toHaveCount(1);
  await expect(renote.locator(':scope > .article > .header')).toHaveCount(1);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as { unhandledExceptions: unknown[] };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(failures).toEqual([]);
});
