import { expect, test } from '@playwright/test';

test('MkEmojiPickerDialog preserves popup drawer focus close and chosen contracts', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      animation: true,
      disableDrawer: false,
      reactionPickerUseDrawerForMobile: false,
      reactions: ['🦊', '🎯'],
      reactionPickerSize: 2,
      reactionPickerWidth: 5,
      reactionPickerHeight: 4,
      disableShowingAnimatedImages: true,
      recentlyUsedEmojis: [],
    }));
    Object.defineProperty(Navigator.prototype, 'maxTouchPoints', {
      configurable: true,
      get: () => 1,
    });
  });
  await page.setViewportSize({ width: 480, height: 800 });
  await page.goto('/__test/components/emoji-picker-dialog');

  await page.locator('[data-contract="open-drawer"]').click();
  let modal = page.locator('body > .qzhlnise.drawer');
  await expect(modal).toBeVisible();
  await expect(modal.locator(':scope > .bg._modalBg')).not.toHaveClass(/transparent/);
  let picker = modal.locator(':scope > .content > .omfetrab.s1.w3.h2.asDrawer.ryghynhb._popup._shadow.drawer');
  await expect(picker).toBeVisible();
  const drawerMaximumHeight = await picker.evaluate(element => Number.parseFloat(getComputedStyle(element).maxHeight));
  expect(drawerMaximumHeight).toBeCloseTo(800 / 1.5, 1);
  await page.keyboard.press('Escape');
  await expect(modal).toHaveCount(0);

  await page.locator('[data-contract="open-reaction"]').click();
  modal = page.locator('body > .qzhlnise.popup');
  await expect(modal).toHaveCount(1);
  await expect(modal.locator(':scope > .bg._modalBg.transparent')).toBeVisible();
  picker = modal.locator(':scope > .content > .omfetrab.s2.w5.h4.ryghynhb._popup._shadow:not(.drawer):not(.asDrawer)');
  await expect(picker).toBeVisible();
  const search = picker.locator(':scope > input.search');
  await expect(search).toBeFocused();
  await expect(search).toHaveAttribute('placeholder', '検索');
  await expect(picker.locator(':scope > .emojis > .group.index > section:first-child > .body > button.item')).toHaveCount(2);
  await picker.locator(':scope > .emojis').evaluate(element => { element.scrollTop = 120; });
  await search.fill('party');
  await expect.poll(() => picker.locator(':scope > .emojis').evaluate(element => element.scrollTop)).toBe(0);
  const customResult = picker.locator(':scope > .emojis > section.result > .body > button.item[title="party"]');
  await expect(customResult.locator('img')).toHaveAttribute('src', '/static-assets/favicon.png?static=1');
  await customResult.click({ position: { x: 12, y: 12 } });
  const state = page.locator('[data-contract="state"]');
  await expect(state).toHaveAttribute('data-chosen', ':party:');
  await expect(state).toHaveAttribute('data-chosen-count', '1');
  await expect(page.locator('.vswabwbm')).toHaveCount(1);
  await expect(modal).toHaveCount(0);

  await page.locator('[data-contract="open-reaction"]').click();
  modal = page.locator('body > .qzhlnise.popup');
  picker = modal.locator(':scope > .content > .omfetrab.ryghynhb');
  await expect(picker).toBeVisible();
  await picker.locator(':scope > input.search').evaluate(element => {
    const transfer = new DataTransfer();
    transfer.setData('text/plain', ':party:');
    element.dispatchEvent(new ClipboardEvent('paste', {
      bubbles: true,
      cancelable: true,
      clipboardData: transfer,
    }));
  });
  await expect(state).toHaveAttribute('data-chosen-count', '2');
  await expect(modal).toHaveCount(0);

  await page.locator('[data-contract="open-drawer"]').click();
  modal = page.locator('body > .qzhlnise.drawer');
  await expect(modal).toBeVisible();
  await modal.locator(':scope > .bg._modalBg').click({ position: { x: 4, y: 4 } });
  await expect(modal).toHaveCount(0);

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
