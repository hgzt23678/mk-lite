export function attach(root, receiver) {
  if (!(root instanceof HTMLElement) || receiver === null || typeof receiver !== 'object') {
    throw new Error('MISSKEY_WIDGETS_CONFIGURATION_INVALID');
  }

  let dragging = null;
  let dropTarget = null;
  let dropAfter = false;

  const clearTarget = () => {
    dropTarget?.classList.remove('drag-before', 'drag-after');
    dropTarget = null;
  };

  const onDragStart = event => {
    const handle = event.target instanceof Element ? event.target.closest('.handle[draggable="true"]') : null;
    const container = handle?.closest('.customize-container[data-widget-id]');
    if (!(container instanceof HTMLElement)) {
      event.preventDefault();
      return;
    }

    dragging = container.dataset.widgetId ?? null;
    if (dragging === null) {
      event.preventDefault();
      return;
    }

    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', dragging);
    container.classList.add('dragging');
  };

  const onDragOver = event => {
    if (dragging === null) return;
    const target = event.target instanceof Element ? event.target.closest('.customize-container[data-widget-id]') : null;
    if (!(target instanceof HTMLElement) || target.dataset.widgetId === dragging) {
      clearTarget();
      return;
    }

    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
    if (dropTarget !== target) clearTarget();
    dropTarget = target;
    const bounds = target.getBoundingClientRect();
    dropAfter = event.clientY >= bounds.top + (bounds.height / 2);
    target.classList.toggle('drag-before', !dropAfter);
    target.classList.toggle('drag-after', dropAfter);
  };

  const onDrop = event => {
    if (dragging === null || dropTarget === null) return;
    event.preventDefault();
    const targetId = dropTarget.dataset.widgetId;
    if (targetId) receiver.invokeMethodAsync('ReorderWidget', dragging, targetId, dropAfter).catch(() => {});
    clearTarget();
  };

  const onDragEnd = () => {
    root.querySelector('.customize-container.dragging')?.classList.remove('dragging');
    dragging = null;
    clearTarget();
  };

  root.addEventListener('dragstart', onDragStart);
  root.addEventListener('dragover', onDragOver);
  root.addEventListener('drop', onDrop);
  root.addEventListener('dragend', onDragEnd);

  return {
    dispose() {
      root.removeEventListener('dragstart', onDragStart);
      root.removeEventListener('dragover', onDragOver);
      root.removeEventListener('drop', onDrop);
      root.removeEventListener('dragend', onDragEnd);
      onDragEnd();
    },
  };
}
