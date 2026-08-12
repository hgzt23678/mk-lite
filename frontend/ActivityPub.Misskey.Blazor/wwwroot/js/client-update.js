import { attach as attachDialogWindow } from './dialog-window.js';

const releaseNotesOrigin = 'https://misskey-hub.net';
const releaseNotesPath = '/docs/releases.html';

function validVersion(value) {
  return typeof value === 'string' && value.trim().length > 0 && value.length <= 128 &&
    ![...value].some(character => /[\u0000-\u001f\u007f]/u.test(character));
}

export function synchronizeVersion(currentVersion) {
  if (!validVersion(currentVersion)) throw new TypeError('The client version is invalid.');

  try {
    // Misskey 12.119.2 stores these two keys as raw localStorage strings rather than
    // the JSON envelope used by newer typed Blazor state. Keep that on-disk contract so
    // an existing Vue installation migrates without losing its update history.
    const previousVersion = window.localStorage.getItem('lastVersion');
    if (previousVersion === currentVersion) {
      return { previousVersion, changed: false, available: true, errorCode: null };
    }

    window.localStorage.setItem('lastVersion', currentVersion);
    window.localStorage.removeItem('theme');
    return { previousVersion, changed: true, available: true, errorCode: null };
  } catch (error) {
    // Browser privacy policy can explicitly deny storage. Convert only those defined
    // DOM storage failures into a safe capability result; module, syntax, and contract
    // failures must still reject the interop call and remain observable.
    const deniedStorageErrors = new Set([
      'SecurityError',
      'QuotaExceededError',
      'InvalidStateError',
      'NotSupportedError',
      'NS_ERROR_DOM_QUOTA_REACHED',
    ]);
    if (error instanceof DOMException && deniedStorageErrors.has(error.name)) {
      return {
        previousVersion: null,
        changed: false,
        available: false,
        errorCode: 'CLIENT_VERSION_STORAGE_UNAVAILABLE',
      };
    }

    throw error;
  }
}

export function attachDialog(modal, content, panel, receiver, releaseNotesUrl) {
  if (!(modal instanceof HTMLElement) || !(content instanceof HTMLElement) ||
      !(panel instanceof HTMLElement)) {
    throw new TypeError('MkUpdated requires its modal, content, and panel elements.');
  }

  const release = new URL(releaseNotesUrl);
  if (release.origin !== releaseNotesOrigin || release.pathname !== releaseNotesPath) {
    throw new TypeError('The release-notes URL is outside the pinned Misskey origin.');
  }

  const background = modal.querySelector(':scope > .bg');
  const releaseButton = panel.querySelector(':scope > button.bghgjjyj:not(.gotIt)');
  const acknowledgementButton = panel.querySelector(':scope > button.bghgjjyj.gotIt');
  if (!(background instanceof HTMLElement) || !(releaseButton instanceof HTMLButtonElement) ||
      !(acknowledgementButton instanceof HTMLButtonElement)) {
    throw new TypeError('MkUpdated no longer matches the pinned button and background DOM contract.');
  }

  const drawer = window.matchMedia('(max-width: 500px)').matches && navigator.maxTouchPoints > 0;
  const motionName = drawer ? 'modal-drawer' : 'modal';
  if (drawer) {
    modal.classList.remove('dialog', 'modal-enter-active', 'modal-enter-from');
    modal.classList.add('drawer', 'modal-drawer-enter-active', 'modal-drawer-enter-from');
  }

  const dialog = attachDialogWindow(modal, content, panel, receiver, 'middle', motionName);
  let disposed = false;
  let contentClicking = false;
  let releaseContentClickingTimer = 0;
  let pendingMouseUp = null;

  const close = () => dialog.close();
  const onBackgroundClick = event => {
    if (contentClicking) return;
    if (event.currentTarget === background || event.target === content) close();
  };
  const onContextMenu = event => {
    event.preventDefault();
    event.stopPropagation();
  };
  const onPanelMouseDown = () => {
    contentClicking = true;
    if (pendingMouseUp) window.removeEventListener('mouseup', pendingMouseUp);
    pendingMouseUp = () => {
      pendingMouseUp = null;
      if (releaseContentClickingTimer) window.clearTimeout(releaseContentClickingTimer);
      releaseContentClickingTimer = window.setTimeout(() => {
        contentClicking = false;
        releaseContentClickingTimer = 0;
      }, 100);
    };
    window.addEventListener('mouseup', pendingMouseUp, { passive: true, once: true });
  };
  const onReleaseNotesClick = () => {
    // Keep window.open in the native click task so browser popup protection does not
    // reject it while the server-side Blazor event roundtrip is in flight.
    close();
    const opened = window.open(release.href, '_blank', 'noopener,noreferrer');
    if (opened) opened.opener = null;
  };

  background.addEventListener('click', onBackgroundClick);
  background.addEventListener('contextmenu', onContextMenu);
  content.addEventListener('click', onBackgroundClick);
  panel.addEventListener('mousedown', onPanelMouseDown, { passive: true });
  releaseButton.addEventListener('click', onReleaseNotesClick);
  acknowledgementButton.addEventListener('click', close);

  return {
    close,
    dispose() {
      if (disposed) return;
      disposed = true;
      if (releaseContentClickingTimer) window.clearTimeout(releaseContentClickingTimer);
      if (pendingMouseUp) window.removeEventListener('mouseup', pendingMouseUp);
      background.removeEventListener('click', onBackgroundClick);
      background.removeEventListener('contextmenu', onContextMenu);
      content.removeEventListener('click', onBackgroundClick);
      panel.removeEventListener('mousedown', onPanelMouseDown);
      releaseButton.removeEventListener('click', onReleaseNotesClick);
      acknowledgementButton.removeEventListener('click', close);
      dialog.dispose();
    },
  };
}
