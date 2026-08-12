function getScrollContainer(element) {
  if (!(element instanceof HTMLElement) || element.tagName === 'HTML') return null;
  const overflow = getComputedStyle(element).getPropertyValue('overflow-y');
  if (overflow === 'scroll' || overflow === 'auto') return element;
  return getScrollContainer(element.parentElement);
}

function scrollState(root) {
  const container = getScrollContainer(root);
  if (container === null) {
    const documentElement = document.documentElement;
    return {
      container: window,
      scrollTop: window.scrollY,
      scrollHeight: documentElement.scrollHeight,
      clientHeight: window.innerHeight,
      usesWindow: true,
    };
  }

  return {
    container,
    scrollTop: container.scrollTop,
    scrollHeight: container.scrollHeight,
    clientHeight: container.clientHeight,
    usesWindow: false,
  };
}

export function isTopVisible(root) {
  if (!(root instanceof HTMLElement)) {
    throw new Error('MISSKEY_PAGINATION_ROOT_INVALID');
  }
  return scrollState(root).scrollTop <= root.offsetTop;
}

export function isBottomVisible(root, tolerance = 1) {
  if (!(root instanceof HTMLElement) || !Number.isFinite(tolerance)) {
    throw new Error('MISSKEY_PAGINATION_BOTTOM_CONFIGURATION_INVALID');
  }
  const container = getScrollContainer(root);
  if (container !== null) {
    return root.scrollHeight <= container.clientHeight + Math.abs(container.scrollTop) + tolerance;
  }
  return root.scrollHeight <= window.innerHeight + window.scrollY + tolerance;
}

export function captureScroll(root) {
  if (!(root instanceof HTMLElement)) {
    throw new Error('MISSKEY_PAGINATION_ROOT_INVALID');
  }
  const state = scrollState(root);
  return {
    scrollTop: state.scrollTop,
    scrollHeight: state.scrollHeight,
    usesWindow: state.usesWindow,
    atBottom: state.scrollTop + state.clientHeight > state.scrollHeight - 32,
  };
}

export function restoreScroll(root, snapshot, stickToBottom) {
  if (!(root instanceof HTMLElement) || snapshot === null ||
      typeof snapshot.scrollTop !== 'number' || typeof snapshot.scrollHeight !== 'number') {
    throw new Error('MISSKEY_PAGINATION_SCROLL_SNAPSHOT_INVALID');
  }
  const state = scrollState(root);
  const top = stickToBottom
    ? Math.max(0, state.scrollHeight - state.clientHeight)
    : Math.max(0, snapshot.scrollTop + state.scrollHeight - snapshot.scrollHeight);
  if (state.usesWindow) window.scrollTo({ top, behavior: 'instant' });
  else state.container.scrollTo({ top, behavior: 'instant' });
}

export function scrollToTop(root) {
  if (!(root instanceof HTMLElement)) {
    throw new Error('MISSKEY_PAGINATION_ROOT_INVALID');
  }
  const state = scrollState(root);
  if (state.usesWindow) window.scroll({ top: 0 });
  else state.container.scroll({ top: 0 });
}

export function isWindowAtTop() {
  return window.scrollY === 0;
}

export function attach(root, receiver, enableAutoLoad) {
  if (!(root instanceof HTMLElement) || !receiver || typeof enableAutoLoad !== 'boolean') {
    throw new Error('MISSKEY_PAGINATION_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let observedTarget = null;
  let topNotificationPending = false;
  const intersectionObserver = enableAutoLoad
    ? new IntersectionObserver(entries => {
      if (!disposed && entries.some(entry => entry.isIntersecting)) {
        receiver.invokeMethodAsync('NotifyAutoLoadAsync').catch(() => {});
      }
    })
    : null;

  const findAutoLoadTarget = () => {
    if (disposed || intersectionObserver === null) return;
    const next = root.querySelector('[data-pagination-auto-load]');
    if (next === observedTarget) return;
    intersectionObserver.disconnect();
    observedTarget = next;
    if (observedTarget instanceof HTMLElement) intersectionObserver.observe(observedTarget);
  };
  const mutationObserver = new MutationObserver(findAutoLoadTarget);
  mutationObserver.observe(root, { childList: true, subtree: true });
  findAutoLoadTarget();

  const state = scrollState(root);
  const onScroll = () => {
    if (disposed || topNotificationPending || !document.body.contains(root) || !isTopVisible(root)) return;
    topNotificationPending = true;
    receiver.invokeMethodAsync('NotifyReachedTopAsync')
      .catch(() => {})
      .finally(() => { topNotificationPending = false; });
  };
  state.container.addEventListener('scroll', onScroll, { passive: true });

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      intersectionObserver?.disconnect();
      mutationObserver.disconnect();
      state.container.removeEventListener('scroll', onScroll);
    },
  };
}
