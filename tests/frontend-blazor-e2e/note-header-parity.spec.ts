import { expect, test } from '@playwright/test';

test('MkNoteHeader preserves the Misskey v12 hierarchy, links, and pinned header CSS', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/);

  const header = page.locator('.tkcbzcuz.qtqtichx > article.article > .main > header.kkwtjztg');
  await expect(header).toHaveCount(1);
  await expect(header.locator(':scope > .name')).toHaveAttribute('href', '/@alice');
  await expect(header.locator(':scope > .name')).toHaveAttribute('data-user-preview', '9duke7z2w3');
  await expect(header.locator(':scope > .name > .havbbuyv.nowrap')).toHaveText('Alice');
  await expect(header.locator(':scope > .is-bot')).toHaveCount(0);
  await expect(header.locator(':scope > .username > .mk-acct')).toHaveText('@alice');
  await expect(header.locator(':scope > .info > a.created-at')).toHaveAttribute('href', '/notes/9dummqy0w3');
  await expect(header.locator(':scope > .info > a.created-at > time')).toHaveCount(1);
  await expect(header.locator(':scope > .info > span')).toHaveCount(0);

  await expect.poll(async () => header.evaluate(element =>
    Array.from(element.children).map(child => child.className))).toEqual(['name', 'username', 'info']);
  await expect.poll(async () => header.evaluate(element => getComputedStyle(element).display)).toBe('flex');
  await expect.poll(async () => header.evaluate(element => getComputedStyle(element).alignItems)).toBe('baseline');
  await expect.poll(async () => header.evaluate(element => getComputedStyle(element).whiteSpace)).toBe('nowrap');
  await expect.poll(async () => header.evaluate(element =>
    Number.parseInt(getComputedStyle(element.querySelector(':scope > .name')!).fontWeight, 10)))
    .toBeGreaterThanOrEqual(700);
  expect(failures).toEqual([]);
});
