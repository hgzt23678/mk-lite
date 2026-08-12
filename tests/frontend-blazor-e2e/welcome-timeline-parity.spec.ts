import { expect, test } from '@playwright/test';

test('welcome.timeline preserves MFM, media, poll, reactions, and measured scroll behavior', async ({ page, request }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  const fixture = await request.post('/__test/cw-note/all');
  expect(fixture.ok()).toBeTruthy();
  try {
    await page.goto('/');

    const timeline = page.locator('.rsqzvsbo > .top > .civpbkhh.tl');
    const scrollbox = timeline.locator(':scope > .scrollbox');
    const note = scrollbox.locator(':scope > .note');
    await expect(note).toHaveCount(1);
    await expect(note.locator(':scope > .content._panel > .body > .havbbuyv')).toContainText('A');
    await expect(note.locator(':scope > .content._panel > .body > .havbbuyv img.mk-emoji')).toHaveCount(1);
    await expect(note.locator(':scope > .content._panel > .richcontent > .hoawjimk')).toHaveCount(1);
    await expect(note.locator('.hoawjimk > .gird-container > div')).toHaveAttribute('data-count', '2');
    await expect(note.locator(':scope > .content._panel > div > .tivcixzd')).toHaveCount(1);
    await expect(note.locator(':scope > .tdflqwzn > .hkzvhatu')).toHaveCount(2);

    await expect.poll(() => scrollbox.evaluate(element => ({
      measured: (element as HTMLElement).clientHeight > window.innerHeight,
      animated: element.classList.contains('scroll'),
    }))).toEqual(await scrollbox.evaluate(element => ({
      measured: (element as HTMLElement).clientHeight > window.innerHeight,
      animated: (element as HTMLElement).clientHeight > window.innerHeight,
    })));
    expect(failures).toEqual([]);
  } finally {
    await request.post('/__test/cw-note/reset');
  }
});
