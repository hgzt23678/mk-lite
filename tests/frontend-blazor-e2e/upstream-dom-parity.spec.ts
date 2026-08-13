import { expect, test } from '@playwright/test';

test('visitor root preserves Misskey 12.119.2 shell and welcome entrance structure', async ({ page }) => {
  await page.goto('/');

  const root = page.locator('body > .mk-app');
  await expect(root).toHaveCount(1);
  await expect(root.locator(':scope > .github-corner')).toHaveCount(1);
  await expect(root.locator(':scope > .side')).toHaveCount(0);
  await expect(root.locator(':scope > .main > .contents > main > .rsqzvsbo')).toHaveCount(1);

  const top = root.locator('.rsqzvsbo > .top');
  await expect(top.locator(':scope > .xfbouadm.bg')).toHaveCount(1);
  await expect(top.locator(':scope > .civpbkhh.tl')).toHaveCount(1);
  await expect(top.locator(':scope > .shape1')).toHaveCount(1);
  await expect(top.locator(':scope > .shape2')).toHaveCount(1);
  await expect(top.locator(':scope > img.misskey')).toHaveAttribute('src', '/client-assets/misskey.svg');
  await expect(top.locator(':scope > .emojis > img.mk-emoji')).toHaveCount(5);
  await expect(top.locator(':scope > .emojis > img.normal, :scope > .emojis > img.noStyle')).toHaveCount(0);
  await expect(top.locator(':scope > .main > .fg > .action > .bghgjjyj')).toHaveCount(2);
  await expect(top.locator(':scope > .main > .fg > .action > [data-cy-signup]')).toHaveText('新規登録');
  await expect(top.locator(':scope > .main > .fg > .action > [data-cy-signin]')).toHaveText('ログイン');
  await expect(top.locator(':scope > .main > button.menu')).toHaveAttribute('aria-label', 'メニュー');

  const federation = top.locator(':scope > .federation');
  const marquee = federation.locator(':scope > ._wrap_1hc4p_1');
  const marqueeContent = marquee.locator(':scope > ._content_1hc4p_9');
  await expect(federation).toHaveCount(1);
  await expect(marqueeContent.locator(':scope > ._text_1hc4p_15')).toHaveCount(2);
  await expect(marqueeContent.locator('a._federationInstance_jmpas_1')).toHaveCount(6);
  await expect.poll(async () => marqueeContent.evaluate(element => element.getAttribute('style') ?? ''))
    .toMatch(/^animation-duration: [0-9.]+s;?$/);
  const measurement = await marqueeContent.evaluate(element => ({
    width: (element as HTMLElement).offsetWidth,
    duration: Number.parseFloat((element as HTMLElement).style.animationDuration)
  }));
  expect(measurement.duration).toBeCloseTo(((measurement.width / 2) * 40) / 3000, 5);

  await marquee.hover();
  await expect.poll(async () => marqueeContent.locator(':scope > ._text_1hc4p_15').first().evaluate(
    element => getComputedStyle(element).animationPlayState))
    .toBe('paused');
});

