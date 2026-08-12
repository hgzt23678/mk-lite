import { expect, test } from '@playwright/test';

test('universal widgets preserve v12 edit persistence settings and supported rendering', async ({ page, request }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      animation: false,
      widgets: [
        { name: 'rss', id: 'unsupported', place: null, data: { url: 'https://example.test/feed' } },
        { name: 'calendar', id: 'calendar', place: null, data: { transparent: false } },
        { name: 'digitalClock', id: 'digital', place: null, data: {
          transparent: false,
          fontSize: 1.5,
          showMs: false,
          showLabel: true,
          timezone: 'asia/tokyo',
        } },
      ],
    }));
  });
  await page.setViewportSize({ width: 480, height: 800 });
  await page.goto('/__test/components/widgets');

  const root = page.locator('[data-contract="widgets"] > .efzpzdvf');
  const widgets = root.locator(':scope > .vjoppmmu');
  await expect(widgets.locator(':scope > .mkw-calendar.widget._panel')).toBeVisible();
  await expect(widgets.locator(':scope > .mkw-digitalClock.widget._panel')).toBeVisible();
  await expect(widgets.locator(':scope > .widget')).toHaveCount(2);
  await expect(widgets.locator(':scope > .widget').first()).toHaveCSS('contain', 'content');
  await expect(root).not.toContainText('RSS');

  await root.locator(':scope > .mk-widget-edit').click();
  await expect(widgets.locator(':scope > header')).toBeVisible();
  await expect(widgets.locator('.customize-container')).toHaveCount(2);
  const select = widgets.locator('.mk-widget-select select');
  await expect(select.locator('option')).toHaveCount(8);
  await expect(select.locator('option')).toHaveText([
    'タイムライン',
    'カレンダー',
    '時計',
    'デジタル時計',
    '投稿フォーム',
    '付箋',
    'UNIX時計',
    'トレンド',
  ]);
  await expect(select.locator('option[value="rss"]')).toHaveCount(0);
  await expect(select.locator('option[value="notifications"]')).toHaveCount(0);

  await select.selectOption('clock');
  await widgets.locator('.mk-widget-add').click();
  await expect(widgets.locator('.customize-container')).toHaveCount(3);
  await expect(widgets.locator(':scope > header > .mk-widget-add')).toHaveCSS('width', '300px');
  await expect(widgets.locator('.customize-container').filter({ has: page.locator('.mkw-clock') }).locator('.clock')).toHaveCSS('height', '150px');
  await expect.poll(() => page.evaluate(() =>
    JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').widgets.map((widget: { name: string }) => widget.name),
  )).toEqual(['clock', 'rss', 'calendar', 'digitalClock']);
  const clock = widgets.locator('.customize-container').filter({ has: page.locator('.mkw-clock') });
  await clock.locator(':scope > .remove').click();
  await expect(widgets.locator('.customize-container')).toHaveCount(2);

  const calendar = widgets.locator('.customize-container[data-widget-id="calendar"]');
  await calendar.locator(':scope > .config').click();
  const dialog = page.locator('body > .qzhlnise.dialog', {
    has: page.locator('.ebkgoccj > .header > .title', { hasText: 'calendar' }),
  });
  await expect(dialog.locator(':scope > .content > .ebkgoccj')).toBeVisible();
  await expect(dialog.locator(':scope > .bg._modalBg')).toBeVisible();
  expect(await dialog.locator(':scope > .bg._modalBg').evaluate(element =>
    getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)');
  await expect(dialog.locator('.xkpnjxcv > .ziffeomt')).toHaveCount(1);
  await dialog.locator('.ziffeomt > .button').click();
  await dialog.locator('.ebkgoccj > .header > button:last-child').click();
  await expect(dialog).toHaveCount(0);
  await expect.poll(() => page.evaluate(() => {
    const state = JSON.parse(localStorage.getItem('pizzax::base') ?? '{}');
    return state.widgets.find((widget: { id: string }) => widget.id === 'calendar')?.data.transparent;
  })).toBe(true);

  const digitalHandle = widgets.locator('.customize-container[data-widget-id="digital"] > .handle');
  await digitalHandle.dragTo(calendar, { targetPosition: { x: 8, y: 2 } });
  await expect.poll(() => page.evaluate(() =>
    JSON.parse(localStorage.getItem('pizzax::base') ?? '{}').widgets.map((widget: { id: string }) => widget.id),
  )).toEqual(['unsupported', 'digital', 'calendar']);

  await root.locator(':scope > ._textButton', { hasText: '編集を終了' }).click();
  await expect(widgets.locator(':scope > .widget')).toHaveCount(2);
  await expect(widgets.locator(':scope > .widget').first()).toHaveClass(/\bmkw-digitalClock\b/);
  await expect(widgets.locator(':scope > .mkw-calendar.widget')).not.toHaveClass(/\b_panel\b/);
  await expect(widgets.locator('[data-widget-id="unsupported"]')).toHaveCount(0);

  const diagnostics = await request.get('/__test/diagnostics');
  expect(diagnostics.ok()).toBeTruthy();
  expect((await diagnostics.json()).unhandledExceptions).toEqual([]);
  expect(browserFailures).toEqual([]);
});
