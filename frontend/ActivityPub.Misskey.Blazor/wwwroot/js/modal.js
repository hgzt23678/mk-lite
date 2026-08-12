import { claimZIndex, focusableItems, registerOverlay } from './overlay-stack.js';

const margin = 16;

function fixedContainer(element) {
  let current = element?.parentElement ?? null;
  while (current && current.tagName !== 'BODY') {
    if (getComputedStyle(current).position === 'fixed') return current;
    current = current.parentElement;
  }
  return null;
}

function align(source, content, isDrawer) {
  if (isDrawer) {
    return {
      maximumHeight: window.innerHeight / 1.5,
      transformOrigin: 'center',
    };
  }

  const fixed = fixedContainer(source) !== null;
  const sourceRect = source.getBoundingClientRect();
  const width = content.offsetWidth;
  const height = content.offsetHeight;
  const pageX = fixed ? 0 : window.pageXOffset;
  const pageY = fixed ? 0 : window.pageYOffset;
  let left = sourceRect.left + pageX + (source.offsetWidth / 2) - (width / 2);
  let top = sourceRect.top + pageY + source.offsetHeight;

  if (left + width - pageX > window.innerWidth) {
    left = window.innerWidth - width + pageX - 1;
  }

  const underSpace = (window.innerHeight - margin) - (top - pageY);
  const upperSpace = sourceRect.top - margin;
  let maximumHeight = underSpace;
  if (top + height - pageY > window.innerHeight - margin) {
    if (underSpace >= upperSpace / 3) {
      maximumHeight = underSpace;
    } else {
      maximumHeight = upperSpace;
      top = pageY + upperSpace + margin - height;
    }
  }

  top = Math.max(margin, top);
  left = Math.max(0, left);
  let originY = 'center';
  if (top >= sourceRect.top + source.offsetHeight + pageY) originY = 'top';
  else if (top + height <= sourceRect.top + pageY) originY = 'bottom';
  let originX = 'center';
  if (left >= sourceRect.left + source.offsetWidth + pageX) originX = 'left';
  else if (left + width <= sourceRect.left + pageX) originX = 'right';

  content.style.position = fixed ? 'fixed' : 'absolute';
  content.style.left = `${left}px`;
  content.style.top = `${top}px`;
  return { maximumHeight, transformOrigin: `${originX} ${originY}` };
}

export function attach(source, modal, content, openedViaKeyboard, receiver, priority = 'low') {
  const isDrawer = window.innerWidth <= 500 && navigator.maxTouchPoints > 0;
  let disposed = false;
  let closeTimer = 0;
  let animationFrame = 0;
  let resizeObserver = null;
  const overlay = registerOverlay({
    root: modal,
    background: modal.querySelector(':scope > .bg'),
    content,
    focusRoot: content,
    source,
    priority,
  });

  if (isDrawer) {
    modal.classList.remove('popup', 'modal-popup-enter-active');
    modal.classList.add('drawer', 'modal-drawer-enter-active');
  }
  modal.classList.add(isDrawer ? 'modal-drawer-enter-from' : 'modal-popup-enter-from');
  const placement = align(source, content, isDrawer);
  modal.style.setProperty('--transformOrigin', placement.transformOrigin);
  animationFrame = requestAnimationFrame(() => {
    animationFrame = requestAnimationFrame(() => {
      modal.classList.remove(isDrawer ? 'modal-drawer-enter-from' : 'modal-popup-enter-from');
    });
  });

  const onKeyDown = event => {
    if (!overlay.isTop()) return;
    const items = focusableItems(content);
    if (event.key === 'Escape') {
      event.preventDefault();
      handle.close();
      return;
    }
    if (!modal.contains(event.target)) {
      event.preventDefault();
      event.stopImmediatePropagation();
      items[0]?.focus({ preventScroll: true });
      return;
    }
    if (items.length === 0) return;
    const index = items.indexOf(document.activeElement);
    if (event.key === 'ArrowDown' || event.key === 'j') {
      event.preventDefault();
      items[(Math.max(index, -1) + 1) % items.length].focus();
    } else if (event.key === 'ArrowUp' || event.key === 'k') {
      event.preventDefault();
      items[(index < 0 ? 0 : index - 1 + items.length) % items.length].focus();
    } else if (event.key === 'Tab') {
      const next = event.shiftKey ? index - 1 : index + 1;
      if (next < 0 || next >= items.length) {
        event.preventDefault();
        items[(next + items.length) % items.length].focus();
      }
    }
  };
  document.addEventListener('keydown', onKeyDown);

  resizeObserver = new ResizeObserver(() => align(source, content, isDrawer));
  resizeObserver.observe(content);
  if (openedViaKeyboard) {
    requestAnimationFrame(() => focusableItems(content)[0]?.focus());
  }

  const handle = {
    getPlacement() {
      return {
        isDrawer,
        maximumHeight: placement.maximumHeight,
        transformOrigin: placement.transformOrigin,
        sourceWidth: source.offsetWidth,
      };
    },
    close() {
      if (disposed || closeTimer) return;
      const className = isDrawer ? 'modal-drawer-leave-to' : 'modal-popup-leave-to';
      modal.classList.add(className);
      closeTimer = window.setTimeout(() => receiver.invokeMethodAsync('NotifyClosed'), 220);
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (animationFrame) cancelAnimationFrame(animationFrame);
      if (closeTimer) clearTimeout(closeTimer);
      resizeObserver?.disconnect();
      document.removeEventListener('keydown', onKeyDown);
      overlay.dispose();
    },
  };

  return handle;
}

