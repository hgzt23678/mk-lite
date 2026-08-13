import { frontendRequestHeaders } from './frontend-request-security.js';

export async function submit(form) {
  if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== 'post') {
    throw new TypeError('NAVBAR_LOGOUT_FORM_INVALID');
  }
  const response = await fetch(form.action, {
    method: 'POST',
    body: new FormData(form),
    credentials: 'same-origin',
    cache: 'no-store',
    redirect: 'follow',
    headers: frontendRequestHeaders(form.action, true),
  });
  if (!response.ok) throw new Error('NAVBAR_LOGOUT_FAILED');
  const destination = new URL(response.url || '/app/', window.location.href);
  if (destination.origin !== window.location.origin) throw new Error('NAVBAR_LOGOUT_REDIRECT_ORIGIN');
  window.location.assign(destination.href);
}