test('about-misskey preserves the pinned page, form primitives, opaque panel, and Matter physics lifecycle', async ({ page }) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}`));

  await page.goto('/about-misskey');
  await expect(page).toHaveTitle('Misskeyについて');

  const pageRoot = page.locator('.znqjceqz');
  await expect(pageRoot).toHaveCount(1);
  await expect(page.locator('.fdidabkb > .titleContainer > .title > .title')).toHaveText('Misskeyについて');
  const about = pageRoot.locator(':scope > .about._formBlock');
  await expect(about.locator(':scope > img.icon')).toHaveAttribute('src', '/client-assets/about-icon.png');
  await expect(about.locator(':scope > .version')).toHaveText('v12.119.2-port.1');
  await expect(about.locator(':scope > span.emoji._physics_circle_')).toHaveCount(32);
  await expect(about).toHaveAttribute('data-physics-prepared', 'true');
  await expect(pageRoot.locator(':scope > .vrtktovh')).toHaveCount(3);
  await expect(pageRoot.locator(':scope > .vrtktovh').nth(0).locator('.ffcbddfc')).toHaveCount(4);
  await expect(pageRoot.locator(':scope > .vrtktovh').nth(1).locator('.ffcbddfc')).toHaveCount(9);
  await expect(pageRoot.locator('a[target="_blank"]').first()).toHaveAttribute('rel', 'noopener noreferrer');

  const alpha = await about.evaluate(element => {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (context === null) return null;
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(alpha, 'about panel must not regress to a transparent background').toBe(255);

  await about.locator(':scope > img.icon').click();
  await expect(about).toHaveClass(/playing/);
  await expect(about).toHaveAttribute('data-physics-active', 'true');
  await expect.poll(async () => about.locator(':scope > span.emoji').first().evaluate(
    element => (element as HTMLElement).style.transform)).toMatch(/^translate\(.+rotate\(.+rad\)$/);
  await expect(about).not.toHaveAttribute('data-physics-error-code', /.+/);

  await page.goto('/');
  await expect(page.locator('.rsqzvsbo')).toHaveCount(1);
  expect(failures).toEqual([]);
});

test('about-misskey route changes do not leave an unhandled interop exception in the server circuit', async ({ page }) => {
  const reset = await page.request.post('/__test/reset-diagnostics');
  expect(reset.status()).toBe(204);

  for (let attempt = 0; attempt < 5; attempt += 1) {
    await page.goto('/about-misskey');
    await expect(page.locator('.znqjceqz > .about')).toHaveAttribute('data-physics-prepared', 'true');
    await page.goto('/');
    await expect(page.locator('.rsqzvsbo')).toHaveCount(1);
  }

  const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
    unhandledExceptions: Array<{ category: string; eventId: number; exceptionType: string }>;
  };
  expect(diagnostics.unhandledExceptions).toEqual([]);
});

test('about-misskey opens the real instant post form with upstream initial MFM text', async ({ page }) => {
  const reset = await page.request.post('/__test/reset-compose');
  expect(reset.status()).toBe(204);
  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/);
  await page.evaluate(() => localStorage.setItem('drafts', JSON.stringify({
    note: { updatedAt: '2026-08-04T00:00:00Z', data: { text: 'unrelated saved draft' } }
  })));
  await page.goto('/about-misskey');

  await page.locator('.znqjceqz button.bghgjjyj.primary').click();
  const dialog = page.locator('body > .qzhlnise.dialog');
  await expect(dialog).toHaveCount(1);
  const textarea = dialog.locator('textarea[data-cy-post-form-text]');
  await expect(textarea).toHaveValue('I $[jelly ❤] #Misskey');

  await dialog.locator('button[data-cy-open-post-form-submit]').click();
  await expect(dialog).toHaveCount(0);
  const state = await (await page.request.get('/__test/compose-state')).json() as {
    createCalls: number;
    lastCreatedText: string | null;
  };
  expect(state).toEqual({ createCalls: 1, lastCreatedText: 'I $[jelly ❤] #Misskey' });
});

test('welcome menu preserves MkModal, MkPopupMenu, and MkMenu behavior', async ({ page }) => {
  await page.goto('/');

  const source = page.locator('.rsqzvsbo > .top > .main > button.menu');
  await source.click();

  const modal = page.locator('body .qzhlnise.popup');
  await expect(modal).toHaveCount(1);
  await expect(modal.locator(':scope > .bg._modalBg.transparent')).toHaveCount(1);
  const menu = modal.locator(':scope > .content > .sfhdhdhq > .rrevdjwt._popup._shadow');
  await expect(menu).toHaveCount(1);
  await expect(menu.locator(':scope > .item')).toHaveCount(3);
  await expect(menu.locator(':scope > .divider')).toHaveCount(1);
  await expect(menu.locator(':scope > .item').nth(0)).toContainText('インスタンス情報');
  await expect(menu.locator(':scope > .item').nth(2)).toHaveAttribute('rel', 'noopener noreferrer');

  await page.keyboard.press('Escape');
  await expect(modal).toHaveCount(0);
  await expect(source).toBeFocused();
});

test('welcome menu survives Escape while its JS attachment is still being established', async ({ page }) => {
  await page.goto('/');

  const source = page.locator('.rsqzvsbo > .top > .main > button.menu');
  const modal = page.locator('body .qzhlnise.popup');
  for (let attempt = 0; attempt < 5; attempt += 1) {
    // Do not wait for the popup's observer/focus JS to finish.  This is the ordering that used
    // to dispose the DotNetObjectReference while Interactive Server was serializing it.
    await source.click();
    await page.keyboard.press('Escape');
    await expect(modal).toHaveCount(0);
    await expect(source).toBeFocused();
  }

  // A terminated circuit leaves Blazor's reconnect UI behind and cannot process this final
  // event.  Reopening the menu therefore proves that the rapid-close sequence stayed live.
  await source.click();
  await expect(modal.locator('.rrevdjwt._popup._shadow')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(modal).toHaveCount(0);
});

test('modal window reproduces enter leave cancellation focus and computed-duration fallback', async ({ page }) => {
  await page.goto('/');

  const startingFrame = await page.evaluate(async () => {
    const source = document.createElement('button');
    source.id = 'dialog-motion-source';
    source.textContent = 'open';
    document.body.append(source);
    source.focus();

    const modal = document.createElement('div');
    modal.id = 'dialog-motion-harness';
    modal.className = 'qzhlnise dialog modal-enter-active modal-enter-from';
    modal.setAttribute('role', 'dialog');
    modal.innerHTML = `
      <div class="bg _modalBg"></div>
      <div class="content">
        <div class="ebkgoccj _narrow_" style="width:370px;height:400px">
          <div class="header"><span class="title">ログイン</span><button class="_button" type="button">close</button></div>
          <div class="body"><input value="alice"><button type="button">submit</button></div>
        </div>
      </div>`;
    document.body.append(modal);

    const events: string[] = [];
    const receiver = {
      invokeMethodAsync(name: string) {
        events.push(name);
        return Promise.resolve();
      }
    };
    const module = await import('/_content/ActivityPub.Misskey.Blazor/js/dialog-window.js');
    const handle = module.attach(
      modal,
      modal.querySelector(':scope > .content'),
      modal.querySelector('.ebkgoccj'),
      receiver);
    (window as any).__misskeyDialogMotion = { handle, events };
    modal.querySelector('input')?.focus();
    return {
      className: modal.className,
      motionState: modal.dataset.motionState,
      contentOpacity: getComputedStyle(modal.querySelector(':scope > .content')!).opacity,
    };
  });

  const modal = page.locator('#dialog-motion-harness');
  expect(startingFrame.className).toContain('modal-enter-from');
  expect(startingFrame.motionState).toBe('entering');
  expect(startingFrame.contentOpacity).toBe('0');
  await expect.poll(async () => modal.getAttribute('data-motion-state')).toBe('entered');
  await expect(page.locator('#dialog-motion-harness input')).toBeFocused();
  const transition = await modal.locator(':scope > .content').evaluate(element => ({
    duration: getComputedStyle(element).transitionDuration,
    properties: getComputedStyle(element).transitionProperty,
  }));
  expect(transition.duration).toContain('0.2s');
  expect(transition.properties).toContain('opacity');
  expect(transition.properties).toContain('transform');

  const leavingFrame = await page.evaluate(() => {
    (window as any).__misskeyDialogMotion.handle.close();
    const current = document.querySelector('#dialog-motion-harness')!;
    return {
      className: current.className,
      motionState: (current as HTMLElement).dataset.motionState,
    };
  });
  expect(leavingFrame.motionState).toBe('leaving');
  expect(leavingFrame.className).toContain('modal-leave-to');
  await expect.poll(async () => modal.getAttribute('data-motion-state')).toBe('left');
  expect(await page.evaluate(() => (window as any).__misskeyDialogMotion.events)).toEqual([
    'NotifyOpened',
    'NotifyClosed'
  ]);
  await page.evaluate(() => (window as any).__misskeyDialogMotion.handle.dispose());
  await expect(page.locator('#dialog-motion-source')).toBeFocused();

  await page.evaluate(async () => {
    const first = document.querySelector('#dialog-motion-harness');
    first?.remove();
    const modal = document.createElement('div');
    modal.id = 'dialog-motion-cancel-harness';
    modal.className = 'qzhlnise dialog modal-enter-active modal-enter-from';
    modal.innerHTML = '<div class="bg _modalBg"></div><div class="content"><div class="ebkgoccj"><button>close</button></div></div>';
    document.body.append(modal);
    const events: string[] = [];
    const receiver = {
      invokeMethodAsync(name: string) {
        events.push(name);
        return Promise.resolve();
      }
    };
    const module = await import('/_content/ActivityPub.Misskey.Blazor/js/dialog-window.js');
    const handle = module.attach(
      modal,
      modal.querySelector(':scope > .content'),
      modal.querySelector('.ebkgoccj'),
      receiver);
    (window as any).__misskeyDialogCancel = { handle, events };
    handle.close();
  });
  const cancelled = page.locator('#dialog-motion-cancel-harness');
  await expect.poll(async () => cancelled.getAttribute('data-motion-state')).toBe('left');
  expect(await page.evaluate(() => (window as any).__misskeyDialogCancel.events)).toEqual(['NotifyClosed']);
  await page.evaluate(() => (window as any).__misskeyDialogCancel.handle.dispose());
});

test('authenticated root preserves the Misskey timeline component hierarchy and real note content', async ({ page }) => {
  await page.goto('/__test/sign-in');
  await expect(page).toHaveURL(/\/$/);

  const shell = page.locator('body > .dkgtipfy');
  await expect(shell).toHaveCount(1);
  await expect(shell.locator(':scope > .mvcprjjd.sidebar')).toHaveCount(1);
  await expect(shell.locator(':scope > .contents')).toHaveCount(1);
  const universalHeader = shell.locator(':scope > .contents > div:first-child');
  await expect(universalHeader.locator(':scope > ._statusbars_1bps6_1')).toHaveCount(1);
  await expect(universalHeader).toHaveText('');
  await expect(universalHeader).toHaveCSS('height', '0px');
  await expect(shell.locator(':scope > .widgets > .efzpzdvf')).toHaveCount(1);

  const timelinePage = shell.locator('.contents main .cmuxhskf');
  await expect(timelinePage).toHaveCount(1);
  await expect(shell.locator(':scope > .contents main .fdidabkb')).toHaveCount(1);
  await expect(timelinePage.locator('xpath=ancestor::div[contains(@class, "_content_b6w6v_6")]')).toHaveCount(1);
  await expect(timelinePage.locator(':scope > .tl._block > .tl > .giivymft.noGap')).toHaveCount(1);
  await expect(timelinePage.locator('.sqadhkmv.noGap.notes > .tkcbzcuz.qtqtichx')).toHaveCount(1);

  const note = timelinePage.locator('.tkcbzcuz.qtqtichx');
  await expect(note.locator(':scope > article.article > .main > header.kkwtjztg')).toHaveCount(1);
  const noteContent = note.locator(
    ':scope > article.article > .main > .body > .content > .text > .havbbuyv');
  await expect(noteContent).toHaveCount(1);
  await expect(noteContent).toContainText('Misskey');
  await expect(noteContent).not.toContainText('AppearNote.Text');
  await expect(noteContent.locator(':scope > b')).toHaveText('v12');
  await expect(note.locator('footer.footer > .tdflqwzn > .hkzvhatu')).toHaveCount(2);
});

test('timeline header switches the rendered timeline without replacing the upstream page structure', async ({ page }) => {
  await page.goto('/__test/sign-in');
  const header = page.locator('.fdidabkb');
  await expect(header.locator(':scope > .tabs > button.tab')).toHaveCount(4);
  await header.locator(':scope > .tabs > button[title="ローカル"]').click();
  await expect(page.locator('.cmuxhskf > .tl._block > .tl > .giivymft.noGap')).toHaveCount(1);
  await expect(header.locator(':scope > .tabs > button[title="ローカル"]')).toHaveClass(/active/);
});

test('note reactions use the complete v12 picker and preserve the selected federated reaction', async ({ page }) => {
  const reset = await page.request.post('/__test/reset-reaction');
  expect(reset.status()).toBe(204);
  await page.goto('/__test/sign-in');
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');

  const note = page.locator('.tkcbzcuz.qtqtichx');
  const addReaction = note.locator('footer.footer > button:has(> i.fa-plus)');
  await expect(addReaction.locator(':scope > i')).toHaveClass(/fa-plus/);
  await addReaction.click();

  const picker = page.locator('body > .qzhlnise.popup > .content > .omfetrab.ryghynhb._popup._shadow');
  await expect(picker).toHaveCount(1);
  const pinned = picker.locator(':scope > .emojis > .group.index > section:first-child > .body > button.item');
  await expect(pinned).toHaveCount(10);
  await pinned.nth(5).click();

  await expect(picker).toHaveCount(0);
  const removeReaction = note.locator('footer.footer > button.reacted:has(> i.fa-minus)');
  await expect(removeReaction.locator(':scope > i')).toHaveClass(/fa-minus/);
  const created = await (await page.request.get('/__test/reaction-state')).json() as {
    viewerReaction: string | null;
    reactionCalls: number;
    lastRemove: boolean | null;
  };
  expect(created).toMatchObject({ viewerReaction: '🎉', reactionCalls: 1, lastRemove: false });

  await removeReaction.click();
  await expect(addReaction.locator(':scope > i')).toHaveClass(/fa-plus/);
  const removed = await (await page.request.get('/__test/reaction-state')).json() as {
    viewerReaction: string | null;
    reactionCalls: number;
    lastRemove: boolean | null;
  };
  expect(removed).toMatchObject({ viewerReaction: null, reactionCalls: 2, lastRemove: true });
});

test('authenticated post form preserves MkPostForm, visibility picker, preview, and modal behavior', async ({ page }) => {
  await page.goto('/__test/sign-in');
  // MFM parsing is performed after the Interactive Server circuit is connected.  Waiting for
  // the parsed node prevents a click from racing server-side event handler attachment.
  await expect(page.locator('.havbbuyv b')).toHaveText('v12');
  const source = page.locator('.mvcprjjd .bottom > button.post[data-cy-open-post-form]');
  await source.click();

  const dialog = page.locator('body > .qzhlnise.dialog');
  await expect(dialog).toHaveCount(1);
  await expect(dialog.locator(':scope > .bg._modalBg')).toHaveCount(1);
  const form = dialog.locator(':scope > .content.top > .gafaadew.modal._popup');
  await expect(form).toHaveCount(1);
  await expect(form.locator(':scope > header > .account > .avatar')).toHaveCount(1);
  await expect(form.locator(':scope > .form > textarea.text[data-cy-post-form-text]')).toHaveCount(1);
  await expect(form.locator(':scope > .form > footer > button')).toHaveCount(6);
  await expect(page.locator('.mk-composer')).toHaveCount(0);

  const formBackgroundAlpha = await form.evaluate(element => {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (context === null) return null;
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(formBackgroundAlpha, 'MkPostForm popup background must not regress to transparent').toBe(255);

  await form.locator(':scope > .form > footer > button[title="絵文字"]').click();
  const emojiPicker = page.locator('body > .qzhlnise.popup > .content > .omfetrab.ryghynhb._popup._shadow');
  await expect(emojiPicker).toHaveCount(1);
  await expect(emojiPicker.locator(':scope > .emojis > .group.index > section:first-child > .body > button.item')).toHaveCount(10);
  await expect(emojiPicker.locator(':scope > .emojis > .group:last-child > section')).toHaveCount(9);
  await emojiPicker.locator(':scope > input.search').fill('grinning');
  const grinning = emojiPicker.locator(':scope > .emojis > section.result > .body > button[title="grinning"]');
  await expect(grinning.locator('img.mk-emoji')).toHaveAttribute('src', /\/twemoji\/1f600\.svg$/);
  await grinning.click();
  await expect(emojiPicker).toHaveCount(0);
  await expect(form.locator('textarea[data-cy-post-form-text]')).toHaveValue('😀');

  await form.locator('header > .right > button.visibility').click();
  const visibility = page.locator('body > .qzhlnise.popup .gqyayizv._popup');
  await expect(visibility).toHaveCount(1);
  await expect(visibility.locator(':scope > button')).toHaveCount(5);
  await visibility.locator(':scope > button[data-index="2"]').click();
  await expect(visibility).toHaveCount(0);
  await expect(form.locator('header > .right > button.visibility > span > i')).toHaveClass(/fa-home/);

  const textarea = form.locator('textarea[data-cy-post-form-text]');
  await textarea.fill('Blazor **post form** #fediverse');
  await form.locator('header > .right > button.preview').click();
  await expect(form.locator(':scope > .form > .fefdfafb.preview .havbbuyv b')).toHaveText('post form');

  await form.locator('button[data-cy-open-post-form-submit]').click();
  await expect(dialog).toHaveCount(0);
  await expect(source).toBeFocused();
});
