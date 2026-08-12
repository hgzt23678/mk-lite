import { expect, test } from '@playwright/test';

test('MkModalPageWindow preserves the pinned shell, navigation and close flow', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/modal-page-window');

  const modal = page.locator('.qzhlnise.contract-window[data-contract="modal-page-window"]');
  await expect(modal).toHaveClass(/\bdialog\b/);
  const root = modal.locator(':scope > .content > .hrmcaedk._narrow_');
  await expect(root).toHaveAttribute('style', 'width: 860px; height: min(660px, 100%);');
  const geometry = await root.evaluate(element => {
    const rootStyle = getComputedStyle(element);
    const headerStyle = getComputedStyle(element.querySelector(':scope > .header')!);
    const bodyStyle = getComputedStyle(element.querySelector(':scope > .body')!);
    return {
      display: rootStyle.display,
      direction: rootStyle.flexDirection,
      overflow: rootStyle.overflow,
      radius: rootStyle.borderRadius,
      width: Number.parseFloat(rootStyle.width),
      height: Number.parseFloat(rootStyle.height),
      headerHeight: headerStyle.height,
      headerBackground: headerStyle.backgroundColor,
      bodyBackground: bodyStyle.backgroundColor,
    };
  });
  expect(geometry).toMatchObject({
    display: 'flex',
    direction: 'column',
    overflow: 'hidden',
    width: 860,
    headerHeight: '52px',
  });
  expect(geometry.height).toBeGreaterThan(500);
  expect(geometry.height).toBeLessThanOrEqual(660);
  expect(geometry.radius).not.toBe('0px');
  expect(geometry.headerBackground).not.toBe('rgba(0, 0, 0, 0)');
  expect(geometry.bodyBackground).not.toBe('rgba(0, 0, 0, 0)');

  const header = root.locator(':scope > .header');
  await expect(header.locator(':scope > span:first-child')).toHaveCSS('width', '20px');
  await expect(header.locator(':scope > .title')).toHaveText('First page');
  await expect(header.locator(':scope > .title > .icon')).toHaveClass(/fa-home/);
  await expect(root.locator(':scope > .body .fdidabkb.thin > .tabs')).toBeVisible();
  await expect(root.locator('[data-page="/first"]')).toContainText('First body');
  await expect(root.locator('[data-page-footer="/first"]')).toHaveText('First footer');

  await root.locator('[data-contract="navigate"]').click();
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-path', '/second');
  await expect(header.locator(':scope > .title')).toHaveText('Second page');
  const back = header.locator(':scope > button:first-child');
  await expect(back).toHaveAttribute('aria-label', /.+/);
  await expect(root.locator('[data-page="/second"]')).toContainText('Second body');
  await expect(root.locator('[data-page-footer="/second"]')).toHaveText('Second footer');

  await back.click();
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-path', '/first');
  await expect(header.locator(':scope > .title')).toHaveText('First page');
  await expect(header.locator(':scope > span:first-child')).toHaveCSS('width', '20px');

  await header.click({ button: 'right' });
  const contextMenu = page.locator('.rrevdjwt[role="menu"]');
  await expect(contextMenu.locator(':scope > .label')).toHaveText('/first');
  await expect(contextMenu.locator(':scope > .item')).toHaveCount(5);
  await page.keyboard.press('Escape');
  await expect(contextMenu).toHaveCount(0);

  await modal.locator(':scope > .bg').evaluate(element => (element as HTMLElement).click());
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-clicked', '1');
  await expect(root).toBeVisible();

  await header.locator(':scope > button:last-child').click();
  await expect(modal).toHaveClass(/modal-leave-active/);
  await expect(page.locator('[data-contract="state"]')).toHaveAttribute('data-closed', '1');
  await expect(modal).toHaveCount(0);

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
