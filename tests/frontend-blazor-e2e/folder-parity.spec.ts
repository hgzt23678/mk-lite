import { expect, test } from '@playwright/test';

test('MkFolder preserves pinned DOM persistence background size and motion contracts', async ({ page }) => {
  const browserFailures: string[] = [];
  page.on('pageerror', error => browserFailures.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error') browserFailures.push(message.text());
  });

  await page.goto('/__test/components/folder');
  await page.evaluate(() => localStorage.removeItem('ui:folder:browser-contract'));
  await page.reload();

  const folder = page.locator('[data-contract="folder"]');
  await expect(folder).toHaveClass(/\bssazuxis\b/);
  await expect(folder).toHaveClass(/\bmax-width_500px\b/);
  await expect(folder.locator(':scope > header > .title')).toContainText('Folder');
  await expect(folder.locator(':scope > div:last-child')).toBeVisible();
  await expect(folder.locator(':scope > header')).toHaveCSS('background-color', 'rgba(20, 30, 40, 0.85)');

  await folder.locator(':scope > header').click();
  await expect(folder.locator(':scope > div:last-child')).toBeHidden();
  expect(await page.evaluate(() => localStorage.getItem('ui:folder:browser-contract'))).toBe('f');

  await page.reload();
  await expect(folder.locator(':scope > div:last-child')).toBeHidden();
  await folder.locator(':scope > header').click();
  await expect(folder.locator(':scope > div:last-child')).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem('ui:folder:browser-contract'))).toBe('t');
  expect(browserFailures).toEqual([]);
});

test('MkFolder starts at its intended opacity and keeps height continuous when toggles reverse', async ({ page }) => {
  await page.goto('/__test/components/folder');
  await page.evaluate(() => localStorage.removeItem('ui:folder:browser-contract'));
  await page.reload();

  const folder = page.locator('[data-contract="folder"]');
  const body = folder.locator(':scope > div:last-child');
  const header = folder.locator(':scope > header');
  await expect(header).toHaveCSS('background-color', 'rgba(20, 30, 40, 0.85)');

  await header.click();
  await expect(body).toHaveClass(/folder-toggle-leave-active/);
  await page.waitForTimeout(120);
  const closing = await body.evaluate(element => ({
    className: element.className,
    height: element.getBoundingClientRect().height,
    opacity: Number.parseFloat(getComputedStyle(element).opacity),
    transition: getComputedStyle(element).transition,
  }));
  expect(closing.className).toContain('folder-toggle-leave-to');
  expect(closing.transition).toContain('opacity 0.5s');
  expect(closing.height).toBeGreaterThan(0);
  expect(closing.opacity).toBeLessThan(1);
  await expect(body).toBeHidden({ timeout: 2_000 });

  await header.click();
  await expect(body).toHaveClass(/folder-toggle-enter-active/);
  await page.waitForTimeout(120);
  const opening = await body.evaluate(element => ({
    className: element.className,
    height: element.getBoundingClientRect().height,
    opacity: Number.parseFloat(getComputedStyle(element).opacity),
  }));
  expect(opening.className).toContain('folder-toggle-enter-to');
  expect(opening.height).toBeGreaterThan(0);
  expect(opening.opacity).toBeLessThan(1);

  await page.waitForTimeout(75);
  const beforeReverse = await body.evaluate(element => ({
    height: element.getBoundingClientRect().height,
    opacity: Number.parseFloat(getComputedStyle(element).opacity),
  }));
  expect(beforeReverse.height).toBeGreaterThan(0);

  const reverseSamples = await page.evaluate(async () => {
    const folderElement = document.querySelector('[data-contract="folder"]') as HTMLElement;
    const target = folderElement.querySelector(':scope > div:last-child') as HTMLElement;
    const trigger = folderElement.querySelector(':scope > header') as HTMLElement;
    const samples: Array<{ className: string; height: number; opacity: number }> = [];
    const started = performance.now();
    do {
      samples.push({
        className: target.className,
        height: target.getBoundingClientRect().height,
        opacity: Number.parseFloat(getComputedStyle(target).opacity),
      });
      if (samples.length === 1) trigger.click();
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    } while (performance.now() - started < 240);
    return samples;
  });
  const reversedLeave = reverseSamples.filter(sample => sample.className.includes('folder-toggle-leave-active'));
  expect(reversedLeave).not.toHaveLength(0);
  expect(reversedLeave[0].height).toBeLessThanOrEqual(beforeReverse.height + 2);
  expect(reversedLeave.every(sample => sample.height <= beforeReverse.height + 2)).toBeTruthy();
  await expect(body).toBeHidden({ timeout: 2_000 });
  await expect(body).not.toHaveAttribute('style', /height|opacity/);

  await folder.evaluate(element => { (element as HTMLElement).style.width = '520px'; });
  await expect(folder).not.toHaveClass(/max-width_500px/);
  await folder.evaluate(element => { (element as HTMLElement).style.width = '480px'; });
  await expect(folder).toHaveClass(/max-width_500px/);
  await expect(header).toBeVisible();
});

test('MkFolder clears motion state immediately when reduced motion is requested', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/__test/components/folder');
  await page.evaluate(() => localStorage.removeItem('ui:folder:browser-contract'));
  await page.reload();

  const folder = page.locator('[data-contract="folder"]');
  const body = folder.locator(':scope > div:last-child');
  const header = folder.locator(':scope > header');
  await expect(header).toHaveCSS('background-color', 'rgba(20, 30, 40, 0.85)');

  await header.click();
  await expect(body).toBeHidden();
  await expect(body).not.toHaveClass(/folder-toggle-/);
  await expect(body).not.toHaveAttribute('style', /height|opacity/);

  await header.click();
  await expect(body).toBeVisible();
  await expect(body).not.toHaveClass(/folder-toggle-/);
  await expect(body).not.toHaveAttribute('style', /height|opacity/);
});
