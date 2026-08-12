import { expect, test } from '@playwright/test';

test('MkNotes preserves the v12 empty, note, advertisement, background and motion contracts', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  await page.goto('/__test/sign-in');
  await page.goto('/__test/components/mk-notes');

  const notes = page.locator('[data-contract="populated-notes"] .giivymft');
  await expect(notes).toBeVisible();
  const list = notes.locator(':scope > .notes');
  await expect(notes).toHaveClass('giivymft');
  await expect(list).toHaveClass('sqadhkmv notes');
  await expect(list).toHaveAttribute('data-direction', 'down');
  await expect(list).toHaveAttribute('data-reversed', 'false');
  await expect(list.locator(':scope > .tkcbzcuz.qtqtichx')).toHaveCount(4);
  await expect(list.locator(':scope > .mk-notes-contract-ad')).toHaveAttribute('data-ad-for', '9note-d');
  await expect.poll(() => list.evaluate(element => Array.from(element.children).map(child =>
    child.classList.contains('mk-notes-contract-ad')
      ? `ad:${child.getAttribute('data-ad-for')}`
      : `note:${child.getAttribute('data-note-id')}`
  ))).toEqual([
    'note:9note-a',
    'note:9note-b',
    'note:9note-c',
    'ad:9note-d',
    'note:9note-d'
  ]);

  const background = await list.evaluate(element => getComputedStyle(element).backgroundColor);
  expect(background).not.toBe('rgba(0, 0, 0, 0)');
  expect(background).not.toBe('transparent');

  const empty = page.locator('[data-contract="empty-notes"] .empty');
  await expect(empty.locator('img._ghost')).toHaveAttribute('src', '/client-assets/about-icon.png');
  await expect(empty.locator('._fullinfo > div')).toHaveText('ノートはありません');

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(failures).toEqual([]);
});
