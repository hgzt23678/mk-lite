function caretCoordinates(textarea, position) {
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
  return { top, left, height: parseInt(computed.lineHeight, 10) || 16 };
}

export function getAutocompleteContext(textarea) {
  if (!(textarea instanceof HTMLTextAreaElement)) throw new TypeError('A textarea is required.');
  const caretPos = textarea.selectionStart ?? textarea.value.length;
  const line = (textarea.value.slice(0, caretPos).split('\n').pop()) ?? '';
  const coords = caretCoordinates(textarea, caretPos);
  const rect = textarea.getBoundingClientRect();
  const x = rect.left + coords.left - textarea.scrollLeft;
  const y = rect.top + coords.top - textarea.scrollTop;
  return { line, x, y, caretStart: caretPos };
}

export function completeAutocomplete(textarea, start, end, replacement) {
  if (!(textarea instanceof HTMLTextAreaElement) || typeof replacement !== 'string') {
    throw new TypeError('A textarea and replacement are required.');
  }
  textarea.setRangeText(replacement, start, end, 'end');
  textarea.dispatchEvent(new Event('input', { bubbles: true }));
  textarea.focus();
  const position = start + replacement.length;
  textarea.setSelectionRange(position, position);
}

export function attachAutocomplete(textarea, root, receiver) {
  if (!(textarea instanceof HTMLTextAreaElement)) throw new TypeError('A textarea is required.');
  if (!(root instanceof HTMLElement)) throw new TypeError('A root element is required.');
  let disposed = false;

  const onKeydown = event => {
    if (disposed) return;
    const handled = ['Enter', 'Escape', 'ArrowUp', 'ArrowDown', 'Tab'].includes(event.key);
    if (!handled) {
      event.stopPropagation();
      textarea.focus();
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    receiver.invokeMethodAsync('HandleAutocompleteKey', event.key).catch(() => {});
  };

  const onMousedown = event => {
    if (disposed) return;
    if (root.contains(event.target) || root === event.target) return;
    receiver.invokeMethodAsync('HandleAutocompleteOutsideMousedown').catch(() => {});
  };

  textarea.addEventListener('keydown', onKeydown);
  document.addEventListener('mousedown', onMousedown);
  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      textarea.removeEventListener('keydown', onKeydown);
      document.removeEventListener('mousedown', onMousedown);
    },
  };
}


export function focusAutocompleteItem(list, index) {
  if (!(list instanceof HTMLElement)) return;
  const items = list.children;
  for (const child of Array.from(items)) child.removeAttribute('data-selected');
  if (index >= 0 && index < items.length) {
    const item = items[index];
    item.setAttribute('data-selected', 'true');
    item.focus();
  }
}

export function disposeAutocomplete(attachment) {
  if (!attachment || typeof attachment.dispose !== 'function') return;
  attachment.dispose();
}
