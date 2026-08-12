const colonFlashMilliseconds = 30;

export function attach(element, showSeconds, showMilliseconds, offsetMinutes) {
  if (!(element instanceof HTMLSpanElement) ||
      typeof showSeconds !== 'boolean' ||
      typeof showMilliseconds !== 'boolean' ||
      offsetMinutes !== null && !Number.isSafeInteger(offsetMinutes)) {
    throw new Error('MISSKEY_DIGITAL_CLOCK_CONFIGURATION_INVALID');
  }

  const children = Array.from(element.children);
  const hours = children[0];
  const minutes = children[2];
  let index = 3;
  const seconds = showSeconds ? children[++index] : null;
  if (showSeconds) index++;
  const milliseconds = showMilliseconds ? children[++index] : null;
  const colons = children.filter(child => child.classList.contains('colon'));
  if (!(hours instanceof HTMLSpanElement) || !(minutes instanceof HTMLSpanElement) ||
      showSeconds !== (seconds instanceof HTMLSpanElement) ||
      showMilliseconds !== (milliseconds instanceof HTMLSpanElement) ||
      colons.some(colon => !(colon instanceof HTMLSpanElement)) ||
      children.length !== 3 + (showSeconds ? 2 : 0) + (showMilliseconds ? 2 : 0)) {
    throw new Error('MISSKEY_DIGITAL_CLOCK_DOM_INVALID');
  }

  const effectiveOffset = offsetMinutes ?? -new Date().getTimezoneOffset();
  let disposed = false;
  let interval = 0;
  let colonTimeout = 0;
  let previousSecond = null;

  const pad = value => value.toString().padStart(2, '0');
  const hideColon = () => {
    colonTimeout = 0;
    if (disposed) return;
    for (const colon of colons) colon.classList.remove('showColon');
  };
  const flashColon = () => {
    if (colonTimeout !== 0) window.clearTimeout(colonTimeout);
    for (const colon of colons) colon.classList.add('showColon');
    colonTimeout = window.setTimeout(hideColon, colonFlashMilliseconds);
  };
  const tick = () => {
    if (disposed || !element.isConnected) return;
    const now = new Date();
    now.setMinutes(now.getMinutes() + now.getTimezoneOffset() + effectiveOffset);
    hours.textContent = pad(now.getHours());
    minutes.textContent = pad(now.getMinutes());
    if (seconds) seconds.textContent = pad(now.getSeconds());
    if (milliseconds) milliseconds.textContent = pad(Math.floor(now.getMilliseconds() / 10));
    if (now.getSeconds() !== previousSecond) flashColon();
    previousSecond = now.getSeconds();
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
