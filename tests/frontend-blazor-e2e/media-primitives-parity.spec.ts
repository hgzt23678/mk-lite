import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({ nsfw: 'force' }));
    localStorage.setItem('miux:mediaVolume', JSON.stringify(0.25));
  });
});

test('media primitives preserve BlurHash pixels, NSFW branches, audio volume, and MkMediaList integration', async ({ page }) => {
  await page.goto('/__test/components/media-primitives');

  const blurhash = page.locator('[data-primitive="blurhash"] .xubzgfgb');
  await expect(blurhash).toHaveClass('xubzgfgb cover');
  const canvas = blurhash.locator(':scope > canvas');
  await expect(canvas).toHaveAttribute('width', '8');
  await expect(canvas).toHaveAttribute('height', '8');
  await expect.poll(() => canvas.evaluate(element => {
    const context = (element as HTMLCanvasElement).getContext('2d')!;
    const pixels = context.getImageData(0, 0, 8, 8).data;
    let hash = 2166136261;
    for (const value of pixels) {
      hash ^= value;
      hash = Math.imul(hash, 16777619) >>> 0;
    }
    return hash;
  })).toBe(4222087079);

  const loaded = page.locator('[data-primitive="loaded-image"] .xubzgfgb');
  await expect(loaded).toHaveClass('xubzgfgb');
  await expect(loaded.locator(':scope > img')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(loaded.locator(':scope > canvas')).toHaveCount(0);
  expect(await loaded.locator(':scope > img').evaluate(element => getComputedStyle(element).objectFit)).toBe('contain');

  const videoHost = page.locator('[data-primitive="video"]');
  await expect(videoHost.locator(':scope > .icozogqfvdetwohsdglrbswgrejoxbdj')).toBeVisible();
  await expect(videoHost.locator('b')).toContainText('閲覧注意');
  await videoHost.locator(':scope > .icozogqfvdetwohsdglrbswgrejoxbdj').click();
  await expect(videoHost.locator(':scope > .kkjnbbplepmiyuadieoenjgutgcmtsvu > video')).toHaveAttribute('preload', 'none');
  await expect(videoHost.locator('video > source')).toHaveAttribute('type', 'video/mp4');
  await videoHost.locator(':scope > .kkjnbbplepmiyuadieoenjgutgcmtsvu > i.fa-eye-slash').click();
  await expect(videoHost.locator(':scope > .icozogqfvdetwohsdglrbswgrejoxbdj')).toBeVisible();

  const banner = page.locator('[data-primitive="banner"] > .mk-media-banner');
  await expect(banner.locator(':scope > .sensitive')).toBeVisible();
  await banner.locator(':scope > .sensitive').click();
  const audio = banner.locator(':scope > .audio > audio.audio');
  await expect(audio).toBeVisible();
  await expect.poll(() => audio.evaluate(element => (element as HTMLAudioElement).volume)).toBe(0.25);
  await audio.evaluate(element => { (element as HTMLAudioElement).volume = 0.65; });
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('miux:mediaVolume')!))).toBe(0.65);

  const list = page.locator('[data-primitive="media-list"] > .hoawjimk');
  const hiddenImage = list.locator('.qjewsnkg.image[data-id="image"]');
  await expect(hiddenImage).toBeVisible();
  await expect(list.locator('.icozogqfvdetwohsdglrbswgrejoxbdj')).toBeVisible();
  await expect(list.locator(':scope > .mk-media-banner > a.download')).toHaveAttribute('href', '/static-assets/favicon.png');
  await hiddenImage.click();
  const revealedImage = list.locator('.gqnyydlz.image[data-id="image"]');
  await expect(revealedImage.locator('.xubzgfgb')).toBeVisible();
  const lightbox = page.locator('body > .pswp');
  await expect(lightbox.locator('.pswp__counter')).toHaveText('1 / 1');
  await lightbox.locator('img.pswp__img[alt="image description"]').click();
  await expect(lightbox).toHaveCount(0);
  await revealedImage.locator(':scope > button.hide').click();
  await expect(list.locator('.qjewsnkg.image[data-id="image"]')).toBeVisible();

  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});
