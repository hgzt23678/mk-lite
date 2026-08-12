import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      nsfw: 'force',
      loadRawImages: false,
      disableShowingAnimatedImages: true,
    }));
  });
});

test('MkMediaImage preserves pinned sensitive, image and viewer-boundary behavior', async ({ page }) => {
  await page.goto('/__test/components/media-image');

  const previewHost = page.locator('[data-primitive="preview"]');
  const hidden = previewHost.locator(':scope > .qjewsnkg.image[data-id="preview"]');
  await expect(hidden).toBeVisible();
  await expect(hidden.locator('.text b')).toContainText('閲覧注意');
  expect(await hidden.locator(':scope > .bg').evaluate(element => getComputedStyle(element).filter)).toContain('brightness(0.5)');

  await hidden.click();
  const visible = previewHost.locator(':scope > .gqnyydlz.image[data-id="preview"]');
  await expect(visible).toBeVisible();
  expect(await visible.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');
  const anchor = visible.locator(':scope > a');
  await expect(anchor).toHaveAttribute('href', '/static-assets/favicon.png');
  await expect(anchor).toHaveAttribute('title', 'preview description');
  await expect(anchor).not.toHaveAttribute('target', /.+/);
  const image = anchor.locator(':scope > .xubzgfgb > img');
  await expect(image).toHaveAttribute('src', '/static-assets/user-unknown.png?static=1');
  await expect(image).toHaveAttribute('alt', 'preview description');
  await expect(image).toHaveAttribute('title', 'preview description');
  expect(await image.evaluate(element => getComputedStyle(element).objectFit)).toBe('contain');
  await expect(anchor.locator(':scope > .gif')).toHaveText('GIF');

  await anchor.click();
  await expect(page.locator('output[data-viewer-opened="preview"]')).toHaveText('preview');
  await expect(page).toHaveURL(/\/__test\/components\/media-image$/);
  await visible.locator(':scope > button.hide').click();
  await expect(hidden).toBeVisible();

  const rawHost = page.locator('[data-primitive="raw"]');
  await rawHost.locator(':scope > .qjewsnkg.image').click();
  await expect(rawHost.locator(':scope > .gqnyydlz .xubzgfgb > img')).toHaveAttribute(
    'src',
    '/static-assets/favicon.png',
  );
  await expect(page.locator('[data-primitive="remote"]')).toBeEmpty();

  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});
