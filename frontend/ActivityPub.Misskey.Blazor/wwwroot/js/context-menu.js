import { claimZIndex } from './overlay-stack.js';

export function attach(root, x, y, animate, receiver) {
  let disposed = false;
  let frame = 0;
  let closeTimer = 0;
  const motionEnabled = animate && !matchMedia('(prefers-reduced-motion: reduce)').matches;

  const zIndex = claimZIndex('high');
  root.style.zIndex = String(zIndex);

  let left = x + 1;
  let top = y + 1;
  const width = root.offsetWidth;
  const height = root.offsetHeight;

  if (left + width - window.pageXOffset > window.innerWidth) {
    left = window.innerWidth - width + window.pageXOffset;
  }

  if (top + height - window.pageYOffset > window.innerHeight) {
    top = window.innerHeight - height + window.pageYOffset;
  }

  if (top < 0) top = 0;
  if (left < 0) left = 0;

  root.style.top = `${top}px`;
  root.style.left = `${left}px`;
  root.style.removeProperty('visibility');

  if (motionEnabled) {
    root.classList.add('fade-enter-active', 'fade-enter-from');
    frame = requestAnimationFrame(() => {
      frame = requestAnimationFrame(() => {
        if (!disposed) {
          root.classList.remove('fade-enter-from');
          root.classList.remove('fade-enter-active');
        }
      });
    });
  }

  const onMousedown = event => {
    if (root.contains(event.target)) return;
    handle.close();
  };
  document.addEventListener('mousedown', onMousedown, { passive: true });

  const handle = {
    close() {
      if (disposed || closeTimer) return;
      if (!motionEnabled) {
        receiver.invokeMethodAsync('NotifyClosed');
        return;
      }
      root.classList.remove('fade-enter-active', 'fade-enter-from', 'fade-enter-to');
      root.classList.add('fade-leave-active', 'fade-leave-to');
      closeTimer = window.setTimeout(() => receiver.invokeMethodAsync('NotifyClosed'), 500);
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      if (frame) cancelAnimationFrame(frame);
      if (closeTimer) clearTimeout(closeTimer);
      document.removeEventListener('mousedown', onMousedown);
    },
  };

  return handle;
}
