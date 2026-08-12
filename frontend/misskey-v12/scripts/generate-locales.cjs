'use strict';

const fs = require('node:fs');
const path = require('node:path');
const locales = require('../locales/index.js');
const meta = require('../package.json');

const output = path.resolve(__dirname, '../public/assets/locales');
fs.mkdirSync(output, { recursive: true });
for (const [language, locale] of Object.entries(locales)) {
  const body = JSON.stringify({ ...locale, _version_: meta.version });
  fs.writeFileSync(path.join(output, `${language}.${meta.version}.json`), body, { encoding: 'utf8', mode: 0o644 });
}
