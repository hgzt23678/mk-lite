import { expect, test } from '@playwright/test';

test('MkTimeline port preserves hierarchy, opaque backgrounds, queue flush, and source replacement', async ({ page }) => {
  const failures: string[] = [];
  await page.addInitScript(() => {
    localStorage.setItem('pizzax::base', JSON.stringify({ showFixedPostForm: true }));
  });
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  const resetDiagnostics = await page.request.post('/__test/reset-diagnostics');
  expect(resetDiagnostics.status()).toBe(204);
  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/);

  const timelinePage = page.locator('.cmuxhskf');
  const pageHeader = page.locator('.fdidabkb');
  await expect(pageHeader.locator(':scope > .titleContainer .title > .title')).toHaveText('タイムライン');
  await expect(pageHeader.locator(':scope > .tabs > button.tab')).toHaveCount(4);
  const tutorial = timelinePage.locator(':scope > .tutorial._block.tbkwesmv');
  await expect(tutorial).toHaveCount(1);
  await expect(tutorial.locator(':scope > .navigation > .step > span')).toHaveText('1 / 7');
  await tutorial.locator(':scope > .navigation > .ok').click();
  await expect(tutorial.locator(':scope > .navigation > .step > span')).toHaveText('2 / 7');
  await expect(timelinePage.locator(':scope > .post-form.gafaadew._block')).toHaveCount(1);
  const pagination = timelinePage.locator(':scope > .tl._block > .tl');
  const notes = pagination.locator(':scope > .giivymft.noGap > .sqadhkmv.noGap.notes');
  await expect(pagination).toHaveCount(1);
  await expect(notes.locator(':scope > .tkcbzcuz.qtqtichx')).toHaveCount(1);
  await expect.poll(async () => {
    const state = await (await page.request.get('/__test/timeline-stream-state')).json() as {
      activeSubscriptions: number;
    };
    return state.activeSubscriptions;
  }).toBeGreaterThan(0);

  const backgroundAlpha = await page.locator('.cmuxhskf > .tl._block').evaluate(element => {
    const alpha = (target: Element): number | null => {
      const canvas = document.createElement('canvas');
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (context === null) return null;
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = getComputedStyle(target).backgroundColor;
      context.fillRect(0, 0, 1, 1);
      return context.getImageData(0, 0, 1, 1).data[3];
    };
    const noteList = element.querySelector('.giivymft.noGap > .notes');
    return {
      timeline: alpha(element),
      notes: noteList === null ? null : alpha(noteList),
    };
  });
  expect(backgroundAlpha).toEqual({ timeline: 255, notes: 255 });

  const scrollState = await pagination.evaluate(root => {
    const container = root.parentElement as HTMLElement;
    container.style.height = '160px';
    container.style.overflowY = 'auto';
    const spacer = document.createElement('div');
    spacer.dataset.timelineScrollFixture = 'true';
    spacer.style.height = '1400px';
    container.append(spacer);
    container.scrollTop = root.offsetTop + 200;
    return { scrollTop: container.scrollTop, rootTop: root.offsetTop };
  });
  expect(scrollState.scrollTop).toBeGreaterThan(scrollState.rootTop);

  const publish = await page.request.post('/__test/timeline-stream-note');
  expect(publish.status()).toBe(204);
  const queued = timelinePage.locator(':scope > .new > button._buttonPrimary');
  await expect(queued).toHaveText('新しいノートがあります');
  await expect(notes.locator(':scope > .tkcbzcuz.qtqtichx')).toHaveCount(1);

  await queued.click();
  await expect(queued).toHaveCount(0);
  await expect(notes.locator(':scope > .tkcbzcuz.qtqtichx')).toHaveCount(2);
  await expect(notes.locator(':scope > .tkcbzcuz.qtqtichx').first()).toContainText('streamed timeline note');

  await pageHeader.locator(':scope > .tabs > button[title="ローカル"]').click();
  await expect(pageHeader.locator(':scope > .tabs > button[title="ローカル"]')).toHaveClass(/active/);
  await expect(page.locator('.cmuxhskf > .tl._block > .tl > .giivymft.noGap')).toHaveCount(1);

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: unknown[];
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
  expect(failures).toEqual([]);
});
