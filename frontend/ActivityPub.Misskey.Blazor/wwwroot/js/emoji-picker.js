let touchUsing = false;
let touchObservers = 0;

const markTouchUsing = () => {
  touchUsing = true;
};

function retainTouchObserver() {
  touchObservers += 1;
  if (touchObservers !== 1) return;
  window.addEventListener('touchstart', markTouchUsing, { passive: true });
  window.addEventListener('touchend', markTouchUsing, { passive: true });
}

function releaseTouchObserver() {
  touchObservers = Math.max(0, touchObservers - 1);
  if (touchObservers !== 0) return;
  window.removeEventListener('touchstart', markTouchUsing);
  window.removeEventListener('touchend', markTouchUsing);
}

function deviceKind() {
  try {
    const state = JSON.parse(localStorage.getItem('pizzax::base') ?? 'null');
    if (state && ['desktop', 'tablet', 'smartphone'].includes(state.overridedDeviceKind)) {
      return state.overridedDeviceKind;
    }
  } catch {
    // An unreadable override is absent in the pinned client.
  }
  const userAgent = navigator.userAgent.toLowerCase();
  const tablet = /ipad/.test(userAgent) || (/mobile|iphone|android/.test(userAgent) && window.innerWidth > 700);
  const smartphone = !tablet && /mobile|iphone|android/.test(userAgent);
  return smartphone ? 'smartphone' : tablet ? 'tablet' : 'desktop';
}

export function attach(search, emojis, receiver) {
  if (!(search instanceof HTMLInputElement) || search.type !== 'search' ||
      !(emojis instanceof HTMLElement) || !emojis.classList.contains('emojis') ||
      receiver === null || typeof receiver !== 'object') {
    throw new TypeError('A complete emoji picker is required.');
  }
  let disposed = false;
  retainTouchObserver();
  const onPaste = event => {
    const value = event.clipboardData?.getData('text') ?? '';
    const start = search.selectionStart ?? search.value.length;
    const end = search.selectionEnd ?? start;
    event.preventDefault();
    event.stopPropagation();
    receiver.invokeMethodAsync('NotifyPasted', value).then(handled => {
      if (disposed || handled) return;
      search.setRangeText(value, start, end, 'end');
      search.dispatchEvent(new InputEvent('input', {
        bubbles: true,
        composed: true,
        inputType: 'insertFromPaste',
        data: value,
      }));
    }).catch(() => {});
  };
  search.addEventListener('paste', onPaste);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      search.removeEventListener('paste', onPaste);
      releaseTouchObserver();
    },
  };
}

export function focus(search) {
  if (!(search instanceof HTMLInputElement) || search.type !== 'search') {
    throw new TypeError('An emoji picker search input is required.');
  }
  if (!['smartphone', 'tablet'].includes(deviceKind()) && !touchUsing) {
    search.focus({ preventScroll: true });
  }
}

export function reset(emojis) {
  if (!(emojis instanceof HTMLElement) || !emojis.classList.contains('emojis')) {
    throw new TypeError('An emoji picker scroll container is required.');
  }
  emojis.scrollTop = 0;
}
