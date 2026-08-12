#!/usr/bin/env node

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const oracleRoot = join(repositoryRoot, 'frontend/misskey-v12');
const localesRoot = join(oracleRoot, 'locales');
const upstreamRoot = join(repositoryRoot, '.cache/upstream/misskey-12.119.2');
const nodeModulesRoot = join(oracleRoot, 'node_modules');
const outputPath = join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/wwwroot/data/locales/catalog.json');
const expectedCommit = 'a5a74f4434b179cdb1f97af98bf294c8b18de0e2';
const checkOnly = process.argv.includes('--check');

for (const path of [localesRoot, upstreamRoot, nodeModulesRoot]) {
  if (!existsSync(path)) throw new Error(`Required locale compiler input is missing: ${path}`);
}

const yaml = require(join(nodeModulesRoot, 'js-yaml'));
const generatedLocales = require(join(localesRoot, 'index.js'));
const upstreamCommit = execFileSync('git', ['-C', upstreamRoot, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
if (upstreamCommit !== expectedCommit) {
  throw new Error(`Unexpected Misskey upstream commit: ${upstreamCommit}`);
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

function pinnedSource(path) {
  return execFileSync('git', ['-C', upstreamRoot, 'show', `HEAD:${path}`]);
}

function assertPinned(path, localSource) {
  const upstreamSource = pinnedSource(path);
  if (!upstreamSource.equals(localSource)) {
    throw new Error(`Locale oracle ${path} differs from pinned Misskey ${expectedCommit}.`);
  }
}

const indexSource = readFileSync(join(localesRoot, 'index.js'));
assertPinned('locales/index.js', indexSource);

const supportedLocales = Object.keys(generatedLocales);
if (supportedLocales.length !== 25) {
  throw new Error(`Expected 25 locales from Misskey locales/index.js, found ${supportedLocales.length}.`);
}

const primaryLocales = { en: 'US', ja: 'JP', zh: 'CN' };
const rightToLeftLanguages = new Set(['ar', 'ug']);
const fallbackChain = locale => {
  if (locale === 'ja-JP') return ['ja-JP'];
  if (locale === 'ja-KS' || locale === 'en-US') return ['ja-JP', locale];
  const language = locale.split('-')[0];
  const primary = primaryLocales[language] === undefined ? null : `${language}-${primaryLocales[language]}`;
  return [...new Set(['ja-JP', 'en-US', primary, locale].filter(Boolean))];
};

function merge(...sources) {
  const result = {};
  for (const source of sources) {
    for (const [key, value] of Object.entries(source)) {
      result[key] = value && typeof value === 'object' && !Array.isArray(value) &&
        result[key] && typeof result[key] === 'object' && !Array.isArray(result[key])
        ? merge(result[key], value)
        : value;
    }
  }
  return result;
}

const rawLocales = {};
const sources = [];
for (const locale of supportedLocales) {
  const fileName = `${locale}.yml`;
  const source = readFileSync(join(localesRoot, fileName));
  assertPinned(`locales/${fileName}`, source);
  const cleanSource = source.toString('utf8').replaceAll(String.fromCodePoint(0x08), '');
  const document = yaml.load(cleanSource) ?? {};
  if (document === null || typeof document !== 'object' || Array.isArray(document)) {
    throw new Error(`Locale ${locale} did not parse as an object.`);
  }
  rawLocales[locale] = document;
  sources.push({ locale, fileName, sha256: sha256(source) });
}

const localeDefinitions = supportedLocales.map(locale => {
  const chain = fallbackChain(locale);
  const merged = merge(...chain.map(layer => rawLocales[layer]));
  if (JSON.stringify(merged) !== JSON.stringify(generatedLocales[locale])) {
    throw new Error(`Fallback merge for ${locale} differs from locales/index.js.`);
  }
  if (typeof merged._lang_ !== 'string' || typeof merged.showMore !== 'string') {
    throw new Error(`Locale ${locale} is missing its language name or showMore translation after fallback.`);
  }
  const direction = rightToLeftLanguages.has(locale.split('-')[0]) ? 'rtl' : 'ltr';
  return {
    locale,
    languageName: merged._lang_,
    direction,
    fallbackChain: chain
  };
});

const catalog = {
  schemaVersion: 1,
  misskeyVersion: '12.119.2',
  upstreamCommit,
  indexSha256: sha256(indexSource),
  sources,
  localeDefinitions,
  rawLocales
};
const serialized = `${JSON.stringify(catalog, null, 2)}\n`;

if (checkOnly) {
  if (!existsSync(outputPath) || readFileSync(outputPath, 'utf8') !== serialized) {
    throw new Error('Blazor locale catalog is stale. Run node eng/generate-blazor-locales.mjs.');
  }
} else {
  mkdirSync(dirname(outputPath), { recursive: true });
  writeFileSync(outputPath, serialized);
}

console.log(`Blazor locale catalog: ${supportedLocales.length} locales from Misskey 12.119.2.`);
