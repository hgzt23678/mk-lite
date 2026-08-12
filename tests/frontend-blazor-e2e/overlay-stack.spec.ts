import { expect, test, type Locator } from '@playwright/test';

async function assertOverlayLayer(root: Locator, expectedZIndex: number) {
  await expect.poll(async () => root.evaluate(element => (element as HTMLElement).style.zIndex))
    .toBe(String(expectedZIndex));

  const styles = await root.evaluate(element => {
    const rootElement = element as HTMLElement;
    const background = rootElement.querySelector(':scope > .bg') as HTMLElement | null;
    const content = rootElement.querySelector(':scope > .content') as HTMLElement | null;
    return {
      root: rootElement.style.zIndex,
      background: background?.style.zIndex,
      content: content?.style.zIndex,
      pointerEvents: rootElement.style.pointerEvents,
    };
  });
  expect(styles).toEqual({
    root: String(expectedZIndex),
    background: String(expectedZIndex),
    content: String(expectedZIndex),
    pointerEvents: 'auto',
  });
}

test('real overlays preserve Misskey priority bands, nested input isolation, and focus restoration', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');

  const composeSource = page.locator('.mvcprjjd .bottom > button.post[data-cy-open-post-form]');
  await composeSource.click();

  const postDialog = page.locator('body > .qzhlnise.dialog');
  const postForm = postDialog.locator(':scope > .content.top > .gafaadew.modal._popup');
  await expect(postForm).toBeVisible();
  await assertOverlayLayer(postDialog, 1_000_100);
  await expect.poll(async () => page.evaluate(() => document.documentElement.style.overflow)).toBe('hidden');

  const emojiSource = postForm.locator(':scope > .form > footer > button[title="絵文字"]');
  await emojiSource.click();
  const emojiOverlay = page.locator('body > .qzhlnise.popup').filter({
    has: page.locator('.omfetrab.ryghynhb'),
  });
  await expect(emojiOverlay).toHaveCount(1);
  await assertOverlayLayer(emojiOverlay, 2_000_100);
  await expect(postDialog).toHaveCSS('pointer-events', 'none');
  await expect(emojiSource).toHaveCSS('pointer-events', 'none');

  await page.keyboard.press('Escape');
  await expect(emojiOverlay).toHaveCount(0);
  await expect(postDialog).toHaveCount(1);
  await expect(postDialog).toHaveCSS('pointer-events', 'auto');
  await expect(emojiSource).toBeFocused();
  await expect.poll(async () => page.evaluate(() => document.documentElement.style.overflow)).toBe('hidden');

  const visibilitySource = postForm.locator('header > .right > button.visibility');
  await visibilitySource.click();
  const visibilityOverlay = page.locator('body > .qzhlnise.popup').filter({
    has: page.locator('.gqyayizv._popup'),
  });
  await expect(visibilityOverlay).toHaveCount(1);
  await assertOverlayLayer(visibilityOverlay, 3_000_100);
  await expect(postDialog).toHaveCSS('pointer-events', 'none');
  await expect(visibilitySource).toHaveCSS('pointer-events', 'none');

  // The upstream picker leaves focus on its source when opened with a pointer. The shared
  // stack must consume that source key instead of allowing the covered compose button to fire.
  await page.keyboard.press('Space');
  await expect(visibilityOverlay).toHaveCount(1);
  await expect(visibilityOverlay.locator('button[data-index="1"]')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(visibilityOverlay).toHaveCount(0);
  await expect(postDialog).toHaveCount(1);
  await expect(visibilitySource).toBeFocused();

  const accountSource = postForm.locator('header > button.account');
  await accountSource.click();
  const menuOverlay = page.locator('body > .qzhlnise.popup').filter({
    has: page.locator('.rrevdjwt._popup._shadow'),
  });
  await expect(menuOverlay).toHaveCount(1);
  await assertOverlayLayer(menuOverlay, 3_000_200);
  await expect(postDialog).toHaveCSS('pointer-events', 'none');
  await expect(accountSource).toHaveCSS('pointer-events', 'none');

  await page.keyboard.press('Escape');
  await expect(menuOverlay).toHaveCount(0);
  await expect(postDialog).toHaveCount(1);
  await expect(accountSource).toBeFocused();

  await page.keyboard.press('Escape');
  await expect(postDialog).toHaveCount(0);
  await expect(composeSource).toBeFocused();
  await expect.poll(async () => page.evaluate(() => document.documentElement.style.overflow)).toBe('');
});

test('nested MkModalWindow dialogs retain low-band ordering and close only the top dialog', async ({ page }) => {
  await page.goto('/');
  const signInSource = page.locator('[data-cy-signin]');
  await signInSource.click();

  const signIn = page.locator('body > .qzhlnise.dialog[aria-label="ログイン"]');
  await assertOverlayLayer(signIn, 1_000_100);
  const forgotSource = signIn.getByRole('button', { name: 'パスワードを忘れた' });
  await forgotSource.click();

  const forgot = page.locator('body > .qzhlnise.dialog[aria-label="パスワードを忘れた"]');
  await assertOverlayLayer(forgot, 1_000_200);
  await expect(signIn).toHaveCSS('pointer-events', 'none');
  await expect.poll(async () => page.evaluate(() => document.documentElement.style.overflow)).toBe('hidden');

  // MkForgotPassword pins upstream's `autofocus` contract on the username MkInput. Once the
  // nested window has entered, keyboard input belongs to that field and never to the covered
  // sign-in action that opened it.
  const username = forgot.locator('input[name="username"]');
  await expect(username).toBeFocused();
  await page.keyboard.press('Space');
  await expect(forgot).toHaveCount(1);
  await expect(username).toBeFocused();
  await expect(username).toHaveValue(' ');

  await page.keyboard.press('Escape');
  await expect(forgot).toHaveCount(0);
  await expect(signIn).toHaveCount(1);
  await expect(signIn).toHaveCSS('pointer-events', 'auto');
  await expect(forgotSource).toBeFocused();
  await expect.poll(async () => page.evaluate(() => document.documentElement.style.overflow)).toBe('hidden');

  // A rapid reopen must create one new top entry, reapply upstream autofocus, and retain the
  // same restoration source after cancellation.
  await forgotSource.press('Enter');
  await expect(forgot).toHaveCount(1);
  await expect(forgot.locator('input[name="username"]')).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(forgot).toHaveCount(0);
  await expect(forgotSource).toBeFocused();

  await page.keyboard.press('Escape');
  await expect(signIn).toHaveCount(0);
  await expect(signInSource).toBeFocused();
  await expect.poll(async () => page.evaluate(() => document.documentElement.style.overflow)).toBe('');
});
