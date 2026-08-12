const allowedKeys = new Set(['fontSize', 'useSystemFont']);

function validateKey(key) {
  if (typeof key !== 'string' || !allowedKeys.has(key)) {
    throw new TypeError('The general settings key is not allowed.');
  }
}

export function readRaw(key) {
  validateKey(key);
  return window.localStorage.getItem(key);
}

export function writeRaw(key, value) {
  validateKey(key);
  if (typeof value !== 'string' || value.length > 16 || /[\u0000-\u001f\u007f]/u.test(value)) {
    throw new TypeError('The general setting value is invalid.');
  }
  window.localStorage.setItem(key, value);
}

export function remove(key) {
  validateKey(key);
  window.localStorage.removeItem(key);
}

export function applySystemFont(enabled) {
  document.documentElement.classList.toggle('useSystemFont', enabled === true);
}
