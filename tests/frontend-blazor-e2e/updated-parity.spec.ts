import { expect, test } from '@playwright/test';

function cssMilliseconds(value: string) {
  const trimmed = value.trim();
  if (trimmed.endsWith('ms')) return Number.parseFloat(trimmed);
  if (trimmed.endsWith('s')) return Number.parseFloat(trimmed) * 1_000;
  return Number.NaN;
}

async function openUpdate(page: import('@playwright/test').Page) {
  await page.locator('#show-updated').click();
  const root = page.locator('.qzhlnise');
  await expect(root).toHaveAttribute('data-motion-state', 'entered');
  return root;
}

test('MkUpdated preserves the pinned DOM, localized runtime data, CSS, sparkle, and opaque surface', async ({ page }) => {
  await page.goto('/__test/components/updated');
  const root = await openUpdate(page);
  await expect(root).toHaveClass(/\bdialog\b/);
  await expect(root).toHaveAttribute('role', 'dialog');
  await expect(root).toHaveAttribute('aria-modal', 'true');
  await expect(root).toHaveAttribute('aria-label', 'Misskeyが更新されました！');
  await expect(root.locator(':scope > .bg._modalBg')).toHaveCount(1);
  const panel = root.locator(':scope > .content > .ewlycnyt');
  await expect(panel.locator(':scope > .title > .mk-sparkle > span')).toHaveText('Misskeyが更新されました！');
  await expect(panel.locator(':scope > .version')).toHaveText('✨12.119.2-port.1🚀');
  const buttons = panel.locator(':scope > button.bghgjjyj._button.full');
  await expect(buttons).toHaveCount(2);
  await expect(buttons.nth(0).locator(':scope > .content')).toHaveText('更新情報を見る');
  await expect(buttons.nth(1)).toHaveClass(/\bgotIt\b/);
  await expect(buttons.nth(1)).toHaveClass(/\bprimary\b/);
  await expect(buttons.nth(1).locator(':scope > .content')).toHaveText('わかった');
  await expect(panel.locator('.mk-sparkle > svg').first()).toBeVisible();

  const contract = await panel.evaluate(element => {
    const style = getComputedStyle(element);
    const version = getComputedStyle(element.querySelector(':scope > .version')!);
    const gotIt = getComputedStyle(element.querySelector(':scope > .gotIt')!);
    const root = element.closest('.qzhlnise')!;
    const content = root.querySelector(':scope > .content')!;
    const background = root.querySelector(':scope > .bg')!;
    const backgroundColor = style.backgroundColor.match(/[\d.]+/g)?.map(Number) ?? [];
    return {
      position: style.position,
      padding: style.padding,
      minWidth: style.minWidth,
      maxWidth: style.maxWidth,
      boxSizing: style.boxSizing,
      textAlign: style.textAlign,
      backgroundColor: style.backgroundColor,
      backgroundAlpha: backgroundColor.length === 4 ? backgroundColor[3] : 1,
      borderRadius: style.borderRadius,
      versionMargin: version.margin,
      gotItMargin: gotIt.margin,
      rootZIndex: Number.parseInt(getComputedStyle(root).zIndex, 10),
      backgroundTransition: getComputedStyle(background).transitionDuration,
      contentTransition: getComputedStyle(content).transitionDuration,
    };
  });
  expect(contract).toMatchObject({
    position: 'relative',
    padding: '32px',
    minWidth: '320px',
    maxWidth: '480px',
    boxSizing: 'border-box',
    textAlign: 'center',
    backgroundAlpha: 1,
    versionMargin: '14px 0px',
    gotItMargin: '8px 0px 0px',
    backgroundTransition: '0.2s',
  });
  expect(contract.backgroundColor).not.toBe('rgba(0, 0, 0, 0)');
  expect(contract.borderRadius).not.toBe('0px');
  expect(contract.rootZIndex).toBeGreaterThan(2_000_000);
  expect(contract.rootZIndex).toBeLessThan(3_000_000);
  expect(contract.contentTransition.split(',').map(value => value.trim())).toEqual(['0.2s', '0.2s']);
});

