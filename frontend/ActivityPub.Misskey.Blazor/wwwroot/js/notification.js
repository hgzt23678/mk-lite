const tooltipDelayMilliseconds = 300;

export function attach(root, reaction, receiver, unread) {
  if (!(root instanceof HTMLElement) || !receiver || typeof unread !== 'boolean') {
    throw new Error('MISSKEY_NOTIFICATION_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let read = !unread;
  let readObserver = null;
  let sizeObserver = null;
  let showTimer = 0;
  let hideTimer = 0;
  const listeners = [];

  const listen = (element, type, handler, options) => {
    element.addEventListener(type, handler, options);
    listeners.push(() => element.removeEventListener(type, handler, options));
  };

  const applySize = width => {
    if (disposed || !Number.isFinite(width)) return;
    root.classList.toggle('max-width_500px', width <= 500);
    root.classList.toggle('max-width_600px', width <= 600);
  };

  const observedWidth = entry => {
    const borderBox = Array.isArray(entry.borderBoxSize) ? entry.borderBoxSize[0] : entry.borderBoxSize;
    return borderBox?.inlineSize ?? entry.contentRect?.width ?? root.getBoundingClientRect().width;
  };

  applySize(root.getBoundingClientRect().width);
  sizeObserver = new ResizeObserver(entries => {
    const entry = entries[entries.length - 1];
    if (entry) applySize(observedWidth(entry));
  });
  sizeObserver.observe(root);

  const disconnectReadObserver = () => {
    readObserver?.disconnect();
    readObserver = null;
  };
  const observeRead = () => {
    if (read || disposed || readObserver) return;
    readObserver = new IntersectionObserver(entries => {
      if (disposed || read || !entries.some(entry => entry.isIntersecting)) return;
      read = true;
      disconnectReadObserver();
      receiver.invokeMethodAsync('MarkNotificationReadAsync').catch(() => {
        if (!disposed) {
          read = false;
          observeRead();
        }
      });
    });
    readObserver.observe(root);
  };
  observeRead();

  if (reaction instanceof HTMLElement) {
    const clearShow = () => {
      if (showTimer !== 0) window.clearTimeout(showTimer);
      showTimer = 0;
    };
    const clearHide = () => {
      if (hideTimer !== 0) window.clearTimeout(hideTimer);
      hideTimer = 0;
    };
    const show = () => {
      clearHide();
      clearShow();
      showTimer = window.setTimeout(() => {
        showTimer = 0;
        if (!disposed) receiver.invokeMethodAsync('ShowReactionTooltipAsync').catch(() => {});
      }, tooltipDelayMilliseconds);
    };
    const hide = () => {
      clearShow();
      clearHide();
      hideTimer = window.setTimeout(() => {
        hideTimer = 0;
        if (!disposed) receiver.invokeMethodAsync('HideReactionTooltipAsync').catch(() => {});
      }, tooltipDelayMilliseconds);
    };
    listen(reaction, 'mouseover', show);
    listen(reaction, 'mouseleave', hide);
    listen(reaction, 'touchstart', show, { passive: true });
    listen(reaction, 'touchend', hide, { passive: true });
    listen(reaction, 'touchcancel', hide, { passive: true });
    listen(reaction, 'click', show);
  }

  return {
    setRead(value) {
      if (disposed || typeof value !== 'boolean') return;
      read = value;
      if (read) disconnectReadObserver();
      else observeRead();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      disconnectReadObserver();
      sizeObserver?.disconnect();
      sizeObserver = null;
      if (showTimer !== 0) window.clearTimeout(showTimer);
      if (hideTimer !== 0) window.clearTimeout(hideTimer);
      for (const remove of listeners.splice(0)) remove();
    },
  };
}
