import { expect, test } from '@playwright/test';

test.beforeEach(async ({ request }) => {
  const fixture = await request.post('/__test/cw-note/all');
  expect(fixture.status()).toBe(204);
});

test.afterEach(async ({ request }) => {
  const reset = await request.post('/__test/cw-note/reset');
  expect(reset.status()).toBe(204);
});

test('real timeline note preserves MkCwButton DOM, v-show state, geometry, motion, and keyboard behavior', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/);
  await expect(page.locator('body > .dkgtipfy')).not.toHaveAttribute('inert', '');
  await expect.poll(async () => page.locator('._root_b6w6v_1').evaluate(
    element => getComputedStyle(element).paddingTop)).not.toBe('0px');

  const note = page.locator('.sqadhkmv.noGap.notes > .tkcbzcuz.qtqtichx');
  const body = note.locator(':scope > .article > .main > .body');
  const warning = body.locator(':scope > p.cw');
  const button = warning.locator(':scope > button.nrvgflfu._button');
  const content = body.locator(':scope > .content');

  await expect(warning.locator(':scope > .text.havbbuyv')).toHaveText('閲覧注意');
  await expect(button).toHaveCount(1);
  await expect(button.locator(':scope > b')).toHaveText('もっと見る');
  await expect(button.locator(':scope > span')).toHaveText('3文字 / 2ファイル / アンケート');
  await expect(button).not.toHaveAttribute('type', /.+/);
  await expect(button).toHaveAttribute('aria-expanded', 'false');

  // Vue v-show keeps the complete subtree alive while the CW is folded. The media and poll
  // nodes must therefore already exist instead of being recreated after interaction.
  await expect(content).toHaveCSS('display', 'none');
  await expect(content.locator('.hoawjimk .gqnyydlz.image')).toHaveCount(2);
  await expect(content.locator('.tivcixzd')).toHaveCount(1);
  await content.evaluate(element => { (window as any).__misskeyCwContent = element; });

  const initial = await button.evaluate(element => {
    const style = getComputedStyle(element);
    const rootStyle = getComputedStyle(document.documentElement);
    return {
      childTags: Array.from(element.children).map(child => child.tagName),
      display: style.display,
      padding: style.padding,
      borderRadius: style.borderRadius,
      fontSize: Number.parseFloat(style.fontSize),
      foreground: style.color,
      background: style.backgroundColor,
      expectedForeground: rootStyle.getPropertyValue('--cwFg').trim(),
      expectedBackground: rootStyle.getPropertyValue('--cwBg').trim(),
      before: getComputedStyle(element, '::before').content,
      labelBefore: getComputedStyle(element.querySelector(':scope > span')!, '::before').content,
      labelAfter: getComputedStyle(element.querySelector(':scope > span')!, '::after').content,
      animationName: style.animationName,
      transitionDuration: style.transitionDuration,
      rect: element.getBoundingClientRect().toJSON(),
      nativeType: (element as HTMLButtonElement).type,
    };
  });

  expect(initial.childTags).toEqual(['B', 'SPAN']);
  expect(initial.display).toBe('inline-block');
  expect(initial.padding).toBe('4px 8px');
  expect(initial.borderRadius).toBe('2px');
  expect(initial.fontSize).toBeGreaterThan(8);
  expect(initial.fontSize).toBeLessThan(13);
  expect(initial.foreground).toBe(initial.expectedForeground);
  expect(initial.background).toBe(initial.expectedBackground);
  expect(initial.before).toBe('none');
  expect(initial.labelBefore).toBe('"("');
  expect(initial.labelAfter).toBe('")"');
  expect(initial.animationName).toBe('none');
  expect(initial.transitionDuration.split(',').every(value => value.trim() === '0s')).toBe(true);
  expect(initial.rect.width).toBeGreaterThan(0);
  expect(initial.rect.height).toBeGreaterThan(0);
  expect(initial.nativeType).toBe('submit');

  await button.hover();
  const hovered = await button.evaluate(element => ({
    background: getComputedStyle(element).backgroundColor,
    expected: getComputedStyle(document.documentElement).getPropertyValue('--cwHoverBg').trim(),
  }));
  expect(hovered.background).toBe(hovered.expected);
  expect(hovered.background).not.toBe(initial.background);

  await button.focus();
  await expect(button).toBeFocused();
  await button.press('Enter');
  await expect(button.locator(':scope > b')).toHaveText('隠す');
  await expect(button.locator(':scope > span')).toHaveCount(0);
  await expect(button).toHaveAttribute('aria-expanded', 'true');
  await expect(button).toBeFocused();
  await expect(content).toHaveCSS('display', 'block');
  expect(await content.evaluate(element => element === (window as any).__misskeyCwContent)).toBe(true);
  await expect(content.locator('.hoawjimk .gqnyydlz.image')).toHaveCount(2);
  await expect(content.locator('.tivcixzd')).toBeVisible();

  // A parent timeline mutation rebinds the same note with a fresh projection. Vue keeps the
  // local showContent ref for the keyed note, so Blazor must not fold it again in OnParametersSet.
  await note.locator('footer.footer > button:has(> i.fa-plus)').click();
  const picker = page.locator('body > .qzhlnise.popup > .content > .omfetrab.ryghynhb._popup._shadow');
  await expect(picker).toHaveCount(1);
  await picker.locator(':scope > .emojis > .group.index > section:first-child > .body > button.item').first().click();
  await expect(picker).toHaveCount(0);
  await expect(button.locator(':scope > b')).toHaveText('隠す');
  await expect(content).toHaveCSS('display', 'block');
  expect(await content.evaluate(element => element === (window as any).__misskeyCwContent)).toBe(true);

  const expanded = await button.evaluate(element => ({
    height: element.getBoundingClientRect().height,
    animationName: getComputedStyle(element).animationName,
    transitionDuration: getComputedStyle(element).transitionDuration,
  }));
  expect(Math.abs(expanded.height - initial.rect.height)).toBeLessThanOrEqual(0.5);
  expect(expanded.animationName).toBe('none');
  expect(expanded.transitionDuration.split(',').every(value => value.trim() === '0s')).toBe(true);

  await button.focus();
  await button.press('Space');
  await expect(button.locator(':scope > b')).toHaveText('もっと見る');
  await expect(button.locator(':scope > span')).toHaveText('3文字 / 2ファイル / アンケート');
  await expect(button).toHaveAttribute('aria-expanded', 'false');
  await expect(button).toBeFocused();
  await expect(content).toHaveCSS('display', 'none');
  expect(await content.evaluate(element => element === (window as any).__misskeyCwContent)).toBe(true);
});

