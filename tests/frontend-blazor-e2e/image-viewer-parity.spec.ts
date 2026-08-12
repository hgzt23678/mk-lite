import { expect, test } from '@playwright/test';

test('MkImageViewer preserves the upstream modal and adds bounded zoom interactions', async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => consoleErrors.push(error.message));
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      nsfw: 'ignore',
      loadRawImages: false,
      disableShowingAnimatedImages: false,
    }));
  });
  await page.request.post('/__test/reset-diagnostics');
  await page.goto('/__test/components/image-viewer');

  const launcher = page.locator('[data-contract="launcher"]');
  await page.locator('[data-contract="open-image-viewer"]').click();

  let modal = page.locator('body > .qzhlnise.dialog', {
    has: page.locator('.xubzgfga > header', { hasText: 'viewer image description' }),
  });
  await expect(modal.locator(':scope > .content')).toBeVisible();
  await expect(modal).toHaveAttribute('role', 'dialog');
  await expect(modal).toHaveAttribute('aria-modal', 'true');
  await expect(modal).toHaveAttribute('aria-label', 'viewer image description');
  await expect(modal).toHaveAttribute('data-motion-state', 'entered');
  await expect(modal).not.toHaveClass(/modal-enter-from/);

  const background = modal.locator(':scope > .bg._modalBg');
  expect(await background.evaluate(element => getComputedStyle(element).backgroundColor))
    .not.toBe('rgba(0, 0, 0, 0)');
  const viewport = modal.locator('.content > .xubzgfga');
  await expect(viewport).toHaveAttribute('tabindex', '-1');
  await expect(viewport.locator(':scope > header')).toHaveText('viewer image description');
  const image = viewport.locator(':scope > img');
  await expect(image).toHaveAttribute('src', '/static-assets/icons/512.png');
  await expect(image).toHaveAttribute('alt', 'viewer image description');
  await expect(image).toHaveAttribute('title', 'viewer image description');
  await expect.poll(async () => viewport.evaluate(element => document.activeElement === element)).toBe(true);
  await expect(viewport.locator(':scope > footer > span')).toHaveText([
    'image/png',
    '2KB',
    '1,920px × 1,080px',
  ]);
  expect(await viewport.locator(':scope > header').evaluate(element => getComputedStyle(element).backgroundColor))
    .not.toBe('rgba(0, 0, 0, 0)');
  await expect(viewport).toHaveAttribute('data-image-scale', '1');

  await image.hover();
  await page.mouse.wheel(0, -350);
  await expect.poll(async () => Number(await viewport.getAttribute('data-image-scale'))).toBeGreaterThan(1);

  const beforeKeyPan = Number(await viewport.getAttribute('data-image-pan-x'));
  await page.keyboard.press('ArrowRight');
  await expect.poll(async () => Number(await viewport.getAttribute('data-image-pan-x'))).toBeLessThan(beforeKeyPan);

  const box = await image.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
  await page.mouse.down();
  await page.mouse.move(box!.x + box!.width / 2 + 35, box!.y + box!.height / 2 + 20, { steps: 3 });
  await page.mouse.up();
  await expect(modal.locator(':scope > .content')).toBeVisible();
  expect(Number(await viewport.getAttribute('data-image-pan-x'))).not.toBe(beforeKeyPan);

  await page.keyboard.press('0');
  await expect(viewport).toHaveAttribute('data-image-scale', '1');
  await image.evaluate(element => {
    const dispatch = (type: string, pointerId: number, x: number) => element.dispatchEvent(new PointerEvent(type, {
      bubbles: true,
      cancelable: true,
      pointerId,
      pointerType: 'touch',
      clientX: x,
      clientY: 120,
      isPrimary: pointerId === 11,
    }));
    dispatch('pointerdown', 11, 100);
    dispatch('pointerdown', 12, 200);
    dispatch('pointermove', 12, 260);
    dispatch('pointerup', 11, 100);
    dispatch('pointerup', 12, 260);
    (element as HTMLElement).click();
  });
  await expect.poll(async () => Number(await viewport.getAttribute('data-image-scale'))).toBeGreaterThan(1);
  await expect(modal.locator(':scope > .content')).toBeVisible();

  await image.click();
  await expect(modal).toHaveCount(0);

  await page.locator('[data-contract="open-image-viewer"]').click();
  modal = page.locator('body > .qzhlnise.dialog', {
    has: page.locator('.xubzgfga > header', { hasText: 'viewer image description' }),
  });
  await expect(modal).toHaveAttribute('data-motion-state', 'entered');
  await page.keyboard.press('Escape');
  await expect(modal).toHaveCount(0);

  await page.keyboard.press('+');
  expect(consoleErrors).toEqual([]);
  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});
