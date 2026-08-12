const storagePrefix = 'ui:folder:';

const nextFrame = () => new Promise(resolve => {
  requestAnimationFrame(() => requestAnimationFrame(resolve));
});

const milliseconds = value => {
  const text = value.trim();
  if (text.endsWith('ms')) return Number.parseFloat(text) || 0;
  if (text.endsWith('s')) return (Number.parseFloat(text) || 0) * 1_000;
  return 0;
};

const splitCssList = value => value.split(',').map(part => part.trim());

const waitForTransition = (element, generation, currentGeneration, registerCleanup) =>
  new Promise(resolve => {
    const style = getComputedStyle(element);
    const properties = splitCssList(style.transitionProperty);
    const durations = splitCssList(style.transitionDuration).map(milliseconds);
    const delays = splitCssList(style.transitionDelay).map(milliseconds);
    const expected = new Set();
    let timeout = 0;
    const count = Math.max(properties.length, durations.length, delays.length);
    for (let index = 0; index < count; index += 1) {
      const property = properties[index % properties.length];
      const duration = durations[index % durations.length] ?? 0;
      const delay = delays[index % delays.length] ?? 0;
      if ((property === 'height' || property === 'opacity' || property === 'all') && duration > 0) {
        if (property === 'all') {
          expected.add('height');
          expected.add('opacity');
        } else {
          expected.add(property);
        }
        timeout = Math.max(timeout, duration + delay);
      }
    }

    let settled = false;
    const completed = new Set();
    const finish = cancelled => {
      if (settled) return;
      settled = true;
      element.removeEventListener('transitionend', onEnd);
      element.removeEventListener('transitioncancel', onCancel);
      window.clearTimeout(fallback);
      resolve(cancelled);
    };
    const onEnd = event => {
      if (event.target !== element || !expected.has(event.propertyName)) return;
      completed.add(event.propertyName);
      if ([...expected].every(property => completed.has(property))) finish(false);
    };
    const onCancel = event => {
      if (event.target !== element || !expected.has(event.propertyName)) return;
      completed.add(event.propertyName);
      if ([...expected].every(property => completed.has(property))) {
        finish(currentGeneration() !== generation);
      }
    };
    const fallback = window.setTimeout(
      () => finish(currentGeneration() !== generation),
      Math.max(50, Math.ceil(timeout) + 50));
    element.addEventListener('transitionend', onEnd);
    element.addEventListener('transitioncancel', onCancel);
    registerCleanup(() => finish(true));
    if (expected.size === 0) queueMicrotask(() => finish(false));
  });

const parentBackground = element => {
  let current = element;
  while (current && current.tagName !== 'BODY') {
    const background = current.style.background || current.style.backgroundColor;
    if (background) return background;
    current = current.parentElement;
  }
  return 'var(--bg)';
};

const resolveVariable = value => {
  const match = /^var\((--[^),\s]+)(?:,[^)]+)?\)$/.exec(value.trim());
  if (!match) return value;
  return getComputedStyle(document.documentElement).getPropertyValue(match[1]).trim();
};

const withAlpha = value => {
  const probe = document.createElement('span');
  probe.style.position = 'fixed';
  probe.style.pointerEvents = 'none';
  probe.style.opacity = '0';
  probe.style.color = resolveVariable(value);
  document.body.append(probe);
  const resolved = getComputedStyle(probe).color;
  probe.remove();
  const values = resolved.match(/[\d.]+/g)?.map(Number) ?? [];
  if (values.length < 3 || values.slice(0, 3).some(channel => !Number.isFinite(channel))) {
    return 'rgba(0, 0, 0, 0.85)';
  }
  return `rgba(${values[0]}, ${values[1]}, ${values[2]}, 0.85)`;
};

export function attach(root, content, persistKey, defaultExpanded, receiver) {
  if (!(root instanceof HTMLElement) || !(content instanceof HTMLElement) || !receiver) {
    throw new Error('MISSKEY_FOLDER_CONFIGURATION_INVALID');
  }
  if (persistKey !== null && (typeof persistKey !== 'string' || persistKey.length === 0)) {
    throw new Error('MISSKEY_FOLDER_PERSIST_KEY_INVALID');
  }

  let disposed = false;
  let generation = 0;
  let motionCleanup = null;
  let narrow = root.getBoundingClientRect().width <= 500;
  const stored = persistKey === null ? null : localStorage.getItem(storagePrefix + persistKey);
  let expanded = stored === 't' || (stored !== 'f' && Boolean(defaultExpanded));
  const background = withAlpha(parentBackground(root));
  content.style.display = expanded ? '' : 'none';

  const resizeObserver = new ResizeObserver(() => {
    const next = root.getBoundingClientRect().width <= 500;
    if (next === narrow || disposed) return;
    narrow = next;
    receiver.invokeMethodAsync('UpdateFolderNarrow', narrow).catch(() => {});
  });
  resizeObserver.observe(root);

  const cleanMotion = () => {
    if (motionCleanup !== null) {
      const cleanup = motionCleanup;
      motionCleanup = null;
      cleanup();
    }
    content.classList.remove(
      'folder-toggle-enter-active',
      'folder-toggle-enter-from',
      'folder-toggle-enter-to',
      'folder-toggle-leave-active',
      'folder-toggle-leave-from',
      'folder-toggle-leave-to');
    content.style.removeProperty('height');
  };

  const setExpanded = async (nextExpanded, animate, requestedGeneration) => {
    if (disposed) return true;
    generation = Number(requestedGeneration);
    cleanMotion();
    expanded = Boolean(nextExpanded);
    if (persistKey !== null) {
      localStorage.setItem(storagePrefix + persistKey, expanded ? 't' : 'f');
    }
    if (!animate || matchMedia('(prefers-reduced-motion: reduce)').matches) {
      content.style.display = expanded ? '' : 'none';
      return false;
    }

    const current = generation;
    let cancelWait = null;
    motionCleanup = () => cancelWait?.();
    if (expanded) {
      content.style.display = '';
      const height = content.getBoundingClientRect().height;
      content.classList.add('folder-toggle-enter-active', 'folder-toggle-enter-from');
      content.style.height = '0px';
      void content.offsetHeight;
      await nextFrame();
      if (disposed || generation !== current) return true;
      content.classList.remove('folder-toggle-enter-from');
      content.classList.add('folder-toggle-enter-to');
      content.style.height = `${height}px`;
    } else {
      const height = content.getBoundingClientRect().height;
      content.classList.add('folder-toggle-leave-active', 'folder-toggle-leave-from');
      content.style.height = `${height}px`;
      void content.offsetHeight;
      await nextFrame();
      if (disposed || generation !== current) return true;
      content.classList.remove('folder-toggle-leave-from');
      content.classList.add('folder-toggle-leave-to');
      content.style.height = '0px';
    }

    const cancelled = await waitForTransition(
      content,
      current,
      () => generation,
      cleanup => { cancelWait = cleanup; });
    if (disposed || generation !== current || cancelled) return true;
    cleanMotion();
    if (!expanded) content.style.display = 'none';
    return false;
  };

  return {
    getState() {
      return { expanded, background, narrow };
    },
    setExpanded,
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      cleanMotion();
      resizeObserver.disconnect();
    },
  };
}
