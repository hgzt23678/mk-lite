import { expect, test } from '@playwright/test';

test('MFM preserves Misskey v12 DOM, routing, emoji, author fallback, and motion setting', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));
  await page.addInitScript(() => localStorage.setItem('pizzax::base', JSON.stringify({
    animatedMfm: false,
    useOsNativeEmojis: false,
    disableShowingAnimatedImages: false,
    showFullAcct: false,
  })));

  await page.goto('/__test/components/mfm');

  const structure = page.locator('[data-mfm="structure"] > .havbbuyv');
  await expect(structure).toHaveAttribute('data-fallthrough', 'preserved');
  await expect(structure.locator(':scope > br')).toHaveCount(1);
  await expect(structure.locator(':scope > a.ieqqeuvs._link > .schema')).toHaveText('https:');
  await expect(structure.locator(':scope > a.ieqqeuvs._link > .hostname')).toHaveText('bücher.example');
  await expect(structure.locator(':scope > a.ieqqeuvs._link > .port')).toHaveText(':8443');
  await expect(structure.locator(':scope > a.ieqqeuvs._link > .pathname')).toHaveText('/a b');
  await expect(structure.locator(':scope > a.ieqqeuvs._link > .query')).toHaveText('?q=x y');
  await expect(structure.locator(':scope > a.ieqqeuvs._link > .hash')).toHaveText('#z z');
  await expect(structure.locator(':scope > a.akbvjaqn')).toHaveAttribute('href', '/@bob@remote.example');
  await expect(structure.locator(':scope > a[href="/explore/tags/topic"]')).toHaveText('#topic');
  await expect(structure.locator(':scope > img[alt=":party:"]')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(structure.locator(':scope > img[alt="👍"]')).toHaveAttribute('src', /\/twemoji\/1f44d\.svg$/);

  const motion = page.locator('[data-mfm="motion"] > .havbbuyv > span');
  await expect(motion).toHaveText('motion');
  await expect(motion).toHaveAttribute('style', 'display: inline-block;');
  expect(await motion.evaluate(element => getComputedStyle(element).animationName)).toBe('none');

  const nowrap = page.locator('[data-mfm="nowrap"] > .havbbuyv.nowrap');
  await expect(nowrap).toHaveText('one two');
  await expect(nowrap.locator('br')).toHaveCount(0);
  expect(await nowrap.evaluate(element => getComputedStyle(element).whiteSpace)).toBe('pre');
  expect(failures).toEqual([]);
});
