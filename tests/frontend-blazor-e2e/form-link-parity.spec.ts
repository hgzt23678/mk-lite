import { expect, test } from '@playwright/test';

test('FormLink preserves the pinned branches, CSS, fallthrough and click-only behavior', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));

  await page.goto('/__test/components/form-link');

  const internal = page.locator('.ffcbddfc.inline.contract-link[data-contract="internal"]');
  const internalAnchor = internal.locator(':scope > a.main._button.active');
  await expect(internal).toHaveAttribute('disabled', 'disabled');
  await expect(internal).toHaveCSS('display', 'inline-block');
  await expect(internalAnchor).toHaveAttribute('href', '/settings/profile');
  await expect(internalAnchor).toHaveAttribute('data-enhance-nav', 'false');
  await expect(internalAnchor).toHaveAttribute('data-misskey-behavior', 'browser');
  await expect(internalAnchor.locator(':scope > .icon > .fa-user')).toHaveCount(1);
  await expect(internalAnchor.locator(':scope > .text')).toHaveText('プロフィール');
  await expect(internalAnchor.locator(':scope > .right > .text')).toHaveText('Account');
  await expect(internalAnchor.locator(':scope > .right > .fa-chevron-right.icon')).toHaveCount(1);
  await expect(internalAnchor).toHaveCSS('display', 'flex');
  await expect(internalAnchor).toHaveCSS('background-color', /.+/);
  await expect(internalAnchor).toHaveCSS('border-radius', '6px');

  const external = page.locator('.ffcbddfc[data-contract="external"] > a.main._button');
  await expect(external).not.toHaveClass(/active/);
  await expect(external).toHaveAttribute('href', 'https://remote.example/path');
  await expect(external).toHaveAttribute('target', '_blank');
  await expect(external).toHaveAttribute('rel', 'noopener noreferrer');
  await expect(external.locator(':scope > .right > .text')).toHaveText('Remote');
  await expect(external.locator(':scope > .right > .fa-external-link-alt.icon')).toHaveCount(1);

  const action = page.locator('.ffcbddfc[data-contract="action"]');
  await expect(action.locator(':scope > a.main._button')).not.toHaveAttribute('href', /.+/);
  await action.click();
  await expect(page.locator('[data-contract="clicks"]')).toHaveText('1');

  expect(errors).toEqual([]);
});