function modalMilliseconds(value) {
  const trimmed = value.trim();
  if (trimmed.endsWith('ms')) return Number.parseFloat(trimmed) || 0;
  if (trimmed.endsWith('s')) return (Number.parseFloat(trimmed) || 0) * 1000;
  return 0;
}

function modalRepeated(values, index) {
  return values[index % values.length] ?? 0;
}

function maximumModalMotionMilliseconds(element) {
  const style = getComputedStyle(element);
  const transitionDurations = style.transitionDuration.split(',').map(modalMilliseconds);
  const transitionDelays = style.transitionDelay.split(',').map(modalMilliseconds);
  const animationDurations = style.animationDuration.split(',').map(modalMilliseconds);
  const animationDelays = style.animationDelay.split(',').map(modalMilliseconds);
  const animationIterations = style.animationIterationCount.split(',').map(value => {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? Math.max(parsed, 1) : 1;
  });
  let maximum = 0;
  for (let index = 0; index < transitionDurations.length; index += 1) {
    maximum = Math.max(maximum, transitionDurations[index] + modalRepeated(transitionDelays, index));
  }
  for (let index = 0; index < animationDurations.length; index += 1) {
    maximum = Math.max(
      maximum,
      animationDurations[index] * modalRepeated(animationIterations, index) + modalRepeated(animationDelays, index));
  }
  return maximum;
}

function waitForModalMotion(element, generationIsCurrent) {
  const expected = maximumModalMotionMilliseconds(element);
  if (expected <= 0) return Promise.resolve();
  return new Promise(resolve => {
    const started = performance.now();
    let timer = 0;
    let completed = false;
    const finish = () => {
      if (completed) return;
      completed = true;
      element.removeEventListener('transitionend', onEnd);
      element.removeEventListener('animationend', onEnd);
      if (timer) clearTimeout(timer);
      resolve();
    };
    const onEnd = event => {
      if (event.target !== element || !generationIsCurrent()) return;
      if (performance.now() - started >= expected - 20) finish();
    };
    element.addEventListener('transitionend', onEnd);
    element.addEventListener('animationend', onEnd);
    timer = window.setTimeout(finish, Math.ceil(expected) + 80);
  });
}

function v12FixedContainer(element) {
  let current = element;
  while (current && current.tagName !== 'BODY') {
    if (getComputedStyle(current).position === 'fixed') return current;
    current = current.parentElement;
  }
  return null;
}

function resolveV12Type(source, options) {
  if (options.preferType !== 'auto') return options.preferType;
  const smartphone = window.matchMedia('(max-width: 500px)').matches;
  if (!options.disableDrawer && navigator.maxTouchPoints > 0 && smartphone) return 'drawer';
  return source instanceof HTMLElement ? 'popup' : 'dialog';
}

