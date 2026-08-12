export function attach(input, button) {
  if (!(input instanceof HTMLInputElement) || !(button instanceof HTMLButtonElement)) {
    throw new TypeError('MISSKEY_GOOGLE_SEARCH_DOM_INVALID');
  }

  let disposed = false;
  const search = event => {
    event.preventDefault();
    if (disposed) return;
    const target = new URL('https://www.google.com/search');
    target.searchParams.set('q', input.value);
    const opened = window.open(target.href, '_blank', 'noopener,noreferrer');
    if (opened !== null) opened.opener = null;
  };
  button.addEventListener('click', search);

  return {
    dispose() {
      if (disposed) return;
      disposed = true;
      button.removeEventListener('click', search);
    },
  };
}
