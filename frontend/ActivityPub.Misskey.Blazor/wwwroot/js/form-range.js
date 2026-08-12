export function attach(container, thumb, highlight, initialValue, receiver) {
  let disposed = false;
  let dragging = false;
  let currentValue = clamp(initialValue);
  let beforeValue = currentValue;
  let frame = 0;
  let pendingNotify = false;
  let cursorStyle = null;

  const render = () => {
    frame = 0;
    if (disposed || !container.isConnected || !thumb.isConnected) return;
    const thumbWidth = thumb.offsetWidth;
    const availableWidth = Math.max(0, container.offsetWidth - thumbWidth);
    thumb.style.left = `${availableWidth * currentValue}px`;
    highlight.style.width = `${currentValue * 100}%`;
    if (pendingNotify) {
      pendingNotify = false;
      receiver.invokeMethodAsync('NotifyRawValue', currentValue).catch(() => {});
    }
  };

  const schedule = (notify = false) => {
    pendingNotify ||= notify;
    if (!disposed && frame === 0) frame = requestAnimationFrame(render);
  };

  const pointerX = event => {
    if (event.touches && event.touches.length > 0) return event.touches[0].clientX;
    return event.clientX;
  };

  const onDrag = event => {
    if (!dragging) return;
    event.preventDefault();
    const rect = container.getBoundingClientRect();
    const thumbWidth = thumb.offsetWidth;
    const availableWidth = Math.max(1, container.offsetWidth - thumbWidth);
    const position = pointerX(event) - (rect.left + (thumbWidth / 2));
    currentValue = clamp(position / availableWidth);
    schedule(true);
  };

  const removeGlobalListeners = () => {
    window.removeEventListener('mousemove', onDrag);
    window.removeEventListener('touchmove', onDrag);
    window.removeEventListener('mouseup', onEnd);
    window.removeEventListener('touchend', onEnd);
    window.removeEventListener('touchcancel', onEnd);
  };

  const onEnd = () => {
    if (!dragging) return;
    dragging = false;
    removeGlobalListeners();
    cursorStyle?.remove();
    cursorStyle = null;
    receiver.invokeMethodAsync('NotifyDragEnded', currentValue, beforeValue !== currentValue).catch(() => {});
  };

  const onStart = event => {
    if (disposed || dragging) return;
    event.preventDefault();
    dragging = true;
    beforeValue = currentValue;
    cursorStyle = document.createElement('style');
    cursorStyle.textContent = '* { cursor: grabbing !important; } body * { pointer-events: none !important; }';
    document.head.appendChild(cursorStyle);
    window.addEventListener('mousemove', onDrag, { passive: false });
    window.addEventListener('touchmove', onDrag, { passive: false });
    window.addEventListener('mouseup', onEnd, { once: true });
    window.addEventListener('touchend', onEnd, { once: true });
    window.addEventListener('touchcancel', onEnd, { once: true });
    receiver.invokeMethodAsync('NotifyDragStarted').catch(() => {});
  };

  const observer = new ResizeObserver(() => schedule());
  observer.observe(container);
  observer.observe(thumb);
  thumb.addEventListener('mousedown', onStart);
  thumb.addEventListener('touchstart', onStart, { passive: false });
  schedule();

  return {
    setValue(value) {
      if (disposed || dragging) return;
      currentValue = clamp(value);
      schedule();
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      dragging = false;
      if (frame) cancelAnimationFrame(frame);
      observer.disconnect();
      thumb.removeEventListener('mousedown', onStart);
      thumb.removeEventListener('touchstart', onStart);
      removeGlobalListeners();
      cursorStyle?.remove();
      cursorStyle = null;
    },
  };
}

function clamp(value) {
  if (!Number.isFinite(value)) return 0;
  return Math.min(1, Math.max(0, value));
}
