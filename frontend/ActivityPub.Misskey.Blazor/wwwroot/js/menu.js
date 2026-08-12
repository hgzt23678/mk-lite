import { focusableItems } from './overlay-stack.js';

function directFocusableItems(items) {
  return focusableItems(items).filter(element => element.closest('.rrevdjwt') === items);
}

function focusSibling(items, direction) {
  let current = document.activeElement;
  if (!(current instanceof Element) || current.parentElement !== items) return;
  current = direction > 0 ? current.nextElementSibling : current.previousElementSibling;
  while (current !== null && !current.hasAttribute('tabindex')) {
    current = direction > 0 ? current.nextElementSibling : current.previousElementSibling;
  }
  if (current instanceof HTMLElement) current.focus();
}

export function attach(root, items, viaKeyboard, receiver) {
  let disposed = false;
  let focusFrame = 0;
  let childTarget = null;

  const onKeyDown = event => {
    const active = document.activeElement;
    if (active instanceof Element &&
        (active.matches('input, textarea') || active.hasAttribute('contenteditable'))) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'j' ||
          event.key === 'k' || event.key === 'Tab') event.stopPropagation();
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      receiver.invokeMethodAsync('NotifyClose');
      return;
    }

    let direction = 0;
    if (event.key === 'ArrowDown' || event.key === 'j' ||
        (event.key === 'Tab' && !event.shiftKey)) {
      direction = 1;
    } else if (event.key === 'ArrowUp' || event.key === 'k' ||
        (event.key === 'Tab' && event.shiftKey)) {
      direction = -1;
    }
    if (direction === 0) return;

    event.preventDefault();
    event.stopPropagation();
    focusSibling(items, direction);
  };

  const onContextMenu = event => {
    if (event.target === items) event.preventDefault();
  };

  const onGlobalMouseDown = event => {
    if (!(childTarget instanceof HTMLElement)) return;
    const child = root.querySelector(':scope > .child');
    if (childTarget.contains(event.target) || child?.contains(event.target)) return;
    receiver.invokeMethodAsync('NotifyChildOutside');
  };

  items.addEventListener('keydown', onKeyDown);
  items.addEventListener('contextmenu', onContextMenu);
  document.addEventListener('mousedown', onGlobalMouseDown, { passive: true });
  if (viaKeyboard) {
    focusFrame = requestAnimationFrame(() => directFocusableItems(items)[0]?.focus({ preventScroll: true }));
  }

  return {
    setChildTarget(target) {
      childTarget = target instanceof HTMLElement ? target : null;
    },
    clearChildTarget() {
      childTarget = null;
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (focusFrame) cancelAnimationFrame(focusFrame);
      items.removeEventListener('keydown', onKeyDown);
      items.removeEventListener('contextmenu', onContextMenu);
      document.removeEventListener('mousedown', onGlobalMouseDown);
      childTarget = null;
    },
  };
}

export function positionChild(child, target, root) {
  if (!(child instanceof HTMLElement) || !(target instanceof HTMLElement) ||
      !(root instanceof HTMLElement)) return;
  const rootRect = root.getBoundingClientRect();
  const targetRect = target.getBoundingClientRect();
  child.style.left = `${target.offsetWidth}px`;
  child.style.top = `${targetRect.top - rootRect.top - 8}px`;
}
