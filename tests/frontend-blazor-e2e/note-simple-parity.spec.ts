import { expect, test } from '@playwright/test';

test('MkSubNoteContent and MkNoteSimple preserve the fixed Misskey v12 contract', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/note-simple');
  await expect(page.locator('.mk-app')).not.toHaveAttribute('inert', '');

  const sub = page.locator('[data-contract="sub-note-host"] > .wrmlmaau');
  await expect(sub).toHaveClass('wrmlmaau collapsed contract-sub-note');
  await expect(sub).toHaveAttribute('data-fallthrough', 'sub-note');
  await expect(sub.locator(':scope > .body')).toContainText('(private)');
  await expect(sub.locator(':scope > .body')).toContainText('(deleted)');
  await expect(sub.locator(':scope > .body > .reply')).toHaveAttribute('href', '/notes/reply-note-id');
  await expect(sub.locator(':scope > .body > .rp')).toHaveAttribute('href', '/notes/renote-note-id');
  await expect(sub.locator(':scope > details').nth(0).locator('summary')).toHaveText('(1つのファイル)');
  await expect(sub.locator(':scope > details').nth(1).locator('summary')).toHaveText('アンケート');
  await expect(sub.locator('.mk-media-banner > a.download')).toHaveAttribute('href', '/static-assets/favicon.png');
  await expect(sub.locator('.tivcixzd > ul > li')).toHaveCount(2);
  const collapsedStyle = await sub.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      overflowWrap: style.overflowWrap,
      overflow: style.overflow,
      maxHeightEm: Number.parseFloat(style.maxHeight) / Number.parseFloat(style.fontSize)
    };
  });
  expect(collapsedStyle.overflowWrap).toBe('break-word');
  expect(collapsedStyle.overflow).toBe('hidden');
  expect(collapsedStyle.maxHeightEm).toBeCloseTo(9, 5);
  expect(await sub.locator(':scope > button.fade').evaluate(element => getComputedStyle(element).backgroundImage))
    .toContain('linear-gradient');

  await sub.locator(':scope > button.fade').click();
  await expect(sub).toHaveClass('wrmlmaau contract-sub-note');
  await expect(sub.locator(':scope > button.fade')).toHaveCount(0);

  const simple = page.locator('[data-contract="simple-note-host"] > .yohlumlk');
  await expect(simple).toHaveClass(/min-width_350px/);
  await expect(simple).toHaveClass(/min-width_500px/);
  await expect(simple).toHaveClass(/contract-simple-note/);
  await expect(simple).toHaveAttribute('data-fallthrough', 'note-simple');
  await expect(simple.locator(':scope > .main > header.header')).toHaveAttribute('mini', 'true');
  const simpleContent = simple.locator(':scope > .main > .body > .content');
  await expect(simpleContent).toBeHidden();
  await expect(simpleContent.locator(':scope > .wrmlmaau.text')).not.toHaveClass(/collapsed/);
  await simple.locator('button.nrvgflfu').click();
  await expect(simpleContent).toBeVisible();
  await expect(simpleContent.locator(':scope > .wrmlmaau.text > details')).toHaveCount(2);

  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
