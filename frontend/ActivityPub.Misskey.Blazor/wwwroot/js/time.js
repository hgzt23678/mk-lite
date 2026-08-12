const tickIntervalMilliseconds = 10_000;

export function attach(element, receiver, generation, unixTimeMilliseconds, updateRelativeTime) {
  if (!(element instanceof HTMLTimeElement) ||
      !receiver ||
      !Number.isSafeInteger(generation) || generation < 1 ||
      !Number.isFinite(unixTimeMilliseconds)) {
    throw new Error('MISSKEY_TIME_CONFIGURATION_INVALID');
  }

  const value = new Date(unixTimeMilliseconds);
  if (Number.isNaN(value.getTime())) {
    throw new Error('MISSKEY_TIME_VALUE_INVALID');
  }

  let disposed = false;
  let visible = true;
  let timeout = 0;
  let frame = 0;
  let observer = null;

  const clearSchedule = () => {
    if (timeout !== 0) {
      window.clearTimeout(timeout);
      timeout = 0;
    }
    if (frame !== 0) {
      window.cancelAnimationFrame(frame);
      frame = 0;
    }
  };

  const canUpdate = () => visible && !document.hidden && element.isConnected;

  const publish = () => {
    frame = 0;
    if (disposed || !canUpdate()) return;
    receiver.invokeMethodAsync('UpdateTime', generation, Date.now(), value.toLocaleString())
      .catch(error => {
        if (!disposed && element.isConnected) {
          console.error('Misskey time callback failed.', error);
        }
      });
  };

  const schedule = () => {
    clearSchedule();
    if (disposed || !updateRelativeTime || !canUpdate()) return;
    timeout = window.setTimeout(() => {
      timeout = 0;
      frame = window.requestAnimationFrame(() => {
        publish();
        schedule();
      });
    }, tickIntervalMilliseconds);
  };

  const synchronize = () => {
    clearSchedule();
    if (disposed || !canUpdate()) return;
    frame = window.requestAnimationFrame(() => {
      publish();
      schedule();
    });
  };

  const onVisibilityChanged = () => {
    if (document.hidden) clearSchedule();
    else synchronize();
  };

  document.addEventListener('visibilitychange', onVisibilityChanged);
  if (updateRelativeTime && 'IntersectionObserver' in globalThis) {
    observer = new IntersectionObserver(entries => {
      const entry = entries.at(-1);
      const nextVisible = entry?.isIntersecting ?? false;
      if (visible === nextVisible) return;
      visible = nextVisible;
      if (visible) synchronize();
      else clearSchedule();
    });
    observer.observe(element);
  }

  synchronize();

  return {
    refresh() {
      synchronize();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      clearSchedule();
      observer?.disconnect();
      document.removeEventListener('visibilitychange', onVisibilityChanged);
    },
  };
}
