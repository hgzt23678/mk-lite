import { expect, test } from '@playwright/test';

test('authenticated navbar preserves pinned desktop DOM, safe popups, compose, and POST logout', async ({ page }) => {
  let logoutMethod: string | null = null;
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });
  page.on('request', request => {
    if (new URL(request.url()).pathname === '/auth/logout') logoutMethod = request.method();
  });

  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  await expect(page.locator('button.eddddedb.canRenote').first())
    .toHaveAttribute('data-renote-button-ready', 'true');

  const navbar = page.locator('.dkgtipfy > .sidebar.mvcprjjd');
  await expect(navbar).toHaveCount(1);
  await expect(navbar).not.toHaveClass(/iconOnly/);
  await expect(navbar.locator(':scope > .body > .top > .instance > img.icon')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(navbar.locator(':scope > .body > .middle > a.index > .text')).toHaveText('タイムライン');
  await expect(navbar.locator(':scope > .body > .middle > .drive, :scope > .body > .middle > .settings')).toHaveCount(0);
  await expect(navbar.locator(':scope > .body > .middle > a.notifications')).toHaveAttribute('href', '/my/notifications');
  await expect(navbar.locator(':scope > .body > .middle > a.announcements')).toHaveAttribute('href', '/announcements');
  await expect(navbar.locator(':scope > .body > .bottom > .account > .avatar img.inner')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(navbar.locator(':scope > .body > .bottom > .account > .mk-acct.text')).toContainText('@alice');

  const navbarBackgroundAlpha = await navbar.locator(':scope > .body').evaluate(element => {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (context === null) return null;
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(navbarBackgroundAlpha, 'navbar background must not be transparent').toBe(255);

  await navbar.locator(':scope > .body > .top > .instance').click();
  let popup = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await expect(popup).toHaveCount(1);
  await expect(popup.locator('a[href="/about-misskey"]')).toHaveCount(1);
  await expect(popup.locator('a[href="/about"], a[href="/my/drive"], a[href="/settings"]')).toHaveCount(0);
  await page.keyboard.press('Escape');
  await expect(popup).toHaveCount(0);

  await navbar.locator(':scope > .body > .middle > button').click();
  const launchPad = page.locator('body > .qzhlnise.popup .szkkfdyq > .main');
  await expect(launchPad.locator(':scope > button')).toHaveCount(1);
  await expect(launchPad.locator(':scope > button')).toContainText('リロード');
  await expect(page.locator('body > .qzhlnise.popup > .content')).toHaveAttribute('style', /left:/);
  await page.keyboard.press('Escape');
  await expect(launchPad).toHaveCount(0);

  await navbar.locator(':scope > .body > .bottom > .post').click();
  const postDialog = page.locator('body > .qzhlnise.dialog > .content.top > .gafaadew.modal._popup');
  await expect(postDialog).toHaveCount(1);
  await postDialog.locator(':scope > header > .cancel').click();
  await expect(postDialog).toHaveCount(0);

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator('.dkgtipfy > .buttons > .nav')).toHaveCount(1);
  await page.locator('.dkgtipfy > .buttons > .nav').click();
  const mobileNavbar = page.locator('.dkgtipfy > .menuDrawer.kmwsukvl');
  await expect(mobileNavbar).toHaveCount(1);
  await expect(mobileNavbar.locator(':scope > .body > .top > .instance > img.icon')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(mobileNavbar.locator(':scope > .body > .middle > .drive, :scope > .body > .middle > .settings')).toHaveCount(0);
  await expect(mobileNavbar.locator(':scope > .body > .middle > a.notifications')).toHaveAttribute('href', '/my/notifications');
  await expect(mobileNavbar.locator(':scope > .body > .middle > a.announcements')).toHaveAttribute('href', '/announcements');
  await expect(mobileNavbar.locator(':scope > .body > .bottom > .account > .mk-acct.text')).toContainText('@alice');
  const mobileNavbarBackgroundAlpha = await mobileNavbar.evaluate(element => {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (context === null) return null;
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(mobileNavbarBackgroundAlpha, 'mobile drawer background must not be transparent').toBe(255);

  await mobileNavbar.locator(':scope > .body > .bottom > .account').click();
  popup = page.locator('body > .qzhlnise.popup .rrevdjwt');
  await expect(popup.locator(':scope > button.item')).toHaveCount(2);
  const logout = popup.getByRole('menuitem', { name: 'ログアウト' });
  await expect(logout).toHaveCount(1);
  await logout.click();

  await expect(page).toHaveURL(/\/$/);
  await expect(page.locator('.mk-app')).toHaveCount(1);
  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
  expect(logoutMethod).toBe('POST');
});