test('MkUpdated keeps focus contained, closes on Escape, and restores the invoking control after leave', async ({ page }) => {
  await page.goto('/__test/components/updated');
  const root = await openUpdate(page);
  await expect(page.locator('#show-updated')).toBeFocused();

  await page.keyboard.press('Tab');
  await expect(root.locator('.ewlycnyt > button').first()).toBeFocused();
  const immediate = await root.evaluate(element => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    return {
      motionState: (element as HTMLElement).dataset.motionState,
      closedCount: document.querySelector('#updated-closed-count')?.textContent,
    };
  });
  expect(immediate).toEqual({ motionState: 'leaving', closedCount: '0' });
  await expect(root).toHaveCount(0);
  await expect(page.locator('#updated-closed-count')).toHaveText('1');
  await expect(page.locator('#show-updated')).toBeFocused();
});

test('MkUpdated background and acknowledgement clicks emit closed only after the leave transition', async ({ page }) => {
  await page.goto('/__test/components/updated');
  let root = await openUpdate(page);

  const backgroundImmediate = await root.locator(':scope > .content').evaluate(element => {
    element.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    const root = element.closest('.qzhlnise') as HTMLElement;
    return {
      motionState: root.dataset.motionState,
      closedCount: document.querySelector('#updated-closed-count')?.textContent,
    };
  });
  expect(backgroundImmediate).toEqual({ motionState: 'leaving', closedCount: '0' });
  await expect(root).toHaveCount(0);
  await expect(page.locator('#updated-closed-count')).toHaveText('1');

  root = await openUpdate(page);
  const acknowledgementImmediate = await root.locator('.gotIt').evaluate((element: HTMLButtonElement) => {
    element.click();
    const root = element.closest('.qzhlnise') as HTMLElement;
    return {
      motionState: root.dataset.motionState,
      closedCount: document.querySelector('#updated-closed-count')?.textContent,
    };
  });
  expect(acknowledgementImmediate).toEqual({ motionState: 'leaving', closedCount: '1' });
  await expect(root).toHaveCount(0);
  await expect(page.locator('#updated-closed-count')).toHaveText('2');
});

test('MkUpdated opens the pinned release URL in a separate noopener page while closing', async ({ page, context }) => {
  await context.route('https://misskey-hub.net/**', route => route.fulfill({
    status: 200,
    contentType: 'text/html',
    body: '<!doctype html><title>Release notes fixture</title>',
  }));
  await page.goto('/__test/components/updated');
  const root = await openUpdate(page);
  const [popup] = await Promise.all([
    page.waitForEvent('popup'),
    root.locator('.ewlycnyt > button').first().click(),
  ]);
  await popup.waitForLoadState('domcontentloaded');
  expect(popup.url()).toBe('https://misskey-hub.net/docs/releases.html#_12-119-2-port-1');
  expect(await popup.evaluate(() => window.opener)).toBeNull();
  await expect(root).toHaveCount(0);
  await expect(page.locator('#updated-closed-count')).toHaveText('1');
  await popup.close();
});

test('MkUpdated uses the pinned narrow dialog geometry without making its panel transparent', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/__test/components/updated');
  const root = await openUpdate(page);
  const geometry = await root.locator(':scope > .content > .ewlycnyt').evaluate(element => {
    const content = getComputedStyle(element.parentElement!);
    const panel = getComputedStyle(element);
    const color = panel.backgroundColor.match(/[\d.]+/g)?.map(Number) ?? [];
    return {
      contentPadding: content.padding,
      panelWidth: element.getBoundingClientRect().width,
      viewportWidth: window.innerWidth,
      alpha: color.length === 4 ? color[3] : 1,
    };
  });
  expect(geometry.contentPadding).toBe('16px');
  expect(geometry.panelWidth).toBeGreaterThanOrEqual(320);
  expect(geometry.panelWidth).toBeLessThanOrEqual(geometry.viewportWidth - 32);
  expect(geometry.alpha).toBe(1);
});

