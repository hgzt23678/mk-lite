import { claimZIndex } from './overlay-stack.js';

// Port of Misskey 12.119.2 components/MkRipple.vue. The SVG owns the visual
// timeline; this module only provides os.claimZIndex('high') and the exact
// 1100ms component lifetime that Vue scheduled from onMounted.
export function attach(element, receiver) {
  if (!(element instanceof HTMLElement)) {
    throw new TypeError('A Misskey ripple root is required.');
  }

  element.style.zIndex = String(claimZIndex('high'));
  let disposed = false;
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const timeout = window.setTimeout(() => {
    if (disposed) return;
    receiver.invokeMethodAsync('NotifyEnded').catch(() => {});
  }, reducedMotion ? 0 : 1100);

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      window.clearTimeout(timeout);
    },
  };
}
