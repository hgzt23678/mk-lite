const styleId = 'misskey-v12-custom-css';
const storageKey = 'customCss';

export function readStored() {
  return localStorage.getItem(storageKey);
}

export function writeStored(css) {
  if (typeof css !== 'string' || css.length > 100000) {
    throw new TypeError('Custom CSS is outside the permitted size.');
  }
  localStorage.setItem(storageKey, css);
}

export function apply(css) {
  if (typeof css !== 'string' || css.length > 100000) {
    throw new TypeError('Custom CSS is outside the permitted size.');
  }

  let style = document.getElementById(styleId);
  if (style !== null && style.tagName !== 'STYLE') {
    style.remove();
    style = null;
  }

  if (css.length === 0) {
    style?.remove();
    return;
  }

  if (style === null) {
    style = document.createElement('style');
    style.id = styleId;
    style.dataset.source = 'misskey-v12-custom-css';
    document.head.appendChild(style);
  }

  // textContent deliberately avoids HTML parsing; the server-side validator
  // rejects remote imports, URL fetches, and markup before this boundary.
  style.textContent = css;
}
