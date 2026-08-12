export function attach(input, prefix, suffix, autofocus) {
  let disposed = false;
  let frame = 0;

  const update = () => {
    frame = 0;
    if (disposed || !input.isConnected) return;

    const prefixWidth = prefix.offsetWidth;
    const suffixWidth = suffix.offsetWidth;
    input.style.paddingLeft = prefixWidth > 0 ? `${prefixWidth}px` : '';
    input.style.paddingRight = suffixWidth > 0 ? `${suffixWidth}px` : '';
  };

  const schedule = () => {
    if (!disposed && frame === 0) frame = requestAnimationFrame(update);
  };

  const observer = new ResizeObserver(schedule);
  const container = input.parentElement;
  const onFocus = () => container?.classList.add('focused');
  const onBlur = () => container?.classList.remove('focused');
  observer.observe(prefix);
  observer.observe(suffix);
  observer.observe(input);
  window.addEventListener('resize', schedule, { passive: true });
  input.addEventListener('focus', onFocus);
  input.addEventListener('blur', onBlur);
  schedule();

  if (autofocus) {
    requestAnimationFrame(() => {
      if (!disposed && input.isConnected) input.focus({ preventScroll: true });
    });
  }

  return {
    focus() {
      if (!disposed && input.isConnected) input.focus();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      observer.disconnect();
      window.removeEventListener('resize', schedule);
      input.removeEventListener('focus', onFocus);
      input.removeEventListener('blur', onBlur);
      container?.classList.remove('focused');
      if (frame !== 0) cancelAnimationFrame(frame);
    },
  };
}