test('MkUpdated switches to the pinned drawer motion on a narrow touch device', async ({ browser, baseURL }) => {
  const context = await browser.newContext({
    baseURL,
    viewport: { width: 390, height: 844 },
    hasTouch: true,
    locale: 'ja-JP',
    timezoneId: 'UTC',
  });
  const page = await context.newPage();
  try {
    await page.addInitScript(() => {
      Object.defineProperty(Navigator.prototype, 'maxTouchPoints', {
        configurable: true,
        get: () => 1,
      });
    });
    await page.goto('/__test/components/updated');
    await page.locator('#show-updated').click();
    const root = page.locator('.qzhlnise.drawer');
    await expect(root).toHaveAttribute('data-motion-state', 'entered');
    await expect(root).not.toHaveClass(/\bdialog\b/);
    const contract = await root.evaluate(element => ({
      contentPosition: getComputedStyle(element.querySelector(':scope > .content')!).position,
      contentBottom: getComputedStyle(element.querySelector(':scope > .content')!).bottom,
      backgroundDuration: getComputedStyle(element.querySelector(':scope > .bg')!).transitionDuration,
    }));
    expect(contract).toEqual({
      contentPosition: 'fixed',
      contentBottom: '0px',
      backgroundDuration: '0.2s',
    });
    await root.locator('.gotIt').click();
    await expect(root).toHaveCount(0);
  } finally {
    await context.close();
  }
});

test('client update bootstrap migrates raw Vue storage and only displays for an authenticated upgrade', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('lastVersion', '12.119.1');
    localStorage.setItem('theme', '{"legacy":true}');
  });
  await page.goto('/__test/sign-in');

  const root = page.locator('.qzhlnise');
  await expect(root).toHaveAttribute('data-motion-state', 'entered');
  await expect(root.locator('.ewlycnyt > .version')).toHaveText('✨12.119.2-port.1🚀');
  const storage = await page.evaluate(() => ({
    lastVersion: localStorage.getItem('lastVersion'),
    theme: localStorage.getItem('theme'),
  }));
  expect(storage).toEqual({ lastVersion: '12.119.2-port.1', theme: null });
  await root.locator('.gotIt').click();
  await expect(root).toHaveCount(0);
});

test('client update bootstrap records a guest upgrade without leaking an authenticated-only popup', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('lastVersion', '12.119.1');
    localStorage.setItem('theme', '{"legacy":true}');
  });
  await page.goto('/__test/components/updated');

  await expect.poll(() => page.evaluate(() => localStorage.getItem('lastVersion')))
    .toBe('12.119.2-port.1');
  expect(await page.evaluate(() => localStorage.getItem('theme'))).toBeNull();
  await expect(page.locator('.qzhlnise')).toHaveCount(0);
  await expect(page.locator('#show-updated')).toBeVisible();
});

test('client update bootstrap reports an explicit storage capability failure without hiding the page', async ({ page }) => {
  await page.addInitScript(() => {
    const original = Storage.prototype.getItem;
    Storage.prototype.getItem = function getItem(key: string) {
      if (key === 'lastVersion') {
        throw new DOMException('fixture detail must not be logged', 'SecurityError');
      }
      return original.call(this, key);
    };
  });
  const browserErrors: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') browserErrors.push(message.text());
  });
  page.on('pageerror', error => browserErrors.push(error.message));

  await page.goto('/__test/components/updated');
  await expect(page.locator('#show-updated')).toBeVisible();
  await expect(page.locator('.qzhlnise')).toHaveCount(0);
  expect(browserErrors).toEqual([]);
});

test('reduced motion suppresses sparkle and shortens modal completion without changing its opaque UI', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/__test/components/updated');
  const root = await openUpdate(page);
  await page.waitForTimeout(50);
  await expect(root.locator('.mk-sparkle > svg')).toHaveCount(0);
  const motion = await root.evaluate(element => {
    const panel = element.querySelector('.ewlycnyt')!;
    const color = getComputedStyle(panel).backgroundColor.match(/[\d.]+/g)?.map(Number) ?? [];
    return {
      backgroundDuration: getComputedStyle(element.querySelector(':scope > .bg')!).transitionDuration,
      contentDuration: getComputedStyle(element.querySelector(':scope > .content')!).transitionDuration,
      alpha: color.length === 4 ? color[3] : 1,
    };
  });
  expect(cssMilliseconds(motion.backgroundDuration)).toBeCloseTo(0.001, 6);
  expect(motion.contentDuration.split(',').every(value =>
    Math.abs(cssMilliseconds(value) - 0.001) < 0.000001)).toBeTruthy();
  expect(motion.alpha).toBe(1);
});
