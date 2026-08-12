import { claimZIndex } from './overlay-stack.js';

const showDelay = 500;
const hideDelay = 500;
const hosts = new Map();
let nextHostId = 0;
let nextSourceId = 0;

function invoke(receiver, method, ...args) {
  return receiver.invokeMethodAsync(method, ...args).catch(error => {
    console.error(`User preview callback ${method} failed.`, error);
  });
}

function connectedElement(value) {
  return value instanceof HTMLElement && value.isConnected && document.body.contains(value);
}

export function attachDirectiveHost(receiver) {
  const hostId = `user-preview-host-${++nextHostId}`;
  const sourceByElement = new Map();
  const sourceById = new Map();
  let disposed = false;
  let current = null;
  let closing = null;
  let generation = 0;
  let hideTimer = 0;

  const clearHide = () => {
    if (hideTimer !== 0) {
      window.clearTimeout(hideTimer);
      hideTimer = 0;
    }
  };
  const clearShow = source => {
    if (source.showTimer !== 0) {
      window.clearTimeout(source.showTimer);
      source.showTimer = 0;
    }
  };
  const clearOtherShows = except => {
    for (const source of sourceById.values()) {
      if (source !== except) clearShow(source);
    }
  };
  const hideCurrent = source => {
    clearHide();
    if (current === null || (source && current.source !== source)) return;
    const closingRequest = current;
    current = null;
    closing = closingRequest;
    if (!disposed) {
      void invoke(receiver, 'HideUserPreviewAsync', hostId, closingRequest.source.id, closingRequest.generation);
    }
  };
  const scheduleHide = source => {
    clearHide();
    if (current === null || current.source !== source) return;
    hideTimer = window.setTimeout(() => {
      hideTimer = 0;
      if (!source.hovering && !source.focused && !source.touching && !source.previewActive) {
        hideCurrent(source);
      }
    }, hideDelay);
  };
  const show = source => {
    source.showTimer = 0;
    if (disposed || !connectedElement(source.element) ||
        (!source.hovering && !source.focused && !source.touching)) return;
    if (current?.source === source) return;

    clearHide();
    if (current !== null) hideCurrent(current.source);
    const nextGeneration = ++generation;
    closing = null;
    current = { source, generation: nextGeneration };
    void invoke(receiver, 'ShowUserPreviewAsync', hostId, source.id, source.query, nextGeneration);
  };
  const scheduleShow = (source, delay = showDelay) => {
    clearOtherShows(source);
    clearShow(source);
    clearHide();
    source.showTimer = window.setTimeout(() => show(source), delay);
  };
  const sourceIsActive = source => source.hovering || source.focused || source.touching;
  const closeIfInactive = source => {
    clearShow(source);
    if (!sourceIsActive(source) && !source.previewActive) scheduleHide(source);
  };

  const detachSource = source => {
    clearShow(source);
    if (source.syntheticMouseTimer !== 0) {
      window.clearTimeout(source.syntheticMouseTimer);
      source.syntheticMouseTimer = 0;
    }
    source.element.removeEventListener('mouseover', source.onMouseover);
    source.element.removeEventListener('mouseleave', source.onMouseleave);
    source.element.removeEventListener('focusin', source.onFocusin);
    source.element.removeEventListener('focusout', source.onFocusout);
    source.element.removeEventListener('touchstart', source.onTouchstart);
    source.element.removeEventListener('touchend', source.onTouchend);
    source.element.removeEventListener('touchcancel', source.onTouchend);
    source.element.removeEventListener('click', source.onClick);
    source.element.removeEventListener('keydown', source.onKeydown);
    delete source.element.dataset.userPreviewReady;
    sourceByElement.delete(source.element);
    sourceById.delete(source.id);
    if (current?.source === source) hideCurrent(source);
  };

  const attachSource = element => {
    const query = element.dataset.userPreview?.trim();
    if (!query) return;
    const existing = sourceByElement.get(element);
    if (existing) {
      existing.query = query;
      return;
    }

    const source = {
      id: `user-preview-source-${++nextSourceId}`,
      element,
      query,
      showTimer: 0,
      hovering: false,
      focused: false,
      touching: false,
      previewActive: false,
      ignoreSyntheticMouse: false,
      syntheticMouseTimer: 0,
    };
    source.onMouseover = () => {
      if (source.hovering || source.ignoreSyntheticMouse) return;
      source.hovering = true;
      scheduleShow(source);
    };
    source.onMouseleave = () => {
      source.hovering = false;
      closeIfInactive(source);
    };
    source.onFocusin = () => {
      source.focused = true;
      scheduleShow(source);
    };
    source.onFocusout = event => {
      if (event.relatedTarget instanceof Node && element.contains(event.relatedTarget)) return;
      source.focused = false;
      closeIfInactive(source);
    };
    source.onTouchstart = () => {
      if (source.syntheticMouseTimer !== 0) {
        window.clearTimeout(source.syntheticMouseTimer);
        source.syntheticMouseTimer = 0;
      }
      source.ignoreSyntheticMouse = true;
      source.touching = true;
      scheduleShow(source);
    };
    source.onTouchend = () => {
      source.touching = false;
      closeIfInactive(source);
      source.syntheticMouseTimer = window.setTimeout(() => {
        source.syntheticMouseTimer = 0;
        if (!disposed) source.ignoreSyntheticMouse = false;
      }, 750);
    };
    source.onClick = () => {
      clearShow(source);
      hideCurrent(source);
    };
    source.onKeydown = event => {
      if (event.key === 'Escape') {
        clearShow(source);
        hideCurrent(source);
      }
    };
    element.addEventListener('mouseover', source.onMouseover, { passive: true });
    element.addEventListener('mouseleave', source.onMouseleave, { passive: true });
    element.addEventListener('focusin', source.onFocusin, { passive: true });
    element.addEventListener('focusout', source.onFocusout, { passive: true });
    element.addEventListener('touchstart', source.onTouchstart, { passive: true });
    element.addEventListener('touchend', source.onTouchend, { passive: true });
    element.addEventListener('touchcancel', source.onTouchend, { passive: true });
    element.addEventListener('click', source.onClick, { passive: true });
    element.addEventListener('keydown', source.onKeydown);
    element.dataset.userPreviewReady = 'true';
    sourceByElement.set(element, source);
    sourceById.set(source.id, source);

    // A pointer or keyboard focus may arrive while Interactive Server is hydrating.
    source.hovering = element.matches(':hover');
    source.focused = element === document.activeElement || element.contains(document.activeElement);
    if (sourceIsActive(source)) scheduleShow(source);
  };

  const scan = root => {
    if (!(root instanceof Element)) return;
    if (root.matches('[data-user-preview]')) attachSource(root);
    for (const element of root.querySelectorAll('[data-user-preview]')) attachSource(element);
  };
  const sweep = () => {
    for (const source of [...sourceById.values()]) {
      if (!connectedElement(source.element) || !source.element.dataset.userPreview) detachSource(source);
    }
  };
  const observer = new MutationObserver(records => {
    if (disposed) return;
    for (const record of records) {
      if (record.type === 'attributes' && record.target instanceof HTMLElement) {
        const query = record.target.dataset.userPreview?.trim();
        if (query) attachSource(record.target);
        else {
          const source = sourceByElement.get(record.target);
          if (source) detachSource(source);
        }
      }
      for (const node of record.addedNodes) scan(node);
    }
    sweep();
  });
  scan(document.body);
  observer.observe(document.body, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ['data-user-preview'],
  });
  const checkTimer = window.setInterval(sweep, 1000);

  const host = {
    source(sourceId, expectedGeneration) {
      const request = current ?? closing;
      if (disposed || request === null || request.source.id !== sourceId ||
          request.generation !== expectedGeneration) return null;
      return request.source;
    },
    previewEntered(sourceId, expectedGeneration) {
      const source = this.source(sourceId, expectedGeneration);
      if (!source || !connectedElement(source.element)) return;
      source.previewActive = true;
      clearHide();
      if (current === null && closing?.source === source && closing.generation === expectedGeneration) {
        current = closing;
        closing = null;
        void invoke(receiver, 'ShowUserPreviewAsync', hostId, source.id, source.query, expectedGeneration);
      }
    },
    previewLeft(sourceId, expectedGeneration) {
      const source = this.source(sourceId, expectedGeneration);
      if (!source) return;
      source.previewActive = false;
      closeIfInactive(source);
    },
    requestHide(sourceId, expectedGeneration) {
      const source = this.source(sourceId, expectedGeneration);
      if (source) hideCurrent(source);
    },
  };
  hosts.set(hostId, host);

  return {
    hostId,
    dispose() {
      if (disposed) return;
      disposed = true;
      observer.disconnect();
      window.clearInterval(checkTimer);
      clearHide();
      for (const source of [...sourceById.values()]) detachSource(source);
      hosts.delete(hostId);
    },
  };
}