test('real note projection preserves files-only and text-plus-poll label branches', async ({ page, request }) => {
  let fixture = await request.post('/__test/cw-note/files-only');
  expect(fixture.status()).toBe(204);
  await page.goto('/__test/sign-in');
  await expect(page.locator('body > .dkgtipfy')).not.toHaveAttribute('inert', '');
  await expect.poll(async () => page.locator('._root_b6w6v_1').evaluate(
    element => getComputedStyle(element).paddingTop)).not.toBe('0px');

  let button = page.locator('.tkcbzcuz.qtqtichx p.cw > button.nrvgflfu._button');
  await expect(button.locator(':scope > span')).toHaveText('2ファイル');

  fixture = await request.post('/__test/cw-note/text-poll');
  expect(fixture.status()).toBe(204);
  await page.reload();
  button = page.locator('.tkcbzcuz.qtqtichx p.cw > button.nrvgflfu._button');
  await expect(button.locator(':scope > span')).toHaveText('7文字 / アンケート');
  await button.press('Enter');
  await expect(page.locator('.tkcbzcuz.qtqtichx > .article > .main > .body > .content .havbbuyv b')).toHaveText('abc');
  await expect.poll(async () => page.locator('.fdidabkb > .tabs > .highlight').evaluate(
    element => (element as HTMLElement).style.width)).not.toBe('');
});
