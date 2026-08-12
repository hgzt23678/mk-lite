function parseTarget(value) {
  if (typeof value !== 'string') throw new Error('MISSKEY_MODAL_PAGE_URL_INVALID');
  const target = new URL(value);
  if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password) {
    throw new Error('MISSKEY_MODAL_PAGE_URL_INVALID');
  }
  return target;
}

export function openNewTab(value) {
  const target = parseTarget(value);
  return window.open(target.href, '_blank', 'noopener,noreferrer') !== null;
}

export function popout(value, root) {
  const target = parseTarget(value);
  if (!(root instanceof HTMLElement)) throw new Error('MISSKEY_MODAL_PAGE_ROOT_INVALID');
  target.searchParams.set('zen', '');
  const position = root.getBoundingClientRect();
  const style = getComputedStyle(root);
  const width = Math.max(1, Number.parseInt(style.width, 10) || Math.round(position.width));
  const height = Math.max(1, Number.parseInt(style.height, 10) || Math.round(position.height));
  const left = Math.round(window.screenX + position.left);
  const top = Math.round(window.screenY + position.top);
  const features = `popup,noopener,width=${width},height=${height},top=${top},left=${left}`;
  return window.open(target.href, target.href, features) !== null;
}
