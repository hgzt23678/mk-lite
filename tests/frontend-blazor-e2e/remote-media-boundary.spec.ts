import { expect, test } from '@playwright/test';

test('Blazor never emits or requests an unproxied remote media URL', async ({ page }) => {
  const remoteRequests: string[] = [];
  page.on('request', request => {
    if (new URL(request.url()).hostname === 'tracker.invalid') remoteRequests.push(request.url());
  });

  const response = await page.goto('/__test/security/remote-media');
  expect(response?.status()).toBe(200);
  const fixture = page.locator('[data-contract="remote-media-boundary"]');
  await expect(fixture).toBeVisible();
  await expect(fixture).not.toContainText('tracker.invalid');
  expect(await fixture.evaluate(element => element.outerHTML)).not.toContain('tracker.invalid');
  expect(remoteRequests).toEqual([]);

  await expect(fixture.locator('.eiwwqkts img.inner')).toHaveAttribute(
    'src',
    '/static-assets/user-unknown.png');
  await expect(fixture.locator('.gird-container img')).toHaveCount(1);
  await expect(fixture.locator('.announcements img')).toHaveCount(1);
  await expect(fixture.locator('.announcements img')).toHaveAttribute(
    'src',
    '/static-assets/favicon.png');
  await expect(fixture.locator('.tdflqwzn img')).toHaveCount(0);
});
