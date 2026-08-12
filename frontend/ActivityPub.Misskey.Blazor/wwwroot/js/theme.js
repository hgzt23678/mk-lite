const allowedColorProperties = new Set([
  'X10',
  'X11',
  'X12',
  'X13',
  'X14',
  'X15',
  'X16',
  'X17',
  'X2',
  'X3',
  'X4',
  'X5',
  'X6',
  'X7',
  'X8',
  'X9',
  'accent',
  'accentDarken',
  'accentLighten',
  'accentedBg',
  'acrylicBg',
  'acrylicPanel',
  'badge',
  'bg',
  'buttonBg',
  'buttonGradateA',
  'buttonGradateB',
  'buttonHoverBg',
  'codeBoolean',
  'codeNumber',
  'codeString',
  'cwBg',
  'cwFg',
  'cwHoverBg',
  'dateLabelFg',
  'deckDivider',
  'divider',
  'driveFolderBg',
  'error',
  'fg',
  'fgHighlighted',
  'fgOnAccent',
  'fgTransparent',
  'fgTransparentWeak',
  'focus',
  'hashtag',
  'header',
  'htmlThemeColor',
  'indicator',
  'infoBg',
  'infoFg',
  'infoWarnBg',
  'infoWarnFg',
  'inputBorder',
  'inputBorderHover',
  'link',
  'listItemHoverBg',
  'mention',
  'mentionMe',
  'messageBg',
  'modalBg',
  'navActive',
  'navBg',
  'navFg',
  'navHoverFg',
  'navIndicator',
  'panel',
  'panelHeaderBg',
  'panelHeaderDivider',
  'panelHeaderFg',
  'panelHighlight',
  'popup',
  'renote',
  'scrollbarHandle',
  'scrollbarHandleHover',
  'shadow',
  'success',
  'switchBg',
  'swutchOffBg',
  'swutchOffFg',
  'swutchOnBg',
  'swutchOnFg',
  'wallpaperOverlay',
  'warn',
  'windowHeader'
]);

const allowedRawProperties = new Set(['panelBorder']);
const opaqueSurfaceProperties = new Set(['bg', 'htmlThemeColor', 'panel', 'popup']);

function colorAlpha(value) {
  if (typeof value !== 'string' || value.length === 0 || value.length > 128) return null;
  const canvas = document.createElement('canvas');
  canvas.width = 1;
  canvas.height = 1;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (context === null) return null;
  context.clearRect(0, 0, 1, 1);
  context.fillStyle = '#010203';
  const sentinel = context.fillStyle;
  context.fillStyle = value;
  if (context.fillStyle === sentinel && value.toLowerCase() !== '#010203' && value.toLowerCase() !== 'rgb(1, 2, 3)') {
    return null;
  }
  context.fillRect(0, 0, 1, 1);
  return context.getImageData(0, 0, 1, 1).data[3];
}

function validatedTheme(theme) {
  if (theme === null || typeof theme !== 'object' || Array.isArray(theme)) return null;
  const result = {};
  for (const [name, value] of Object.entries(theme)) {
    if (allowedRawProperties.has(name)) {
      if (value !== 'solid 1px var(--divider)') return null;
      result[name] = value;
      continue;
    }
    if (!allowedColorProperties.has(name)) continue;
    const alpha = colorAlpha(value);
    if (alpha === null || (opaqueSurfaceProperties.has(name) && alpha !== 255)) return null;
    result[name] = value;
  }
  return typeof result.bg === 'string' && typeof result.panel === 'string' && typeof result.popup === 'string'
    ? result
    : null;
}

export function applyTheme(theme, colorScheme, persist, themeId = null) {
  const validated = validatedTheme(theme);
  if (validated === null) return false;
  const scheme = colorScheme === 'dark' ? 'dark' : 'light';
  for (const name of allowedColorProperties) document.documentElement.style.removeProperty(`--${name}`);
  for (const name of allowedRawProperties) document.documentElement.style.removeProperty(`--${name}`);
  for (const [name, value] of Object.entries(validated)) {
    document.documentElement.style.setProperty(`--${name}`, value);
  }
  document.documentElement.dataset.theme = scheme;
  if (typeof themeId === 'string' && themeId.length > 0 && themeId.length <= 128) {
    document.documentElement.dataset.themeId = themeId;
  } else {
    document.documentElement.removeAttribute('data-theme-id');
  }
  document.documentElement.style.colorScheme = scheme;
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta !== null) meta.setAttribute('content', validated.htmlThemeColor ?? validated.bg);
  if (persist) {
    localStorage.setItem('theme', JSON.stringify(validated));
    localStorage.setItem('colorSchema', scheme);
    if (typeof themeId === 'string' && themeId.length > 0 && themeId.length <= 128) {
      localStorage.setItem('themeId', themeId);
    } else {
      localStorage.removeItem('themeId');
    }
  }
  document.documentElement.dataset.themeBootstrap = 'applied';
  window.dispatchEvent(new CustomEvent('misskey:theme-changed'));
  return true;
}

export function clearTheme() {
  for (const name of allowedColorProperties) document.documentElement.style.removeProperty(`--${name}`);
  for (const name of allowedRawProperties) document.documentElement.style.removeProperty(`--${name}`);
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.removeAttribute('data-theme-id');
  document.documentElement.style.removeProperty('color-scheme');
  localStorage.removeItem('theme');
  localStorage.removeItem('colorSchema');
  localStorage.removeItem('themeId');
  document.documentElement.dataset.themeBootstrap = 'cleared';
  window.dispatchEvent(new CustomEvent('misskey:theme-changed'));
}

try {
  const stored = localStorage.getItem('theme');
  if (stored !== null) {
    const parsed = JSON.parse(stored);
    if (!applyTheme(parsed, localStorage.getItem('colorSchema'), false, localStorage.getItem('themeId'))) {
      localStorage.removeItem('theme');
      localStorage.removeItem('colorSchema');
      localStorage.removeItem('themeId');
      document.documentElement.dataset.themeBootstrap = 'rejected';
    }
  }
} catch {
  localStorage.removeItem('theme');
  localStorage.removeItem('colorSchema');
  localStorage.removeItem('themeId');
  document.documentElement.dataset.themeBootstrap = 'rejected';
}
