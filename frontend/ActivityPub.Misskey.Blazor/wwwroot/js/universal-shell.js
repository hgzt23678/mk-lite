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
    let completeCalled = false;
    const complete = () => {
      if (completeCalled) return;
      completeCalled = true;
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

function deviceKind() {
  try {
    const state = JSON.parse(localStorage.getItem('pizzax::base') ?? 'null');
    if (state && ['desktop', 'tablet', 'smartphone'].includes(state.overridedDeviceKind)) {
      return state.overridedDeviceKind;
    }
  } catch {
    // The pinned client treats an unreadable device override as absent.
  }

  const userAgent = navigator.userAgent.toLowerCase();
  const tablet = /ipad/.test(userAgent) || (/mobile|iphone|android/.test(userAgent) && window.innerWidth > 700);
  const smartphone = !tablet && /mobile|iphone|android/.test(userAgent);
  return smartphone ? 'smartphone' : tablet ? 'tablet' : 'desktop';
}

function focusableItems(root) {
  return [...root.querySelectorAll(
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
    .filter(element => element instanceof HTMLElement && !element.hidden && element.getClientRects().length > 0);
}

function attachStickySidebar(container) {
  const element = container.children[0];
  if (!(element instanceof HTMLElement)) return null;

  const spacer = document.createElement('div');
  container.prepend(spacer);
  element.style.position = 'sticky';
  const offsetTop = container.getBoundingClientRect().top;
  let lastScrollTop = 0;
  let isTop = false;
  let isBottom = false;

  const calculate = scrollTop => {
    if (scrollTop > lastScrollTop) {
      const overflow = Math.max(0, element.clientHeight - window.innerHeight);
      element.style.bottom = '';
      element.style.top = `${-overflow}px`;
      isBottom = scrollTop + window.innerHeight >= element.offsetTop + element.clientHeight;
      if (isTop) {
        isTop = false;
        spacer.style.marginTop = `${Math.max(0, lastScrollTop - offsetTop)}px`;
      }
    } else {
      const overflow = element.clientHeight - window.innerHeight;
      element.style.top = '';
      element.style.bottom = `${-overflow}px`;
      isTop = scrollTop <= element.offsetTop;
      if (isBottom) {
        isBottom = false;
        spacer.style.marginTop = `${lastScrollTop - offsetTop - overflow}px`;
      }
    }
    lastScrollTop = scrollTop <= 0 ? 0 : scrollTop;
  };

  return {
    calculate,
    dispose() {
      spacer.remove();
      element.style.position = '';
      element.style.top = '';
      element.style.bottom = '';
    },
  };
}

export function attach(root, receiver) {
  let disposed = false;
  let resizeFrame = 0;
  let firstMotionFrame = 0;
  let secondMotionFrame = 0;
  let sticky = null;
  let stickyContainer = null;
  let activePanel = null;
  let activeKind = null;
  const motionGenerations = new Map();
  let menuBackdrop = null;
  let widgetsBackdrop = null;

  document.documentElement.style.overflowY = 'scroll';

  const publish = () => {
    resizeFrame = 0;
    if (disposed || !root.isConnected) return;
    receiver.invokeMethodAsync(
      'UpdateUniversalMetrics',
      Math.round(window.innerWidth),
      deviceKind(),
      localStorage.getItem('wallpaper') !== null).catch(() => {});
  };
  const schedule = () => {
    if (!disposed && resizeFrame === 0) resizeFrame = requestAnimationFrame(publish);
  };
  const onResize = () => schedule();

  const requestBackdropClose = kind => {
    receiver.invokeMethodAsync('RequestUniversalBackdropClose', kind).catch(() => {});
  };
  const onMenuTouch = () => requestBackdropClose('menuDrawer');
  const onWidgetsTouch = () => requestBackdropClose('widgetsDrawer');

  const observeChildren = () => {
    const nextStickyContainer = root.querySelector(':scope > .widgets');
    if (nextStickyContainer !== stickyContainer) {
      sticky?.dispose();
      sticky = null;
      stickyContainer = nextStickyContainer;
      if (stickyContainer instanceof HTMLElement) sticky = attachStickySidebar(stickyContainer);
    }

    const nextMenuBackdrop = root.querySelector(':scope > .menuDrawer-back');
    if (nextMenuBackdrop !== menuBackdrop) {
      menuBackdrop?.removeEventListener('touchstart', onMenuTouch);
      menuBackdrop = nextMenuBackdrop;
      menuBackdrop?.addEventListener('touchstart', onMenuTouch, { passive: true });
    }

    const nextWidgetsBackdrop = root.querySelector(':scope > .widgetsDrawer-back');
    if (nextWidgetsBackdrop !== widgetsBackdrop) {
      widgetsBackdrop?.removeEventListener('touchstart', onWidgetsTouch);
      widgetsBackdrop = nextWidgetsBackdrop;
      widgetsBackdrop?.addEventListener('touchstart', onWidgetsTouch, { passive: true });
    }
  };

  const mutationObserver = new MutationObserver(observeChildren);
  mutationObserver.observe(root, { childList: true, subtree: true });

  const onScroll = () => sticky?.calculate(window.scrollY);
  window.addEventListener('scroll', onScroll, { passive: true });
  window.addEventListener('resize', onResize, { passive: true });

  const onKeyDown = event => {
    if (!activePanel || !activePanel.isConnected) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      requestBackdropClose(activeKind);
      return;
    }
    if (event.key !== 'Tab') return;
    const items = focusableItems(activePanel);
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

  observeChildren();
  publish();

  return {
    beginEnter(kind, background, panel, generation) {
      motionGenerations.set(kind, generation);
      if (firstMotionFrame) cancelAnimationFrame(firstMotionFrame);
      if (secondMotionFrame) cancelAnimationFrame(secondMotionFrame);
      activeKind = kind;
      activePanel = panel;
      firstMotionFrame = requestAnimationFrame(() => {
        firstMotionFrame = 0;
        secondMotionFrame = requestAnimationFrame(async () => {
          secondMotionFrame = 0;
          if (disposed) return;
          background.classList.remove(`${kind}-back-enter-from`);
          panel.classList.remove(`${kind}-enter-from`);
          focusableItems(panel)[0]?.focus({ preventScroll: true });
          const isCurrent = () => !disposed && motionGenerations.get(kind) === generation && activeKind === kind && activePanel === panel;
          await waitForMotion([background, panel], isCurrent);
          if (isCurrent()) {
            receiver.invokeMethodAsync('NotifyUniversalMotionCompleted', kind, generation, true).catch(() => {});
          }
        });
      });
    },
    beginLeave(kind, background, panel, source, generation) {
      motionGenerations.set(kind, generation);
      if (firstMotionFrame) cancelAnimationFrame(firstMotionFrame);
      if (secondMotionFrame) cancelAnimationFrame(secondMotionFrame);
      firstMotionFrame = 0;
      secondMotionFrame = 0;
      background.classList.remove(`${kind}-back-enter-active`, `${kind}-back-enter-from`);
      panel.classList.remove(`${kind}-enter-active`, `${kind}-enter-from`);
      void panel.offsetWidth;
      background.classList.add(`${kind}-back-leave-active`);
      panel.classList.add(`${kind}-leave-active`);
      activePanel = null;
      activeKind = null;
      const isCurrent = () => !disposed && motionGenerations.get(kind) === generation && panel.isConnected;
      waitForMotion([background, panel], isCurrent).then(() => {
        if (!isCurrent()) return;
        if (source instanceof HTMLElement && source.isConnected) source.focus({ preventScroll: true });
        receiver.invokeMethodAsync('NotifyUniversalMotionCompleted', kind, generation, false).catch(() => {});
      });
    },
    scrollTop() {
      window.scroll({ top: 0, behavior: 'smooth' });
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (resizeFrame) cancelAnimationFrame(resizeFrame);
      if (firstMotionFrame) cancelAnimationFrame(firstMotionFrame);
      if (secondMotionFrame) cancelAnimationFrame(secondMotionFrame);
      window.removeEventListener('resize', onResize);
      window.removeEventListener('scroll', onScroll);
      document.removeEventListener('keydown', onKeyDown, true);
      menuBackdrop?.removeEventListener('touchstart', onMenuTouch);
      widgetsBackdrop?.removeEventListener('touchstart', onWidgetsTouch);
      mutationObserver.disconnect();
      sticky?.dispose();
    },
  };
}
