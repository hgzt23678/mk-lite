import { expect, test } from '@playwright/test';

test('MkEmoji preserves native, reaction, custom, static-image, and CSS branches', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      useOsNativeEmojis: true,
      disableShowingAnimatedImages: true,
    }));
  });
  await page.goto('/');

  const welcomeEmojis = page.locator('.rsqzvsbo > .top > .emojis');
  await expect(welcomeEmojis.locator(':scope > span')).toHaveCount(5);
  await expect(welcomeEmojis.locator(':scope > img')).toHaveCount(0);
  await expect(welcomeEmojis).toContainText('👍');

  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/);
  const reactions = page.locator('.tkcbzcuz.qtqtichx footer.footer > .tdflqwzn > button.hkzvhatu');
  await expect(reactions).toHaveCount(2);

  const unicode = reactions.nth(0).locator(':scope > img.icon.mk-emoji');
  await expect(unicode).toHaveAttribute('src', /\/twemoji\/1f44d\.svg$/);
  await expect(unicode).not.toHaveClass(/normal|noStyle/);
  await expect(unicode).toHaveCSS('height', /.+/);

  const custom = reactions.nth(1).locator(':scope > img.icon.mk-emoji.custom.normal');
  await expect(custom).toHaveAttribute('src', '/static-assets/favicon.png?static=1');
  await expect(custom).toHaveAttribute('alt', ':party_parrot:');
  await expect(custom).toHaveCSS('height', /.+/);
  expect(failures).toEqual([]);
});
