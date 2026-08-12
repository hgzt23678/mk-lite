function legacyWriteText(value) {
  const container = document.createElement('div');
  const content = document.createElement('pre');
  content.style.webkitUserSelect = 'auto';
  content.style.userSelect = 'auto';
  content.textContent = value;
  container.append(content);
  container.style.position = 'fixed';
  container.style.right = '200%';
  container.setAttribute('aria-hidden', 'true');
  document.body.append(container);

  try {
    const selection = document.getSelection();
    if (selection === null) {
      return false;
    }
    selection.selectAllChildren(container);
    return document.execCommand('copy') === true;
  } catch {
    return false;
  } finally {
    document.getSelection()?.removeAllRanges();
    container.remove();
  }
}

export async function writeText(value) {
  if (typeof value !== 'string') {
    return { succeeded: false, method: 'none', errorCode: 'INVALID_VALUE' };
  }

  if (globalThis.isSecureContext && typeof navigator.clipboard?.writeText === 'function') {
    try {
      await navigator.clipboard.writeText(value);
      return { succeeded: true, method: 'async-clipboard', errorCode: null };
    } catch {
      // Misskey 12.119.2 uses execCommand. Keep that browser-compatible path as a fallback when
      // the asynchronous clipboard API is unavailable or denied for the current document.
    }
  }

  if (typeof document.execCommand === 'function' && legacyWriteText(value)) {
    return { succeeded: true, method: 'exec-command', errorCode: null };
  }

  return { succeeded: false, method: 'none', errorCode: 'CLIPBOARD_WRITE_FAILED' };
}
