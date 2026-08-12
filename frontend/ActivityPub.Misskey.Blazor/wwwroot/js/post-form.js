export function observeSize(root, receiver) {
  let disposed = false;
  let frame = 0;
  const textarea = root.querySelector('textarea[data-cy-post-form-text]');
  const onCompositionUpdate = event => receiver.invokeMethodAsync('UpdateCompositionText', event.data ?? '');
  const onCompositionEnd = () => receiver.invokeMethodAsync('UpdateCompositionText', '');
  const publish = width => {
    if (frame) cancelAnimationFrame(frame);
    frame = requestAnimationFrame(() => {
      frame = 0;
      if (!disposed) receiver.invokeMethodAsync('UpdatePostFormWidth', width);
    });
  };
  const observer = new ResizeObserver(entries => publish(entries[0]?.contentRect.width ?? root.clientWidth));
  observer.observe(root);
  textarea?.addEventListener('compositionupdate', onCompositionUpdate);
  textarea?.addEventListener('compositionend', onCompositionEnd);
  publish(root.clientWidth);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      observer.disconnect();
      textarea?.removeEventListener('compositionupdate', onCompositionUpdate);
      textarea?.removeEventListener('compositionend', onCompositionEnd);
      if (frame) cancelAnimationFrame(frame);
    },
  };
}

export function openFiles(input) {
  if (!(input instanceof HTMLInputElement) || input.type !== 'file') throw new TypeError('A file input is required.');
  input.click();
}

export function attachDropTarget(root, input) {
  if (!(root instanceof HTMLElement)) throw new TypeError('A post form root is required.');
  if (!(input instanceof HTMLInputElement) || input.type !== 'file') throw new TypeError('A file input is required.');
  let disposed = false;
  let dragDepth = 0;
  const supportsFiles = event => [...(event.dataTransfer?.items ?? [])].some(item => item.kind === 'file');
  const onDragEnter = event => {
    if (!supportsFiles(event)) return;
    event.preventDefault();
    dragDepth += 1;
    root.classList.add('draghover');
  };
  const onDragOver = event => {
    if (!supportsFiles(event)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  };
  const onDragLeave = event => {
    if (!supportsFiles(event)) return;
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) root.classList.remove('draghover');
  };
  const onDrop = event => {
    if (!event.dataTransfer?.files?.length) return;
    event.preventDefault();
    dragDepth = 0;
    root.classList.remove('draghover');
    const transfer = new DataTransfer();
    for (const file of event.dataTransfer.files) transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const onPaste = event => {
    const pastedFiles = [...(event.clipboardData?.items ?? [])]
      .filter(item => item.kind === 'file')
      .map(item => item.getAsFile())
      .filter(file => file instanceof File);
    if (pastedFiles.length === 0) return;
    const transfer = new DataTransfer();
    for (const file of pastedFiles) transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };
  root.addEventListener('dragenter', onDragEnter);
  root.addEventListener('dragover', onDragOver);
  root.addEventListener('dragleave', onDragLeave);
  root.addEventListener('drop', onDrop);
  root.addEventListener('paste', onPaste);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      root.classList.remove('draghover');
      root.removeEventListener('dragenter', onDragEnter);
      root.removeEventListener('dragover', onDragOver);
      root.removeEventListener('dragleave', onDragLeave);
      root.removeEventListener('drop', onDrop);
      root.removeEventListener('paste', onPaste);
    },
  };
}

export function createPreviewUrls(input) {
  if (!(input instanceof HTMLInputElement) || input.type !== 'file') throw new TypeError('A file input is required.');
  return [...(input.files ?? [])].map(file => URL.createObjectURL(file));
}

export function revokePreviewUrls(urls) {
  for (const url of urls) {
    if (typeof url === 'string' && url.startsWith('blob:')) URL.revokeObjectURL(url);
  }
}

export function insertText(textarea, value) {
  if (!(textarea instanceof HTMLTextAreaElement) || typeof value !== 'string') throw new TypeError('A textarea and text are required.');
  const start = textarea.selectionStart ?? textarea.value.length;
  const end = textarea.selectionEnd ?? start;
  textarea.setRangeText(value, start, end, 'end');
  textarea.dispatchEvent(new Event('input', { bubbles: true }));
  textarea.focus();
}

export function focus(textarea) {
  if (!(textarea instanceof HTMLTextAreaElement)) throw new TypeError('A textarea is required.');
  textarea.focus();
  const end = textarea.value.length;
  textarea.setSelectionRange(end, end);
}


function caretCoordinatesForAutocomplete(textarea, position) {
  const value = textarea.value ?? '';
  const mirror = document.createElement('div');
  const computed = window.getComputedStyle(textarea);
  const properties = [
    'fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'letterSpacing', 'lineHeight',
    'textTransform', 'wordSpacing', 'textIndent', 'paddingTop', 'paddingRight',
    'paddingBottom', 'paddingLeft', 'borderTopWidth', 'borderRightWidth',
    'borderBottomWidth', 'borderLeftWidth', 'boxSizing', 'whiteSpace', 'wordWrap',
    'overflowWrap', 'tabSize',
  ];
  for (const property of properties) mirror.style[property] = computed[property];
  mirror.style.position = 'absolute';
  mirror.style.visibility = 'hidden';
  mirror.style.whiteSpace = 'pre-wrap';
  mirror.style.wordWrap = 'break-word';
  mirror.style.overflow = 'hidden';
  mirror.style.width = textarea.clientWidth + 'px';
  mirror.textContent = value.slice(0, position);
  const span = document.createElement('span');
  span.textContent = value.slice(position) || '.';
  mirror.appendChild(span);
  document.body.appendChild(mirror);
  const top = span.offsetTop;
  const left = span.offsetLeft;
  document.body.removeChild(mirror);
  return { top, left };
}

export function getAutocompleteContext(textarea) {
  if (!(textarea instanceof HTMLTextAreaElement)) throw new TypeError('A textarea is required.');
  const caretStart = textarea.selectionStart ?? textarea.value.length;
  const line = (textarea.value.slice(0, caretStart).split('\n').pop()) ?? '';
  const coords = caretCoordinatesForAutocomplete(textarea, caretStart);
  const rect = textarea.getBoundingClientRect();
  return {
    line,
    caretStart,
    x: rect.left + coords.left - textarea.scrollLeft,
    y: rect.top + coords.top - textarea.scrollTop,
  };
}

export function completeAutocomplete(textarea, start, endOffset, replacement) {
  if (!(textarea instanceof HTMLTextAreaElement) || typeof replacement !== 'string') {
    throw new TypeError('A textarea and replacement are required.');
  }
  textarea.setRangeText(replacement, start, endOffset, 'end');
  textarea.dispatchEvent(new Event('input', { bubbles: true }));
  textarea.focus();
  const position = start + replacement.length;
  textarea.setSelectionRange(position, position);
}
