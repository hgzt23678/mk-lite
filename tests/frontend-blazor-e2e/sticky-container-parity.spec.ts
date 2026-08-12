import { expect, test } from '@playwright/test';

test('MkStickyContainer preserves pinned nested offsets and resize lifecycle', async ({ page }) => {
  await page.goto('/__test/sticky-container');

  const outer = page.locator('.fixture-sticky-outer');
  const outerHeader = outer.locator(':scope > div').nth(0);
  const outerBody = outer.locator(':scope > div').nth(1);
  const inner = outerBody.locator(':scope > .fixture-sticky-inner');
  const innerHeader = inner.locator(':scope > div').nth(0);
  const innerBody = inner.locator(':scope > div').nth(1);

  await expect(outer).toHaveAttribute('data-contract', 'outer');
  await expect(inner).toHaveAttribute('data-contract', 'inner');
  await expect.poll(() => outerBody.getAttribute('data-sticky-container-header-height')).toBe('48');
  await expect.poll(() => innerBody.getAttribute('data-sticky-container-header-height')).toBe('32');
  await expect.poll(() => innerBody.evaluate(
    element => getComputedStyle(element).getPropertyValue('--stickyTop').trim())).toBe('80px');

  const initial = await outer.evaluate(element => {
    const outerHeaderElement = element.children[0] as HTMLElement;
    const outerBodyElement = element.children[1] as HTMLElement;
    const innerElement = outerBodyElement.querySelector(':scope > .fixture-sticky-inner') as HTMLElement;
    const innerHeaderElement = innerElement.children[0] as HTMLElement;
    const innerBodyElement = innerElement.children[1] as HTMLElement;
    return {
      outerTopDeclaration: outerHeaderElement.style.top,
      outerComputedTop: getComputedStyle(outerHeaderElement).top,
      outerStickyTop: getComputedStyle(outerBodyElement).getPropertyValue('--stickyTop').trim(),
      outerPosition: getComputedStyle(outerHeaderElement).position,
      outerZ: getComputedStyle(outerHeaderElement).zIndex,
      innerTopDeclaration: innerHeaderElement.style.top,
      innerComputedTop: getComputedStyle(innerHeaderElement).top,
      innerStickyTop: getComputedStyle(innerBodyElement).getPropertyValue('--stickyTop').trim(),
    };
  });

  expect(initial).toEqual({
    outerTopDeclaration: 'var(--stickyTop, 0)',
    outerComputedTop: '0px',
    outerStickyTop: '48px',
    outerPosition: 'sticky',
    outerZ: '1000',
    innerTopDeclaration: 'var(--stickyTop, 0)',
    innerComputedTop: '48px',
    innerStickyTop: '80px',
  });

  await page.locator('.fixture-sticky-outer-height').evaluate(element => {
    (element as HTMLElement).style.height = '96px';
  });
  await expect.poll(() => outerBody.getAttribute('data-sticky-container-header-height')).toBe('96');
  await expect.poll(() => innerHeader.evaluate(element => getComputedStyle(element).top)).toBe('96px');
  await expect.poll(() => innerBody.evaluate(
    element => getComputedStyle(element).getPropertyValue('--stickyTop').trim())).toBe('128px');

  await page.goto('/');
  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});
