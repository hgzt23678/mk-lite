import { expect, test } from '@playwright/test';

test('note page preserves the v12 detail hierarchy and notes/show-backed projection', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}:${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
  });

  await page.goto('/__test/sign-in');
  await page.goto('/notes/9dummqy0w3', { waitUntil: 'domcontentloaded' });

  const pageRoot = page.locator('.fcuexfpr');
  await expect(pageRoot).toHaveAttribute('data-note-page-state', 'loaded');
  await expect(pageRoot.locator(':scope > .note > .main._gap > .note._gap')).toHaveCount(1);

  const detailed = pageRoot.locator(':scope .lxwezrsl._block').first();
  await expect(detailed).toHaveCount(1);
  await expect(detailed.locator(':scope > .article > .header > .body > .top > .name')).toContainText('Alice');
  await expect(detailed.locator(':scope > .article > .main > .body > .content')).toContainText('Misskey');
  await expect(detailed.locator(':scope > .article > .main > .body > .content')).toContainText('fediverse');
  await expect(detailed.locator(':scope > .article > .main > .footer > .button')).toHaveCount(4);

  const background = await pageRoot.evaluate(element => getComputedStyle(element).backgroundColor);
  expect(background).not.toBe('rgba(0, 0, 0, 0)');
  expect(background).not.toBe('transparent');

  await page.goto('/notes/unknown-note-id', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.fcuexfpr[data-note-page-state="error"][data-error-code="NOTE_NOT_FOUND"]')).toHaveCount(1);
  expect(failures).toEqual([]);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
});
