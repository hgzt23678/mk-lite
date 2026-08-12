import { focusableItems, registerOverlay } from './overlay-stack.js';

function milliseconds(value) {
  const trimmed = value.trim();
  if (trimmed.endsWith('ms')) return Number.parseFloat(trimmed) || 0;
  if (trimmed.endsWith('s')) return (Number.parseFloat(trimmed) || 0) * 1000;
  return 0;
}

function repeated(values, index) {
  return values[index % values.length] ?? 0;
}

function maximumMotionMilliseconds(element) {
  const style = getComputedStyle(element);
  const transitionDurations = style.transitionDuration.split(',').map(milliseconds);
  const transitionDelays = style.transitionDelay.split(',').map(milliseconds);
  const animationDurations = style.animationDuration.split(',').map(milliseconds);
  const animationDelays = style.animationDelay.split(',').map(milliseconds);
  const animationIterations = style.animationIterationCount.split(',').map(value => {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? Math.max(parsed, 1) : 1;
  });

  let maximum = 0;
  for (let index = 0; index < transitionDurations.length; index += 1) {
    maximum = Math.max(maximum, transitionDurations[index] + repeated(transitionDelays, index));
  }
  for (let index = 0; index < animationDurations.length; index += 1) {
    maximum = Math.max(
      maximum,
      animationDurations[index] * repeated(animationIterations, index) + repeated(animationDelays, index));
  }
  return maximum;
}

function waitForMotion(element, generationIsCurrent) {
  const expected = maximumMotionMilliseconds(element);
  if (expected <= 0) return Promise.resolve();

  return new Promise(resolve => {
    const started = performance.now();
    let timer = 0;
    let done = false;
    const complete = () => {
      if (done) return;
      done = true;
      element.removeEventListener('transitionend', onEnd);
      element.removeEventListener('animationend', onEnd);
      if (timer) clearTimeout(timer);
      resolve();
    };
    const onEnd = event => {
      if (event.target !== element || !generationIsCurrent()) return;
      // Several properties can finish on the same element. Do not accept an early property;
      // wait until the computed longest delay+duration has elapsed.
      if (performance.now() - started >= expected - 20) complete();
    };
    element.addEventListener('transitionend', onEnd);
    element.addEventListener('animationend', onEnd);
    timer = window.setTimeout(complete, Math.ceil(expected) + 80);
  });
}

export function attach(modal, content, windowElement, receiver, priority = 'low', motionName = 'modal') {
  let disposed = false;
  let closing = false;
  let generation = 1;
  let firstFrame = 0;
  let secondFrame = 0;
  const background = modal.querySelector(':scope > .bg');
  const overlay = registerOverlay({
    root: modal,
    background,
    content,
    focusRoot: windowElement,
    priority,
    lockScroll: true,
  });
  modal.style.setProperty('--transformOrigin', 'center');
  modal.dataset.motionState = 'entering';
  const enterFromClass = `${motionName}-enter-from`;
  const leaveToClass = `${motionName}-leave-to`;
  modal.classList.add(enterFromClass);

  const currentGeneration = generation;
  firstFrame = requestAnimationFrame(() => {
    secondFrame = requestAnimationFrame(async () => {
      if (disposed || currentGeneration !== generation) return;
      modal.classList.remove(enterFromClass);
      const isCurrent = () => !disposed && currentGeneration === generation;
      await Promise.all([
        waitForMotion(background, isCurrent),
        waitForMotion(content, isCurrent),
      ]);
      if (!isCurrent()) return;
      const autofocus = windowElement.querySelector('[data-mk-autofocus="true"]');
      if (autofocus instanceof HTMLElement && !windowElement.contains(document.activeElement)) {
        autofocus.focus({ preventScroll: true });
      }
      modal.dataset.motionState = 'entered';
      receiver.invokeMethodAsync('NotifyOpened').catch(() => {});
    });
  });

  const close = async () => {
    if (disposed || closing) return;
    closing = true;
    generation += 1;
    const closeGeneration = generation;
    modal.dataset.motionState = 'leaving';
    modal.classList.add(leaveToClass);
    const isCurrent = () => !disposed && closeGeneration === generation;
    await Promise.all([
      waitForMotion(background, isCurrent),
      waitForMotion(content, isCurrent),
    ]);
    if (!isCurrent()) return;
    modal.dataset.motionState = 'left';
    receiver.invokeMethodAsync('NotifyClosed').catch(() => {});
  };

  const onKeyDown = event => {
    if (!overlay.isTop()) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      close();
      return;
    }
    if (!modal.contains(event.target)) {
      event.preventDefault();
      event.stopImmediatePropagation();
      focusableItems(windowElement)[0]?.focus({ preventScroll: true });
      return;
    }
    if (event.key !== 'Tab') return;
    const items = focusableItems(windowElement);
    if (items.length === 0) {
      event.preventDefault();
      return;
    }
    const index = items.indexOf(document.activeElement);
    const next = event.shiftKey ? index - 1 : index + 1;
    if (index < 0 || next < 0 || next >= items.length) {
      event.preventDefault();
      items[event.shiftKey ? items.length - 1 : 0].focus();
    }
  };
  document.addEventListener('keydown', onKeyDown, true);

  return {
    close,
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      if (firstFrame) cancelAnimationFrame(firstFrame);
      if (secondFrame) cancelAnimationFrame(secondFrame);
      document.removeEventListener('keydown', onKeyDown, true);
      overlay.dispose();
    },
  };
}
