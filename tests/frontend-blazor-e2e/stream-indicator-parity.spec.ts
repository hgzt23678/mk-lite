import { expect, test } from '@playwright/test';

test('stream indicator preserves the pinned quiet disconnect DOM, CSS, dismissal, and reload', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', error => errors.push(error.message));

  await page.goto('/__test/components/stream-indicator');
  const indicator = page.locator('.nsbbhtug');
  await expect(indicator).toBeVisible();
  await expect(indicator.locator(':scope > div')).toHaveCount(2);
  await expect(indicator.locator(':scope > div').first()).toHaveText('サーバーから切断されました');
  const buttons = indicator.locator(':scope > .command > button._textButton');
  await expect(buttons).toHaveCount(2);
  await expect(buttons.nth(0)).toHaveText('リロード');
  await expect(buttons.nth(1)).toHaveText('なにもしない');

  const css = await indicator.evaluate(element => {
    const root = getComputedStyle(element);
    const command = getComputedStyle(element.querySelector(':scope > .command')!);
    const button = getComputedStyle(element.querySelector(':scope > .command > button')!);
    return {
      position: root.position,
      zIndex: root.zIndex,
      bottom: root.bottom,
      right: root.right,
      margin: root.margin,
      padding: root.padding,
      fontSize: root.fontSize,
      color: root.color,
      backgroundColor: root.backgroundColor,
      opacity: root.opacity,
      borderRadius: root.borderRadius,
      maxWidth: root.maxWidth,
      commandDisplay: command.display,
      commandJustifyContent: command.justifyContent,
      buttonPadding: button.padding,
    };
  });
  expect(css).toMatchObject({
    position: 'fixed',
    zIndex: '16385',
    bottom: '8px',
    right: '8px',
    margin: '0px',
    padding: '6px 12px',
    color: 'rgb(255, 255, 255)',
    backgroundColor: 'rgb(0, 0, 0)',
    opacity: '0.8',
    borderRadius: '4px',
    maxWidth: '320px',
    commandDisplay: 'flex',
    commandJustifyContent: 'space-around',
  });
  expect(Number.parseFloat(css.fontSize)).toBeGreaterThan(0);
  expect(Number.parseFloat(css.buttonPadding)).toBeGreaterThan(0);

  await buttons.nth(1).click();
  await expect(indicator).toHaveCount(0);

  await page.locator('#disconnect-stream').click();
  await expect(indicator).toBeVisible();
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
    indicator.locator(':scope > .command > button').first().click(),
  ]);
  await expect(page.locator('.nsbbhtug')).toBeVisible();
  const navigationType = await page.evaluate(() =>
    (performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming).type);
  expect(navigationType).toBe('reload');
  expect(errors).toEqual([]);
});
