export function observe(element, receiver) {
  if (!(element instanceof HTMLElement)) throw new TypeError('WelcomeTimeline requires its scrollbox element.');

  let disposed = false;
  let frame = 0;
  const publish = () => {
    frame = 0;
    if (!disposed && element.isConnected) {
      receiver.invokeMethodAsync('UpdateWelcomeTimelineScroll', element.clientHeight > window.innerHeight)
        .catch(error => {
          if (!disposed && element.isConnected) console.error('Welcome timeline size callback failed.', error);
        });
    }
  };
  const schedule = () => {
    if (frame === 0) frame = window.requestAnimationFrame(publish);
  };
  const observer = new ResizeObserver(schedule);
  observer.observe(element);
  window.addEventListener('resize', schedule, { passive: true });
  publish();

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      observer.disconnect();
      window.removeEventListener('resize', schedule);
      if (frame !== 0) window.cancelAnimationFrame(frame);
    },
  };
}
