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
  const expected = new Set();
  let maximum = 0;
  for (let index = 0; index < properties.length; index += 1) {
    const duration = repeated(durations, index);
    const total = duration + repeated(delays, index);
    maximum = Math.max(maximum, total);
    if (duration > 0 && properties[index] !== 'none') expected.add(properties[index]);
  }
  return { expected, maximum };
}

function normalizeProperty(value) {
  return value === '-webkit-transform' ? 'transform' : value;
}

export function attach(element, receiver, generation, phase) {
  if (!(element instanceof HTMLElement) || !receiver ||
      !Number.isSafeInteger(generation) || generation < 1 ||
      (phase !== 'enter' && phase !== 'leave')) {
    throw new Error('MISSKEY_FORM_SUSPENSE_TRANSITION_INVALID');
  }

  let disposed = false;
  let firstFrame = 0;
  let secondFrame = 0;
  let fallbackTimer = 0;
  let settled = false;
  const prefix = `fade-${phase}`;
  const activeClass = `${prefix}-active`;
  const fromClass = `${prefix}-from`;
  const toClass = `${prefix}-to`;
  const motionEnabled = !matchMedia('(prefers-reduced-motion: reduce)').matches;

  const cleanup = () => {
    if (firstFrame !== 0) cancelAnimationFrame(firstFrame);
    if (secondFrame !== 0) cancelAnimationFrame(secondFrame);
    if (fallbackTimer !== 0) clearTimeout(fallbackTimer);
    firstFrame = 0;
    secondFrame = 0;
    fallbackTimer = 0;
    element.removeEventListener('transitionend', onTransitionEnd);
    element.removeEventListener('transitioncancel', onTransitionCancel);
    element.classList.remove(activeClass, fromClass, toClass);
  };
  const finish = notify => {
    if (settled) return;
    settled = true;
    cleanup();
    if (notify && !disposed) {
      receiver.invokeMethodAsync('NotifyTransitionCompleted', generation, phase).catch(() => {});
    }
  };
  const completedProperties = new Set();
  let contract = { expected: new Set(), maximum: 0 };
  const onTransitionEnd = event => {
    if (event.target !== element || disposed) return;
    const property = normalizeProperty(event.propertyName);
    for (const configured of contract.expected) {
      if (configured === 'all' || normalizeProperty(configured) === property) {
        completedProperties.add(configured);
      }
    }
    if (completedProperties.size === contract.expected.size) finish(true);
  };
  const onTransitionCancel = event => {
    if (event.target === element && !disposed) finish(true);
  };

  if (!motionEnabled) {
    queueMicrotask(() => finish(true));
  } else {
    element.classList.add(activeClass, fromClass);
    firstFrame = requestAnimationFrame(() => {
      firstFrame = 0;
      secondFrame = requestAnimationFrame(() => {
        secondFrame = 0;
        if (disposed) return;
        element.classList.remove(fromClass);
        element.classList.add(toClass);
        contract = transitionContract(element);
        if (contract.maximum <= 0 || contract.expected.size === 0) {
          finish(true);
          return;
        }
        element.addEventListener('transitionend', onTransitionEnd);
        element.addEventListener('transitioncancel', onTransitionCancel);
        fallbackTimer = setTimeout(
          () => finish(true),
          Math.ceil(contract.maximum) + fallbackMarginMilliseconds);
      });
    });
  }

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      cleanup();
    },
  };
}
