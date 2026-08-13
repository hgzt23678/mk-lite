import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.goto('/__test/components/container');
  await expect(page.locator('[data-contract="mk-container"]')).toBeVisible();
  await expect(page.locator('.fixture-container')).toHaveClass(/max-width_380px/);
});

test('MkContainer preserves the pinned DOM, scoped CSS, responsive class, and omit lifecycle', async ({ page }) => {
  const root = page.locator('.fixture-container');
  const header = root.locator(':scope > header');
  const content = root.locator(':scope > .content');
  const fold = header.locator(':scope > .sub > button').last();

  await expect(root).toHaveAttribute('data-container-contract', 'primary');
  await expect(root).toHaveClass('ukygtjoj _panel scrollable max-width_380px fixture-container');
  await expect(header.locator(':scope > .title')).toHaveText('Container header');
  await expect(header.locator(':scope > .sub > .fixture-function')).toHaveAttribute('aria-label', 'header action');
  await expect(fold).toHaveAttribute('aria-expanded', 'true');
  await expect(content).toHaveClass('content omitted');
  await expect(content.locator(':scope > .fade > span')).toHaveText('もっと見る');

  const styles = await root.evaluate(element => {
    const container = element as HTMLElement;
    const headerElement = container.querySelector(':scope > header') as HTMLElement;
    const contentElement = container.querySelector(':scope > .content') as HTMLElement;
    const title = headerElement.querySelector(':scope > .title') as HTMLElement;
    const fade = contentElement.querySelector(':scope > .fade') as HTMLElement;
    return {
      position: getComputedStyle(container).position,
      overflow: getComputedStyle(container).overflow,
      contain: getComputedStyle(container).contain,
      maxHeight: getComputedStyle(container).getPropertyValue('--maxHeight').trim(),
      minHeight: container.style.minHeight,
      flexBasis: container.style.flexBasis,
      headerPosition: getComputedStyle(headerElement).position,
      headerTop: getComputedStyle(headerElement).top,
      titlePadding: getComputedStyle(title).padding,
      contentMaxHeight: getComputedStyle(contentElement).maxHeight,
      contentOverflow: getComputedStyle(contentElement).overflow,
      fadePosition: getComputedStyle(fade).position,
      fadeHeight: getComputedStyle(fade).height,
    };
  });

  expect(styles).toEqual({
    position: 'relative',
    overflow: 'clip',
    contain: 'content',
    maxHeight: '120px',
    minHeight: expect.stringMatching(/^\d+(?:\.\d+)?px$/),
    flexBasis: 'auto',
    headerPosition: 'sticky',
    headerTop: '0px',
    titlePadding: '8px 10px',
    contentMaxHeight: '120px',
    contentOverflow: 'hidden',
    fadePosition: 'absolute',
    fadeHeight: '64px',
  });

  await content.locator(':scope > .fade').click();
  await expect(content).toHaveClass('content');
  await expect(content.locator(':scope > .fade')).toHaveCount(0);
  await expect.poll(() => content.evaluate(element => getComputedStyle(element).maxHeight)).toBe('none');

  const collapsed = page.locator('.fixture-container-collapsed');
  await expect(collapsed).toHaveClass('ukygtjoj _panel naked thin hideHeader closed fixture-container-collapsed');
  await expect(collapsed.locator(':scope > header')).toHaveCount(0);
  await expect(collapsed.locator(':scope > .content')).toBeHidden();
});

