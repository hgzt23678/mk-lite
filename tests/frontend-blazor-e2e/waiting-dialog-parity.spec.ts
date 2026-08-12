import { expect, test } from '@playwright/test';

test('MkWaitingDialog keeps waiting state open and closes only after showing becomes false', async ({ page }) => {
  await page.goto('/__test/components/waiting-dialog');
  await page.locator('#show-waiting').click();

  const root = page.locator('.qzhlnise.dialog');
  const dialog = root.locator('.iuyakobc');
  await expect(root).toHaveAttribute('data-motion-state', 'entered');
  await expect(dialog).not.toHaveClass(/iconOnly/);
  await expect(dialog.locator(':scope > .icon.waiting')).toHaveCount(1);
  await expect(dialog.locator(':scope > .text')).toHaveText('同期しています...');
  const surface = await dialog.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      width: style.width,
      padding: style.padding,
      background: style.backgroundColor,
      borderRadius: style.borderRadius,
      zIndex: Number.parseInt(getComputedStyle(element.closest('.qzhlnise')!).zIndex, 10),
    };
  });
  expect(surface.width).toBe('250px');
  expect(surface.padding).toBe('32px');
  expect(surface.borderRadius).not.toBe('0px');
  expect(surface.background).not.toBe('rgba(0, 0, 0, 0)');
  expect(surface.zIndex).toBeGreaterThan(3_000_000);

  // MkModal's full-viewport content layer sits above .bg and emits the same
  // background-click event only when the content element itself is targeted.
  await root.locator(':scope > .content').click({ position: { x: 10, y: 10 } });
  await expect(root).toHaveCount(1);
  await expect(page.locator('#waiting-done-count')).toHaveText('0');

  await page.locator('#hide-waiting').evaluate((element: HTMLButtonElement) => element.click());
  await expect(root).toHaveCount(0);
  await expect(page.locator('#waiting-done-count')).toHaveText('1');
  await expect(page.locator('#waiting-closed-count')).toHaveText('1');
});

test('MkWaitingDialog success branch is icon-only and dismisses after the leave transition', async ({ page }) => {
  await page.goto('/__test/components/waiting-dialog');
  await page.locator('#show-waiting').click();
  await page.locator('#mark-success').evaluate((element: HTMLButtonElement) => element.click());

  const root = page.locator('.qzhlnise.dialog');
  const dialog = root.locator('.iuyakobc.iconOnly');
  await expect(dialog.locator(':scope > .icon.success')).toHaveCount(1);
  await expect(dialog.locator(':scope > .text')).toHaveCount(0);
  const geometry = await dialog.evaluate(element => {
    const style = getComputedStyle(element);
    return {
      width: style.width,
      height: style.height,
      padding: style.padding,
      display: style.display,
      alignItems: style.alignItems,
      justifyContent: style.justifyContent,
      background: style.backgroundColor,
    };
  });
  expect(geometry).toMatchObject({
    width: '96px',
    height: '96px',
    padding: '0px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
  });
  expect(geometry.background).not.toBe('rgba(0, 0, 0, 0)');

  await root.locator(':scope > .content').click({ position: { x: 10, y: 10 } });
  await expect(root).toHaveAttribute('data-motion-state', 'leaving');
  await expect(root).toHaveCount(0);
  await expect(page.locator('#waiting-done-count')).toHaveText('1');
  await expect(page.locator('#waiting-closed-count')).toHaveText('1');
});
