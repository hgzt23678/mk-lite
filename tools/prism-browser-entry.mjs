import Prism from '../frontend/misskey-v12/node_modules/prismjs/prism.js';

function appendToken(parent, token) {
  if (typeof token === 'string') {
    parent.append(document.createTextNode(token));
    return;
  }

  const span = document.createElement('span');
  span.classList.add('token', token.type);
  const aliases = Array.isArray(token.alias) ? token.alias : token.alias ? [token.alias] : [];
  for (const alias of aliases) span.classList.add(alias);
  const content = Array.isArray(token.content) ? token.content : [token.content];
  for (const child of content) appendToken(span, child);
  parent.append(span);
}

function replaceLanguageClass(element, language) {
  for (const cssClass of [...element.classList]) {
    if (cssClass.startsWith('language-')) element.classList.remove(cssClass);
  }
  element.classList.add(`language-${language}`);
}

export function highlight(element, code, requestedLanguage) {
  if (!(element instanceof HTMLElement) || typeof code !== 'string') {
    throw new Error('MISSKEY_CODE_HIGHLIGHT_CONFIGURATION_INVALID');
  }

  const requested = typeof requestedLanguage === 'string' ? requestedLanguage : '';
  const language = Prism.languages[requested] ? requested : 'js';
  const fragment = document.createDocumentFragment();
  for (const token of Prism.tokenize(code, Prism.languages[language])) appendToken(fragment, token);
  element.replaceChildren(fragment);
  replaceLanguageClass(element, language);
  if (element.parentElement?.tagName === 'PRE') replaceLanguageClass(element.parentElement, language);
  element.dataset.prismLanguage = language;
  return language;
}
