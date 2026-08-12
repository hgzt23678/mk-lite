export function attach(files, receiver) {
  if (!(files instanceof HTMLElement) || receiver === null || typeof receiver !== 'object') {
    throw new Error('MISSKEY_POST_FORM_ATTACHES_CONFIGURATION_INVALID');
  }

  let disposed = false;
  let active = null;
  let activationTimer = 0;
  let suppressClick = false;

  const fileElement = target => target instanceof Element ? target.closest('.file') : null;
  const idOf = element => element.querySelector(':scope > .thumbnail[data-id]')?.getAttribute('data-id');
  const activate = session => {
    if (disposed || active !== session) return;
    session.activated = true;
    session.element.classList.add('sortable-chosen', 'sortable-drag');
    try {
      session.element.setPointerCapture?.(session.pointerId);
    } catch {
      // Synthetic pointer fixtures have no active native pointer to capture.
    }
  };
  const onPointerDown = event => {
    if (active !== null || event.button !== 0) return;
    const element = fileElement(event.target);
    if (!(element instanceof HTMLElement) || element.parentElement !== files) return;
    const session = {
      element,
      pointerId: event.pointerId,
      touch: event.pointerType === 'touch',
      startX: event.clientX,
      startY: event.clientY,
      activated: false,
      moved: false,
    };
    active = session;
    if (session.touch) activationTimer = window.setTimeout(() => activate(session), 100);
    else activate(session);
    window.addEventListener('pointermove', onPointerMove, { passive: false });
    window.addEventListener('pointerup', finishPointer, { once: true });
    window.addEventListener('pointercancel', cancelPointer, { once: true });
  };
  const onPointerMove = event => {
    const session = active;
    if (session === null || event.pointerId !== session.pointerId) return;
    const distance = Math.hypot(event.clientX - session.startX, event.clientY - session.startY);
    if (session.touch && !session.activated && distance > 8) {
      cancelSession(session);
      return;
    }
    if (!session.activated) return;
    event.preventDefault();
    const target = fileElement(document.elementFromPoint(event.clientX, event.clientY));
    if (!(target instanceof HTMLElement) || target === session.element || target.parentElement !== files) return;
    const rectangle = target.getBoundingClientRect();
    const after = event.clientY > rectangle.top + rectangle.height / 2 ||
      Math.abs(event.clientY - (rectangle.top + rectangle.height / 2)) < rectangle.height / 2 &&
      event.clientX > rectangle.left + rectangle.width / 2;
    files.insertBefore(session.element, after ? target.nextSibling : target);
    session.moved = true;
  };
  const finishPointer = event => {
    const session = active;
    if (session === null || event.pointerId !== session.pointerId) return;
    cleanupSession(session);
    if (!session.activated || !session.moved) return;
    suppressClick = true;
    window.setTimeout(() => {
      suppressClick = false;
    }, 0);
    const ids = [...files.querySelectorAll(':scope > .file')].map(idOf);
    if (ids.every(id => typeof id === 'string' && id.length > 0)) {
      receiver.invokeMethodAsync('NotifyReordered', ids).catch(() => {});
    }
  };
  const cancelPointer = event => {
    const session = active;
    if (session !== null && event.pointerId === session.pointerId) cancelSession(session);
  };
  const cancelSession = session => cleanupSession(session);
  const cleanupSession = session => {
    if (activationTimer) clearTimeout(activationTimer);
    activationTimer = 0;
    session.element.classList.remove('sortable-chosen', 'sortable-drag');
    try {
      session.element.releasePointerCapture?.(session.pointerId);
    } catch {
      // The browser may already have released capture on pointercancel.
    }
    if (active === session) active = null;
    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', finishPointer);
    window.removeEventListener('pointercancel', cancelPointer);
  };
  const onClickCapture = event => {
    if (!suppressClick) return;
    event.preventDefault();
    event.stopImmediatePropagation();
  };
  const onDragStart = event => {
    if (fileElement(event.target) instanceof HTMLElement) event.preventDefault();
  };

  files.addEventListener('pointerdown', onPointerDown);
  files.addEventListener('dragstart', onDragStart);
  files.addEventListener('click', onClickCapture, true);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      if (active !== null) cancelSession(active);
      files.removeEventListener('pointerdown', onPointerDown);
      files.removeEventListener('dragstart', onDragStart);
      files.removeEventListener('click', onClickCapture, true);
    },
  };
}