function applyV12Type(modal, background, content, type, options) {
  modal.classList.remove('drawer', 'dialog', 'popup');
  modal.classList.add(type === 'drawer' ? 'drawer' : type === 'popup' ? 'popup' : 'dialog');
  content.classList.toggle('top', type === 'dialog:top');
  background.classList.toggle('transparent', options.transparentBackground && type === 'popup');
}

function alignV12Modal(source, content, type, options) {
  if (type === 'drawer') {
    content.classList.remove('fixed');
    content.style.removeProperty('left');
    content.style.removeProperty('top');
    return {
      type,
      fixed: true,
      maximumHeight: window.innerHeight / 1.5,
      transformOrigin: 'center',
      sourceWidth: source instanceof HTMLElement ? source.offsetWidth : 0,
    };
  }
  if (type === 'dialog' || type === 'dialog:top') {
    content.classList.remove('fixed');
    content.style.removeProperty('left');
    content.style.removeProperty('top');
    return {
      type,
      fixed: false,
      maximumHeight: null,
      transformOrigin: 'center',
      sourceWidth: source instanceof HTMLElement ? source.offsetWidth : 0,
    };
  }
  if (!(source instanceof HTMLElement)) throw new Error('MISSKEY_MODAL_SOURCE_REQUIRED');

  const fixed = v12FixedContainer(source) !== null;
  const sourceRect = source.getBoundingClientRect();
  const width = content.offsetWidth;
  const height = content.offsetHeight;
  const pageX = fixed ? 0 : window.pageXOffset;
  const pageY = fixed ? 0 : window.pageYOffset;
  const x = sourceRect.left + pageX;
  const y = sourceRect.top + pageY;
  let left = options.anchorX === 'left'
    ? x - width
    : options.anchorX === 'right'
      ? x + source.offsetWidth
      : x + (source.offsetWidth / 2) - (width / 2);
  let top = options.anchorY === 'top'
    ? y - height
    : options.anchorY === 'center'
      ? y - (height / 2)
      : y + source.offsetHeight;

  if (fixed) {
    if (left + width > window.innerWidth) left = window.innerWidth - width;
  } else if (left + width - window.pageXOffset > window.innerWidth) {
    left = window.innerWidth - width + window.pageXOffset - 1;
  }

  const underSpace = (window.innerHeight - margin) - (top - pageY);
  const upperSpace = sourceRect.top - margin;
  let maximumHeight = underSpace;
  if (top + height - pageY > window.innerHeight - margin) {
    if (options.noOverlap && options.anchorX === 'center') {
      if (underSpace >= upperSpace / 3) {
        maximumHeight = underSpace;
      } else {
        maximumHeight = upperSpace;
        top = pageY + upperSpace + margin - height;
      }
    } else {
      top = window.innerHeight - margin - height + pageY - (fixed ? 0 : 1);
    }
  }

  if (top < 0) top = margin;
  if (left < 0) left = 0;
  let originY = 'center';
  if (top >= sourceRect.top + source.offsetHeight + pageY) originY = 'top';
  else if (top + height <= sourceRect.top + pageY) originY = 'bottom';
  let originX = 'center';
  if (left >= sourceRect.left + source.offsetWidth + pageX) originX = 'left';
  else if (left + width <= sourceRect.left + pageX) originX = 'right';

  content.classList.toggle('fixed', fixed);
  content.style.left = `${left}px`;
  content.style.top = `${top}px`;
  return {
    type,
    fixed,
    maximumHeight,
    transformOrigin: `${originX} ${originY}`,
    sourceWidth: source.offsetWidth,
  };
}

function sameV12Placement(left, right) {
  return left?.type === right?.type && left?.fixed === right?.fixed &&
    left?.maximumHeight === right?.maximumHeight && left?.transformOrigin === right?.transformOrigin &&
    left?.sourceWidth === right?.sourceWidth;
}

