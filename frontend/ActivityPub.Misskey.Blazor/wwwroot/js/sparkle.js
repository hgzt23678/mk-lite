export function attach(content, receiver, generation, animationEnabled) {
  if (!(content instanceof HTMLElement) || !receiver ||
      !Number.isSafeInteger(generation) || generation < 1) {
    throw new Error('MISSKEY_SPARKLE_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let frame = 0;
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const publish = () => {
    frame = 0;
    if (disposed || !content.isConnected) return;
    receiver.invokeMethodAsync(
      'UpdateSparkleMetrics',
      generation,
      content.offsetWidth,
      content.offsetHeight,
      !animationEnabled || reducedMotion.matches)
      .catch(error => {
        if (!disposed && content.isConnected) console.error('Misskey sparkle callback failed.', error);
      });
  };
  const schedule = () => {
    if (!disposed && frame === 0) frame = requestAnimationFrame(publish);
  };
  const observer = new ResizeObserver(schedule);
  observer.observe(content);
  reducedMotion.addEventListener('change', schedule);
  schedule();

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      observer.disconnect();
      reducedMotion.removeEventListener('change', schedule);
      if (frame !== 0) cancelAnimationFrame(frame);
    },
  };
}
