import { expect, test } from '@playwright/test';

test('MkNotePreview and MkNoteSimple preserve the fixed Misskey v12 projection contract', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/note-preview');

  const preview = page.locator('[data-contract="preview-host"] > .fefdfafb');
  await expect(preview).toBeVisible();
  await expect(preview).toHaveClass('fefdfafb min-width_350px min-width_500px contract-note-preview');
  await expect(preview).toHaveAttribute('data-fallthrough', 'note-preview');
  await expect(preview).toHaveAttribute('aria-label', '投稿プレビュー');
  await expect(preview.locator(':scope > .main > .header')).toHaveText('Alice');
  await expect(preview.locator(':scope > .main > .body > .content > .havbbuyv')).toContainText('preview text');
  await expect(preview.locator(':scope > .main > .body > .content img.mk-emoji.custom'))
    .toHaveAttribute('src', '/static-assets/favicon.png');

  const style = await preview.evaluate(element => {
    const root = getComputedStyle(element);
    const avatar = getComputedStyle(element.querySelector(':scope > .avatar')!);
    const header = getComputedStyle(element.querySelector(':scope > .main > .header')!);
    return {
      display: root.display,
      overflow: root.overflow,
      avatarWidth: avatar.width,
      avatarHeight: avatar.height,
      avatarMarginRight: avatar.marginRight,
      avatarPointerEvents: avatar.pointerEvents,
      headerWeight: header.fontWeight
    };
  });
  expect(style).toEqual({
    display: 'flex',
    overflow: 'clip',
    avatarWidth: '48px',
    avatarHeight: '48px',
    avatarMarginRight: '12px',
    avatarPointerEvents: 'none',
    headerWeight: '700'
  });

  await page.locator('[data-contract="resize"]').click();
  await expect(preview).toHaveClass('fefdfafb contract-note-preview');

  await page.goto('/__test/components/note-simple');
  const simple = page.locator('[data-contract="simple-note-host"] > .yohlumlk');
  await expect(simple).toBeVisible();
  await expect(simple).toHaveClass(/min-width_350px/);
  await expect(simple).toHaveClass(/min-width_500px/);
  await expect(simple).toHaveClass(/contract-simple-note/);
  await expect(simple).toHaveAttribute('data-fallthrough', 'note-simple');

  const header = simple.locator(':scope > .main > header.header');
  await expect(header).toHaveAttribute('mini', 'true');
  await expect(header.locator(':scope > .name')).toHaveAttribute('href', '/@alice');
  await expect(header.locator(':scope > .name')).toContainText('Alice');
  await expect(header.locator(':scope > .username')).toContainText('@alice');
  await expect(header.locator(':scope > .info > .created-at')).toHaveAttribute('href', '/notes/simple-note-id');
  await expect(header.locator(':scope > .info > .created-at > time')).toHaveAttribute('title', /.+/);

  const warning = simple.locator(':scope > .main > .body > .cw');
  await expect(warning.locator(':scope > .text img.mk-emoji.custom'))
    .toHaveAttribute('src', '/static-assets/favicon.png');
  const simpleContent = simple.locator(':scope > .main > .body > .content');
  await expect(simpleContent).toBeHidden();
  await simple.locator('button.nrvgflfu').click();
  await expect(simpleContent).toBeVisible();
  await expect(simpleContent.locator(':scope > .wrmlmaau.text > .body img.mk-emoji.custom'))
    .toHaveAttribute('src', '/static-assets/favicon.png');

  const opaqueSurfaces = await page.evaluate(() => ['html', 'body'].map(selector => {
    const element = document.querySelector(selector);
    if (!(element instanceof HTMLElement)) return { selector, alpha: 0 };
    const context = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
    if (context === null) return { selector, alpha: 0 };
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return { selector, alpha: context.getImageData(0, 0, 1, 1).data[3] };
  }));
  expect(opaqueSurfaces).toEqual([
    { selector: 'html', alpha: 255 },
    { selector: 'body', alpha: 255 }
  ]);

  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
