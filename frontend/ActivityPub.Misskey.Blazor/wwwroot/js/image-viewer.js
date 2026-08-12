function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

function distance(first, second) {
  return Math.hypot(second.x - first.x, second.y - first.y);
}

function center(first, second) {
  return { x: (first.x + second.x) / 2, y: (first.y + second.y) / 2 };
}

export function attach(modal, viewport, image) {
  if (!(modal instanceof HTMLElement)) throw new TypeError('MkImageViewer requires a modal root.');
  if (!(viewport instanceof HTMLElement)) throw new TypeError('MkImageViewer requires a viewport.');
  if (!(image instanceof HTMLImageElement)) throw new TypeError('MkImageViewer requires an image.');

  const pointers = new Map();
  const original = {
    transform: image.style.transform,
    transformOrigin: image.style.transformOrigin,
    touchAction: image.style.touchAction,
    userSelect: image.style.userSelect,
    cursor: image.style.cursor,
    draggable: image.draggable,
    overflow: viewport.style.overflow,
  };
  let disposed = false;
  let scale = 1;
  let panX = 0;
  let panY = 0;
  let moved = false;
  let suppressClick = false;
  let pinchDistance = 0;
  let pinchScale = 1;
  let pinchCenter = null;

  const isActive = () => !disposed &&
    modal.style.pointerEvents !== 'none' &&
    modal.dataset.motionState !== 'leaving' &&
    modal.dataset.motionState !== 'left';

  const clampPan = () => {
    if (scale <= 1) {
      panX = 0;
      panY = 0;
      return;
    }

    const maximumX = Math.max(0, viewport.clientWidth * (scale - 1) / 2);
    const maximumY = Math.max(0, viewport.clientHeight * (scale - 1) / 2);
    panX = clamp(panX, -maximumX, maximumX);
    panY = clamp(panY, -maximumY, maximumY);
  };

  const render = () => {
    clampPan();
    image.style.transform = `translate3d(${panX}px, ${panY}px, 0) scale(${scale})`;
    image.style.cursor = scale > 1 ? (pointers.size > 0 ? 'grabbing' : 'grab') : original.cursor;
    viewport.dataset.imageScale = String(scale);
    viewport.dataset.imagePanX = String(panX);
    viewport.dataset.imagePanY = String(panY);
  };

  const setScale = value => {
    scale = clamp(value, 1, 8);
    render();
  };

  const reset = () => {
    scale = 1;
    panX = 0;
    panY = 0;
    render();
  };

  const onWheel = event => {
    if (!isActive()) return;
    event.preventDefault();
    setScale(scale * Math.exp(-event.deltaY * 0.002));
  };

  const onPointerDown = event => {
    if (!isActive() || event.pointerType === 'mouse' && event.button !== 0) return;
    pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    moved = false;
    try {
      image.setPointerCapture(event.pointerId);
    } catch {
      // A synthetic test event or an already-detached pointer cannot be captured.
    }
    if (pointers.size === 2) {
      const [first, second] = [...pointers.values()];
      pinchDistance = Math.max(1, distance(first, second));
      pinchScale = scale;
      pinchCenter = center(first, second);
      suppressClick = true;
    }
    render();
  };

  const onPointerMove = event => {
    const previous = pointers.get(event.pointerId);
    if (!previous || !isActive()) return;
    const current = { x: event.clientX, y: event.clientY };
    pointers.set(event.pointerId, current);
    if (pointers.size >= 2) {
      const [first, second] = [...pointers.values()];
      const nextCenter = center(first, second);
      scale = clamp(pinchScale * distance(first, second) / Math.max(1, pinchDistance), 1, 8);
      if (pinchCenter !== null) {
        panX += nextCenter.x - pinchCenter.x;
        panY += nextCenter.y - pinchCenter.y;
      }
      pinchCenter = nextCenter;
      moved = true;
      suppressClick = true;
    } else if (scale > 1) {
      const deltaX = current.x - previous.x;
      const deltaY = current.y - previous.y;
      panX += deltaX;
      panY += deltaY;
      if (Math.hypot(deltaX, deltaY) >= 2) {
        moved = true;
        suppressClick = true;
      }
    }
    render();
  };

  const onPointerEnd = event => {
    if (!pointers.has(event.pointerId)) return;
    pointers.delete(event.pointerId);
    if (pointers.size < 2) {
      pinchDistance = 0;
      pinchScale = scale;
      pinchCenter = null;
    }
    if (moved) suppressClick = true;
    render();
  };

  const onClick = event => {
    if (!suppressClick) return;
    suppressClick = false;
    event.preventDefault();
    event.stopImmediatePropagation();
  };

  const onKeyDown = event => {
    if (!isActive() || event.altKey || event.ctrlKey || event.metaKey) return;
    const target = event.target;
    if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement) {
      return;
    }

    if (event.key === '+' || event.key === '=') {
      event.preventDefault();
      setScale(scale * 1.25);
    } else if (event.key === '-' || event.key === '_') {
      event.preventDefault();
      setScale(scale / 1.25);
    } else if (event.key === '0') {
      event.preventDefault();
      reset();
    } else if (scale > 1 && event.key === 'ArrowLeft') {
      event.preventDefault();
      panX += 40;
      render();
    } else if (scale > 1 && event.key === 'ArrowRight') {
      event.preventDefault();
      panX -= 40;
      render();
    } else if (scale > 1 && event.key === 'ArrowUp') {
      event.preventDefault();
      panY += 40;
      render();
    } else if (scale > 1 && event.key === 'ArrowDown') {
      event.preventDefault();
      panY -= 40;
      render();
    }
  };

  const onLoad = () => render();
  const resizeObserver = new ResizeObserver(render);
  viewport.style.overflow = 'hidden';
  image.style.transformOrigin = 'center';
  image.style.touchAction = 'none';
  image.style.userSelect = 'none';
  image.draggable = false;
  image.addEventListener('wheel', onWheel, { passive: false });
  image.addEventListener('pointerdown', onPointerDown);
  image.addEventListener('pointermove', onPointerMove);
  image.addEventListener('pointerup', onPointerEnd);
  image.addEventListener('pointercancel', onPointerEnd);
  image.addEventListener('click', onClick, true);
  image.addEventListener('load', onLoad);
  document.addEventListener('keydown', onKeyDown, true);
  resizeObserver.observe(viewport);
  viewport.focus({ preventScroll: true });
  render();

  return {
    reset,
    dispose() {
      if (disposed) return;
      disposed = true;
      resizeObserver.disconnect();
      image.removeEventListener('wheel', onWheel);
      image.removeEventListener('pointerdown', onPointerDown);
      image.removeEventListener('pointermove', onPointerMove);
      image.removeEventListener('pointerup', onPointerEnd);
      image.removeEventListener('pointercancel', onPointerEnd);
      image.removeEventListener('click', onClick, true);
      image.removeEventListener('load', onLoad);
      document.removeEventListener('keydown', onKeyDown, true);
      pointers.clear();
      image.style.transform = original.transform;
      image.style.transformOrigin = original.transformOrigin;
      image.style.touchAction = original.touchAction;
      image.style.userSelect = original.userSelect;
      image.style.cursor = original.cursor;
      image.draggable = original.draggable;
      viewport.style.overflow = original.overflow;
      delete viewport.dataset.imageScale;
      delete viewport.dataset.imagePanX;
      delete viewport.dataset.imagePanY;
    },
  };
}
