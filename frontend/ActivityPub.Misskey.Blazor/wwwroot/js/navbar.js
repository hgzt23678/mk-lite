export function submit(form) {
  if (!(form instanceof HTMLFormElement) || form.method.toLowerCase() !== 'post') {
    throw new TypeError('NAVBAR_LOGOUT_FORM_INVALID');
  }
  form.requestSubmit();
}