test('MkContainer completes enter and leave only after both height and opacity transitions', async ({ page }) => {
  const root = page.locator('.fixture-container');
  const content = root.locator(':scope > .content');
  const fold = root.locator(':scope > header > .sub > button').last();
  await content.locator(':scope > .fade').click();

  await content.evaluate(element => {
    const target = element as HTMLElement;
    const records: Array<{ type: string; property?: string; classes: string; height: string }> = [];
    (window as typeof window & { containerMotionRecords?: typeof records }).containerMotionRecords = records;
    new MutationObserver(() => records.push({
      type: 'mutation',
      classes: target.className,
      height: target.style.height,
    })).observe(target, { attributes: true, attributeFilter: ['class', 'style'] });
    target.addEventListener('transitionend', event => records.push({
      type: 'end',
      property: event.propertyName,
      classes: target.className,
      height: target.style.height,
    }));
  });

  await fold.click();
  await expect(root).toHaveClass(/closed/);
  await expect(content).toBeHidden({ timeout: 2_000 });
  await expect(fold).toHaveAttribute('aria-expanded', 'false');
  const leaveRecords = await page.evaluate(() =>
    (window as typeof window & { containerMotionRecords?: Array<{ type: string; property?: string; classes: string }> })
      .containerMotionRecords ?? []);
  expect(leaveRecords.some(record => record.classes.includes('container-toggle-leave-active'))).toBeTruthy();
  expect(leaveRecords.some(record => record.classes.includes('container-toggle-leave-from'))).toBeTruthy();
  expect(leaveRecords.some(record => record.classes.includes('container-toggle-leave-to'))).toBeTruthy();
  expect(new Set(leaveRecords.filter(record => record.type === 'end').map(record => record.property)))
    .toEqual(new Set(['height', 'opacity']));
  await expect(content).not.toHaveClass(/container-toggle-/);
  await expect(content).not.toHaveAttribute('style', /height/);

  await page.evaluate(() => {
    const records = (window as typeof window & { containerMotionRecords?: unknown[] }).containerMotionRecords;
    records?.splice(0);
  });
  await page.locator('[data-action="expand"]').click();
  await expect(root).not.toHaveClass(/closed/);
  await expect(content).toBeVisible();
  await expect(fold).toHaveAttribute('aria-expanded', 'true');
  await expect(content).not.toHaveClass(/container-toggle-/, { timeout: 2_000 });
  const enterRecords = await page.evaluate(() =>
    (window as typeof window & { containerMotionRecords?: Array<{ type: string; property?: string; classes: string }> })
      .containerMotionRecords ?? []);
  expect(enterRecords.some(record => record.classes.includes('container-toggle-enter-active'))).toBeTruthy();
  expect(enterRecords.some(record => record.classes.includes('container-toggle-enter-from'))).toBeTruthy();
  expect(enterRecords.some(record => record.classes.includes('container-toggle-enter-to'))).toBeTruthy();
  expect(new Set(enterRecords.filter(record => record.type === 'end').map(record => record.property)))
    .toEqual(new Set(['height', 'opacity']));
});

test('MkContainer cancels stale motion during rapid reversal and honors the Vue animation setting', async ({ page }) => {
  const root = page.locator('.fixture-container');
  const content = root.locator(':scope > .content');
  const fold = root.locator(':scope > header > .sub > button').last();
  await content.locator(':scope > .fade').click();

  await fold.click();
  await expect(root).toHaveClass(/closed/);
  await page.waitForTimeout(75);
  await fold.click();
  await expect(root).not.toHaveClass(/closed/);
  await expect(content).toBeVisible();
  await page.waitForTimeout(650);
  await expect(content).not.toHaveClass(/container-toggle-/);
  await expect(content).not.toHaveAttribute('style', /height/);

  await page.evaluate(() => localStorage.setItem('pizzax::base', JSON.stringify({ animation: false })));
  await page.reload();
  await expect(root).toHaveClass(/max-width_380px/);
  const reloadedContent = root.locator(':scope > .content');
  const reloadedFold = root.locator(':scope > header > .sub > button').last();
  await reloadedFold.click();
  await expect(reloadedContent).toBeHidden();
  await expect(reloadedContent).not.toHaveClass(/container-toggle-/);

  await page.goto('/');
  const diagnostics = await page.request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
});

