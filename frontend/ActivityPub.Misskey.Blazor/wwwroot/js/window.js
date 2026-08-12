import { claimZIndex } from './overlay-stack.js';

const minimumHeight = 50;
const minimumWidth = 250;

function point(event) {
  const touch = event.touches?.[0];
  return touch ? { x: touch.clientX, y: touch.clientY } : { x: event.clientX, y: event.clientY };
}

function maximumMotionMilliseconds(element) {
  const style = getComputedStyle(element);
  const durations = style.transitionDuration.split(',').map(parseTime);
  const delays = style.transitionDelay.split(',').map(parseTime);
  let maximum = 0;
  for (let index = 0; index < Math.max(durations.length, delays.length); index += 1) {
    maximum = Math.max(maximum, durations[index % durations.length] + delays[index % delays.length]);
  }
  return maximum;
}

function parseTime(value) {
  const normalized = value.trim();
  if (normalized.endsWith('ms')) return Number.parseFloat(normalized) || 0;
  if (normalized.endsWith('s')) return (Number.parseFloat(normalized) || 0) * 1000;
  return 0;
}

function waitForMotion(element, current) {
  const duration = maximumMotionMilliseconds(element);
  if (duration <= 0) return Promise.resolve();
  return new Promise(resolve => {
    let timer = 0;
    const finish = event => {
      if (event && event.target !== element) return;
      element.removeEventListener('transitionend', finish);
      element.removeEventListener('transitioncancel', finish);
      if (timer) clearTimeout(timer);
      resolve();
    };
    element.addEventListener('transitionend', finish);
    element.addEventListener('transitioncancel', finish);
    timer = window.setTimeout(finish, duration + 50);
    if (!current()) finish();
  });
}

