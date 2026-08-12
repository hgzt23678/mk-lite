import { expect, test } from '@playwright/test';

test('MkMediaCaption preserves the pinned editor, preview, limit and confirm flow', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));

  await page.goto('/__test/components/media-caption');

  const modal = page.locator('.qzhlnise.dialog[data-contract="media-caption"]');
  await expect(modal).toHaveAttribute('role', 'dialog');
  await expect(modal).toHaveAttribute('aria-modal', 'true');
  const container = modal.locator(':scope > .content > .container');
  await expect(container).toHaveCSS('display', 'flex');
  await expect(container).toHaveCSS('position', 'fixed');
  await expect(container).toHaveCSS('flex-direction', 'row');

  const editor = container.locator(':scope > .top-caption > .mk-dialog');
  await expect(editor.locator(':scope > header > .title')).toHaveText('画像の説明');
  await expect(editor.locator(':scope > header > .text-count')).toHaveText('507');
  await expect(editor).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  await expect(editor).toHaveCSS('border-radius', /.+/);
  const textarea = editor.locator(':scope > textarea');
  await expect(textarea).toHaveAttribute('placeholder', '新しい説明を入力');
  await expect(textarea).toBeFocused();

  const preview = container.locator(':scope > .hdrwpsaf');
  await expect(preview.locator(':scope > header')).toHaveText('fixture.png');
  await expect(preview.locator(':scope > img')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(preview.locator(':scope > img')).toHaveAttribute('alt', 'fixture description');
  await expect(preview.locator(':scope > footer > span')).toHaveText([
    'image/png',
    '3KB',
    '1,920px × 1,080px',
  ]);

  await textarea.fill('a'.repeat(513));
  await expect(editor.locator(':scope > header > .text-count.over')).toHaveText('-1');
  await expect(editor.locator(':scope > .buttons > button.primary')).toBeDisabled();

  await textarea.fill('新しい説明');
  await textarea.press('Control+Enter');
  await expect(page.locator('[data-contract="canceled"]')).toHaveText('false');
  await expect(page.locator('[data-contract="result"]')).toHaveText('新しい説明');
  await expect(page.locator('[data-contract="closed"]')).toHaveText('1');
  await expect(modal).toHaveCount(0);
  expect(errors).toEqual([]);
});
