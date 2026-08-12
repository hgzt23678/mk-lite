import { expect, test } from '@playwright/test';

const route = '/__test/key-value';
const copyButton = '.alqyeyti[data-contract="copy"] > .value > button._textButton';

async function waitForInteractiveCopyButton(page: import('@playwright/test').Page) {
  await expect(page.locator('section[data-interactive="true"]')).toHaveCount(1);
}

async function installClipboardProbe(
  page: import('@playwright/test').Page,
  forceLegacy: boolean
) {
  await page.addInitScript(({ useLegacy }) => {
    const descriptor = Object.getOwnPropertyDescriptor(Navigator.prototype, 'clipboard');
    if (useLegacy) {
      Object.defineProperty(Navigator.prototype, 'clipboard', {
        configurable: true,
        get: () => undefined
      });
    } else if (descriptor?.get !== undefined) {
      Object.defineProperty(Navigator.prototype, 'clipboard', {
        configurable: true,
        get() {
          const clipboard = descriptor.get!.call(this) as Clipboard;
          return {
            writeText: async (value: string) => {
              await clipboard.writeText(value);
              (window as Window & { __keyValueCopied?: string }).__keyValueCopied = value;
            },
            readText: () => clipboard.readText()
          };
        }
      });
    }
    window.addEventListener('DOMContentLoaded', () => {
      document.addEventListener('copy', () => {
        (window as Window & { __keyValueCopied?: string }).__keyValueCopied =
          document.getSelection()?.toString() ?? '';
      }, true);
    }, { once: true });
  }, { useLegacy: forceLegacy });
}

test('preserves the pinned key/value DOM, scoped geometry, and falsy copy branch', async ({ page }) => {
  await page.goto(route);

  const normal = page.locator('.alqyeyti.fixture-key-value[data-contract="copy"]');
  const oneline = page.locator('.alqyeyti.oneline.fixture-oneline[data-contract="oneline"]');
  const emptyCopy = page.locator('.alqyeyti[data-contract="empty-copy"]');
  await expect(normal.locator(':scope > .key')).toHaveText('バージョン');
  await expect(normal.locator(':scope > .value')).toContainText('12.119.2');
  await expect(normal.locator(':scope > .value > button._textButton')).toHaveAttribute('type', 'button');
  await expect(normal.locator(':scope > .value > button._textButton')).toHaveAttribute('title', 'コピー');
  await expect(normal.locator(':scope > .value > button._textButton')).toHaveAttribute('aria-label', 'コピー');
  await expect(normal.locator(':scope > .value > button > i.far.fa-copy')).toHaveAttribute('aria-hidden', 'true');
  await expect(emptyCopy.locator('button')).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText('contract-copy-値-✓');

  const geometry = await page.evaluate(() => {
    const normalRoot = document.querySelector<HTMLElement>('[data-contract="copy"]')!;
    const normalKey = normalRoot.querySelector<HTMLElement>(':scope > .key')!;
    const normalButton = normalRoot.querySelector<HTMLElement>('button')!;
    const oneRoot = document.querySelector<HTMLElement>('[data-contract="oneline"]')!;
    const oneKey = oneRoot.querySelector<HTMLElement>(':scope > .key')!;
    const oneValue = oneRoot.querySelector<HTMLElement>(':scope > .value')!;
    const rootWidth = oneRoot.getBoundingClientRect().width;
    const style = (element: Element) => getComputedStyle(element);
    return {
      rootFont: Number.parseFloat(style(normalRoot).fontSize),
      normalKeyFont: Number.parseFloat(style(normalKey).fontSize),
      normalKeyOpacity: style(normalKey).opacity,
      normalKeyPaddingBottom: Number.parseFloat(style(normalKey).paddingBottom),
      copyMarginLeft: Number.parseFloat(style(normalButton).marginLeft),
      oneDisplay: style(oneRoot).display,
      oneKeyFont: Number.parseFloat(style(oneKey).fontSize),
      oneKeyPaddingRight: style(oneKey).paddingRight,
      keyRatio: oneKey.getBoundingClientRect().width / rootWidth,
      valueRatio: oneValue.getBoundingClientRect().width / rootWidth,
      valueWhiteSpace: style(oneValue).whiteSpace,
      valueOverflow: style(oneValue).overflow,
      valueTextOverflow: style(oneValue).textOverflow
    };
  });

  expect(geometry.normalKeyFont / geometry.rootFont).toBeCloseTo(0.85, 2);
  expect(geometry.normalKeyOpacity).toBe('0.75');
  expect(geometry.normalKeyPaddingBottom).toBeCloseTo(geometry.normalKeyFont * 0.25, 1);
  expect(geometry.copyMarginLeft).toBeCloseTo(geometry.rootFont * 0.5, 1);
  expect(geometry.oneDisplay).toBe('flex');
  expect(geometry.oneKeyFont).toBeCloseTo(geometry.rootFont, 1);
  expect(geometry.oneKeyPaddingRight).toBe('8px');
  expect(geometry.keyRatio).toBeCloseTo(0.3, 2);
  expect(geometry.valueRatio).toBeCloseTo(0.7, 2);
  expect(geometry.valueWhiteSpace).toBe('nowrap');
  expect(geometry.valueOverflow).toBe('hidden');
  expect(geometry.valueTextOverflow).toBe('ellipsis');
});

