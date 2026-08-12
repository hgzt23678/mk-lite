const colonFlashMilliseconds = 30;

export function attach(root, showMilliseconds) {
  if (!(root instanceof HTMLElement) || typeof showMilliseconds !== 'boolean') {
    throw new Error('MISSKEY_UNIX_CLOCK_CONFIGURATION_INVALID');
  }

  const time = root.querySelector(':scope > .time');
  if (!(time instanceof HTMLElement)) {
    throw new Error('MISSKEY_UNIX_CLOCK_DOM_INVALID');
  }

  const children = Array.from(time.children);
  const seconds = children[0];
  const colon = showMilliseconds ? children[1] : null;
  const milliseconds = showMilliseconds ? children[2] : null;
  if (!(seconds instanceof HTMLSpanElement) ||
      showMilliseconds !== (colon instanceof HTMLSpanElement) ||
      showMilliseconds !== (milliseconds instanceof HTMLSpanElement) ||
      children.length !== 1 + (showMilliseconds ? 2 : 0)) {
    throw new Error('MISSKEY_UNIX_CLOCK_DOM_INVALID');
  }

  let disposed = false;
  let interval = 0;
  let colonTimeout = 0;
  let previousSecond = null;

  const pad = value => value.toString().padStart(2, '0');
  const tick = () => {
    if (disposed || !root.isConnected) return;
    const now = Date.now();
    const second = Math.floor(now / 1000).toString();
    seconds.textContent = second;
    if (milliseconds) milliseconds.textContent = pad(Math.floor((now % 1000) / 10));
    if (second !== previousSecond && colon) {
      colon.classList.add('showColon');
      if (colonTimeout !== 0) window.clearTimeout(colonTimeout);
      colonTimeout = window.setTimeout(() => colon.classList.remove('showColon'), colonFlashMilliseconds);
    }

    previousSecond = second;
  };

  tick();
  interval = window.setInterval(tick, showMilliseconds ? 10 : 1000);

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      window.clearInterval(interval);
      if (colonTimeout !== 0) window.clearTimeout(colonTimeout);
    },
  };
}