function splitTimes(value) {
  return value.split(',').map(part => {
    const normalized = part.trim();
    if (normalized.endsWith('ms')) return Number.parseFloat(normalized) || 0;
    if (normalized.endsWith('s')) return (Number.parseFloat(normalized) || 0) * 1000;
    return 0;
  });
}

function maximumTransitionTime(element) {
  const style = getComputedStyle(element);
  const durations = splitTimes(style.transitionDuration);
  const delays = splitTimes(style.transitionDelay);
  const count = Math.max(durations.length, delays.length, 1);
  let maximum = 0;
  for (let index = 0; index < count; index += 1) {
    maximum = Math.max(maximum,
      durations[index % Math.max(durations.length, 1)] + delays[index % Math.max(delays.length, 1)]);
  }
  return maximum;
}

function position(source, preview) {
  const rect = source.getBoundingClientRect();
  const left = rect.left + (source.offsetWidth / 2) - (300 / 2) + window.pageXOffset;
  const top = rect.top + source.offsetHeight + window.pageYOffset;
  preview.style.left = `${left}px`;
  preview.style.top = `${top}px`;
}

export function attachPreview(hostId, sourceId, generation, preview, receiver) {
  if (!(preview instanceof HTMLElement)) throw new TypeError('A user preview element is required.');
  const host = hosts.get(hostId);
  const source = host?.source(sourceId, generation);
  let disposed = false;
  let showing = false;
  let positionFrame = 0;
  let motionFrame = 0;
  let completionTimer = 0;
  let transitionHandler = null;
  let motionGeneration = 0;

  const cancelMotion = () => {
    motionGeneration += 1;
    if (motionFrame !== 0) window.cancelAnimationFrame(motionFrame);
    motionFrame = 0;
    if (completionTimer !== 0) window.clearTimeout(completionTimer);
    completionTimer = 0;
    if (transitionHandler !== null) preview.removeEventListener('transitionend', transitionHandler);
    transitionHandler = null;
  };
  const stopPosition = () => {
    if (positionFrame !== 0) window.cancelAnimationFrame(positionFrame);
    positionFrame = 0;
  };
  const startPosition = () => {
    const update = () => {
      if (disposed || !showing || !connectedElement(source?.element) || !preview.isConnected) {
        positionFrame = 0;
        if (!disposed && !connectedElement(source?.element)) host?.requestHide(sourceId, generation);
        return;
      }
      position(source.element, preview);
      positionFrame = window.requestAnimationFrame(update);
    };
    if (positionFrame === 0) update();
  };
  const show = () => {
    if (disposed || !connectedElement(source?.element)) {
      host?.requestHide(sourceId, generation);
      return;
    }
    cancelMotion();
    const expectedGeneration = motionGeneration;
    showing = true;
    preview.hidden = false;
    preview.dataset.previewState = 'entering';
    preview.classList.remove('popup-leave-active', 'popup-leave-to');
    preview.classList.add('popup-enter-active', 'popup-enter-from');
    position(source.element, preview);
    startPosition();
    motionFrame = window.requestAnimationFrame(() => {
      motionFrame = window.requestAnimationFrame(() => {
        motionFrame = 0;
        if (disposed || !showing || expectedGeneration !== motionGeneration) return;
        preview.classList.remove('popup-enter-from');
        preview.dataset.previewState = 'shown';
        completionTimer = window.setTimeout(() => {
          completionTimer = 0;
          if (showing && expectedGeneration === motionGeneration) preview.classList.remove('popup-enter-active');
        }, maximumTransitionTime(preview) + 50);
      });
    });
  };
  const hide = () => {
    if (disposed || !showing) return;
    showing = false;
    stopPosition();
    cancelMotion();
    const expectedGeneration = motionGeneration;
    preview.classList.remove('popup-enter-active', 'popup-enter-from');
    preview.classList.add('popup-leave-active', 'popup-leave-to');
    preview.dataset.previewState = 'leaving';
    const expectedProperties = new Set(['opacity', 'transform']);
    const finish = () => {
      if (disposed || showing || expectedGeneration !== motionGeneration) return;
      cancelMotion();
      preview.hidden = true;
      preview.dataset.previewState = 'closed';
      void invoke(receiver, 'NotifyUserPreviewClosedAsync');
    };
    transitionHandler = event => {
      if (event.target !== preview) return;
      expectedProperties.delete(event.propertyName);
      if (expectedProperties.size === 0) finish();
    };
    preview.addEventListener('transitionend', transitionHandler);
    completionTimer = window.setTimeout(finish, maximumTransitionTime(preview) + 50);
  };
  const onMouseover = () => host?.previewEntered(sourceId, generation);
  const onMouseleave = () => host?.previewLeft(sourceId, generation);
  const onFocusin = () => host?.previewEntered(sourceId, generation);
  const onFocusout = event => {
    if (!(event.relatedTarget instanceof Node) || !preview.contains(event.relatedTarget)) {
      host?.previewLeft(sourceId, generation);
    }
  };
  const onKeydown = event => {
    if (event.key === 'Escape') {
      event.preventDefault();
      host?.requestHide(sourceId, generation);
    }
  };

  preview.style.zIndex = String(claimZIndex('middle'));
  preview.addEventListener('mouseover', onMouseover, { passive: true });
  preview.addEventListener('mouseleave', onMouseleave, { passive: true });
  preview.addEventListener('focusin', onFocusin, { passive: true });
  preview.addEventListener('focusout', onFocusout, { passive: true });
  preview.addEventListener('keydown', onKeydown);
  show();

  return {
    show,
    hide,
    dispose() {
      if (disposed) return;
      disposed = true;
      showing = false;
      stopPosition();
      cancelMotion();
      preview.removeEventListener('mouseover', onMouseover);
      preview.removeEventListener('mouseleave', onMouseleave);
      preview.removeEventListener('focusin', onFocusin);
      preview.removeEventListener('focusout', onFocusout);
      preview.removeEventListener('keydown', onKeydown);
    },
  };
}
