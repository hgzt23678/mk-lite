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

export function getCalendarParts(unixTimeMilliseconds) {
  if (!Array.isArray(unixTimeMilliseconds) ||
      unixTimeMilliseconds.some(value => !Number.isSafeInteger(value))) {
    throw new Error('MISSKEY_DATE_SEPARATED_LIST_DATES_INVALID');
  }

  return unixTimeMilliseconds.map(value => {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      throw new Error('MISSKEY_DATE_SEPARATED_LIST_DATE_INVALID');
    }
    return { month: date.getMonth() + 1, day: date.getDate() };
  });
}

export function attach(root) {
  if (!(root instanceof HTMLElement)) {
    throw new Error('MISSKEY_DATE_SEPARATED_LIST_ROOT_INVALID');
  }

  let disposed = false;
  let frame = 0;
  let secondFrame = 0;
  let generation = 0;
  const cleanups = new Map();
  const positions = new Map();
  const motionEnabled = !matchMedia('(prefers-reduced-motion: reduce)').matches;

  const children = () => Array.from(root.children).filter(element => element instanceof HTMLElement);
  const rememberPositions = () => {
    positions.clear();
    for (const element of children()) positions.set(element, element.getBoundingClientRect());
  };
  const stopAnimation = element => {
    const cleanup = cleanups.get(element);
    if (cleanup) cleanup();
  };
  const beginAnimation = (element, activeClass, expectedProperties, restore) => {
    let finished = false;
    let timer = 0;
    const pending = new Set(expectedProperties);
    const finish = () => {
      if (finished) return;
      finished = true;
      if (timer !== 0) clearTimeout(timer);
      element.removeEventListener('transitionend', onTransitionEnd);
      element.removeEventListener('transitioncancel', onTransitionCancel);
      element.classList.remove(activeClass, 'list-enter-from', 'list-enter-to');
      restore?.();
      cleanups.delete(element);
    };
    const onTransitionEnd = event => {
      if (event.target !== element || !pending.has(event.propertyName)) return;
      pending.delete(event.propertyName);
      if (pending.size === 0) finish();
    };
    const onTransitionCancel = event => {
      if (event.target === element) finish();
    };
    cleanups.set(element, finish);
    element.addEventListener('transitionend', onTransitionEnd);
    element.addEventListener('transitioncancel', onTransitionCancel);
    const duration = maximumTransitionTime(element);
    if (duration <= 0) finish();
    else timer = setTimeout(finish, Math.ceil(duration) + fallbackMarginMilliseconds);
  };
  const enter = element => {
    stopAnimation(element);
    element.classList.add('list-enter-active', 'list-enter-from');
    beginAnimation(element, 'list-enter-active', ['opacity', 'transform']);
  };
  const move = (element, previous, current) => {
    const deltaX = previous.left - current.left;
    const deltaY = previous.top - current.top;
    if (Math.abs(deltaX) < 0.5 && Math.abs(deltaY) < 0.5) return;

    stopAnimation(element);
    const originalTransition = element.style.transition;
    const originalTransform = element.style.transform;
    element.style.transition = 'none';
    element.style.transform = `translate(${deltaX}px, ${deltaY}px)`;
    void element.offsetHeight;
    element.classList.add('list-move');
    element.style.transition = originalTransition;
    element.style.transform = originalTransform;
    beginAnimation(element, 'list-move', ['transform'], () => {
      element.style.transition = originalTransition;
      element.style.transform = originalTransform;
    });
  };

  rememberPositions();
  const observer = new MutationObserver(records => {
    if (disposed || !motionEnabled) return;
    const previous = new Map(positions);
    const added = new Set();
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node instanceof HTMLElement && node.parentElement === root) added.add(node);
      }
    }
    const callbackGeneration = ++generation;
    if (frame !== 0) cancelAnimationFrame(frame);
    if (secondFrame !== 0) cancelAnimationFrame(secondFrame);
    frame = requestAnimationFrame(() => {
      frame = 0;
      if (disposed || callbackGeneration !== generation) return;
      const current = children();
      const targets = new Map(current.map(element => [element, element.getBoundingClientRect()]));
      for (const element of current) {
        const oldPosition = previous.get(element);
        if (oldPosition) move(element, oldPosition, targets.get(element));
        else if (added.has(element)) enter(element);
      }
      positions.clear();
      for (const [element, position] of targets) positions.set(element, position);
      secondFrame = requestAnimationFrame(() => {
        secondFrame = 0;
        if (disposed || callbackGeneration !== generation) return;
        for (const element of current) {
          if (!element.classList.contains('list-enter-from')) continue;
          element.classList.remove('list-enter-from');
          element.classList.add('list-enter-to');
        }
      });
    });
  });
  observer.observe(root, { childList: true });

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      observer.disconnect();
      if (frame !== 0) cancelAnimationFrame(frame);
      if (secondFrame !== 0) cancelAnimationFrame(secondFrame);
      for (const cleanup of [...cleanups.values()]) cleanup();
      cleanups.clear();
      positions.clear();
    },
  };
}