export function attachV12(source, modal, background, content, receiver, options) {
  const invalid = [];
  if (source !== null && source !== undefined && !(source instanceof HTMLElement)) invalid.push('source');
  if (!(modal instanceof HTMLElement)) invalid.push('modal');
  if (!(background instanceof HTMLElement)) invalid.push('background');
  if (!(content instanceof HTMLElement)) invalid.push('content');
  if (receiver === null || typeof receiver !== 'object') invalid.push('receiver');
  if (options === null || typeof options !== 'object') {
    invalid.push('options');
  } else {
    if (!['auto', 'popup', 'dialog', 'dialog:top', 'drawer'].includes(options.preferType)) invalid.push('preferType');
    if (!['left', 'center', 'right'].includes(options.anchorX)) invalid.push('anchorX');
    if (!['top', 'center', 'bottom'].includes(options.anchorY)) invalid.push('anchorY');
    if (!['low', 'middle', 'high'].includes(options.priority)) invalid.push('priority');
    if (typeof options.noOverlap !== 'boolean') invalid.push('noOverlap');
    if (typeof options.transparentBackground !== 'boolean') invalid.push('transparentBackground');
    if (typeof options.animation !== 'boolean') invalid.push('animation');
    if (typeof options.disableDrawer !== 'boolean') invalid.push('disableDrawer');
    if (typeof options.showing !== 'boolean') invalid.push('showing');
  }
  if (invalid.length > 0) {
    throw new Error(`MISSKEY_MODAL_CONFIGURATION_INVALID:${invalid.join(',')}`);
  }

  const type = resolveV12Type(source, options);
  const motionName = type === 'drawer' ? 'modal-drawer' : type === 'popup' ? 'modal-popup' : 'modal';
  const zIndex = claimZIndex(options.priority);
  let placement = alignV12Modal(source, content, type, options);
  placement.zIndex = zIndex;
  let overlay = null;
  let disposed = false;
  let visible = false;
  let generation = 0;
  let firstFrame = 0;
  let secondFrame = 0;
  let contentClicking = false;
  let contentGuard = null;
  let mouseUpReset = 0;
  applyV12Type(modal, background, content, type, options);
  modal.style.zIndex = String(zIndex);
  background.style.zIndex = String(zIndex);
  content.style.zIndex = String(zIndex);
  modal.style.setProperty('--transformOrigin', placement.transformOrigin);

  const updatePlacement = notify => {
    const next = alignV12Modal(source, content, type, options);
    next.zIndex = zIndex;
    modal.style.setProperty('--transformOrigin', next.transformOrigin);
    if (notify && !sameV12Placement(placement, next)) {
      receiver.invokeMethodAsync('NotifyPlacement', next).catch(() => {});
    }
    placement = next;
  };

  const register = () => {
    if (overlay !== null) return;
    overlay = registerOverlay({
      root: modal,
      background,
      content,
      focusRoot: content.firstElementChild instanceof HTMLElement ? content.firstElementChild : content,
      source,
      priority: options.priority,
      lockScroll: type !== 'popup',
      zIndex,
    });
  };

  const removeMotionClasses = () => {
    modal.classList.remove(
      `${motionName}-enter-active`, `${motionName}-enter-from`,
      `${motionName}-leave-active`, `${motionName}-leave-to`);
  };

  const installContentGuard = () => {
    const child = content.firstElementChild;
    if (!(child instanceof HTMLElement) || child === contentGuard) return;
    if (contentGuard !== null) contentGuard.removeEventListener('mousedown', onContentMouseDown);
    contentGuard = child;
    contentGuard.addEventListener('mousedown', onContentMouseDown, { passive: true });
  };

  const show = () => {
    if (disposed || visible) return;
    visible = true;
    generation += 1;
    const current = generation;
    updatePlacement(true);
    register();
    modal.style.display = '';
    modal.style.pointerEvents = 'auto';
    removeMotionClasses();
    if (options.animation) modal.classList.add(`${motionName}-enter-active`, `${motionName}-enter-from`);
    receiver.invokeMethodAsync('NotifyOpening').catch(() => {});
    const finish = async () => {
      if (disposed || current !== generation || !visible) return;
      if (options.animation) modal.classList.remove(`${motionName}-enter-from`);
      const isCurrent = () => !disposed && current === generation && visible;
      await Promise.all([waitForModalMotion(background, isCurrent), waitForModalMotion(content, isCurrent)]);
      if (!isCurrent()) return;
      removeMotionClasses();
      installContentGuard();
      const autofocus = content.querySelector('[data-mk-autofocus="true"]');
      if (autofocus instanceof HTMLElement && !content.contains(document.activeElement)) {
        autofocus.focus({ preventScroll: true });
      }
      receiver.invokeMethodAsync('NotifyOpened').catch(() => {});
    };
    if (options.animation) {
      firstFrame = requestAnimationFrame(() => {
        secondFrame = requestAnimationFrame(finish);
      });
    } else {
      queueMicrotask(finish);
    }
  };

  const hide = async () => {
    if (disposed || !visible) return;
    visible = false;
    generation += 1;
    const current = generation;
    if (firstFrame) cancelAnimationFrame(firstFrame);
    if (secondFrame) cancelAnimationFrame(secondFrame);
    firstFrame = 0;
    secondFrame = 0;
    overlay?.releaseSource();
    modal.style.pointerEvents = 'none';
    removeMotionClasses();
    if (options.animation) modal.classList.add(`${motionName}-leave-active`, `${motionName}-leave-to`);
    const isCurrent = () => !disposed && current === generation && !visible;
    await Promise.all([waitForModalMotion(background, isCurrent), waitForModalMotion(content, isCurrent)]);
    if (!isCurrent()) return;
    modal.style.display = 'none';
    removeMotionClasses();
    overlay?.dispose();
    overlay = null;
    receiver.invokeMethodAsync('NotifyClosed').catch(() => {});
  };

  function onContentMouseDown() {
    contentClicking = true;
    window.addEventListener('mouseup', () => {
      if (mouseUpReset) clearTimeout(mouseUpReset);
      mouseUpReset = window.setTimeout(() => {
        contentClicking = false;
        mouseUpReset = 0;
      }, 100);
    }, { passive: true, once: true });
  }

  const onBackgroundClick = () => {
    if (!contentClicking && visible) receiver.invokeMethodAsync('NotifyClicked').catch(() => {});
  };
  const onContentClick = event => {
    if (event.target === content) onBackgroundClick();
  };
  const onContextMenu = event => {
    event.preventDefault();
    event.stopPropagation();
  };
  const onKeyDown = event => {
    if (!visible || overlay === null || !overlay.isTop()) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      receiver.invokeMethodAsync('NotifyEscape').catch(() => {});
      return;
    }
    if (event.key !== 'Tab') return;
    const focusRoot = content.firstElementChild instanceof HTMLElement ? content.firstElementChild : content;
    const items = focusableItems(focusRoot);
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

  background.addEventListener('click', onBackgroundClick);
  background.addEventListener('contextmenu', onContextMenu);
  content.addEventListener('click', onContentClick);
  document.addEventListener('keydown', onKeyDown, true);
  const resizeObserver = new ResizeObserver(() => {
    if (!disposed) updatePlacement(true);
  });
  resizeObserver.observe(content);
  if (options.showing) show();
  else {
    modal.style.display = 'none';
    modal.style.pointerEvents = 'none';
  }

  return {
    getPlacement() {
      return placement;
    },
    show,
    hide,
    close: hide,
    releaseSource() {
      overlay?.releaseSource();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      if (firstFrame) cancelAnimationFrame(firstFrame);
      if (secondFrame) cancelAnimationFrame(secondFrame);
      if (mouseUpReset) clearTimeout(mouseUpReset);
      resizeObserver.disconnect();
      background.removeEventListener('click', onBackgroundClick);
      background.removeEventListener('contextmenu', onContextMenu);
      content.removeEventListener('click', onContentClick);
      contentGuard?.removeEventListener('mousedown', onContentMouseDown);
      document.removeEventListener('keydown', onKeyDown, true);
      overlay?.dispose();
      overlay = null;
    },
  };
}
