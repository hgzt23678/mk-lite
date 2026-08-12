import { expect, test } from '@playwright/test';

test('code, formula, error and not-found preserve the pinned browser contracts', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/code-formula-error');

  const blockCode = page.locator('[data-contract="code"] > pre.fixture-code');
  await expect(blockCode).toHaveClass(/language-javascript/);
  await expect(blockCode.locator(':scope > code > .token.keyword')).toHaveText('const');
  await expect(blockCode.locator(':scope > code > .token.number')).toHaveText('42');
  await expect(blockCode).toHaveCSS('background-color', 'rgb(39, 40, 34)');

  const inlineCode = page.locator('code.fixture-inline-code');
  await expect(inlineCode).toHaveClass(/language-js/);
  await expect(inlineCode).toHaveText('<unsafe>');
  await expect(inlineCode.locator(':scope > .token.operator')).toHaveText(['<', '>']);

  const blockFormula = page.locator('.fixture-block-formula');
  await expect(blockFormula.locator(':scope > .katex')).toBeVisible();
  await expect(blockFormula).toContainText('x2+y2');
  const inlineFormula = page.locator('.fixture-inline-formula');
  await expect(inlineFormula.locator(':scope > .katex')).toBeVisible();
  await expect(inlineFormula).toContainText('12');

  const error = page.locator('[data-contract="error"] > .mjndxjcg');
  await expect(error).toHaveAttribute('data-motion-state', 'entered');
  await expect(error.locator(':scope > img._ghost')).toHaveCSS('height', '128px');
  await expect(error.locator(':scope > p')).toContainText('問題が発生しました');
  await error.locator(':scope > .button').click();
  await expect(page.locator('[data-contract="retry-count"]')).toHaveText('1');

  await expect(page.locator('[data-contract="not-found"] .ipledcug > ._fullinfo > div'))
    .toHaveText('指定されたURLに該当するページはありませんでした。');
  await expect(page).toHaveTitle('見つかりません');
  await expect(page.locator('head meta[name="misskey:page-icon"]'))
    .toHaveAttribute('content', 'fas fa-exclamation-triangle');
  expect(browserFailures).toEqual([]);
});
