const nextFrame = () => new Promise(resolve => {
  requestAnimationFrame(() => requestAnimationFrame(resolve));
});

const splitCssList = value => value.split(',').map(part => part.trim());

const milliseconds = value => {
  const text = value.trim();
  if (text.endsWith('ms')) return Number.parseFloat(text) || 0;
  if (text.endsWith('s')) return (Number.parseFloat(text) || 0) * 1000;
  return 0;
};

const transitionPlan = element => {
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
  return { expected, timeout };
};

const waitForTransition = (element, generation, currentGeneration, registerCleanup) =>
  new Promise(resolve => {
    const { expected, timeout } = transitionPlan(element);
    const completed = new Set();
    let settled = false;
    const finish = cancelled => {
      if (settled) return;
      settled = true;
      element.removeEventListener('transitionend', onEnd);
      element.removeEventListener('transitioncancel', onCancel);
      clearTimeout(fallback);
      resolve({ cancelled });
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
    const fallback = setTimeout(
      () => finish(currentGeneration() !== generation),
      Math.max(50, Math.ceil(timeout) + 50));
    element.addEventListener('transitionend', onEnd);
    element.addEventListener('transitioncancel', onCancel);
    registerCleanup(() => finish(true));
    if (expected.size === 0) queueMicrotask(() => finish(false));
  });

export function attach(root, header, content, maxHeight, expanded, dotnet) {
  if (!(root instanceof HTMLElement) || !(content instanceof HTMLElement)) {
    throw new Error('MkContainer requires root and content elements');
  }

  let disposed = false;
  let showBody = Boolean(expanded);
  let ignoreOmit = false;
  let omittedState = false;
  let generation = 0;
  let motionCleanup = null;
  let lastMeasurement = '';

  root.style.setProperty('--maxHeight', `${maxHeight}px`);

  const measure = () => {
    if (disposed) return;
    const headerHeight = header instanceof HTMLElement ? header.offsetHeight : 0;
    const narrow = root.getBoundingClientRect().width <= 380;
    if (!omittedState && !ignoreOmit && typeof maxHeight === 'number' &&
      Number.isFinite(maxHeight)) {
      omittedState = content.offsetHeight > maxHeight;
    }
    root.style.minHeight = `${headerHeight}px`;
    root.style.flexBasis = showBody ? 'auto' : `${headerHeight}px`;
    const signature = `${headerHeight}|${narrow}|${omittedState}`;
    if (signature === lastMeasurement) return;
    lastMeasurement = signature;
    dotnet.invokeMethodAsync('UpdateContainerMeasurements', headerHeight, narrow, omittedState)
      .catch(() => {});
  };

  const observer = new ResizeObserver(measure);
  observer.observe(root);
  observer.observe(content);
  if (header instanceof HTMLElement) observer.observe(header);
  measure();

  const cleanMotion = () => {
    if (motionCleanup !== null) {
      const cleanup = motionCleanup;
      motionCleanup = null;
      cleanup();
    }
    content.classList.remove(
      'container-toggle-enter-active',
      'container-toggle-enter-from',
      'container-toggle-enter-to',
      'container-toggle-leave-active',
      'container-toggle-leave-from',
      'container-toggle-leave-to');
    content.style.removeProperty('height');
  };

  const setExpanded = async (nextExpanded, animate, requestedGeneration) => {
    if (disposed) return true;
    generation = Number(requestedGeneration);
    cleanMotion();
    showBody = Boolean(nextExpanded);
    measure();
    const thisGeneration = generation;
    if (!animate) {
      content.style.display = showBody ? '' : 'none';
      return false;
    }

    await new Promise(resolve => requestAnimationFrame(resolve));
    if (disposed || generation !== thisGeneration) return true;

    let cancelWait = null;
    const registerCleanup = cleanup => { cancelWait = cleanup; };
    motionCleanup = () => cancelWait?.();

    if (showBody) {
      content.style.display = '';
      const elementHeight = content.getBoundingClientRect().height;
      content.classList.add('container-toggle-enter-active', 'container-toggle-enter-from');
      content.style.height = '0px';
      void content.offsetHeight;
      await nextFrame();
      if (disposed || generation !== thisGeneration) return true;
      content.classList.remove('container-toggle-enter-from');
      content.classList.add('container-toggle-enter-to');
      content.style.height = `${elementHeight}px`;
    } else {
      const elementHeight = content.getBoundingClientRect().height;
      content.classList.add('container-toggle-leave-active', 'container-toggle-leave-from');
      content.style.height = `${elementHeight}px`;
      void content.offsetHeight;
      await nextFrame();
      if (disposed || generation !== thisGeneration) return true;
      content.classList.remove('container-toggle-leave-from');
      content.classList.add('container-toggle-leave-to');
      content.style.height = '0px';
    }

    const result = await waitForTransition(
      content,
      thisGeneration,
      () => generation,
      registerCleanup);
    if (disposed || generation !== thisGeneration || result.cancelled) return true;
    cleanMotion();
    if (!showBody) content.style.display = 'none';
    measure();
    return false;
  };

  return {
    setExpanded,
    reveal() {
      ignoreOmit = true;
      omittedState = false;
      lastMeasurement = '';
      measure();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      cleanMotion();
      observer.disconnect();
    },
  };
}
