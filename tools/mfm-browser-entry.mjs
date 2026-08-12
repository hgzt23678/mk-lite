import * as mfm from '../frontend/misskey-v12/node_modules/mfm-js/built/index.js';

const functionNames = [
  'tada', 'jelly', 'twitch', 'shake', 'spin', 'jump', 'bounce', 'flip',
  'x2', 'x3', 'x4', 'font', 'blur', 'rainbow', 'sparkle', 'rotate',
];

export function parse(text, simple = false) {
  if (typeof text !== 'string' || text.length > 100_000) {
    throw new TypeError('MFM input must be a string no longer than 100000 characters.');
  }

  const ast = simple
    ? mfm.parseSimple(text)
    : mfm.parse(text, { fnNameList: functionNames });
  return JSON.stringify(ast);
}
