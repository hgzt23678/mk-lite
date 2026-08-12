import { expect, test } from '@playwright/test';

declare global {
  interface Window {
    __pageHeaderUnderlineSamples?: number[];
    __pageHeaderObserved?: number;
    __pageHeaderDisconnected?: number;
  }
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    const NativeResizeObserver = window.ResizeObserver;
    window.__pageHeaderObserved = 0;
    window.__pageHeaderDisconnected = 0;
    window.ResizeObserver = class extends NativeResizeObserver {
      private pageHeader = false;

      observe(target: Element, options?: ResizeObserverOptions) {
        if (target.querySelector(':scope > .fdidabkb, :scope .fdidabkb') !== null) {
          this.pageHeader = true;
          window.__pageHeaderObserved = (window.__pageHeaderObserved ?? 0) + 1;
        }
        super.observe(target, options);
      }

      disconnect() {
        if (this.pageHeader) {
          window.__pageHeaderDisconnected = (window.__pageHeaderDisconnected ?? 0) + 1;
        }
        super.disconnect();
      }
    };
  });
  await page.goto('/__test/page-header');
});

test('wide header preserves pinned metadata DOM, CSS, background, tabs, tooltip, actions, underline motion, and scroll', async ({ page }) => {
  const header = page.locator('.fixture-page-header.fdidabkb');
  await expect(header).toHaveCount(1);
  await expect(header).toHaveAttribute('data-contract', 'page-header');
  await expect(header).not.toHaveClass(/slim/);
  await expect(header).not.toHaveClass(/thin/);
  await expect(header).toHaveCSS('height', '55px');
  await expect(header).toHaveCSS('background-color', 'rgba(34, 68, 102, 0.85)');
  expect(Number.parseFloat(await header.evaluate(element => getComputedStyle(element).borderBottomWidth))).toBeGreaterThan(0);
  await page.evaluate(() => {
    document.querySelector('.fixture-page-header')?.removeAttribute('data-header-background');
    document.documentElement.style.setProperty('--bg', 'rgb(12, 34, 56)');
  });
  await expect(header).toHaveCSS('background-color', 'rgba(12, 34, 56, 0.85)');

  const title = header.locator(':scope > .titleContainer');
  await expect(title.locator(':scope > i.icon.fas.fa-vial')).toHaveCount(1);
  await expect(title.locator(':scope > .title > .title')).toHaveText('契約ヘッダー');
  await expect(title.locator(':scope > .title > .subtitle')).toHaveText('icon subtitle');
  await expect(title).not.toHaveAttribute('role', /.+/);

  const tabs = header.locator(':scope > .tabs');
  await expect(tabs).toHaveAttribute('role', 'tablist');
  await expect(tabs.locator(':scope > button.tab')).toHaveCount(3);
  const overview = tabs.locator(':scope > button.tab').nth(0);
  const activity = tabs.locator(':scope > button.tab').nth(1);
  await expect(overview).toHaveClass(/active/);
  await expect(overview).toHaveAttribute('aria-selected', 'true');
  await expect(activity).toHaveAttribute('aria-selected', 'false');

  const highlight = tabs.locator(':scope > .highlight');
  await expect.poll(() => highlight.evaluate(element => (element as HTMLElement).style.width)).not.toBe('');
  const initialHighlight = await highlight.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      left: Number.parseFloat((element as HTMLElement).style.left),
      width: Number.parseFloat((element as HTMLElement).style.width),
      height: style.height,
      bottom: style.bottom,
      duration: style.transitionDuration,
      timing: style.transitionTimingFunction,
      radius: style.borderRadius,
    };
  });
  expect(initialHighlight.width).toBeGreaterThan(0);
  expect(initialHighlight.height).toBe('3px');
  expect(initialHighlight.bottom).toBe('0px');
  expect(initialHighlight.duration).toBe('0.2s');
  expect(initialHighlight.timing).toBe('ease');
  expect(Number.parseFloat(initialHighlight.radius)).toBeGreaterThan(100);

  const action = header.locator(':scope > .buttons.right > button[aria-label="metadataを切り替え"]');
  const tooltip = page.locator('body > .buebdbiu[data-page-header-tooltip]');
  await expect(action).toHaveClass(/highlighted/);
  const touchBubbled = await action.evaluate(element => {
    let bubbled = false;
    const listener = () => { bubbled = true; };
    document.addEventListener('touchstart', listener);
    element.dispatchEvent(new Event('touchstart', { bubbles: true, cancelable: true }));
    element.dispatchEvent(new Event('touchend', { bubbles: true, cancelable: true }));
    document.removeEventListener('touchstart', listener);
    return bubbled;
  });
  expect(touchBubbled).toBeFalsy();
  await expect(tooltip).toHaveCount(0, { timeout: 1_000 });
  await action.hover();
  await expect(tooltip).toHaveText('metadataを切り替え');
  await expect(tooltip).toHaveAttribute('role', 'tooltip');
  await expect(tooltip).toHaveCSS('pointer-events', 'none');
  await page.mouse.move(0, 0);
  await expect(tooltip).toHaveCount(0, { timeout: 1_000 });

  await action.click();
  await expect(header).toHaveCSS('background-color', 'rgba(34, 68, 102, 0.85)');
  await page.evaluate(() => document.documentElement.style.removeProperty('--bg'));
  await expect(title.locator(':scope > .avatar.eiwwqkts > .inner')).toHaveAttribute('src', '/static-assets/favicon.png');
  await expect(title.locator(':scope > .avatar > .indicator.fzgwjkgc.online')).toHaveCount(1);
  await expect(page.locator('[data-contract-state]')).toContainText('|1|');
  await action.click();
  await expect(title.locator(':scope > .title > .title.havbbuyv.nowrap')).toContainText('Header');
  await expect(page.locator('[data-contract-state]')).toContainText('|2|');
  await action.click();
  await expect(title.locator(':scope > i.icon.fas.fa-vial')).toHaveCount(1);

  await page.evaluate(() => {
    window.__pageHeaderUnderlineSamples = [];
    const highlightElement = document.querySelector('.fixture-page-header > .tabs > .highlight');
    const deadline = performance.now() + 700;
    const sample = () => {
      if (highlightElement instanceof HTMLElement) {
        window.__pageHeaderUnderlineSamples!.push(Number.parseFloat(getComputedStyle(highlightElement).left));
      }
      if (performance.now() < deadline) requestAnimationFrame(sample);
    };
    requestAnimationFrame(sample);
  });
  await activity.click();
  await expect(activity).toHaveClass(/active/);
  await expect(page.locator('[data-contract-state]')).toContainText('activity|');
  await expect.poll(() => highlight.evaluate(element => Number.parseFloat((element as HTMLElement).style.left)))
    .not.toBe(initialHighlight.left);
  await page.waitForTimeout(750);
  const samples = await page.evaluate(() => window.__pageHeaderUnderlineSamples ?? []);
  const distinctSamples = new Set(samples.filter(Number.isFinite).map(value => value.toFixed(2)));
  expect(distinctSamples.size).toBeGreaterThanOrEqual(3);
  const finalRect = await activity.evaluate(element => element.getBoundingClientRect().toJSON());
  const parentRect = await tabs.evaluate(element => element.getBoundingClientRect().toJSON());
  const finalHighlight = await highlight.evaluate(element => ({
    left: Number.parseFloat((element as HTMLElement).style.left),
    width: Number.parseFloat((element as HTMLElement).style.width),
  }));
  expect(Math.abs(finalHighlight.left - (finalRect.left - parentRect.left))).toBeLessThanOrEqual(0.75);
  expect(Math.abs(finalHighlight.width - finalRect.width)).toBeLessThanOrEqual(0.75);

  const scroll = page.locator('.fixture-page-header-scroll');
  await scroll.evaluate(element => { element.scrollTop = 650; });
  await expect.poll(() => scroll.evaluate(element => element.scrollTop)).toBeGreaterThan(600);
  await title.click();
  await expect.poll(() => scroll.evaluate(element => element.scrollTop)).toBeLessThan(5);
});

