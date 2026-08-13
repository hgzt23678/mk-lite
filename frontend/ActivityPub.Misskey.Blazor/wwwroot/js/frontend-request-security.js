let antiforgeryToken = null;
let requiresAntiforgeryHeader = false;

export function requireAntiforgeryHeader() {
  requiresAntiforgeryHeader = true;
}

export function replaceAntiforgeryToken(value) {
  if (typeof value !== 'string' || value.length === 0 || value.length > 2048 || /[\u0000-\u001f\u007f]/.test(value)) {
    antiforgeryToken = null;
    return;
  }
  antiforgeryToken = value;
}

export function clearAntiforgeryToken() {
  antiforgeryToken = null;
}

export function frontendRequestHeaders(target, unsafe) {
  const url = new URL(target, window.location.href);
  if (url.origin !== window.location.origin || url.username || url.password) {
    throw new Error('FRONTEND_REQUEST_ORIGIN');
  }
  const headers = {
    Accept: 'application/json',
    'X-ActivityPub-Frontend': '1',
  };
  if (unsafe) {
    if (!antiforgeryToken && requiresAntiforgeryHeader) throw new Error('FRONTEND_CSRF_NOT_INITIALIZED');
    if (antiforgeryToken) headers['X-CSRF-TOKEN'] = antiforgeryToken;
  }
  return headers;
}
