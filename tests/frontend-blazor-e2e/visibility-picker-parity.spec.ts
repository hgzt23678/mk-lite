import { expect, test } from '@playwright/test';

test('MkVisibilityPicker preserves the pinned options, local-only state and overlay keyboard lifecycle', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  const composeSource = page.locator('.mvcprjjd .bottom > button.post[data-cy-open-post-form]');
  await composeSource.click();
  const postForm = page.locator('body > .qzhlnise.dialog > .content.top > .gafaadew.modal._popup');
  await expect(postForm).toBeVisible();
  const visibilitySource = postForm.locator(':scope > header > .right > button.visibility');

  const openPicker = async () => {
    await visibilitySource.click();
    const root = page.locator('body > .qzhlnise.popup');
    const picker = root.locator(':scope > .content > .gqyayizv._popup[role=menu]');
    await expect(picker).toBeVisible();
    return { root, picker };
  };

  let opened = await openPicker();
  await expect(opened.root.locator(':scope > .bg._modalBg:not(.transparent)')).toHaveCount(1);
  await expect(opened.picker).toHaveAttribute('aria-label', '公開範囲');
  await expect(opened.picker.locator(':scope > button')).toHaveCount(5);
  await expect(opened.picker.locator(':scope > .divider')).toHaveCount(1);
  await expect(opened.picker.locator(':scope > button > div:nth-child(2) > span:first-child')).toHaveText([
    'パブリック',
    'ホーム',
    'フォロワー',
    'ダイレクト',
    'ローカルのみ',
  ]);
  await expect(opened.picker.locator(':scope > button > div:nth-child(2) > span:last-child')).toHaveText([
    '全てのユーザーに公開',
    'ホームタイムラインのみに公開',
    '自分のフォロワーのみに公開',
    '指定したユーザーのみに公開',
    'リモートユーザーには非公開',
  ]);
  await expect(opened.picker).toHaveCSS('width', '240px');
  await expect(opened.picker).toHaveCSS('padding-top', '8px');
  const pickerAlpha = await opened.picker.evaluate(element => {
    const context = document.createElement('canvas').getContext('2d');
    if (context === null) throw new Error('Canvas 2D context unavailable');
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(pickerAlpha, 'the visibility popup surface must remain opaque').toBe(255);
  const zIndexes = await page.locator('body > .qzhlnise.dialog, body > .qzhlnise.popup')
    .evaluateAll(elements => elements.map(element => Number(getComputedStyle(element).zIndex)));
  expect(zIndexes).toHaveLength(2);
  expect(zIndexes[1]).toBeGreaterThan(zIndexes[0]);
  await expect(visibilitySource).toHaveCSS('pointer-events', 'none');
  await expect(opened.picker.locator(':scope > button[data-index="1"]')).toHaveAttribute('aria-checked', 'true');

  await page.keyboard.press('ArrowDown');
  await expect(opened.picker.locator(':scope > button[data-index="1"]')).toBeFocused();
  await page.keyboard.press('ArrowDown');
  await expect(opened.picker.locator(':scope > button[data-index="2"]')).toBeFocused();
  await page.keyboard.press('ArrowUp');
  await expect(opened.picker.locator(':scope > button[data-index="1"]')).toBeFocused();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await expect(opened.root).toHaveCount(0);
  await expect(visibilitySource).toBeFocused();
  await expect(visibilitySource.locator('i')).toHaveClass(/fa-home/);

  opened = await openPicker();
  await opened.picker.locator(':scope > button[data-index="3"]').click();
  await expect(opened.root).toHaveCount(0);
  await expect(visibilitySource).toBeFocused();
  await expect(visibilitySource.locator('i')).toHaveClass(/fa-unlock/);

  opened = await openPicker();
  await opened.picker.locator(':scope > button[data-index="4"]').click();
  await expect(opened.root).toHaveCount(0);
  await expect(visibilitySource.locator('i')).toHaveClass(/fa-envelope/);
  await expect(postForm.locator(':scope > .form > .to-specified')).toHaveCount(1);

  opened = await openPicker();
  const specified = opened.picker.locator(':scope > button[data-index="4"]');
  const localOnly = opened.picker.locator(':scope > button[data-index="5"]');
  await localOnly.click();
  await expect(opened.root).toHaveCount(1);
  await expect(specified).toBeDisabled();
  await expect(specified).toHaveClass(/active/);
  await expect(specified).toHaveAttribute('aria-checked', 'true');
  await expect(localOnly).toHaveClass(/active/);
  await expect(localOnly).toHaveAttribute('aria-checked', 'true');
  await expect(localOnly.locator(':scope > div:nth-child(3) > i')).toHaveClass(/fa-toggle-on/);
  await expect(postForm.locator(':scope > header > .right > .local-only')).toHaveCount(1);
  await expect(postForm.locator(':scope > .form > .to-specified')).toHaveCount(1);

  await localOnly.click();
  await expect(specified).toBeEnabled();
  await expect(specified).toHaveClass(/active/);
  await expect(localOnly).not.toHaveClass(/active/);
  await expect(localOnly).toHaveAttribute('aria-checked', 'false');
  await expect(postForm.locator(':scope > header > .right > .local-only')).toHaveCount(0);
  await opened.root.locator(':scope > .bg._modalBg').click({ position: { x: 2, y: 2 } });
  await expect(opened.root).toHaveCount(0);
  await expect(visibilitySource).toBeFocused();

  opened = await openPicker();
  await opened.picker.locator(':scope > button[data-index="1"]').click();
  await expect(opened.root).toHaveCount(0);
  await expect(visibilitySource.locator('i')).toHaveClass(/fa-globe/);
  await expect(postForm.locator(':scope > .form > .to-specified')).toHaveCount(0);

  opened = await openPicker();
  await page.keyboard.press('Escape');
  await expect(opened.root).toHaveCount(0);
  await expect(visibilitySource).toBeFocused();
  await expect(page.locator('body > .qzhlnise.popup')).toHaveCount(0);
  expect(failures).toEqual([]);
});
