import { expect, test } from '@playwright/test';

test('MkUserInfo preserves the pinned card DOM and child-component geometry', async ({ page }) => {
  const browserErrors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') browserErrors.push(message.text());
  });
  page.on('pageerror', error => browserErrors.push(error.message));
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      squareAvatars: false,
      disableShowingAnimatedImages: false,
      showFullAcct: false,
    }));
  });
  await page.request.post('/__test/reset-diagnostics');
  await page.goto('/__test/components/user-info');

  const card = page.locator('[data-user-card="followable"]._panel.vjnjpkug.user');
  await expect(card).toBeVisible();
  expect(await card.evaluate(element => getComputedStyle(element).backgroundColor))
    .not.toBe('rgba(0, 0, 0, 0)');

  const banner = card.locator(':scope > .banner');
  await expect(banner).toHaveCSS('height', '84px');
  expect(await banner.evaluate(element => getComputedStyle(element).backgroundImage))
    .toContain('/static-assets/icons/512.png');
  expect(await banner.evaluate(element => getComputedStyle(element).backgroundColor))
    .not.toBe('rgba(0, 0, 0, 0)');

  const avatar = card.locator(':scope > .avatar.eiwwqkts');
  await expect(avatar).toHaveCSS('position', 'absolute');
  await expect(avatar).toHaveCSS('top', '62px');
  await expect(avatar).toHaveCSS('left', '13px');
  await expect(avatar).toHaveCSS('width', '58px');
  await expect(avatar).toHaveCSS('height', '58px');
  await expect(avatar).toHaveCSS('border-top-width', '4px');
  await expect(avatar.locator(':scope > img.inner')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(avatar.locator(':scope > .indicator')).toHaveCount(1);
  await expect(avatar).not.toHaveAttribute('data-user-preview', /.+/);

  const title = card.locator(':scope > .title');
  await expect(title).toHaveCSS('padding-left', '88px');
  await expect(title.locator(':scope > a.name')).toHaveAttribute('href', '/@alice@xn--bcher-kva.example');
  await expect(title.locator(':scope > a.name')).toContainText('Alice');
  await expect(title.locator(':scope > p.username')).toContainText('@alice@bücher.example');

  const description = card.locator(':scope > .description > .mfm');
  await expect(description).toContainText('Hello Fediverse');
  expect(await description.evaluate(element => getComputedStyle(element).webkitLineClamp)).toBe('3');
  await expect(description).toHaveCSS('overflow', 'hidden');

  const stats = card.locator(':scope > .status > div');
  await expect(stats).toHaveCount(3);
  await expect(stats.locator(':scope > p')).toHaveText(['ノート', 'フォロー', 'フォロワー']);
  await expect(stats.locator(':scope > span')).toHaveText(['73', '19', '31']);
  const statWidths = await stats.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().width));
  expect(Math.max(...statWidths) - Math.min(...statWidths)).toBeLessThan(1);

  const follow = card.locator(':scope > button.kpoogebi.koudoku-button');
  await expect(follow).toBeVisible();
  await expect(follow).toHaveAttribute('mini', '');
  await expect(follow).toHaveCSS('position', 'absolute');
  await expect(follow).toHaveCSS('top', '8px');
  await expect(follow).toHaveCSS('right', '8px');

  const hidden = page.locator('[data-user-card="self-or-anonymous"]._panel.vjnjpkug.user');
  await expect(hidden.locator(':scope > .description > span')).toHaveText('自己紹介はありません');
  await expect(hidden.locator(':scope > .koudoku-button')).toHaveCount(0);
  await expect(hidden.locator(':scope > .banner')).toHaveCSS('background-image', 'none');
  await expect(page.locator('body')).not.toContainText('tracker.invalid');

  expect(browserErrors).toEqual([]);
  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});
