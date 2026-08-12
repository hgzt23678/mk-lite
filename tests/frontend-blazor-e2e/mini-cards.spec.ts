import { expect, test } from '@playwright/test';

test('instance and user mini cards preserve the pinned DOM, theme states, media boundary and user preview', async ({ page }) => {
  const failures: string[] = [];
  page.on('pageerror', error => failures.push(`pageerror:${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });

  await page.goto('/__test/sign-in');
  await page.goto('/__test/components/mini-cards');

  const instance = page.locator('[data-contract="instance-card-mini"]');
  await expect(instance).toHaveClass(/_root_gc11e_1/);
  await expect(instance).toHaveClass(/yellow/);
  await expect(instance.locator(':scope > img.icon')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(instance.locator(':scope > .body > .host')).toHaveText('Mastodon test instance');
  await expect(instance.locator(':scope > .body > .sub')).toHaveText('mastodon.example / Mastodon 4.6.2');
  await expect(instance.locator(':scope > .chart')).toHaveCount(0);
  const instanceStyle = await instance.evaluate(element => {
    const style = getComputedStyle(element);
    const iconStyle = getComputedStyle(element.querySelector(':scope > img.icon')!);
    return {
      backgroundColor: style.backgroundColor,
      backgroundImage: style.backgroundImage,
      padding: style.padding,
      borderRadius: style.borderRadius,
      iconWidth: iconStyle.width,
      iconHeight: iconStyle.height,
    };
  });
  expect(instanceStyle.backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
  expect(instanceStyle.backgroundImage).not.toBe('none');
  expect(instanceStyle.padding).toBe('16px');
  expect(instanceStyle.borderRadius).toBe('8px');
  expect(instanceStyle.iconWidth).toBe('34px');
  expect(instanceStyle.iconHeight).toBe('34px');

  const user = page.locator('[data-contract="user-card-mini"]');
  await expect(user).toHaveClass(/_root_18erp_1/);
  await expect(user).toHaveClass(/yellow/);
  await expect(user).toHaveClass(/red/);
  const avatar = user.locator(':scope > .avatar');
  await expect(avatar).toHaveAttribute('data-user-preview', 'alice-id');
  await expect(avatar).not.toHaveAttribute('href', /.+/);
  await expect(avatar.locator(':scope > img.inner')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(avatar.locator(':scope > .indicator.active')).toHaveCount(1);
  await expect(user.locator(':scope > .body > .name > .name')).toContainText('Alice');
  await expect(user.locator(':scope > .body > .sub > .acct')).toHaveText('@alice@xn--bcher-kva.example');
  await expect(user.locator(':scope > .chart')).toHaveCount(0);
  const userStyle = await user.evaluate(element => {
    const style = getComputedStyle(element);
    const avatarStyle = getComputedStyle(element.querySelector(':scope > .avatar')!);
    return {
      backgroundColor: style.backgroundColor,
      backgroundImage: style.backgroundImage,
      padding: style.padding,
      borderRadius: style.borderRadius,
      avatarWidth: avatarStyle.width,
      avatarHeight: avatarStyle.height,
    };
  });
  expect(userStyle.backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
  expect(userStyle.backgroundImage).not.toBe('none');
  expect(userStyle.padding).toBe('16px');
  expect(userStyle.borderRadius).toBe('8px');
  expect(userStyle.avatarWidth).toBe('34px');
  expect(userStyle.avatarHeight).toBe('34px');

  await avatar.hover();
  const preview = page.locator('[data-user-preview-popup]');
  await expect(preview).toBeVisible({ timeout: 2_000 });
  await expect(preview.locator(':scope > .info > .banner')).toHaveCSS('background-image', /favicon\.png/);
  await expect(preview.locator(':scope > .info > .description')).toContainText('Hello @world #fediverse');
  await expect(preview.locator(':scope > .info > .koudoku-button')).toHaveCount(1);
  await expect(preview.locator(':scope > .info > .banner > .followed')).toHaveCount(1);

  expect(failures).toEqual([]);
});