test('keyboard activation copies the exact value and shows the real Misskey success surface', async ({ page }, testInfo) => {
  await installClipboardProbe(page, testInfo.project.name === 'firefox');
  await page.goto(route);
  await waitForInteractiveCopyButton(page);
  const button = page.locator(copyButton);
  await button.focus();
  await expect(button).toBeFocused();

  await page.keyboard.press('Enter');

  await expect.poll(() => page.evaluate(async () =>
    (window as Window & { __keyValueCopied?: string }).__keyValueCopied ??
      await navigator.clipboard?.readText?.().catch(() => null) ?? null))
    .toBe('contract-copy-値-✓');
  expect(await page.evaluate(() => document.getSelection()?.rangeCount ?? -1)).toBe(0);
  const success = page.locator('[data-feedback-kind="success"]');
  await expect(success.locator(':scope > .content > .iuyakobc.iconOnly')).toBeVisible();
  await expect(success).toHaveAttribute('role', 'status');
  await expect(success).toHaveAttribute('aria-live', 'polite');
  await expect(success).toHaveAttribute('aria-atomic', 'true');
  await expect(success).toHaveAttribute('aria-label', 'コピーしました');
  await expect(success.locator(':scope > .content > .iuyakobc.iconOnly > i.fas.fa-check.icon.success'))
    .toBeVisible();
  await expect(success.locator('.mk-visually-hidden')).toHaveText('コピーしました');
  await expect(button).toBeFocused();

  await page.waitForFunction(() =>
    document.querySelector<HTMLElement>('[data-feedback-kind="success"]')?.dataset.motionState === 'entered');
  const surface = await success.locator('.iuyakobc.iconOnly').evaluate(element => {
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    const root = element.closest<HTMLElement>('[data-feedback-kind="success"]')!;
    return {
      width: rect.width,
      height: rect.height,
      padding: style.padding,
      display: style.display,
      alignItems: style.alignItems,
      justifyContent: style.justifyContent,
      background: style.backgroundColor,
      radius: style.borderRadius,
      zIndex: Number.parseInt(getComputedStyle(root).zIndex, 10)
    };
  });
  expect(surface.width).toBeCloseTo(96, 0);
  expect(surface.height).toBeCloseTo(96, 0);
  expect(surface.padding).toBe('0px');
  expect(surface.display).toBe('flex');
  expect(surface.alignItems).toBe('center');
  expect(surface.justifyContent).toBe('center');
  expect(surface.background).not.toBe('rgba(0, 0, 0, 0)');
  expect(surface.radius).not.toBe('0px');
  expect(surface.zIndex).toBeGreaterThanOrEqual(3_000_100);

  await expect(success).toHaveCount(0, { timeout: 2_500 });
  await expect(button).toBeFocused();
});

test('a rejected clipboard write never reports success and exposes only the safe failure announcement', async ({ page }) => {
  await page.goto(route);
  await waitForInteractiveCopyButton(page);
  await page.evaluate(() => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: () => Promise.reject(new Error('sensitive browser failure')) }
    });
    Document.prototype.execCommand = () => false;
  });

  await page.locator(copyButton).click();

  await expect(page.locator('[role="alert"].mk-visually-hidden')).toHaveText('コピーできませんでした');
  await expect(page.locator('[data-feedback-kind="success"]')).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText('sensitive browser failure');
});

test('disposing the routed component while feedback is active leaves no callback or overlay leak', async ({ page }, testInfo) => {
  await installClipboardProbe(page, testInfo.project.name === 'firefox');
  await page.request.post('/__test/reset-diagnostics');
  await page.goto(route);
  await waitForInteractiveCopyButton(page);
  await page.locator(copyButton).click();
  await expect(page.locator('[data-feedback-kind="success"] .iuyakobc.iconOnly')).toBeVisible();

  await page.goto('/about-misskey');
  await expect(page.locator('[data-feedback-kind="success"]')).toHaveCount(0);
  await expect.poll(async () => {
    const response = await page.request.get('/__test/diagnostics');
    return (await response.json()).unhandledExceptions as string[];
  }).toEqual([]);
});
