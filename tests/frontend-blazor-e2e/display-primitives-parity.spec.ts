import { expect, test } from '@playwright/test';

test('pinned display primitives preserve Misskey DOM, theme surfaces, links, and avatar geometry', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await page.goto('/__test/components/display-primitives');

  const mention = page.locator('[data-primitive="mention"]');
  await expect(mention).toHaveClass('akbvjaqn isMe');
  await expect(mention).toHaveAttribute('href', '/@alice');
  await expect(mention).toHaveAttribute('data-user-preview', '@alice');
  await expect(mention.locator(':scope > img.icon')).toHaveAttribute('src', '/avatar/@alice@127.0.0.1');
  await expect(mention.locator(':scope > .main > .username')).toHaveText('@alice');
  await expect(mention.locator(':scope > .main > .host')).toHaveCount(0);
  expect(await mention.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');

  const mfmMention = page.locator('[data-primitive="mfm-mention"] > .havbbuyv > .akbvjaqn');
  await expect(mfmMention).toHaveAttribute('href', '/@alice');
  await expect(mfmMention.locator(':scope > img.icon')).toHaveAttribute('src', '/avatar/@alice@127.0.0.1');

  const caution = page.locator('[data-primitive="remote-caution"]');
  await expect(caution).toHaveClass('jmgmzlwq _block');
  await expect(caution.locator(':scope > i.fas.fa-exclamation-triangle')).toHaveCount(1);
  await expect(caution.locator(':scope > a.link')).toHaveAttribute('href', 'https://remote.example/@alice');
  await expect(caution.locator(':scope > a.link')).toHaveAttribute('rel', 'nofollow noopener');
  expect(await caution.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');

  const online = page.locator('[data-primitive="online-indicator"]');
  await expect(online).toHaveClass('fzgwjkgc online');
  expect(await online.evaluate(element => getComputedStyle(element).backgroundColor)).toBe('rgb(88, 212, 201)');

  const avatars = page.locator('[data-primitive="avatars"]');
  await expect(avatars.locator(':scope > div')).toHaveCount(2);
  expect(await avatars.locator(':scope > div').first().evaluate(element => ({
    width: getComputedStyle(element).width,
    height: getComputedStyle(element).height,
    marginRight: getComputedStyle(element).marginRight,
  }))).toEqual({ width: '32px', height: '32px', marginRight: '8px' });

  await expect(page.locator('[data-primitive="file-type"]')).toHaveClass('mk-file-type-icon');
  await expect(page.locator('[data-primitive="file-type"] > i.fas.fa-file-image')).toHaveCount(1);
});