export function attach(root, body, title, receiver, options) {
  const invalid = [];
  if (!(root instanceof HTMLElement)) invalid.push('root');
  if (!(body instanceof HTMLElement)) invalid.push('body');
  if (!(title instanceof HTMLElement)) invalid.push('title');
  if (receiver === null || typeof receiver !== 'object') invalid.push('receiver');
  if (options === null || typeof options !== 'object') {
    invalid.push('options');
  } else {
    for (const name of ['initialWidth', 'initialHeight']) {
      if (options[name] !== null && options[name] !== undefined &&
          (!Number.isFinite(options[name]) || options[name] <= 0)) invalid.push(name);
    }
    if (typeof options.canResize !== 'boolean') invalid.push('canResize');
    if (typeof options.front !== 'boolean') invalid.push('front');
    if (typeof options.animation !== 'boolean') invalid.push('animation');
  }
  if (invalid.length > 0) throw new Error(`MISSKEY_WINDOW_CONFIGURATION_INVALID:${invalid.join(',')}`);

  let disposed = false;
  let closing = false;
  let maximized = false;
  let beforeClickedAt = 0;
  let generation = 0;
  let enterFrame = 0;
  let settleFrame = 0;
  let unMaximized = { top: '', left: '', width: '', height: '' };
  const dragCleanups = new Set();

  const applyHeight = height => {
    root.style.height = `${Math.min(height, window.innerHeight)}px`;
  };
  const applyWidth = width => {
    root.style.width = `${Math.min(width, window.innerWidth)}px`;
  };
  const applyTop = top => {
    root.style.top = `${top}px`;
  };
  const applyLeft = left => {
    root.style.left = `${left}px`;
  };
  const state = () => {
    const rectangle = root.getBoundingClientRect();
    return {
      maximized,
      top: rectangle.top,
      left: rectangle.left,
      width: rectangle.width,
      height: rectangle.height,
      zIndex: Number.parseInt(root.style.zIndex, 10) || 0,
    };
  };
  const notifyState = () => receiver.invokeMethodAsync('NotifyWindowState', state()).catch(() => {});
  const bringToFront = (notify = true) => {
    root.style.zIndex = String(claimZIndex(options.front ? 'middle' : 'low'));
    if (notify) notifyState();
  };
  const maximize = () => {
    if (disposed || maximized) return;
    maximized = true;
    unMaximized = {
      top: root.style.top,
      left: root.style.left,
      width: root.style.width,
      height: root.style.height,
    };
    root.style.top = '0';
    root.style.left = '0';
    root.style.width = '100%';
    root.style.height = '100%';
    root.classList.add('maximized');
    notifyState();
  };
  const restore = () => {
    if (disposed || !maximized) return;
    maximized = false;
    root.style.top = unMaximized.top;
    root.style.left = unMaximized.left;
    root.style.width = unMaximized.width;
    root.style.height = unMaximized.height;
    root.classList.remove('maximized');
    notifyState();
  };
  const dragListen = listener => {
    const clear = () => {
      window.removeEventListener('mousemove', listener);
      window.removeEventListener('touchmove', listener);
      window.removeEventListener('mouseleave', clear);
      window.removeEventListener('mouseup', clear);
      window.removeEventListener('touchend', clear);
      dragCleanups.delete(clear);
      if (!disposed) notifyState();
    };
    dragCleanups.add(clear);
    window.addEventListener('mousemove', listener);
    window.addEventListener('touchmove', listener, { passive: false });
    window.addEventListener('mouseleave', clear);
    window.addEventListener('mouseup', clear);
    window.addEventListener('touchend', clear);
  };
  const onHeaderDown = event => {
    if (event.button === 2) return;
    event.preventDefault();
    let beforeMaximized = false;
    if (maximized) {
      beforeMaximized = true;
      restore();
    }
    const now = Date.now();
    if (now - beforeClickedAt < 300) {
      beforeClickedAt = now;
      maximize();
      return;
    }
    beforeClickedAt = now;
    if (!root.contains(document.activeElement)) root.focus();
    const position = root.getBoundingClientRect();
    const clicked = point(event);
    const moveBaseX = beforeMaximized ? Number.parseInt(unMaximized.width, 10) / 2 : clicked.x - position.left;
    const moveBaseY = beforeMaximized ? 20 : clicked.y - position.top;
    const browserWidth = window.innerWidth;
    const browserHeight = window.innerHeight;
    const windowWidth = root.offsetWidth;
    const windowHeight = root.offsetHeight;
    const move = (x, y) => {
      let moveLeft = x - moveBaseX;
      let moveTop = y - moveBaseY;
      if (moveTop + windowHeight > browserHeight) moveTop = browserHeight - windowHeight;
      if (moveLeft < 0) moveLeft = 0;
      if (moveTop < 0) moveTop = 0;
      if (moveLeft + windowWidth > browserWidth) moveLeft = browserWidth - windowWidth;
      applyLeft(moveLeft);
      applyTop(moveTop);
    };
    if (beforeMaximized) move(clicked.x, clicked.y);
    dragListen(moveEvent => {
      moveEvent.preventDefault?.();
      const next = point(moveEvent);
      move(next.x, next.y);
    });
  };
  const resizeTop = event => {
    const base = event.clientY;
    const height = Number.parseInt(getComputedStyle(root).height, 10);
    const top = Number.parseInt(getComputedStyle(root).top, 10);
    dragListen(moveEvent => {
      const move = moveEvent.clientY - base;
      if (top + move > 0) {
        if (height - move > minimumHeight) {
          applyHeight(height - move);
          applyTop(top + move);
        } else {
          applyHeight(minimumHeight);
          applyTop(top + (height - minimumHeight));
        }
      } else {
        applyHeight(top + height);
        applyTop(0);
      }
    });
  };
  const resizeRight = event => {
    const base = event.clientX;
    const width = Number.parseInt(getComputedStyle(root).width, 10);
    const left = Number.parseInt(getComputedStyle(root).left, 10);
    const browserWidth = window.innerWidth;
    dragListen(moveEvent => {
      const move = moveEvent.clientX - base;
      if (left + width + move < browserWidth) applyWidth(Math.max(minimumWidth, width + move));
      else applyWidth(browserWidth - left);
    });
  };
  const resizeBottom = event => {
    const base = event.clientY;
    const height = Number.parseInt(getComputedStyle(root).height, 10);
    const top = Number.parseInt(getComputedStyle(root).top, 10);
    const browserHeight = window.innerHeight;
    dragListen(moveEvent => {
      const move = moveEvent.clientY - base;
      if (top + height + move < browserHeight) applyHeight(Math.max(minimumHeight, height + move));
      else applyHeight(browserHeight - top);
    });
  };
  const resizeLeft = event => {
    const base = event.clientX;
    const width = Number.parseInt(getComputedStyle(root).width, 10);
    const left = Number.parseInt(getComputedStyle(root).left, 10);
    dragListen(moveEvent => {
      const move = moveEvent.clientX - base;
      if (left + move > 0) {
        if (width - move > minimumWidth) {
          applyWidth(width - move);
          applyLeft(left + move);
        } else {
          applyWidth(minimumWidth);
          applyLeft(left + (width - minimumWidth));
        }
      } else {
        applyWidth(left + width);
        applyLeft(0);
      }
    });
  };
  const startResize = (event, first, second = null) => {
    event.preventDefault();
    first(event);
    second?.(event);
  };
  const handleListeners = [
    ['.handle.top', resizeTop],
    ['.handle.right', resizeRight],
    ['.handle.bottom', resizeBottom],
    ['.handle.left', resizeLeft],
    ['.handle.top-left', resizeTop, resizeLeft],
    ['.handle.top-right', resizeTop, resizeRight],
    ['.handle.bottom-right', resizeBottom, resizeRight],
    ['.handle.bottom-left', resizeBottom, resizeLeft],
  ].map(([selector, first, second]) => {
    const element = root.querySelector(`:scope > ${selector}`);
    if (!(element instanceof HTMLElement)) return null;
    const listener = event => startResize(event, first, second);
    element.addEventListener('mousedown', listener);
    return { element, listener };
  }).filter(Boolean);
  const onBodyMouseDown = () => bringToFront();
  const onKeyDown = event => {
    if ((event.which === 27 || event.key === 'Escape') && !closing) {
      event.preventDefault();
      event.stopPropagation();
      receiver.invokeMethodAsync('NotifyEscape').catch(() => {});
    }
  };
  const onBrowserResize = () => {
    const position = root.getBoundingClientRect();
    const browserWidth = window.innerWidth;
    const browserHeight = window.innerHeight;
    const windowWidth = root.offsetWidth;
    const windowHeight = root.offsetHeight;
    if (position.left < 0) root.style.left = '0';
    if (position.top + windowHeight > browserHeight) root.style.top = `${browserHeight - windowHeight}px`;
    if (position.left + windowWidth > browserWidth) root.style.left = `${browserWidth - windowWidth}px`;
    if (position.top < 0) root.style.top = '0';
    notifyState();
  };
  const close = async () => {
    if (disposed || closing) return;
    closing = true;
    generation += 1;
    const current = generation;
    if (enterFrame) cancelAnimationFrame(enterFrame);
    if (settleFrame) cancelAnimationFrame(settleFrame);
    root.classList.remove('window-enter-active', 'window-enter-from');
    if (options.animation) root.classList.add('window-leave-active', 'window-leave-to');
    const isCurrent = () => !disposed && closing && current === generation;
    if (options.animation) await waitForMotion(root, isCurrent);
    if (!isCurrent()) return;
    root.classList.remove('window-leave-active', 'window-leave-to');
    receiver.invokeMethodAsync('NotifyClosed').catch(() => {});
  };

  if (options.initialWidth) applyWidth(options.initialWidth);
  if (options.initialHeight) applyHeight(options.initialHeight);
  applyTop((window.innerHeight / 2) - (root.offsetHeight / 2));
  applyLeft((window.innerWidth / 2) - (root.offsetWidth / 2));
  bringToFront(false);
  body.addEventListener('mousedown', onBodyMouseDown);
  body.addEventListener('keydown', onKeyDown);
  title.addEventListener('mousedown', onHeaderDown);
  title.addEventListener('touchstart', onHeaderDown, { passive: false });
  window.addEventListener('resize', onBrowserResize);

  generation += 1;
  const enterGeneration = generation;
  const finishEnter = async () => {
    if (disposed || closing || enterGeneration !== generation) return;
    root.classList.remove('window-enter-from');
    const isCurrent = () => !disposed && !closing && enterGeneration === generation;
    if (options.animation) await waitForMotion(root, isCurrent);
    if (!isCurrent()) return;
    root.classList.remove('window-enter-active');
    receiver.invokeMethodAsync('NotifyOpened').catch(() => {});
  };
  if (options.animation) {
    root.classList.add('window-enter-active', 'window-enter-from');
    enterFrame = requestAnimationFrame(() => {
      settleFrame = requestAnimationFrame(finishEnter);
    });
  } else {
    queueMicrotask(finishEnter);
  }

  return {
    getState: state,
    maximize,
    restore,
    bringToFront,
    close,
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      if (enterFrame) cancelAnimationFrame(enterFrame);
      if (settleFrame) cancelAnimationFrame(settleFrame);
      for (const clear of [...dragCleanups]) clear();
      body.removeEventListener('mousedown', onBodyMouseDown);
      body.removeEventListener('keydown', onKeyDown);
      title.removeEventListener('mousedown', onHeaderDown);
      title.removeEventListener('touchstart', onHeaderDown);
      window.removeEventListener('resize', onBrowserResize);
      for (const item of handleListeners) item.element.removeEventListener('mousedown', item.listener);
    },
  };
}
