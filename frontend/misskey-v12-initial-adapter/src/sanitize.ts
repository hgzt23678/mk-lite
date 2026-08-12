import DOMPurify from 'dompurify';

const allowedTags = [
  'a', 'abbr', 'b', 'blockquote', 'br', 'code', 'del', 'em', 'i', 'li',
  'ol', 'p', 'pre', 's', 'span', 'strong', 'sub', 'sup', 'u', 'ul',
];

export function sanitizeStatusHtml(value: string): string {
  const fragment = DOMPurify.sanitize(value, {
    ALLOWED_TAGS: allowedTags,
    ALLOWED_ATTR: ['class', 'href', 'lang', 'title'],
    FORBID_ATTR: ['style'],
    RETURN_DOM_FRAGMENT: true,
  });
  for (const link of fragment.querySelectorAll('a')) {
    const raw = link.getAttribute('href');
    if (!raw) continue;
    try {
      const target = new URL(raw, window.location.origin);
      if (target.protocol !== 'https:' && target.protocol !== 'http:') {
        link.removeAttribute('href');
        continue;
      }
      link.setAttribute('target', '_blank');
      link.setAttribute('rel', 'nofollow noopener noreferrer');
    } catch {
      link.removeAttribute('href');
    }
  }
  const container = document.createElement('div');
  container.append(fragment);
  return container.innerHTML;
}

export function safeMediaUrl(value: string): string | null {
  try {
    const url = new URL(value, window.location.origin);
    return url.origin === window.location.origin && url.pathname.startsWith('/media/') ? url.href : null;
  } catch {
    return null;
  }
}
