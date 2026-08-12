import { expect, test } from '@playwright/test';

test('ported form controls preserve the pinned DOM, CSS states and model behavior', async ({ page }) => {
  await page.goto('/__test/components/form-controls');

  const link = page.locator('.ffcbddfc.inline.fixture-link[data-fixture="link"]');
  await expect(link.locator(':scope > a.main._button.active')).toHaveAttribute('href', '/settings/profile');
  await expect(link.locator(':scope > a')).toHaveAttribute('data-enhance-nav', 'false');
  await expect(link.locator(':scope > a')).toHaveAttribute('data-misskey-behavior', 'browser');
  await expect(link.locator(':scope > a > .icon > .fa-user')).toHaveCount(1);
  await expect(link.locator(':scope > a > .right > .text')).toHaveText('Account');

  const slot = page.locator('.adhpbeou[data-fixture="slot"]');
  await expect(slot.locator(':scope > .label')).toHaveText('スロット');
  await expect(slot.locator(':scope > .content > #slot-content')).toHaveText('内容');
  await expect(slot.locator(':scope > .caption')).toHaveText('スロット説明');

  const split = page.locator('.terlnhxf._formBlock[data-fixture="split"]');
  await expect(split.locator(':scope > div')).toHaveCount(2);
  await expect(split).toHaveCSS('grid-template-columns', /.+/);
  expect(await split.evaluate(element => getComputedStyle(element).getPropertyValue('--mk-form-split-min-width').trim()))
    .toBe('180px');

  const checkbox = page.locator('.ziffeoms[data-fixture="checkbox"]');
  await expect(checkbox.locator(':scope > input[type="checkbox"]')).toHaveCSS('opacity', '0');
  await expect(checkbox.locator(':scope > .button > .check')).toHaveCSS('transform', /matrix\(0\.5/);
  await checkbox.locator(':scope > .button').click();
  await expect(page.locator('#checkbox-value')).toHaveText('true');
  await expect(checkbox).toHaveClass(/checked/);
  await expect(checkbox.locator(':scope > .button > .check')).toHaveCSS('opacity', '1');

  const formSwitch = page.locator('.ziffeomt[data-fixture="switch"]');
  const switchInput = formSwitch.locator(':scope > input[type="checkbox"]');
  await expect(switchInput).not.toHaveAttribute('checked', /.+/);
  await expect(formSwitch.locator(':scope > .label > span')).toHaveText('利用規約に同意する');
  await expect(formSwitch.locator(':scope > .button')).toHaveAttribute('title', 'オフになっています');
  await switchInput.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#switch-value')).toHaveText('true');
  await expect(formSwitch).toHaveClass(/checked/);
  await expect(formSwitch.locator(':scope > .button')).toHaveAttribute('title', 'オンになっています');

  const radios = page.locator('.novjtcto[data-fixture="radios"]');
  const options = radios.locator(':scope > .body > .novjtctn');
  await expect(options).toHaveCount(3);
  await expect(options.nth(0)).toHaveAttribute('aria-checked', 'true');
  await options.nth(1).click();
  await expect(page.locator('#radio-value')).toHaveText('followers');
  await expect(options.nth(0)).toHaveAttribute('aria-checked', 'false');
  await expect(options.nth(1)).toHaveAttribute('aria-checked', 'true');
  await options.nth(2).click();
  await expect(page.locator('#radio-value')).toHaveText('followers');

  const input = page.locator('.matxzzsk.fixture-input[data-fixture="input"]');
  const numericEditor = input.locator(':scope > .input > input[type="number"]');
  await expect(numericEditor).toHaveAttribute('step', '0.25');
  const listId = await numericEditor.getAttribute('list');
  expect(listId).not.toBeNull();
  await expect(input.locator(`datalist#${listId} > option`)).toHaveCount(2);
  await expect(numericEditor).toHaveCSS('height', '40px');
  await expect.poll(async () => Number.parseFloat(await numericEditor.evaluate(
    element => getComputedStyle(element).paddingLeft))).toBeGreaterThan(12);
  await input.locator(':scope > .label').click();
  await expect(numericEditor).toBeFocused();
  await numericEditor.fill('2.75');
  await expect(page.locator('#input-value')).toHaveText('1.5');
  await expect(input.locator(':scope > .save')).toHaveText(/保存/);
  await input.locator(':scope > .save').click();
  await expect(page.locator('#input-value')).toHaveText('2.75');

  const textarea = page.locator('.adhpbeos[data-fixture="textarea"]');
  const editor = textarea.locator(':scope > .input.tall.pre > textarea.code._monospace');
  await editor.fill('after');
  await expect(textarea.locator(':scope > .save')).toBeVisible();
  await expect(page.locator('#textarea-value')).toHaveText('before');
  await textarea.locator(':scope > .save').click();
  await expect(page.locator('#textarea-value')).toHaveText('after');
  await expect(textarea.locator(':scope > .save')).toHaveCount(0);
});
