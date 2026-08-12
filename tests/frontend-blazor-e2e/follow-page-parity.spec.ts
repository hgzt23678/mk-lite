import { expect, test } from '@playwright/test';

test('follow page preserves the v12 confirmation flow and durable follow command', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push('console:' + message.text());
  });
  page.on('pageerror', error => failures.push('page:' + error.message));
  page.on('response', response => {
    if (response.status() >= 400) failures.push('http:' + response.status() + ':' + new URL(response.url()).pathname);
  });

  await page.request.post('/__test/reset-user-preview');
  await page.goto('/__test/sign-in');
  await page.goto('/authorize-follow?acct=alice-id');
  await expect(page.locator('.mk-follow-page')).toHaveAttribute('data-follow-state', 'confirming');

  const dialog = page.locator('.qzhlnise.dialog');
  await expect(dialog).toHaveCount(1);
  await expect(dialog.locator('.body')).toContainText('Alice');
  await dialog.locator('.buttons button').first().click();
  await expect(page.locator('.mk-follow-page')).toHaveAttribute('data-follow-state', 'completed');

  await expect.poll(async () => {
    const response = await page.request.get('/__test/user-preview-state');
    const state = await response.json() as { followCalls: number; lastQuery: string | null };
    return state.followCalls + ':' + state.lastQuery;
  }).toBe('1:alice-id');

  expect(failures).toEqual([]);
  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
});
