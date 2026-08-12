// Pinned to Misskey 12.119.2 src/os.ts. Priority bands are intentionally independent;
// a later low-priority dialog must never overtake a middle/high compatibility overlay.
const zIndexes = {
  low: 1000000,
  middle: 2000000,
  high: 3000000,
};

const overlays = [];
const sourceLocks = new WeakMap();
let nextSequence = 0;
let scrollLocks = 0;
let originalDocumentOverflow = '';
let lastNonOverlayFocus = null;

function rememberNonOverlayTarget(event) {
  const target = event.target;
  if (!(target instanceof Element) || target.closest('.qzhlnise') !== null) return;
  const interactive = target.closest(
    'button, a[href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
  lastNonOverlayFocus = interactive instanceof HTMLElement
    ? interactive
    : target instanceof HTMLElement ? target : null;
}

document.addEventListener('focusin', rememberNonOverlayTarget, true);
document.addEventListener('pointerdown', rememberNonOverlayTarget, true);

export function claimZIndex(priority = 'low') {
  if (!Object.prototype.hasOwnProperty.call(zIndexes, priority)) {
    throw new RangeError(`Unknown Misskey overlay priority: ${priority}`);
  }

  zIndexes[priority] += 100;
  return zIndexes[priority];
}

function claimSource(source) {
  if (!(source instanceof HTMLElement)) return;
  const existing = sourceLocks.get(source);
  if (existing) {
    existing.count += 1;
    return;
  }

  sourceLocks.set(source, {
    count: 1,
    pointerEvents: source.style.pointerEvents,
  });
  source.style.pointerEvents = 'none';
}

function releaseSource(source) {
  if (!(source instanceof HTMLElement)) return;
  const existing = sourceLocks.get(source);
  if (!existing) return;
  existing.count -= 1;
  if (existing.count > 0) return;
  source.style.pointerEvents = existing.pointerEvents;
  sourceLocks.delete(source);
}

function lockDocumentScroll() {
  if (scrollLocks === 0) {
    originalDocumentOverflow = document.documentElement.style.overflow;
    document.documentElement.style.overflow = 'hidden';
  }
  scrollLocks += 1;
}

function unlockDocumentScroll() {
  if (scrollLocks === 0) return;
  scrollLocks -= 1;
  if (scrollLocks === 0) {
    document.documentElement.style.overflow = originalDocumentOverflow;
    originalDocumentOverflow = '';
  }
}

function topOverlay() {
  let top = null;
  for (const overlay of overlays) {
    if (overlay.disposed) continue;
    if (top === null || overlay.zIndex > top.zIndex ||
        (overlay.zIndex === top.zIndex && overlay.sequence > top.sequence)) {
      top = overlay;
    }
  }
  return top;
}

function hasPendingLaterOverlay(entry) {
  for (const candidate of document.querySelectorAll('.qzhlnise')) {
    if (candidate === entry.root || !(candidate instanceof HTMLElement) ||
        candidate.getClientRects().length === 0) continue;
    if (overlays.some(overlay => !overlay.disposed && overlay.root === candidate)) continue;
    if ((entry.root.compareDocumentPosition(candidate) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0) {
      return true;
    }
  }
  return false;
}

function topPendingDialog() {
  let pending = null;
  for (const candidate of document.querySelectorAll('body > .qzhlnise.dialog')) {
    if (!(candidate instanceof HTMLElement) || candidate.getClientRects().length === 0) continue;
    if (overlays.some(overlay => !overlay.disposed && overlay.root === candidate)) continue;
    pending = candidate;
  }
  return pending;
}

// Interactive Server can paint a dialog one round trip before its component-specific module is
// attached. Consume Escape during that bounded gap and dispatch the real Razor close action;
// otherwise the registered dialog behind it could close or the key could be lost entirely.
document.addEventListener('keydown', event => {
  if (event.key !== 'Escape') return;
  const pending = topPendingDialog();
  if (pending === null) return;
  const close = pending.querySelector(
    ':scope > .content > .ebkgoccj > .header > button[data-mk-dialog-close="true"]');
  if (!(close instanceof HTMLButtonElement) || close.disabled) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  close.click();
}, true);

function synchronizePointerEvents() {
  const top = topOverlay();
  for (const overlay of overlays) {
    if (overlay.disposed) continue;
    overlay.root.style.pointerEvents = overlay === top ? 'auto' : 'none';
  }
}

function capturePreviousFocus(root, source) {
  const current = document.activeElement;
  if (current instanceof HTMLElement && !root.contains(current)) return current;
  if (source instanceof HTMLElement && !root.contains(source)) return source;
  return lastNonOverlayFocus;
}

function restoreFocus(entry) {
  const top = topOverlay();
  const previous = entry.previousFocus;
  requestAnimationFrame(() => {
    const currentTop = topOverlay();
    if (currentTop !== top) return;

    if (top !== null) {
      if (previous instanceof HTMLElement && previous.isConnected && top.root.contains(previous)) {
        previous.focus({ preventScroll: true });
        return;
      }

      if (!(document.activeElement instanceof Element) || !top.root.contains(document.activeElement)) {
        focusableItems(top.focusRoot)[0]?.focus({ preventScroll: true });
      }
      return;
    }

    if (previous instanceof HTMLElement && previous.isConnected) {
      previous.focus({ preventScroll: true });
    }
  });
}

export function focusableItems(content) {
  return [...content.querySelectorAll(
    'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
    .filter(element => element instanceof HTMLElement && element.getClientRects().length > 0);
}

export function registerOverlay({
  root,
  background,
  content,
  focusRoot = content,
  source = null,
  priority = 'low',
  lockScroll = false,
  zIndex: requestedZIndex = null,
}) {
  if (!(root instanceof HTMLElement)) throw new TypeError('A Misskey overlay root is required.');
  if (!(background instanceof HTMLElement)) throw new TypeError('A Misskey overlay background is required.');
  if (!(content instanceof HTMLElement)) throw new TypeError('A Misskey overlay content element is required.');
  if (!(focusRoot instanceof HTMLElement)) throw new TypeError('A Misskey overlay focus root is required.');

  const zIndex = requestedZIndex ?? claimZIndex(priority);
  if (!Number.isSafeInteger(zIndex) || zIndex < 0) {
    throw new RangeError('A Misskey overlay z-index must be a non-negative safe integer.');
  }
  let sourceClaimed = true;
  const entry = {
    sequence: ++nextSequence,
    zIndex,
    root,
    focusRoot,
    previousFocus: capturePreviousFocus(root, source),
    source,
    lockScroll,
    disposed: false,
    original: {
      rootZIndex: root.style.zIndex,
      rootPointerEvents: root.style.pointerEvents,
      backgroundZIndex: background.style.zIndex,
      contentZIndex: content.style.zIndex,
    },
  };

  const renderedZIndex = String(zIndex);
  root.style.zIndex = renderedZIndex;
  background.style.zIndex = renderedZIndex;
  content.style.zIndex = renderedZIndex;
  claimSource(source);
  if (lockScroll) lockDocumentScroll();
  overlays.push(entry);
  synchronizePointerEvents();

  return {
    zIndex,
    isTop() {
      // Interactive Server can render a newer overlay before its JS attachment is ready.
      // Let the Razor overlay host handle Escape during that gap instead of allowing the
      // older registered dialog to consume the key and close behind the visible surface.
      return !entry.disposed && topOverlay() === entry && !hasPendingLaterOverlay(entry);
    },
    releaseSource() {
      if (!sourceClaimed) return;
      sourceClaimed = false;
      releaseSource(source);
    },
    dispose() {
      if (entry.disposed) return;
      const wasTop = topOverlay() === entry;
      entry.disposed = true;
      const index = overlays.indexOf(entry);
      if (index >= 0) overlays.splice(index, 1);
      if (sourceClaimed) releaseSource(source);
      if (lockScroll) unlockDocumentScroll();
      root.style.zIndex = entry.original.rootZIndex;
      root.style.pointerEvents = entry.original.rootPointerEvents;
      background.style.zIndex = entry.original.backgroundZIndex;
      content.style.zIndex = entry.original.contentZIndex;
      synchronizePointerEvents();
      if (wasTop) restoreFocus(entry);
    },
  };
}
