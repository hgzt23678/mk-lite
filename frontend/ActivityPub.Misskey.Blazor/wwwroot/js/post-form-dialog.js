import { focusableItems, registerOverlay } from './overlay-stack.js';

export function attach(modal, content, initialFocus, receiver) {
  let disposed = false;
  let closing = false;
  let closeTimer = 0;
  let firstFrame = 0;
  let secondFrame = 0;
  const overlay = registerOverlay({
    root: modal,
    background: modal.querySelector(':scope > .bg'),
    content,
    focusRoot: content,
    priority: 'low',
    lockScroll: true,
  });
  modal.classList.add('modal-enter-from');

  firstFrame = requestAnimationFrame(() => {
    secondFrame = requestAnimationFrame(() => {
      if (disposed) return;
      modal.classList.remove('modal-enter-from');
      if (initialFocus instanceof HTMLElement && initialFocus.isConnected) initialFocus.focus();
    });
  });

  const close = () => {
    if (disposed || closing) return;
    closing = true;
    modal.classList.add('modal-leave-to');
    closeTimer = window.setTimeout(() => receiver.invokeMethodAsync('NotifyClosed'), 220);
  };

  const onKeyDown = event => {
    if (!overlay.isTop()) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      close();
      return;
    }
    if (!modal.contains(event.target)) {
      event.preventDefault();
      event.stopImmediatePropagation();
      focusableItems(content)[0]?.focus({ preventScroll: true });
      return;
    }
    if (event.key !== 'Tab') return;
    const items = focusableItems(content);
    if (items.length === 0) {
      event.preventDefault();
      return;
    }
    const index = items.indexOf(document.activeElement);
    const next = event.shiftKey ? index - 1 : index + 1;
    if (index < 0 || next < 0 || next >= items.length) {
      event.preventDefault();
      items[event.shiftKey ? items.length - 1 : 0].focus();
    }
  };
  document.addEventListener('keydown', onKeyDown);

  return {
    close,
    dispose() {
      if (disposed) return;
      disposed = true;
      if (firstFrame) cancelAnimationFrame(firstFrame);
      if (secondFrame) cancelAnimationFrame(secondFrame);
      if (closeTimer) clearTimeout(closeTimer);
      document.removeEventListener('keydown', onKeyDown);
      overlay.dispose();
    },
  };
}
