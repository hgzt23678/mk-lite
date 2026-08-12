function milliseconds(value) {
  const trimmed = value.trim();
  if (trimmed.endsWith('ms')) return Number.parseFloat(trimmed) || 0;
  if (trimmed.endsWith('s')) return (Number.parseFloat(trimmed) || 0) * 1000;
  return 0;
}

function repeated(values, index) {
  return values[index % values.length] ?? 0;
}

function maximumMotionMilliseconds(element) {
  const style = getComputedStyle(element);
  const transitionDurations = style.transitionDuration.split(',').map(milliseconds);
  const transitionDelays = style.transitionDelay.split(',').map(milliseconds);
  const animationDurations = style.animationDuration.split(',').map(milliseconds);
  const animationDelays = style.animationDelay.split(',').map(milliseconds);
  const animationIterations = style.animationIterationCount.split(',').map(value => {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? Math.max(parsed, 1) : 1;
  });

  let maximum = 0;
  for (let index = 0; index < transitionDurations.length; index += 1) {
    maximum = Math.max(maximum, transitionDurations[index] + repeated(transitionDelays, index));
  }
  for (let index = 0; index < animationDurations.length; index += 1) {
    maximum = Math.max(
      maximum,
      animationDurations[index] * repeated(animationIterations, index) + repeated(animationDelays, index));
  }
  return maximum;
}

function waitForMotion(elements, generationIsCurrent) {
  const expected = Math.max(...elements.map(maximumMotionMilliseconds), 0);
  if (expected <= 0) return Promise.resolve();

  return new Promise(resolve => {
    const started = performance.now();
    let timer = 0;
    let done = false;
    const complete = () => {
      if (done) return;
      done = true;
      for (const element of elements) {
        element.removeEventListener('transitionend', onEnd);
        element.removeEventListener('transitioncancel', onCancel);
        element.removeEventListener('animationend', onEnd);
        element.removeEventListener('animationcancel', onCancel);
      }
      if (timer) clearTimeout(timer);
      resolve();
    };
    const onEnd = event => {
      if (!elements.includes(event.target) || !generationIsCurrent()) return;
      // The tray transitions both opacity and transform.  A shorter property or a nested
      // transition must not remove the drawer before the longest computed motion finishes.
      if (performance.now() - started >= expected - 20) complete();
    };
    const onCancel = event => {
      if (elements.includes(event.target) && !generationIsCurrent()) complete();
    };
    for (const element of elements) {
      element.addEventListener('transitionend', onEnd);
      element.addEventListener('transitioncancel', onCancel);
      element.addEventListener('animationend', onEnd);
      element.addEventListener('animationcancel', onCancel);
    }
    timer = window.setTimeout(complete, Math.ceil(expected) + 80);
  });
}

