const localeCookieName = 'misskey.lang';
const legacyLocaleStorageKey = 'locale';
const localeStorageKey = 'lang';

function supportedLocaleMap(definitions) {
  const result = new Map();
  if (!Array.isArray(definitions)) return result;
  for (const definition of definitions) {
    if (definition === null || typeof definition !== 'object') continue;
    const locale = definition.locale;
    const direction = definition.direction;
    if (typeof locale !== 'string' || locale.length === 0 || locale.length > 35) continue;
    if (direction !== 'ltr' && direction !== 'rtl') continue;
    result.set(locale.toLowerCase(), { locale, direction });
  }
  return result;
}

function canonicalLocale(candidate, supported) {
  if (typeof candidate !== 'string' || candidate.length === 0 || candidate.length > 35) return null;
  return supported.get(candidate.toLowerCase()) ?? null;
}

function writeLocaleCookie(locale) {
  const secure = window.location.protocol === 'https:' ? '; Secure' : '';
  document.cookie = `${localeCookieName}=${encodeURIComponent(locale)}; Path=/; Max-Age=31536000; SameSite=Lax${secure}`;
}

function applyLocale(locale, direction, persist) {
  document.documentElement.lang = locale;
  document.documentElement.dir = direction;
  document.documentElement.dataset.locale = locale;
  if (persist) localStorage.setItem(localeStorageKey, locale);
  writeLocaleCookie(locale);
}

export function attachLocale(definitions, currentLocale, currentDirection, receiver) {
  const supported = supportedLocaleMap(definitions);
  const current = canonicalLocale(currentLocale, supported);
  if (current === null || current.direction !== currentDirection) {
    throw new Error('The server supplied an unsupported locale state.');
  }

  let disposed = false;
  let active = current;
  const selectStored = candidate => {
    const selected = canonicalLocale(candidate, supported);
    if (selected === null) return false;
    active = selected;
    applyLocale(selected.locale, selected.direction, true);
    void receiver.invokeMethodAsync('SelectStoredLocaleAsync', selected.locale);
    return true;
  };
  const storageListener = event => {
    if (disposed || event.storageArea !== localStorage || event.key !== localeStorageKey) return;
    if (!selectStored(event.newValue)) applyLocale(active.locale, active.direction, false);
  };

  // The old Vue `locale` JSON is deliberately neither parsed nor removed. It remains migration
  // compatible data, but only the build-time pinned catalog is a translation source.
  void legacyLocaleStorageKey;
  window.addEventListener('storage', storageListener);
  const stored = localStorage.getItem(localeStorageKey);
  if (!selectStored(stored)) applyLocale(current.locale, current.direction, false);

  return {
    applyLocale(locale, direction) {
      if (disposed) return;
      const selected = canonicalLocale(locale, supported);
      if (selected === null || selected.direction !== direction) return;
      active = selected;
      applyLocale(selected.locale, selected.direction, true);
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      window.removeEventListener('storage', storageListener);
    }
  };
}
