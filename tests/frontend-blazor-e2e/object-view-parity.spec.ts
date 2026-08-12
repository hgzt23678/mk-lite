import { expect, test } from '@playwright/test';

test('MkObjectView preserves recursive type presentation and collapse behavior', async ({ page }) => {
  await page.goto('/__test/components/object-view');
  const root = page.locator('[data-contract="mk-object-view"] > .zhyxdalp > .igpposuu > .object');
  const entries = root.locator(':scope > .kv');
  await expect(entries).toHaveCount(4);
  await expect(entries.filter({ hasText: 'enabled:' }).locator(':scope > .v')).toHaveText('true');
  await expect(entries.filter({ hasText: 'count:' }).locator(':scope > .v')).toHaveText('1,234');

  const nested = entries.filter({ has: page.locator(':scope > .k', { hasText: 'nested:' }) });
  await expect(nested.locator(':scope > .v')).toHaveText('{...}');
  await nested.locator(':scope > button.toggle').click();
  await expect(nested.locator(':scope > .v')).toContainText('answer:');
  await expect(nested.locator(':scope > .v')).toContainText('42');

  const items = entries.filter({ has: page.locator(':scope > .k', { hasText: 'items:' }) });
  await items.locator(':scope > button.toggle').click();
  await expect(items.locator('.array > .element')).toHaveText(['1: false', '2: "x"']);

  await page.context().route('https://www.google.com/search**', route => route.fulfill({
    status: 200,
    contentType: 'text/html',
    body: '<title>search fixture</title>',
  }));
  const search = page.locator('.mk-google');
  await expect(search.locator(':scope > input')).toHaveValue('initial query');
  await search.locator(':scope > input').fill('misskey federation');
  const popupPromise = page.waitForEvent('popup');
  await search.locator(':scope > button').click();
  const popup = await popupPromise;
  await popup.waitForLoadState('domcontentloaded');
  expect(new URL(popup.url()).searchParams.get('q')).toBe('misskey federation');
  await popup.close();

  const tooltip = page.locator('[role="tooltip"]', { has: page.locator('.beeadbfb') });
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toHaveCSS('max-width', '340px');
  await expect(tooltip.locator('.beeadbfb > .name')).toHaveText(':party:');
  await expect(tooltip.locator('.beeadbfb > .icon')).toBeVisible();

  const coordinateTooltip = page.locator('[data-contract="coordinate-tooltip"]');
  await expect(coordinateTooltip).toBeVisible();
  await expect(coordinateTooltip).toHaveAttribute('data-tooltip-state', 'shown');
  await expect(coordinateTooltip.locator(':scope > span')).toHaveText('coordinate tooltip');
  expect(await coordinateTooltip.evaluate(element => (element as HTMLElement).style.transformOrigin)).toBe('left center');
  expect(await coordinateTooltip.evaluate(element => Number.parseFloat((element as HTMLElement).style.left))).toBeGreaterThan(320);

  const emojiSection = page.locator('[data-contract="emoji-picker-section"]');
  await expect(emojiSection.locator('section > .body')).toHaveCount(0);
  await emojiSection.locator('section > header').click();
  await expect(emojiSection.locator('section > .body > button')).toHaveCount(2);
  await emojiSection.locator('section > .body > button').nth(1).click();
  await expect(emojiSection.locator('output')).toHaveText('🎉');

  const ticker = page.locator('[data-contract="instance-ticker"]');
  await expect(ticker).toHaveClass(/\bhpaizdrt\b/);
  await expect(ticker).toHaveClass(/\bticker\b/);
  await expect(ticker.locator(':scope > .name')).toHaveText('remote.example');
  await expect(ticker).toHaveCSS('background-image', 'linear-gradient(90deg, rgb(18, 52, 86), rgba(18, 52, 86, 0))');

  const featured = page.locator('[data-contract="featured-photos"]');
  await expect(featured).toHaveClass(/\bxfbouadm\b/);
  await expect(featured).toHaveClass(/\bcover\b/);
  await expect(featured).toHaveCSS('background-position', '50% 50%');
  await expect(featured).toHaveCSS('background-size', 'cover');

  const selectFixture = page.locator('[data-contract="form-select"]');
  const selectContainer = selectFixture.locator('.vblkjoeq > .input');
  await expect(selectFixture.locator('select > optgroup > option, select > option')).toHaveCount(3);
  await expect(selectFixture.locator('option[value="home"]')).toHaveAttribute('selected', '');
  await selectContainer.click();
  const selectMenu = page.locator('body > .qzhlnise .rrevdjwt');
  await expect(selectMenu.locator(':scope > .label')).toHaveText('公開');
  await expect(selectMenu.locator(':scope > .item')).toHaveCount(4);
  const [containerBox, menuBox] = await Promise.all([
    selectContainer.boundingBox(),
    selectMenu.boundingBox(),
  ]);
  expect(containerBox).not.toBeNull();
  expect(menuBox).not.toBeNull();
  // v12 supplies container.offsetWidth to popupMenu. MkMenu itself has min-width: 200px,
  // so narrow source controls keep that upstream minimum rather than shrinking the menu.
  const minimumMenuWidth = Number.parseFloat(await selectMenu.evaluate(element => getComputedStyle(element).minWidth));
  expect(menuBox!.width).toBeCloseTo(Math.max(Math.round(containerBox!.width), minimumMenuWidth), 0);
  await selectMenu.getByRole('menuitem', { name: 'パブリック' }).click();
  await expect(selectFixture.locator('output')).toHaveText('public');
  await expect(selectMenu).toHaveCount(0);

  await page.locator('[data-contract="dialog-trigger"]').click();
  const dialog = page.locator('body > .qzhlnise.dialog[role="alertdialog"]');
  await expect(dialog.locator('.mk-dialog > header')).toHaveText('公開範囲');
  await expect(dialog.locator('.mk-dialog > .body')).toHaveText('ノートの公開範囲を選択してください');
  await expect(dialog.locator('.mk-dialog > .icon')).toHaveCount(0);
  await dialog.locator('.vblkjoeq > .input').click();
  const dialogMenu = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await dialogMenu.getByRole('menuitem', { name: 'パブリック' }).click();
  await expect(dialogMenu).toHaveCount(0);
  await dialog.locator('.mk-dialog > .buttons > button.primary').click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('[data-contract="dialog-result"]')).toHaveText('public');

  const range = page.locator('[data-contract="form-range"]');
  await expect(range.locator('.timctyfi > .body > .container > .ticks > .tick')).toHaveCount(6);
  await expect(range.locator('.track > .highlight')).toHaveAttribute('style', 'width: 20%;');
  const rangeContainer = range.locator('.timctyfi > .body > .container');
  const rangeThumb = range.locator('.timctyfi > .body > .container > .thumb');
  const rangeContainerBox = await rangeContainer.boundingBox();
  const rangeThumbBox = await rangeThumb.boundingBox();
  expect(rangeContainerBox).not.toBeNull();
  expect(rangeThumbBox).not.toBeNull();
  await page.mouse.move(rangeThumbBox!.x + rangeThumbBox!.width / 2, rangeThumbBox!.y + rangeThumbBox!.height / 2);
  await page.mouse.down();
  await page.mouse.move(rangeContainerBox!.x + rangeContainerBox!.width * 0.75, rangeContainerBox!.y + rangeContainerBox!.height / 2);
  await expect(page.locator('[role="tooltip"]', { hasText: '音量' })).toBeVisible();
  await page.mouse.up();
  await expect(range.locator('output')).toHaveText('8');
  await expect(page.locator('[role="tooltip"]', { hasText: '音量' })).toHaveCount(0);
});
