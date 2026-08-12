import { expect, test } from '@playwright/test';

test('user page preserves the v12 profile structure and renders notes from the supported users/show boundary', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}:${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
  });

  await page.goto('/__test/sign-in');
  await page.goto('/@alice', { waitUntil: 'domcontentloaded' });

  const root = page.locator('.ftskorzw[data-user-page-state="loaded"]');
  await expect(root).toHaveCount(1);
  const profile = root.locator(':scope > .main > .profile > .main');
  await expect(profile.locator(':scope > .banner-container')).toHaveCount(1);
  await expect(profile.locator(':scope > .banner-container > .banner')).toHaveCount(1);
  await expect(profile.locator(':scope > .avatar.eiwwqkts')).toHaveCount(1);
  await expect(profile.locator(':scope > .banner-container > .title > .name')).toContainText('Alice');
  await expect(profile.locator(':scope > .title > .bottom > .username .mk-acct')).toContainText('@alice');
  await expect(profile.locator(':scope > .description .havbbuyv')).toContainText('Hello');
  await expect(profile.locator(':scope > .status > a')).toHaveCount(3);
  await expect(root.locator('.contents .notes .qtqtichx')).toHaveCount(1);
  await expect(root.locator('.contents .qtqtichx')).toContainText('Misskey');

  const background = await profile.evaluate(element => getComputedStyle(element).backgroundColor);
  expect(background).not.toBe('rgba(0, 0, 0, 0)');
  expect(background).not.toBe('transparent');

  await page.goto('/@alice/clips', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-capability-state="false"][data-capability="user/clips"]')).toHaveCount(1);
  await page.goto('/@alice/following', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.mk-following-or-followers .users .user')).toHaveCount(1);
  await expect(page.locator('.mk-following-or-followers .users .user')).toContainText('Bob');
  await page.goto('/@alice/followers', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.mk-following-or-followers .users')).toHaveCount(0);
  await expect(page.locator('._fullinfo')).toContainText('ユーザーはいません');
  expect(failures).toEqual([]);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
});
