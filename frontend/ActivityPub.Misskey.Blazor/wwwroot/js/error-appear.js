const fallbackMarginMilliseconds = 80;

function milliseconds(value) {
  const trimmed = value.trim();
  if (trimmed.endsWith('ms')) return Number.parseFloat(trimmed) || 0;
  if (trimmed.endsWith('s')) return (Number.parseFloat(trimmed) || 0) * 1_000;
  return 0;
}

function maximumTransitionTime(element) {
  const style = getComputedStyle(element);
  const durations = style.transitionDuration.split(',').map(milliseconds);
  const delays = style.transitionDelay.split(',').map(milliseconds);
  let maximum = 0;
  const count = Math.max(durations.length, delays.length);
  for (let index = 0; index < count; index += 1) {
    maximum = Math.max(
      maximum,
      (durations[index % durations.length] ?? 0) + (delays[index % delays.length] ?? 0));
  }
  return maximum;
}

export function attach(element, animate) {
  if (!(element instanceof HTMLElement) || typeof animate !== 'boolean') {
    throw new Error('MISSKEY_ERROR_APPEAR_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let firstFrame = 0;
  let secondFrame = 0;
  let fallbackTimer = 0;
  const motionEnabled = animate && !matchMedia('(prefers-reduced-motion: reduce)').matches;

  const finish = () => {
    if (disposed) return;
    element.removeEventListener('transitionend', onTransitionEnd);
    element.removeEventListener('transitioncancel', onTransitionCancel);
    if (fallbackTimer !== 0) {
      clearTimeout(fallbackTimer);
      fallbackTimer = 0;
    }
    element.classList.remove('zoom-enter-active', 'zoom-enter-from', 'zoom-enter-to');
    element.dataset.motionState = 'entered';
  };
  const onTransitionEnd = event => {
    if (event.target === element &&
        (event.propertyName === 'opacity' || event.propertyName === 'transform')) finish();
  };
  const onTransitionCancel = event => {
    if (event.target === element) finish();
  };

  if (!motionEnabled) {
    finish();
  } else {
    element.dataset.motionState = 'entering';
    element.classList.add('zoom-enter-active', 'zoom-enter-from');
    firstFrame = requestAnimationFrame(() => {
      firstFrame = 0;
      secondFrame = requestAnimationFrame(() => {
        secondFrame = 0;
        if (disposed) return;
        element.classList.remove('zoom-enter-from');
        element.classList.add('zoom-enter-to');
        element.addEventListener('transitionend', onTransitionEnd);
        element.addEventListener('transitioncancel', onTransitionCancel);
        const duration = maximumTransitionTime(element);
        if (duration <= 0) finish();
        else fallbackTimer = setTimeout(finish, Math.ceil(duration) + fallbackMarginMilliseconds);
      });
    });
  }

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      if (firstFrame !== 0) cancelAnimationFrame(firstFrame);
      if (secondFrame !== 0) cancelAnimationFrame(secondFrame);
      if (fallbackTimer !== 0) clearTimeout(fallbackTimer);
      element.removeEventListener('transitionend', onTransitionEnd);
      element.removeEventListener('transitioncancel', onTransitionCancel);
    },
  };
}
