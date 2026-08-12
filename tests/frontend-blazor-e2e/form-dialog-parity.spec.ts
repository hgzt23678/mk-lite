import { expect, test } from '@playwright/test';

test('MkFormDialog preserves dynamic fields and returns typed values', async ({ page }) => {
  await page.goto('/__test/components/form-dialog');
  await page.locator('[data-contract="open"]').click();

  const dialog = page.locator('body > .qzhlnise.dialog', {
    has: page.locator('.ebkgoccj > .header > .title', { hasText: '設定' }),
  });
  await expect(dialog.locator(':scope > .content > .ebkgoccj')).toBeVisible();
  await expect(dialog.locator(':scope > .bg._modalBg')).toBeVisible();
  expect(await dialog.locator(':scope > .bg._modalBg').evaluate(element =>
    getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');
  await expect(dialog.locator('.ebkgoccj')).toHaveCSS('width', '450px');
  await expect(dialog.locator('.xkpnjxcv._formRoot > ._formBlock')).toHaveCount(6);
  await expect(dialog.locator('.matxzzsk > .label')).toContainText('タイトル (任意)');
  await expect(dialog).not.toContainText('private');

  await dialog.locator('input[type="text"]').fill('after');
  await dialog.locator('.ziffeomt > .button').click();
  await dialog.locator('.vblkjoeq > .input').click();
  const menu = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await menu.getByRole('menuitem', { name: 'ベル' }).click();
  await dialog.locator('.novjtctn', { hasText: '2列' }).click();
  await dialog.locator('.xkpnjxcv > .bghgjjyj', { hasText: '試聴' }).click();
  await expect(page.locator('[data-contract="action"]')).toHaveText('after|bell');

  await dialog.locator('.ebkgoccj > .header > button:last-child').click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('[data-contract="result"]')).toHaveText('after|True|bell|2|0.5');

  const folder = page.locator('[data-contract="form-folder"]');
  await expect(folder).toHaveClass(/\bdwzlatin\b/);
  await expect(folder.locator(':scope > .body')).toHaveCount(0);
  await folder.locator(':scope > .header').click();
  await expect(folder).toHaveClass(/\bopened\b/);
  await folder.locator('[data-contract="folder-value"]').click();
  await expect(folder.locator('[data-contract="folder-value"]')).toHaveText('1');
  await folder.locator(':scope > .header').click();
  await expect(folder.locator(':scope > .body')).toBeHidden();
  await folder.locator(':scope > .header').click();
  await expect(folder.locator('[data-contract="folder-value"]')).toHaveText('1');
});