test('resize, slim popup, keyboard focus, thin/omit branches, and route leave preserve lifecycle', async ({ page }) => {
  const header = page.locator('.fixture-page-header.fdidabkb');
  await expect.poll(() => page.evaluate(() => window.__pageHeaderObserved ?? 0)).toBeGreaterThan(0);

  await page.locator('[data-contract-resize]').click();
  await expect(header).toHaveClass(/slim/);
  await expect(header.locator(':scope > .tabs')).toHaveCount(0);
  await expect(header.locator(':scope > .buttons.left > .avatar.eiwwqkts')).toHaveCount(1);
  const title = header.locator(':scope > .titleContainer[data-tabs-popup-trigger=true]');
  await expect(title).toHaveAttribute('role', 'button');
  await expect(title).toHaveAttribute('tabindex', '0');
  await expect(title.locator('.subtitle.activeTab')).toContainText('概要');

  await title.click();
  let popup = page.locator('body > .qzhlnise.popup');
  await expect(popup).toHaveCount(1);
  await expect(title).toHaveAttribute('aria-expanded', 'true');
  const menuItems = popup.locator('.rrevdjwt > button.item');
  await expect(menuItems).toHaveCount(3);
  await expect(menuItems.nth(0)).toBeDisabled();
  await expect(menuItems.nth(0)).toHaveClass(/active/);
  await menuItems.nth(1).click();
  await expect(popup).toHaveCount(0);
  await expect(title).toHaveAttribute('aria-expanded', 'false');
  await expect(title.locator('.subtitle.activeTab')).toContainText('アクティビティ');

  await title.focus();
  await title.press('ArrowDown');
  popup = page.locator('body > .qzhlnise.popup');
  await expect(popup).toHaveCount(1);
  await expect(popup.locator('.rrevdjwt > button.item:not(:disabled)').first()).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(popup).toHaveCount(0);
  await expect(title).toBeFocused();

  await page.locator('[data-contract-thin]').click();
  await expect(header).toHaveClass(/thin/);
  await expect(header).toHaveCSS('height', '45px');

  await page.locator('[data-contract-omit]').click();
  await expect(header.locator(':scope > .titleContainer')).toHaveCount(0);
  await expect(header.locator(':scope > .tabs > button.tab')).toHaveCount(3);
  await expect(header.locator(':scope > .buttons.left')).toHaveCount(1);
  await expect.poll(() => header.locator(':scope > .tabs > .highlight').evaluate(
    element => (element as HTMLElement).style.width)).not.toBe('');

  const observed = await page.evaluate(() => window.__pageHeaderObserved ?? 0);
  await header.locator(':scope > .tabs > button.tab').nth(2).click();
  await expect(page).toHaveURL(/\/__test\/key-value\?from=page-header$/);
  await expect(page.locator('.fixture-page-header')).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => window.__pageHeaderDisconnected ?? 0)).toBeGreaterThanOrEqual(observed);
  await page.goBack();
  await expect(page).toHaveURL(/\/__test\/page-header$/);
  await expect(page.locator('.fixture-page-header')).toHaveCount(1);
  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});
