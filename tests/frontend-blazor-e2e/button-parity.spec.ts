import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('.rsqzvsbo > .top > .main')).toBeVisible();
});

test('MkButton preserves target-relative ripple geometry and the 1/1000/2000ms lifecycle', async ({ page }) => {
  const renderedButton = page.locator('[data-cy-signin]');
  await renderedButton.click();
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(1);
  await page.keyboard.press('Escape');
  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(0);
  await renderedButton.locator(':scope > .ripples').evaluate(element => element.replaceChildren());

  const result = await page.locator('[data-cy-signin]').evaluate(buttonElement => {
    const button = buttonElement as HTMLButtonElement;
    const content = button.querySelector<HTMLElement>(':scope > .content');
    const ripples = button.querySelector<HTMLElement>(':scope > .ripples');
    if (!content || !ripples) throw new Error('MkButton DOM is incomplete');

    const originalSetTimeout = window.setTimeout;
    const scheduled: Array<{ callback: () => void; delay: number }> = [];
    let nextTimer = 10_000;
    window.setTimeout = ((callback: TimerHandler, delay?: number) => {
      if (typeof callback !== 'function') throw new Error('Unexpected string timer');
      scheduled.push({ callback: callback as () => void, delay: delay ?? 0 });
      return nextTimer++;
    }) as typeof window.setTimeout;

    try {
      const targetRect = content.getBoundingClientRect();
      const buttonRect = button.getBoundingClientRect();
      const clientX = targetRect.left + Math.max(2, content.clientWidth * 0.2);
      const clientY = targetRect.top + Math.max(2, content.clientHeight * 0.35);
      const pointer = new MouseEvent('mousedown', {
        bubbles: true,
        clientX,
        clientY,
        button: 0,
      });
      // Browser engines quantize synthetic pointer coordinates differently. The
      // upstream implementation consumes the coordinates exposed by MouseEvent,
      // so the oracle must use those delivered values as well.
      const x = pointer.clientX - targetRect.left;
      const y = pointer.clientY - targetRect.top;
      const expectedTargetScale = Math.max(
        Math.hypot(x, y),
        Math.hypot(content.clientWidth - x, y),
        Math.hypot(x, content.clientHeight - y),
        Math.hypot(content.clientWidth - x, content.clientHeight - y));
      const rootX = pointer.clientX - buttonRect.left;
      const rootY = pointer.clientY - buttonRect.top;
      const rootScale = Math.max(
        Math.hypot(rootX, rootY),
        Math.hypot(button.clientWidth - rootX, rootY),
        Math.hypot(rootX, button.clientHeight - rootY),
        Math.hypot(button.clientWidth - rootX, button.clientHeight - rootY));

      content.dispatchEvent(pointer);

      const ripple = ripples.firstElementChild as HTMLElement | null;
      if (!ripple) throw new Error('Ripple was not created');
      const initial = {
        top: Number.parseFloat(ripple.style.top),
        left: Number.parseFloat(ripple.style.left),
        transform: ripple.style.transform,
        opacity: ripple.style.opacity,
      };
      scheduled.find(item => item.delay === 1)?.callback();
      const scaled = ripple.style.transform;
      scheduled.find(item => item.delay === 1000)?.callback();
      const faded = {
        property: ripple.style.transitionProperty,
        duration: ripple.style.transitionDuration,
        timingFunction: ripple.style.transitionTimingFunction,
        opacity: ripple.style.opacity,
      };
      scheduled.find(item => item.delay === 2000)?.callback();

      return {
        rootClass: button.className,
        rootTag: button.tagName,
        childClasses: Array.from(button.children, child => child.className),
        delays: scheduled.map(item => item.delay),
        initial,
        scaled,
        faded,
        remaining: ripples.childElementCount,
        expectedTop: y - 1,
        expectedLeft: x - 1,
        expectedTargetScale,
        rootScale,
        computed: {
          position: getComputedStyle(button).position,
          minWidth: getComputedStyle(button).minWidth,
          overflow: getComputedStyle(button).overflow,
        },
      };
    } finally {
      window.setTimeout = originalSetTimeout;
    }
  });

  expect(result.rootTag).toBe('BUTTON');
  expect(result.rootClass).toBe('bghgjjyj _button inline rounded');
  expect(result.childClasses).toEqual(['ripples', 'content']);
  expect(result.delays).toEqual([1, 1000, 2000]);
  expect(result.initial.top).toBeCloseTo(result.expectedTop, 3);
  expect(result.initial.left).toBeCloseTo(result.expectedLeft, 3);
  expect(result.initial.transform).toBe('');
  expect(result.initial.opacity).toBe('');
  const scale = Number(/^scale\((.+)\)$/.exec(result.scaled)?.[1]);
  expect(scale).toBeCloseTo(result.expectedTargetScale, 3);
  expect(Math.abs(scale - result.rootScale)).toBeGreaterThan(1);
  expect(result.faded).toEqual({
    property: 'all',
    duration: '1s',
    timingFunction: 'ease',
    opacity: '0',
  });
  expect(result.remaining).toBe(0);
  expect(result.computed).toEqual({ position: 'relative', minWidth: '100px', overflow: 'clip' });
});

test('button ripple module autofocuses after mount and fully disposes timers and listeners', async ({ page }) => {
  const result = await page.evaluate(async () => {
    const module = await import('/_content/ActivityPub.Misskey.Blazor/js/button-ripple.js');
    const button = document.createElement('button');
    button.type = 'button';
    button.innerHTML = '<div class="ripples"></div><div class="content">fixture</div>';
    document.body.appendChild(button);
    const attachment = module.attach(button, true);
    await Promise.resolve();
    const focused = document.activeElement === button;

    const content = button.querySelector<HTMLElement>('.content')!;
    content.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: 1, clientY: 1 }));
    const beforeDispose = button.querySelector('.ripples')!.childElementCount;
    attachment.dispose();
    const afterDispose = button.querySelector('.ripples')!.childElementCount;
    content.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: 1, clientY: 1 }));
    const afterRedispatch = button.querySelector('.ripples')!.childElementCount;

    const cancelled = document.createElement('button');
    cancelled.innerHTML = '<div class="ripples"></div>';
    document.body.appendChild(cancelled);
    const cancelledAttachment = module.attach(cancelled, true);
    cancelledAttachment.dispose();
    await Promise.resolve();
    const cancelledFocus = document.activeElement === cancelled;

    button.remove();
    cancelled.remove();
    return { focused, beforeDispose, afterDispose, afterRedispatch, cancelledFocus };
  });

  expect(result).toEqual({
    focused: true,
    beforeDispose: 1,
    afterDispose: 0,
    afterRedispatch: 0,
    cancelledFocus: false,
  });
});

test('keyboard activation keeps native button semantics and does not synthesize a mouse ripple', async ({ page }) => {
  const button = page.locator('[data-cy-signin]');
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  for (let tabIndex = 0; tabIndex < 30; tabIndex++) {
    if (await button.evaluate(element => document.activeElement === element)) break;
    await page.keyboard.press('Tab');
  }
  await expect(button).toBeFocused();
  await expect(button).toHaveCSS('outline-style', 'solid');
  await expect(button).toHaveCSS('outline-width', '2px');

  await page.keyboard.press('Enter');

  await expect(page.locator('body > .qzhlnise.dialog')).toHaveCount(1);
  await expect(button.locator(':scope > .ripples')).toBeEmpty();
});
