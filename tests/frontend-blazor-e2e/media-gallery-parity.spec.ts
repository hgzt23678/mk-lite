import { expect, test } from '@playwright/test';

test('MkMediaList uses pinned PhotoSwipe gallery behavior and four-item grid', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));
  await page.addInitScript(() => localStorage.setItem('pizzax::base', JSON.stringify({
    nsfw: 'ignore',
    loadRawImages: false,
    disableShowingAnimatedImages: false,
  })));

  await page.goto('/__test/components/media-gallery');

  const list = page.locator('.hoawjimk[data-gallery-fallthrough="preserved"]');
  const grid = list.locator(':scope > .gird-container > div');
  await expect(grid).toHaveAttribute('data-count', '4');
  await expect(grid.locator(':scope > .image')).toHaveCount(3);
  await expect(grid.locator(':scope > .kkjnbbplepmiyuadieoenjgutgcmtsvu')).toHaveCount(1);
  expect(await grid.evaluate(element => ({
    columns: getComputedStyle(element).gridTemplateColumns.split(' ').length,
    rows: getComputedStyle(element).gridTemplateRows.split(' ').length,
    gap: getComputedStyle(element).gap,
  }))).toEqual({ columns: 2, rows: 2, gap: '8px' });

  await grid.locator(':scope > .image[data-id="first"] > a').click();
  const lightbox = page.locator('body > .pswp');
  await expect(lightbox).toBeVisible();
  await expect(lightbox.locator('.pswp__counter')).toHaveText('1 / 3');
  await expect(lightbox.locator('img.pswp__img[alt="first image"]')).toBeVisible();
  await expect(lightbox.locator('img.pswp__img[alt="first image"]')).toHaveAttribute('src', '/static-assets/icons/512.png');

  await page.keyboard.press('ArrowRight');
  await expect(lightbox.locator('.pswp__counter')).toHaveText('2 / 3');
  await expect(lightbox.locator('img.pswp__img[alt="second image"]')).toBeVisible();
  await expect(lightbox.locator('img.pswp__img[alt="second image"]')).toHaveAttribute('src', '/static-assets/splash.png');
  await page.keyboard.press('ArrowRight');
  await expect(lightbox.locator('.pswp__counter')).toHaveText('3 / 3');
  await page.keyboard.press('ArrowRight');
  await expect(lightbox.locator('.pswp__counter')).toHaveText('3 / 3');

  await expect(lightbox.locator('img.pswp__img[alt="third image"]')).toBeVisible();
  await lightbox.locator('img.pswp__img[alt="third image"]').click();
  await expect(lightbox).toHaveCount(0);
  expect(failures).toEqual([]);
});
