import { expect, test } from '@playwright/test';

test('MkPopupMenu preserves pinned popup and touch drawer contracts', async ({ browser }) => {
  const desktopContext = await browser.newContext({
    viewport: { width: 1440, height: 900 },
    locale: 'ja-JP',
    timezoneId: 'UTC',
  });
  const desktop = await desktopContext.newPage();
  const desktopErrors: string[] = [];
  desktop.on('console', message => {
    if (message.type() === 'error') desktopErrors.push(message.text());
  });
  desktop.on('pageerror', error => desktopErrors.push(error.message));
  await desktop.goto('/__test/components/popup-menu');

  const desktopSource = desktop.locator('[data-contract="open"]');
  await desktopSource.click();
  const popup = desktop.locator('body > .qzhlnise.popup.modal-popup-enter-active');
  await expect(popup).toHaveCount(1);
  const popupBackground = popup.locator(':scope > .bg._modalBg.transparent');
  const popupMenu = popup.locator(':scope > .content > .sfhdhdhq:not(.drawer) > .rrevdjwt._popup._shadow.center:not(.asDrawer)');
  await expect(popupMenu).toHaveCount(1);
  await expect(popupMenu).toHaveCSS('width', '288px');
  await expect(popupMenu.locator(':scope > .item')).toHaveCount(4);
  await expect(popupMenu.locator(':scope > .divider')).toHaveCount(1);
  await expect(popupMenu.locator(':scope > button.item').first()).toBeFocused();
  await expect(desktopSource).toHaveCSS('pointer-events', 'none');
  const popupContract = await popup.evaluate(element => {
    const root = element as HTMLElement;
    const background = root.querySelector(':scope > .bg') as HTMLElement;
    const content = root.querySelector(':scope > .content') as HTMLElement;
    return {
      rootZIndex: Number(root.style.zIndex),
      backgroundZIndex: Number(background.style.zIndex),
      contentZIndex: Number(content.style.zIndex),
      backgroundColor: getComputedStyle(background).backgroundColor,
      backdropFilter: getComputedStyle(background).backdropFilter,
    };
  });
  expect(popupContract.rootZIndex).toBeGreaterThanOrEqual(3_000_100);
  expect(popupContract.backgroundZIndex).toBe(popupContract.rootZIndex);
  expect(popupContract.contentZIndex).toBe(popupContract.rootZIndex);
  expect(popupContract.backgroundColor).toBe('rgba(0, 0, 0, 0)');
  expect(popupContract.backdropFilter).toBe('none');

  await desktop.keyboard.press('ArrowDown');
  await expect(popupMenu.locator(':scope > a.item').first()).toBeFocused();
  await popupMenu.locator(':scope > button.item').click();
  await expect(popup).toHaveCount(0);
  await expect(desktop.locator('[data-contract="action-count"]')).toHaveText('1');
  await expect(desktop.locator('[data-contract="closed-count"]')).toHaveText('1');
  await expect(desktopSource).toBeFocused();

  const completeSource = desktop.locator('[data-contract="open-complete"]');
  await completeSource.click();
  const completePopup = desktop.locator('body > .qzhlnise.popup.modal-popup-enter-active');
  const completeMenu = completePopup.locator(':scope > .content > .sfhdhdhq > .rrevdjwt');
  await expect(completeMenu.locator(':scope > .pending.item .mk-ellipsis')).toHaveCount(1);
  await expect(completeMenu.locator(':scope > a.item[href="/@alice"] > .avatar')).toHaveCount(1);
  await expect(completeMenu.locator(':scope > a.item[href="/@alice"] > .indicator')).toHaveCount(1);
  await expect(completeMenu.locator(':scope > button.item .havbbuyv')).toHaveText('Alice');
  await expect(completeMenu.locator(':scope > .item > .form-switch.checked')).toHaveCount(1);
  const parent = completeMenu.locator(':scope > button.item.parent');
  await parent.hover();
  const child = completePopup.locator('.child > .sfhdhdhr');
  await expect(parent).toHaveClass(/childShowing/);
  await expect(child.locator('.rrevdjwt > button.item', { hasText: 'Child action' })).toHaveCount(1);
  const childPosition = await child.evaluate(element => ({
    left: (element as HTMLElement).style.left,
    top: (element as HTMLElement).style.top,
    position: getComputedStyle(element).position,
  }));
  expect(childPosition.position).toBe('absolute');
  expect(childPosition.left).not.toBe('');
  expect(childPosition.top).not.toBe('');

  await completeMenu.locator(':scope > .item > .form-switch .button').click();
  await expect(desktop.locator('[data-contract="switch-value"]')).toHaveText('false');
  await expect(completePopup.locator('.child')).toHaveCount(0);
  await expect(completeMenu.locator(':scope > .pending.item')).toHaveCount(0, { timeout: 3_000 });
  await expect(completeMenu.locator(':scope > button.item', { hasText: 'Resolved' })).toHaveCount(1);

  await parent.hover();
  await completePopup.locator('.child .rrevdjwt > button.item', { hasText: 'Child action' }).click();
  await expect(completePopup).toHaveCount(0);
  await expect(desktop.locator('[data-contract="child-action-count"]')).toHaveText('1');
  await expect(desktop.locator('[data-contract="complete-closed-count"]')).toHaveText('1');
  await expect(completeSource).toBeFocused();
  expect(desktopErrors).toEqual([]);
  await desktopContext.close();

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
    hasTouch: true,
    isMobile: true,
    locale: 'ja-JP',
    timezoneId: 'UTC',
  });
  const mobile = await mobileContext.newPage();
  const mobileErrors: string[] = [];
  mobile.on('console', message => {
    if (message.type() === 'error') mobileErrors.push(message.text());
  });
  mobile.on('pageerror', error => mobileErrors.push(error.message));
  await mobile.goto('/__test/components/popup-menu');
  await mobile.locator('[data-contract="open"]').click();

  const drawer = mobile.locator('body > .qzhlnise.drawer.modal-drawer-enter-active');
  const wrapper = drawer.locator(':scope > .content > .sfhdhdhq.drawer');
  const drawerMenu = wrapper.locator(':scope > .rrevdjwt._popup._shadow.center.asDrawer');
  await expect(drawer).toHaveCount(1);
  await expect(drawerMenu).toHaveCount(1);
  expect(await drawerMenu.evaluate(element => (element as HTMLElement).style.width)).toBe('');
  const drawerContract = await wrapper.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      topLeft: style.borderTopLeftRadius,
      topRight: style.borderTopRightRadius,
      bottomRight: style.borderBottomRightRadius,
      bottomLeft: style.borderBottomLeftRadius,
    };
  });
  expect(drawerContract).toEqual({
    topLeft: '24px',
    topRight: '24px',
    bottomRight: '0px',
    bottomLeft: '0px',
  });
  await expect(drawerMenu.locator(':scope > button.item').first()).toBeFocused();
  await mobile.keyboard.press('Escape');
  await expect(drawer).toHaveCount(0);
  await expect(mobile.locator('[data-contract="closed-count"]')).toHaveText('1');
  expect(mobileErrors).toEqual([]);
  await mobileContext.close();
});
