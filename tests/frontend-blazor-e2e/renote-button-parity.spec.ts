import { expect, test } from '@playwright/test';

test('MkRenoteButton preserves the pinned DOM, CSS, tooltip, authentication menu, quote, and renote paths', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));

  const reset = await page.request.post('/__test/reset-renote');
  expect(reset.status()).toBe(204);
  await page.goto('/__test/sign-in');
  await page.goto('/__test/components/renote-button');

  const root = page.locator('main[data-contract="renote-button"]');
  const button = root.locator('button.eddddedb.canRenote.button');
  await expect(button).toHaveCount(1);
  await expect(button.locator(':scope > i.fas.fa-retweet')).toHaveCount(1);
  await expect(button.locator(':scope > p.count')).toHaveText('15');
  await expect(button).toHaveAttribute('data-renote-button-ready', 'true');
  const banned = root.locator('button.eddddedb.private-note:not(.canRenote)');
  await expect(banned.locator(':scope > i.fas.fa-ban')).toHaveCount(1);
  await expect(banned.locator(':scope > .count')).toHaveCount(0);

  const css = await button.evaluate(element => {
    const style = getComputedStyle(element);
    const count = getComputedStyle(element.querySelector(':scope > .count')!);
    return {
      display: style.display,
      height: style.height,
      margin: style.margin,
      padding: style.padding,
      borderRadius: style.borderRadius,
      countDisplay: count.display,
      countMarginLeft: count.marginLeft,
      countOpacity: count.opacity,
    };
  });
  expect(css).toEqual({
    display: 'inline-block',
    height: '32px',
    margin: '2px',
    padding: '0px 6px',
    borderRadius: '4px',
    countDisplay: 'inline',
    countMarginLeft: '8px',
    countOpacity: '0.7',
  });

  await button.hover();
  const tooltip = page.locator('.beaffaef');
  await expect(tooltip).toBeVisible();
  await expect(tooltip.locator(':scope > .user')).toHaveCount(11);
  await expect(tooltip.locator(':scope > .omitted')).toHaveText('+4');
  await page.mouse.move(0, 0);
  await expect(tooltip).toBeHidden();

  await button.click();
  const menu = page.locator('.rrevdjwt[role="menu"]');
  await expect(menu.locator(':scope > button[role="menuitem"]')).toHaveCount(2);
  await expect(menu.locator(':scope > button').nth(0)).toContainText('Renote');
  await expect(menu.locator(':scope > button').nth(1)).toContainText('引用');
  await menu.locator(':scope > button').nth(1).click();
  const postForm = page.locator('.gafaadew.modal');
  await expect(postForm).toBeVisible();
  await expect(postForm.locator('.with-quote')).toContainText('引用付き');
  await postForm.locator('button.cancel').click();
  await expect(postForm).toHaveCount(0);

  await button.click();
  await page.locator('.rrevdjwt[role="menu"] > button').nth(0).click();
  await expect.poll(async () => (await page.request.get('/__test/renote-state')).json()).toMatchObject({
    renoteCalls: 1,
    lastRenotedId: '9dummqy0w3',
  });
  expect(errors).toEqual([]);
});
