import { expect, test } from '@playwright/test';

test('user follow pages use the Dolphin relation projection and v12 user grid', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}:${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
  });

  await page.goto('/__test/sign-in');
  await page.goto('/@alice/following', { waitUntil: 'domcontentloaded' });
  const root = page.locator('.ftskorzw[data-user-page-state="loaded"]');
  await expect(root).toHaveCount(1);
  // The canonical href contains the remote host when the fixture actor has a
  // federated acct; assert the route suffix rather than assuming a local-only
  // account identifier.
  await expect(root.locator('.status > a.active[href$="/following"]')).toHaveCount(1);
  const users = page.locator('.mk-following-or-followers .users');
  await expect(users).toBeVisible();
  await expect(users.locator('.user')).toHaveCount(1);
  await expect(users.locator('.user')).toContainText('Bob');
  // The grid itself is intentionally transparent in the v12 stylesheet; the
  // user cards are the opaque panels that must carry the background.
  await expect(users.locator('.user').first()).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');

  await page.goto('/@alice/followers', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.status > a.active[href$="/followers"]')).toHaveCount(1);
  await expect(page.locator('.mk-following-or-followers')).toHaveCount(0);
  await expect(page.locator('._fullinfo')).toContainText('ユーザーはいません');
  expect(failures).toEqual([]);
  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
});
