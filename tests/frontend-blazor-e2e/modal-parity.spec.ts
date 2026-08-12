import { expect, test } from '@playwright/test';

test('MkModal preserves popup, dialog-top, drawer, pointer, focus and transition contracts', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/modal');
  await expect(page.locator('.mk-app')).not.toHaveAttribute('inert', '');
  const events = page.locator('[data-contract="events"]');
  const source = page.locator('[data-contract="source"]');
  await source.click();
  let modal = page.locator('.qzhlnise.contract-modal');
  let surface = modal.locator(':scope > .content > .contract-surface');

  await expect(modal).toHaveClass(/\bpopup\b/);
  await expect(modal).toHaveAttribute('data-fallthrough', 'modal');
  await expect(modal.locator(':scope > .bg')).toHaveClass('bg _modalBg transparent');
  await expect(surface).toHaveAttribute('data-modal-type', 'popup');
  await expect(events).toHaveAttribute('data-opening', '1');
  await expect(events).toHaveAttribute('data-opened', '1');
  await expect(surface.locator('[data-contract="first"]')).toBeFocused();
  await expect(source).toHaveCSS('pointer-events', 'none');

  const popupGeometry = await modal.evaluate(element => {
    const root = element as HTMLElement;
    const background = root.querySelector(':scope > .bg') as HTMLElement;
    const content = root.querySelector(':scope > .content') as HTMLElement;
    const contentStyle = getComputedStyle(content);
    return {
      rootZ: root.style.zIndex,
      backgroundZ: background.style.zIndex,
      contentZ: content.style.zIndex,
      contentPosition: contentStyle.position,
      left: Number.parseFloat(contentStyle.left),
      top: Number.parseFloat(contentStyle.top),
      transformOrigin: root.style.getPropertyValue('--transformOrigin').trim(),
      background: getComputedStyle(background).backgroundColor,
      backdrop: getComputedStyle(background).backdropFilter,
    };
  });
  expect(popupGeometry.rootZ).toBe('3000100');
  expect(popupGeometry.backgroundZ).toBe(popupGeometry.rootZ);
  expect(popupGeometry.contentZ).toBe(popupGeometry.rootZ);
  expect(popupGeometry.contentPosition).toBe('fixed');
  expect(popupGeometry.left).toBeGreaterThanOrEqual(0);
  expect(popupGeometry.top).toBeGreaterThan(96);
  expect(popupGeometry.transformOrigin).toContain('top');
  expect(popupGeometry.background).toBe('rgba(0, 0, 0, 0)');
  expect(popupGeometry.backdrop).toBe('none');

  await surface.evaluate(element => {
    const background = element.parentElement?.previousElementSibling as HTMLElement;
    element.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    window.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
    background.click();
  });
  await expect(events).toHaveAttribute('data-clicked', '0');
  await page.waitForTimeout(120);
  await modal.locator(':scope > .bg').click({ position: { x: 2, y: 2 } });
  await expect(events).toHaveAttribute('data-clicked', '1');
  await page.keyboard.press('Escape');
  await expect(events).toHaveAttribute('data-escape', '1');
  await expect(surface).toBeVisible();

  await surface.locator('[data-contract="last"]').focus();
  await page.keyboard.press('Tab');
  await expect(surface.locator('[data-contract="close"]')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(surface.locator('[data-contract="first"]')).toBeFocused();

  await surface.locator('[data-contract="close"]').click();
  await expect(events).toHaveAttribute('data-close', '1');
  await expect(source).toHaveCSS('pointer-events', 'auto');
  await expect(events).toHaveAttribute('data-closed', '1');
  await expect(modal).toHaveCSS('display', 'none');
  await expect(source).toBeFocused();

  await page.locator('[data-contract="dialog-top"]').click();
  modal = page.locator('.qzhlnise.contract-modal');
  surface = modal.locator(':scope > .content > .contract-surface');
  await expect(modal).toHaveClass(/\bdialog\b/);
  await expect(modal.locator(':scope > .content')).toHaveClass('content top');
  await expect(surface).toHaveAttribute('data-modal-type', 'dialog:top');
  await expect(surface.locator('[data-contract="first"]')).toBeFocused();
  const dialogBackground = await modal.locator(':scope > .bg').evaluate(element => getComputedStyle(element).backgroundColor);
  expect(dialogBackground).not.toBe('rgba(0, 0, 0, 0)');
  await surface.locator('[data-contract="close"]').click();
  await expect(modal).toHaveCSS('display', 'none');

  await page.locator('[data-contract="drawer"]').click();
  modal = page.locator('.qzhlnise.contract-modal');
  surface = modal.locator(':scope > .content > .contract-surface');
  await expect(modal).toHaveClass(/\bdrawer\b/);
  await expect(modal.locator(':scope > .bg')).toHaveClass('bg _modalBg');
  await expect(surface).toHaveAttribute('data-modal-type', 'drawer');
  expect(Number(await surface.getAttribute('data-max-height'))).toBeCloseTo((await page.evaluate(() => innerHeight)) / 1.5, 2);
  const drawerGeometry = await modal.evaluate(element => {
    const root = getComputedStyle(element);
    const content = getComputedStyle(element.querySelector(':scope > .content')!);
    return {
      rootPosition: root.position,
      rootWidth: root.width,
      rootHeight: root.height,
      contentPosition: content.position,
      contentBottom: content.bottom,
      contentLeft: content.left,
      contentRight: content.right,
    };
  });
  expect(drawerGeometry).toMatchObject({
    rootPosition: 'fixed',
    contentPosition: 'fixed',
    contentBottom: '0px',
    contentLeft: '0px',
    contentRight: '0px',
  });

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
