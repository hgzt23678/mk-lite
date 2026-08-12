import { expect, test } from '@playwright/test';

test('MkWindow preserves pinned DOM, drag, resize, maximize, z-index and close motion', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/window');
  const events = page.locator('[data-contract="events"]');
  let windowRoot = page.locator('.ebkgocck.contract-window');
  let header = windowRoot.locator(':scope > .body > .header');
  await expect(windowRoot).toHaveAttribute('data-fallthrough', 'window');
  await expect(header).toHaveClass('header mini');
  await expect(header.locator(':scope > .title [data-contract="title"]')).toHaveText('Pinned window');
  await expect(windowRoot.locator(':scope > .handle')).toHaveCount(8);
  await expect(header.locator('.left > .button.highlighted > .fa-arrow-left')).toBeVisible();
  await expect(header.locator('.right > .button > .fa-expand-alt')).toBeVisible();
  await expect(header.locator('.right > .button > .fa-window-maximize')).toBeVisible();
  await expect(header.locator('.right > .button > .fa-times')).toBeVisible();
  await expect(windowRoot).toHaveCSS('width', '500px');
  await expect(windowRoot).toHaveCSS('height', '360px');
  await expect(windowRoot).toHaveCSS('z-index', '1000100');
  await expect(windowRoot).not.toHaveClass(/window-enter-active/);

  const initial = await geometry(windowRoot);
  expect(initial).toMatchObject({ width: 500, height: 360, zIndex: 1000100 });
  expect(initial.left).toBeCloseTo((1280 - 500) / 2, 0);
  expect(initial.top).toBeCloseTo((720 - 360) / 2, 0);
  expect(initial.bodyOverflow).toBe('clip');
  expect(initial.headerHeight).toBe(38);

  await header.locator('.left > .button').click();
  await header.locator('.right > .button').first().click();
  await expect(events).toHaveAttribute('data-left', '1');
  await expect(events).toHaveAttribute('data-right', '1');
  expect((await geometry(windowRoot)).zIndex).toBeGreaterThan(initial.zIndex);

  const title = header.locator(':scope > .title');
  const titleBox = await title.boundingBox();
  expect(titleBox).not.toBeNull();
  await page.mouse.move(titleBox!.x + titleBox!.width / 2, titleBox!.y + titleBox!.height / 2);
  await page.mouse.down();
  await page.mouse.move(titleBox!.x + titleBox!.width / 2 + 120, titleBox!.y + titleBox!.height / 2 + 80);
  await page.mouse.up();
  const dragged = await geometry(windowRoot);
  expect(dragged.left).toBeGreaterThan(initial.left + 80);
  expect(dragged.top).toBeGreaterThan(initial.top + 40);

  const rightHandle = windowRoot.locator(':scope > .handle.right');
  const rightBox = await rightHandle.boundingBox();
  expect(rightBox).not.toBeNull();
  await page.mouse.move(rightBox!.x + 2, rightBox!.y + rightBox!.height / 2);
  await page.mouse.down();
  await page.mouse.move(rightBox!.x + 102, rightBox!.y + rightBox!.height / 2);
  await page.mouse.up();
  const resized = await geometry(windowRoot);
  expect(resized.width).toBeGreaterThan(dragged.width + 70);

  const restored = resized;
  await header.locator('.fa-window-maximize').click();
  await expect(windowRoot).toHaveClass(/\bmaximized\b/);
  await expect(header.locator('.fa-window-restore')).toBeVisible();
  const maximized = await geometry(windowRoot);
  expect(maximized).toMatchObject({ top: 0, left: 0, width: 1280, height: 720 });
  await header.locator('.fa-window-restore').click();
  await expect(windowRoot).not.toHaveClass(/\bmaximized\b/);
  const afterRestore = await geometry(windowRoot);
  expect(afterRestore.left).toBeCloseTo(restored.left, 0);
  expect(afterRestore.top).toBeCloseTo(restored.top, 0);
  expect(afterRestore.width).toBeCloseTo(restored.width, 0);
  expect(afterRestore.height).toBeCloseTo(restored.height, 0);

  await windowRoot.locator('[data-contract="focus"]').focus();
  await page.keyboard.press('Escape');
  await expect(windowRoot).toHaveClass(/window-leave-active/);
  await expect(events).toHaveAttribute('data-closed', '1');
  await expect(windowRoot).toHaveCount(0);

  await page.locator('[data-contract="open"]').click();
  windowRoot = page.locator('.ebkgocck.contract-window');
  header = windowRoot.locator(':scope > .body > .header');
  await expect(windowRoot).toBeVisible();
  await header.locator('.fa-times').click();
  await expect(events).toHaveAttribute('data-closed', '2');
  await expect(windowRoot).toHaveCount(0);

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});

async function geometry(locator: import('@playwright/test').Locator) {
  return locator.evaluate(element => {
    const root = element as HTMLElement;
    const rectangle = root.getBoundingClientRect();
    const body = root.querySelector(':scope > .body')!;
    const header = root.querySelector(':scope > .body > .header')!;
    return {
      top: rectangle.top,
      left: rectangle.left,
      width: rectangle.width,
      height: rectangle.height,
      zIndex: Number.parseInt(getComputedStyle(root).zIndex, 10),
      bodyOverflow: getComputedStyle(body).overflow,
      headerHeight: header.getBoundingClientRect().height,
    };
  });
}
