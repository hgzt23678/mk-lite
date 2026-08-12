import { expect, test } from '@playwright/test';

test('post form autocomplete suggests users, emojis, and MFM tags like v12', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  const source = page.locator('.mvcprjjd .bottom > button.post[data-cy-open-post-form]');
  await source.click();
  const dialog = page.locator('body > .qzhlnise.dialog');
  await expect(dialog).toHaveCount(1);
  const form = dialog.locator(':scope > .content.top > .gafaadew.modal._popup');
  await expect(form).toHaveCount(1);
  const textarea = form.locator('textarea[data-cy-post-form-text]');
  await expect(textarea).toHaveCount(1);

  await textarea.fill('@ali');
  const userPopup = page.locator('body > .swhvrteh._popup._shadow');
  await expect(userPopup).toHaveCount(1);
  await expect(userPopup.locator(':scope > ol.users > li.user')).toHaveCount(1);
  await expect(userPopup.locator(':scope > ol.users > li.choose')).toHaveCount(1);
  await expect(userPopup.locator(':scope > ol.users > li.user .name')).toHaveText('Alice');
  await expect(userPopup.locator(':scope > ol.users > li.user .username')).toHaveText('@alice');

  await page.keyboard.press('ArrowDown');
  await expect(userPopup.locator(':scope > ol.users > li.user')).toHaveAttribute('data-selected', 'true');
  await page.keyboard.press('Enter');
  await expect(userPopup).toHaveCount(0);
  await expect(textarea).toHaveValue('@alice ');

  await textarea.fill(':grin');
  const emojiPopup = page.locator('body > .swhvrteh._popup._shadow');
  await expect(emojiPopup).toHaveCount(1);
  await expect(emojiPopup.locator(':scope > ol.emojis > li')).toHaveCount(1);
  const grinning = emojiPopup.locator(':scope > ol.emojis > li');
  await expect(grinning.locator(':scope > .emoji > img')).toHaveAttribute('src', /\/twemoji\/1f600\.svg$/);
  await expect(grinning.locator(':scope > .name')).toContainText('grinning');
  await grinning.click();
  await expect(emojiPopup).toHaveCount(0);
  await expect(textarea).toHaveValue('😀');

  await textarea.fill('$sp');
  const mfmPopup = page.locator('body > .swhvrteh._popup._shadow');
  await expect(mfmPopup).toHaveCount(1);
  await expect(mfmPopup.locator(':scope > ol.mfmTags > li')).toHaveCount(1);
  await expect(mfmPopup.locator(':scope > ol.mfmTags > li .tag')).toHaveText('spin');
  await page.keyboard.press('ArrowDown');
  await expect(mfmPopup.locator(':scope > ol.mfmTags > li')).toHaveAttribute('data-selected', 'true');
  await page.keyboard.press('Enter');
  await expect(mfmPopup).toHaveCount(0);
  await expect(textarea).toHaveValue('$[spin ]');

  await textarea.fill('$nomatch');
  const emptyPopup = page.locator('body > .swhvrteh._popup._shadow');
  await expect(emptyPopup).toHaveCount(1);
  await expect(emptyPopup.locator(':scope > ol.mfmTags > li')).toHaveCount(0);
});
