import katex from '../frontend/misskey-v12/node_modules/katex/dist/katex.mjs';

export function renderFormula(element, formula) {
  if (!(element instanceof HTMLElement) || typeof formula !== 'string') {
    throw new Error('MISSKEY_FORMULA_CONFIGURATION_INVALID');
  }

  katex.render(formula, element, {
    throwOnError: false,
    trust: false,
  });
}
