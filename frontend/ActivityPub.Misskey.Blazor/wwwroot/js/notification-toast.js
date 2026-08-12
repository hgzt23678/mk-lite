import { claimZIndex } from './overlay-stack.js';

const displayMilliseconds = 6_000;
const fallbackMarginMilliseconds = 80;

function milliseconds(value) {
  const trimmed = value.trim();
  if (trimmed.endsWith('ms')) return Number.parseFloat(trimmed) || 0;
  if (trimmed.endsWith('s')) return (Number.parseFloat(trimmed) || 0) * 1_000;
  return 0;
}

function repeated(values, index) {
  return values[index % values.length] ?? values.at(-1) ?? 0;
}

function transitionContract(element) {
  const style = getComputedStyle(element);
  const properties = style.transitionProperty.split(',').map(value => value.trim());
  const durations = style.transitionDuration.split(',').map(milliseconds);
  const delays = style.transitionDelay.split(',').map(milliseconds);
  const expected = new Map();
  let maximum = 0;
  for (let index = 0; index < properties.length; index += 1) {
    const duration = repeated(durations, index);
    const total = duration + repeated(delays, index);
    maximum = Math.max(maximum, total);
    if (duration > 0 && properties[index] !== 'none') expected.set(properties[index], total);
  }
  return { expected, maximum };
}

function normalizeProperty(value) {
  return value === '-webkit-transform' ? 'transform' : value;
}

function waitForTransition(element, generationIsCurrent) {
  const contract = transitionContract(element);
  if (contract.maximum <= 0 || contract.expected.size === 0) return Promise.resolve();

  return new Promise(resolve => {
    const completed = new Set();
    let timer = 0;
    let settled = false;
    const finish = () => {
      if (settled) return;
      settled = true;
      element.removeEventListener('transitionend', onEnd);
      element.removeEventListener('transitioncancel', onCancel);
      if (timer !== 0) window.clearTimeout(timer);
      resolve();
    };
    const onEnd = event => {
      if (event.target !== element || !generationIsCurrent()) return;
      const property = normalizeProperty(event.propertyName);
      for (const configured of contract.expected.keys()) {
        if (configured === 'all' || normalizeProperty(configured) === property) completed.add(configured);
      }
      if (completed.size === contract.expected.size) finish();
    };
    const onCancel = event => {
      if (event.target === element && generationIsCurrent()) finish();
    };
    element.addEventListener('transitionend', onEnd);
    element.addEventListener('transitioncancel', onCancel);
    timer = window.setTimeout(finish, Math.ceil(contract.maximum) + fallbackMarginMilliseconds);
  });
}

export function attach(root, notification, receiver, generation, animate) {
  if (!(root instanceof HTMLElement) || !(notification instanceof HTMLElement) || !receiver ||
      !Number.isSafeInteger(generation) || generation < 1) {
    throw new Error('MISSKEY_NOTIFICATION_TOAST_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let phaseGeneration = 1;
  let displayTimer = 0;
  let firstFrame = 0;
  let secondFrame = 0;
  const motionEnabled = animate && !matchMedia('(prefers-reduced-motion: reduce)').matches;
  root.style.zIndex = String(claimZIndex('high'));

  const current = value => !disposed && phaseGeneration === value && notification.isConnected;
  const close = async () => {
    if (disposed || notification.dataset.motionState === 'leaving' ||
        notification.dataset.motionState === 'left') return;
    const closeGeneration = ++phaseGeneration;
    notification.dataset.motionState = 'leaving';
    if (motionEnabled) {
      notification.classList.add('notification-toast-leave-active', 'notification-toast-leave-to');
      await waitForTransition(notification, () => current(closeGeneration));
    }
    if (!current(closeGeneration)) return;
    notification.dataset.motionState = 'left';
    receiver.invokeMethodAsync('NotifyClosed', generation).catch(() => {});
  };

  if (motionEnabled) {
    notification.dataset.motionState = 'entering';
    notification.classList.add('notification-toast-enter-active', 'notification-toast-enter-from');
    const enterGeneration = phaseGeneration;
    firstFrame = requestAnimationFrame(() => {
      firstFrame = 0;
      secondFrame = requestAnimationFrame(async () => {
        secondFrame = 0;
        if (!current(enterGeneration)) return;
        notification.classList.remove('notification-toast-enter-from');
        await waitForTransition(notification, () => current(enterGeneration));
        if (current(enterGeneration)) {
          notification.classList.remove('notification-toast-enter-active');
          notification.dataset.motionState = 'entered';
        }
      });
    });
  } else {
    notification.dataset.motionState = 'entered';
  }

  displayTimer = window.setTimeout(() => {
    displayTimer = 0;
    void close();
  }, displayMilliseconds);

  return {
    close,
    dispose() {
      if (disposed) return;
      disposed = true;
      phaseGeneration += 1;
      if (displayTimer !== 0) window.clearTimeout(displayTimer);
      if (firstFrame !== 0) cancelAnimationFrame(firstFrame);
      if (secondFrame !== 0) cancelAnimationFrame(secondFrame);
    },
  };
}