test('MkContainer keeps the rendered height and opacity continuous when a leave reverses into enter', async ({ page }) => {
  const root = page.locator('.fixture-container');
  const content = root.locator(':scope > .content');
  const fold = root.locator(':scope > header > .sub > button').last();
  await content.locator(':scope > .fade').click();

  await fold.click();
  await page.waitForTimeout(75);
  const beforeReverse = await content.evaluate(element => ({
    height: element.getBoundingClientRect().height,
    opacity: Number.parseFloat(getComputedStyle(element).opacity),
  }));
  expect(beforeReverse.height).toBeGreaterThan(40);
  expect(beforeReverse.opacity).toBeGreaterThan(0.2);

  const samples = await page.evaluate(async () => {
    const fixture = document.querySelector('.fixture-container') as HTMLElement;
    const target = fixture.querySelector(':scope > .content') as HTMLElement;
    const button = fixture.querySelector(':scope > header > .sub > button:last-child') as HTMLButtonElement;
    const records: Array<{ className: string; height: number; opacity: number }> = [];
    const started = performance.now();
    do {
      records.push({
        className: target.className,
        height: target.getBoundingClientRect().height,
        opacity: Number.parseFloat(getComputedStyle(target).opacity),
      });
      if (records.length === 1) button.click();
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    } while (performance.now() - started < 260);
    return records;
  });
  const reversal = samples.filter(sample =>
    sample.className.includes('container-toggle-enter-active'));

  expect(reversal).not.toHaveLength(0);
  expect(reversal[0].height).toBeLessThanOrEqual(beforeReverse.height + 8);
  expect(reversal.every(sample => sample.height > 1)).toBeTruthy();
  expect(reversal.every(sample => sample.opacity > 0.2)).toBeTruthy();

  await expect(content).not.toHaveClass(/container-toggle-/, { timeout: 2_000 });
  await expect(content).not.toHaveAttribute('style', /height|opacity/);
});

test('MkContainer keeps the rendered height and opacity continuous when an enter reverses into leave', async ({ page }) => {
  const root = page.locator('.fixture-container');
  const content = root.locator(':scope > .content');
  const fold = root.locator(':scope > header > .sub > button').last();
  await content.locator(':scope > .fade').click();

  await fold.click();
  await expect(content).toBeHidden({ timeout: 2_000 });
  await page.locator('[data-action="expand"]').click();
  await page.waitForTimeout(75);
  const beforeReverse = await content.evaluate(element => ({
    height: element.getBoundingClientRect().height,
    opacity: Number.parseFloat(getComputedStyle(element).opacity),
  }));
  expect(beforeReverse.height).toBeGreaterThan(1);
  expect(beforeReverse.height).toBeLessThan(160);

  // Sample and reverse in one browser task. Starting a separate async
  // evaluation and then issuing a Playwright click can otherwise start the
  // sampler after the click has already been dispatched.
  const samples = await page.evaluate(async () => {
    const fixture = document.querySelector('.fixture-container') as HTMLElement;
    const target = fixture.querySelector(':scope > .content') as HTMLElement;
    const button = fixture.querySelector(':scope > header > .sub > button:last-child') as HTMLButtonElement;
    const records: Array<{ className: string; height: number; opacity: number }> = [];
    const started = performance.now();
    do {
      records.push({
        className: target.className,
        height: target.getBoundingClientRect().height,
        opacity: Number.parseFloat(getComputedStyle(target).opacity),
      });
      if (records.length === 1) button.click();
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    } while (performance.now() - started < 260);
    return records;
  });
  const reversal = samples.filter(sample =>
    sample.className.includes('container-toggle-leave-active'));

  expect(reversal).not.toHaveLength(0);
  expect(reversal[0].height).toBeLessThanOrEqual(beforeReverse.height + 8);
  expect(reversal.every(sample => sample.height <= beforeReverse.height + 8)).toBeTruthy();
  expect(reversal.every(sample => sample.opacity <= 1)).toBeTruthy();

  await expect(content).toBeHidden({ timeout: 2_000 });
  await expect(content).not.toHaveClass(/container-toggle-/);
});
