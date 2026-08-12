import { expect, test } from '@playwright/test';

test('MkPostFormAttaches preserves pinned thumbnail, reorder, menu, caption and detach contracts', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/post-form-attaches');
  const root = page.locator('.skeikyzd.contract-attaches');
  const files = root.locator(':scope > .files > .file');
  const state = page.locator('[data-contract="state"]');
  await expect(files).toHaveCount(3);
  await expect(root.locator(':scope > .remain')).toHaveText('13/16');
  const firstId = await files.nth(0).locator(':scope > .thumbnail').getAttribute('data-id');
  const secondId = await files.nth(1).locator(':scope > .thumbnail').getAttribute('data-id');
  const thirdId = await files.nth(2).locator(':scope > .thumbnail').getAttribute('data-id');
  expect(firstId).toBeTruthy();
  expect(secondId).toBeTruthy();
  expect(thirdId).toBeTruthy();
  await expect(files.nth(0).locator(':scope > .thumbnail.zdjebgpv .xubzgfgb.cover img')).toHaveAttribute('title', 'first.png');
  await expect(files.nth(1).locator(':scope > .sensitive > .icon')).toHaveClass('fas fa-exclamation-triangle icon');
  const dimensions = await files.nth(0).evaluate(element => {
    const rectangle = element.getBoundingClientRect();
    const image = element.querySelector('.thumbnail')!.getBoundingClientRect();
    return { width: rectangle.width, height: rectangle.height, imageWidth: image.width, imageHeight: image.height };
  });
  expect(dimensions).toEqual({ width: 64, height: 64, imageWidth: 64, imageHeight: 64 });

  await page.waitForTimeout(100);
  await drag(page, files.nth(0), files.nth(2));
  await expect(state).toHaveAttribute('data-order', `${secondId},${thirdId},${firstId}`);
  await expect(files.nth(0).locator(':scope > .thumbnail')).toHaveAttribute('data-id', secondId!);
  await expect(page.locator('body > .qzhlnise.popup')).toHaveCount(0);

  await touchDrag(page, files.nth(2), files.nth(0));
  await expect(state).toHaveAttribute('data-order', `${firstId},${secondId},${thirdId}`);

  await files.nth(1).click();
  let menu = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await expect(menu.locator(':scope > button.item')).toHaveCount(4);
  await menu.locator(':scope > button.item').nth(1).click();
  await expect(state).toHaveAttribute('data-sensitive', '');
  await expect(files.nth(1).locator(':scope > .sensitive')).toHaveCount(0);

  const imageFile = files.filter({ has: page.locator(`.thumbnail[data-id="${firstId}"]`) });
  await imageFile.click();
  menu = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await menu.locator(':scope > button.item').nth(0).click();
  const rename = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await rename.locator('input').fill('renamed.png');
  await rename.locator('.buttons button').first().click();
  await expect(state).toHaveAttribute('data-names', 'renamed.png|second.png|third.png');
  await expect(imageFile.locator('.thumbnail img')).toHaveAttribute('title', 'renamed.png');

  await imageFile.click();
  menu = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await menu.locator(':scope > button.item').nth(2).click();
  const caption = page.locator('.qzhlnise.dialog').filter({ has: page.locator('.hdrwpsaf') });
  await expect(caption.locator('.hdrwpsaf img')).toBeVisible();
  await caption.locator('textarea').fill('caption text');
  await caption.locator('.buttons button').first().click();
  await expect(state).toHaveAttribute('data-descriptions', 'caption text||');
  await expect(caption).toHaveCount(0);

  await imageFile.click();
  menu = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await menu.locator(':scope > button.item').nth(3).click();
  await expect(files).toHaveCount(2);
  await expect(root.locator(':scope > .remain')).toHaveText('14/16');
  await expect(state).toHaveAttribute('data-detached', firstId!);

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});

async function drag(page: import('@playwright/test').Page, source: import('@playwright/test').Locator, target: import('@playwright/test').Locator) {
  const from = await source.boundingBox();
  const to = await target.boundingBox();
  expect(from).not.toBeNull();
  expect(to).not.toBeNull();
  await page.mouse.move(from!.x + from!.width / 2, from!.y + from!.height / 2);
  await page.mouse.down();
  await page.mouse.move(to!.x + to!.width * 0.8, to!.y + to!.height / 2, { steps: 8 });
  await page.mouse.up();
}

async function touchDrag(page: import('@playwright/test').Page, source: import('@playwright/test').Locator, target: import('@playwright/test').Locator) {
  const from = await source.boundingBox();
  const to = await target.boundingBox();
  expect(from).not.toBeNull();
  expect(to).not.toBeNull();
  await source.evaluate((element, point) => {
    element.dispatchEvent(new PointerEvent('pointerdown', {
      bubbles: true,
      pointerId: 41,
      pointerType: 'touch',
      button: 0,
      clientX: point.x,
      clientY: point.y,
    }));
  }, { x: from!.x + from!.width / 2, y: from!.y + from!.height / 2 });
  await page.waitForTimeout(120);
  await page.evaluate(point => {
    window.dispatchEvent(new PointerEvent('pointermove', {
      bubbles: true,
      pointerId: 41,
      pointerType: 'touch',
      button: 0,
      clientX: point.x,
      clientY: point.y,
    }));
    window.dispatchEvent(new PointerEvent('pointerup', {
      bubbles: true,
      pointerId: 41,
      pointerType: 'touch',
      button: 0,
      clientX: point.x,
      clientY: point.y,
    }));
  }, { x: to!.x + to!.width * 0.2, y: to!.y + to!.height / 2 });
}