function focusableItems(root) {
  return [...root.querySelectorAll(
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
    .filter(element => element instanceof HTMLElement && !element.hidden && element.getClientRects().length > 0);
}

export function attach(root, receiver) {
  let disposed = false;
  let frame = 0;
  let firstMotionFrame = 0;
  let secondMotionFrame = 0;
  let motionGeneration = 0;
  let observedHeader = null;
  let lastViewportWidth = -1;
  let lastHeaderWidth = -1;
  let lastTitle = null;
  let observedBackground = null;
  let activeMenu = null;

  const resizeObserver = new ResizeObserver(() => schedule());
  const titleObserver = new MutationObserver(() => schedule());
  const rootObserver = new MutationObserver(() => {
    observeHeader();
    observeBackground();
    if (activeMenu && !activeMenu.isConnected) activeMenu = null;
    schedule();
  });

  const observeHeader = () => {
    const header = root.querySelector(':scope > .main > .contents > .header');
    if (header === observedHeader) return;
    if (observedHeader) resizeObserver.unobserve(observedHeader);
    observedHeader = header;
    if (observedHeader) resizeObserver.observe(observedHeader);
  };

  const onBackgroundTouch = () => {
    receiver.invokeMethodAsync('RequestCloseTrayFromBrowser').catch(() => {});
  };

  const observeBackground = () => {
    const background = root.querySelector(':scope > .menu-back');
    if (background === observedBackground) return;
    if (observedBackground) observedBackground.removeEventListener('touchstart', onBackgroundTouch);
    observedBackground = background;
    if (observedBackground) observedBackground.addEventListener('touchstart', onBackgroundTouch, { passive: true });
  };

  const publish = () => {
    frame = 0;
    if (disposed || !root.isConnected) return;
    observeHeader();
    const viewportWidth = Math.round(window.innerWidth);
    const headerWidth = observedHeader ? Math.round(observedHeader.getBoundingClientRect().width) : -1;
    const title = document.title;
    if (viewportWidth === lastViewportWidth && headerWidth === lastHeaderWidth && title === lastTitle) return;
    lastViewportWidth = viewportWidth;
    lastHeaderWidth = headerWidth;
    lastTitle = title;
    receiver.invokeMethodAsync('UpdateVisitorMetrics', viewportWidth, headerWidth, title).catch(() => {});
  };

  const schedule = () => {
    if (!disposed && frame === 0) frame = requestAnimationFrame(publish);
  };

  const onResize = () => schedule();
  window.addEventListener('resize', onResize, { passive: true });
  rootObserver.observe(root, { childList: true, subtree: true });
  const titleElement = document.head.querySelector('title');
  if (titleElement) titleObserver.observe(titleElement, { childList: true, subtree: true, characterData: true });
  observeHeader();
  observeBackground();
  publish();

  const onKeyDown = event => {
    if (!activeMenu || !activeMenu.isConnected) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      receiver.invokeMethodAsync('RequestCloseTrayFromBrowser').catch(() => {});
      return;
    }
    if (event.key !== 'Tab') return;
    const items = focusableItems(activeMenu);
    if (items.length === 0) {
      event.preventDefault();
      return;
    }
    const index = items.indexOf(document.activeElement);
    const next = event.shiftKey ? index - 1 : index + 1;
    if (index < 0 || next < 0 || next >= items.length) {
      event.preventDefault();
      items[event.shiftKey ? items.length - 1 : 0].focus({ preventScroll: true });
    }
  };
  document.addEventListener('keydown', onKeyDown, true);

  const beginEnter = async (background, menu) => {
    const generation = ++motionGeneration;
    if (firstMotionFrame) cancelAnimationFrame(firstMotionFrame);
    if (secondMotionFrame) cancelAnimationFrame(secondMotionFrame);
    background.dataset.motionState = 'entering';
    menu.dataset.motionState = 'entering';
    activeMenu = menu;
    firstMotionFrame = requestAnimationFrame(() => {
      firstMotionFrame = 0;
      secondMotionFrame = requestAnimationFrame(async () => {
        secondMotionFrame = 0;
        if (disposed || generation !== motionGeneration) return;
        background.classList.remove('tray-back-enter-from');
        menu.classList.remove('tray-enter-from');
        focusableItems(menu)[0]?.focus({ preventScroll: true });
        const isCurrent = () => !disposed && generation === motionGeneration;
        await waitForMotion([background, menu], isCurrent);
        if (!isCurrent()) return;
        background.dataset.motionState = 'entered';
        menu.dataset.motionState = 'entered';
        receiver.invokeMethodAsync('NotifyTrayEntered').catch(() => {});
      });
    });
    return generation;
  };

  const beginLeave = (background, menu, source) => {
    const generation = ++motionGeneration;
    if (firstMotionFrame) cancelAnimationFrame(firstMotionFrame);
    if (secondMotionFrame) cancelAnimationFrame(secondMotionFrame);
    firstMotionFrame = 0;
    secondMotionFrame = 0;
    background.classList.remove('tray-back-enter-active', 'tray-back-enter-from');
    menu.classList.remove('tray-enter-active', 'tray-enter-from');
    // Read the entered frame before applying Vue's leave-active target classes.  This keeps
    // cancellation during the two-rAF enter boundary from snapping directly to the end frame.
    void menu.offsetWidth;
    background.classList.add('tray-back-leave-active');
    menu.classList.add('tray-leave-active');
    background.dataset.motionState = 'leaving';
    menu.dataset.motionState = 'leaving';
    activeMenu = null;
    const isCurrent = () => !disposed && generation === motionGeneration;
    waitForMotion([background, menu], isCurrent).then(() => {
      if (!isCurrent()) return;
      background.dataset.motionState = 'left';
      menu.dataset.motionState = 'left';
      if (source instanceof HTMLElement && source.isConnected) source.focus({ preventScroll: true });
      receiver.invokeMethodAsync('NotifyTrayLeft').catch(() => {});
    });
    return generation;
  };

  return {
    beginEnter,
    beginLeave,
    dispose() {
      if (disposed) return;
      disposed = true;
      motionGeneration += 1;
      if (frame) cancelAnimationFrame(frame);
      if (firstMotionFrame) cancelAnimationFrame(firstMotionFrame);
      if (secondMotionFrame) cancelAnimationFrame(secondMotionFrame);
      window.removeEventListener('resize', onResize);
      document.removeEventListener('keydown', onKeyDown, true);
      if (observedBackground) observedBackground.removeEventListener('touchstart', onBackgroundTouch);
      resizeObserver.disconnect();
      titleObserver.disconnect();
      rootObserver.disconnect();
    },
  };
}
