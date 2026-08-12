import { expect, test } from '@playwright/test';

test('MkAvatar preserves the v12 link, cat, square, static-image, and click branches', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      squareAvatars: true,
      disableShowingAnimatedImages: true,
    }));
  });

  await page.goto('/__test/components/avatar');
  const linked = page.locator('[data-avatar="linked"]');
  await expect(linked).toHaveClass(/eiwwqkts _noSelect cat square linked-avatar/);
  await expect(linked).toHaveAttribute('href', '/@cat');
  await expect(linked).toHaveAttribute('target', '_blank');
  await expect(linked).toHaveAttribute('data-user-preview', '9cat');
  await expect(linked.locator(':scope > img.inner')).toHaveAttribute(
    'src',
    '/static-assets/favicon.png?avatar=cat&static=1');
  expect(await linked.locator(':scope > img.inner').getAttribute('alt')).toBeNull();
  await expect(linked.locator(':scope > .indicator.active')).toHaveCount(1);
  await expect(linked).toHaveCSS('width', '64px');
  await expect(linked).toHaveCSS('height', '64px');
  await expect(linked).toHaveCSS('border-radius', /20%|12\.8px/);
  await expect(linked.locator(':scope > img.inner')).toHaveCSS('border-radius', /20%|12\.8px/);

  const ears = await linked.evaluate(element => ({
    beforeContent: getComputedStyle(element, '::before').content,
    beforeBorder: getComputedStyle(element, '::before').borderTopWidth,
    beforeHeight: getComputedStyle(element, '::before').height,
    afterContent: getComputedStyle(element, '::after').content,
  }));
  expect(ears.beforeContent).toBe('""');
  expect(ears.beforeBorder).toBe('4px');
  expect(ears.beforeHeight).toBe('32px');
  expect(ears.afterContent).toBe('""');

  await linked.hover();
  await expect.poll(async () => linked.evaluate(element =>
    getComputedStyle(element, '::before').animationName)).toBe('earwiggleleft');
  await expect.poll(async () => linked.evaluate(element =>
    getComputedStyle(element, '::after').animationName)).toBe('earwiggleright');

  const disabled = page.locator('[data-avatar="disabled"]');
  await expect(disabled).toHaveCount(1);
  await expect(disabled).not.toHaveAttribute('data-user-preview', /.+/);
  await disabled.click();
  await expect(page.locator('#disabled-clicks')).toHaveText('1');
  await expect(page.locator('#linked-clicks')).toHaveText('0');
  expect(failures).toEqual([]);
});
