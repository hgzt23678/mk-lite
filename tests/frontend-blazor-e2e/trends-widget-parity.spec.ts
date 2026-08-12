import { expect, test } from '@playwright/test';

test('trends widget preserves the v12 container, tag rows, and mini chart contract', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({
      animation: false,
      widgets: [
        { name: 'trends', id: 'trends', place: null, data: { showHeader: true } },
      ],
    }));
  });
  await page.goto('/__test/components/widgets');
  const root = page.locator('[data-contract="widgets"] > .efzpzdvf');
  const widgets = root.locator(':scope > .vjoppmmu');
  const widget = widgets.locator(':scope > .mkw-trends.widget._panel');
  await expect(widget).toBeVisible();
  await expect(widget.locator(':scope > header .title')).toContainText('トレンド');

  const trend = widget.locator(':scope > .content > .wbrkwala > .tags > div').first();
  await expect(trend).toHaveCount(1);
  await expect(trend.locator(':scope > .tag > a.a')).toHaveText('#Misskey');
  await expect(trend.locator(':scope > .tag > a.a')).toHaveAttribute('href', '/tags/Misskey');
  await expect(trend.locator(':scope > .tag > p')).toHaveText('42人が投稿');
  await expect(trend.locator(':scope > svg.chart')).toHaveCount(1);
  await expect(trend.locator(':scope > svg.chart polyline')).toHaveAttribute('points', /.+/);

  const second = widget.locator(':scope > .content > .wbrkwala > .tags > div').nth(1);
  await expect(second.locator(':scope > .tag > a.a')).toHaveText('#activitypub');
  await expect(second.locator(':scope > .tag > p')).toHaveText('17人が投稿');
});
