import { expect, test } from '@playwright/test';

test('MkEmojiPickerWindow preserves the v12 mini front window, selection and close contracts', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`page:${error.message}`));
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      animation: true,
      reactions: ['🦊'],
      reactionPickerSize: 1,
      reactionPickerWidth: 5,
      reactionPickerHeight: 4,
      disableShowingAnimatedImages: true,
      recentlyUsedEmojis: [],
    }));
  });

  await page.goto('/__test/components/emoji-picker-window');

  let windowRoot = page.locator('.ebkgocck');
  await expect(windowRoot).toBeVisible();
  await expect(windowRoot.locator(':scope > .body > .header.mini')).toHaveCount(1);
  await expect(windowRoot.locator(':scope > .handle')).toHaveCount(0);
  await expect(windowRoot.locator(':scope > .body > .header .fa-window-maximize')).toHaveCount(0);
  const picker = windowRoot.locator(':scope > .body > .body > .omfetrab');
  await expect(picker).toHaveClass(/s1/);
  await expect(picker).toHaveClass(/w5/);
  await expect(picker).toHaveClass(/h4/);
  await expect(picker.locator(':scope > .emojis > .group.index > section')).toHaveCount(1);
  await expect(picker.locator(':scope > .emojis > .group.index > section > .body > button')).toHaveCount(0);

  await picker.locator(':scope > input.search').fill('party');
  const custom = picker.locator(':scope > .emojis > section.result > .body > button.item[title="party"]');
  await expect(custom.locator('img')).toHaveAttribute('src', '/static-assets/favicon.png?static=1');
  await custom.click();
  const state = page.locator('[data-contract="state"]');
  await expect(state).toHaveAttribute('data-chosen', ':party:');
  await expect(state).toHaveAttribute('data-chosen-count', '1');

  await windowRoot.locator(':scope > .body > .header .fa-times').click();
  await expect(windowRoot).toHaveCount(0);
  await expect(state).toHaveAttribute('data-closed', '1');

  await page.locator('[data-contract="open"]').click();
  windowRoot = page.locator('.ebkgocck');
  await expect(windowRoot).toBeVisible();
  const background = await windowRoot.locator(':scope > .body > .body').evaluate(element => getComputedStyle(element).backgroundColor);
  expect(background).not.toBe('rgba(0, 0, 0, 0)');
  expect(background).not.toBe('transparent');

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(errors).toEqual([]);
});
