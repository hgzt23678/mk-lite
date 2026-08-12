import { expect, test } from '@playwright/test';

test('server rendering and API resolve a safe cookie before Accept-Language without using Host', async ({ request }) => {
  const arabic = await request.get('/__test/components/localization', {
    headers: { 'Accept-Language': 'ar-SA,en-US;q=0.5' },
  });
  expect(arabic.ok()).toBeTruthy();
  const arabicHtml = await arabic.text();
  expect(arabicHtml).toMatch(/<html lang="ar-SA" dir="rtl">/);
  expect(arabicHtml).toContain('data-locale="ar-SA"');

  const cookiePreferred = await request.get('/__test/localization-state', {
    headers: {
      'Accept-Language': 'ar-SA,en-US;q=0.5',
      Cookie: 'misskey.lang=en-US',
      Host: 'ja-JP.tailnet.invalid',
    },
  });
  expect(cookiePreferred.ok()).toBeTruthy();
  expect(await cookiePreferred.json()).toEqual({
    currentLocale: 'en-US',
    direction: 'ltr',
    culture: 'en-US',
    showMore: 'Show more',
    supportedLocaleCount: 25,
    completeLocaleCount: 25,
  });
});

test('hydration migrates only supported Vue lang state and never trusts legacy locale JSON', async ({ page, context }) => {
  const tamperedLegacyLocale = JSON.stringify({ showMore: 'PWNED', nested: { value: '<script>' } });
  await page.addInitScript(({ legacy }) => {
    localStorage.setItem('lang', 'en-US');
    localStorage.setItem('locale', legacy);
  }, { legacy: tamperedLegacyLocale });
  await page.setExtraHTTPHeaders({ 'Accept-Language': 'ja-JP' });
  await page.goto('/__test/components/localization');

  const contract = page.locator('[data-contract="localization"]');
  await expect(contract).toHaveAttribute('data-locale', 'en-US');
  await expect(contract).toHaveAttribute('data-direction', 'ltr');
  await expect(contract.locator('[data-translation="showMore"]')).toHaveText('Show more');
  await expect(page.locator('.localization-container > .content > .fade > span')).toHaveText('Show more');
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  expect(await page.evaluate(() => localStorage.getItem('locale'))).toBe(tamperedLegacyLocale);
  expect(await page.evaluate(() => document.body.textContent?.includes('PWNED'))).toBeFalsy();

  const cookies = await context.cookies();
  expect(cookies).toEqual(expect.arrayContaining([
    expect.objectContaining({ name: 'misskey.lang', value: 'en-US', path: '/', sameSite: 'Lax' }),
  ]));

  await page.evaluate(() => {
    const oldValue = localStorage.getItem('lang');
    localStorage.setItem('lang', 'ar-SA');
    window.dispatchEvent(new StorageEvent('storage', {
      key: 'lang', oldValue, newValue: 'ar-SA', storageArea: localStorage, url: window.location.href,
    }));
  });
  await expect(contract).toHaveAttribute('data-locale', 'ar-SA');
  await expect(contract.locator('[data-translation="showMore"]')).toHaveText('عرض المزيد');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar-SA');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  await page.evaluate(() => {
    const oldValue = localStorage.getItem('lang');
    localStorage.setItem('lang', '../../not-supported');
    window.dispatchEvent(new StorageEvent('storage', {
      key: 'lang', oldValue, newValue: '../../not-supported', storageArea: localStorage, url: window.location.href,
    }));
  });
  await expect(contract).toHaveAttribute('data-locale', 'ar-SA');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar-SA');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});
