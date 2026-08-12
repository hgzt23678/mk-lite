import { registerOverlay } from './overlay-stack.js';

function milliseconds(value) {
  const trimmed = value.trim();
  if (trimmed.endsWith('ms')) return Number.parseFloat(trimmed) || 0;
  if (trimmed.endsWith('s')) return (Number.parseFloat(trimmed) || 0) * 1000;
  return 0;
}

function repeated(values, index) {
  return values[index % values.length] ?? 0;
}

function maximumTransitionMilliseconds(element) {
  const style = getComputedStyle(element);
  const durations = style.transitionDuration.split(',').map(milliseconds);
  const delays = style.transitionDelay.split(',').map(milliseconds);
  let maximum = 0;
  for (let index = 0; index < durations.length; index += 1) {
    maximum = Math.max(maximum, durations[index] + repeated(delays, index));
  }
  return maximum;
}

function waitForTransition(element, generationIsCurrent) {
  const expected = maximumTransitionMilliseconds(element);
  if (expected <= 0) return Promise.resolve();

  return new Promise(resolve => {
    const started = performance.now();
    let timer = 0;
    let complete = false;
    const finish = () => {
      if (complete) return;
      complete = true;
      element.removeEventListener('transitionend', onEnd);
      if (timer !== 0) clearTimeout(timer);
      resolve();
    };
    const onEnd = event => {
      if (event.target !== element || !generationIsCurrent()) return;
      if (performance.now() - started >= expected - 20) finish();
    };
    element.addEventListener('transitionend', onEnd);
    timer = window.setTimeout(finish, Math.ceil(expected) + 80);
  });
}

export function attach(modal, background, content, receiver) {
  let disposed = false;
  let closing = false;
  let generation = 1;
  let firstFrame = 0;
  let secondFrame = 0;
  const overlay = registerOverlay({
    root: modal,
    background,
    content,
    focusRoot: content,
    priority: 'high',
    lockScroll: false,
  });

  modal.style.setProperty('--transformOrigin', 'center');
  modal.dataset.motionState = 'entering';
  modal.classList.add('modal-enter-from');
  const enterGeneration = generation;
  firstFrame = requestAnimationFrame(() => {
    secondFrame = requestAnimationFrame(async () => {
      if (disposed || enterGeneration !== generation) return;
      modal.classList.remove('modal-enter-from');
      const current = () => !disposed && enterGeneration === generation;
      await Promise.all([
        waitForTransition(background, current),
        waitForTransition(content, current),
      ]);
      if (!current()) return;
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
    modal.classList.add('modal-leave-to');
    const current = () => !disposed && closeGeneration === generation;
    await Promise.all([
      waitForTransition(background, current),
      waitForTransition(content, current),
    ]);
    if (!current()) return;
    modal.dataset.motionState = 'left';
    receiver.invokeMethodAsync('NotifyClosed').catch(() => {});
  };

  return {
    close,
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      if (firstFrame !== 0) cancelAnimationFrame(firstFrame);
      if (secondFrame !== 0) cancelAnimationFrame(secondFrame);
      overlay.dispose();
    },
  };
}
