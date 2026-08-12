#!/usr/bin/env node

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const oracleRoot = join(repositoryRoot, 'frontend/misskey-v12');
const themeRoot = join(oracleRoot, 'src/themes');
const nodeModulesRoot = join(oracleRoot, 'node_modules');
const upstreamRoot = join(repositoryRoot, '.cache/upstream/misskey-12.119.2');
const outputPath = join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/wwwroot/themes/catalog.json');
const defaultCssOutputPath = join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/wwwroot/css/misskey-v12-default-theme.css');
const expectedCommit = 'a5a74f4434b179cdb1f97af98bf294c8b18de0e2';
const checkOnly = process.argv.includes('--check');

for (const path of [themeRoot, nodeModulesRoot, upstreamRoot]) {
  if (!existsSync(path)) throw new Error(`Required theme compiler input is missing: ${path}`);
}

const json5 = require(join(nodeModulesRoot, 'json5'));
const tinycolor = require(join(nodeModulesRoot, 'tinycolor2'));
const upstreamCommit = execFileSync('git', ['-C', upstreamRoot, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
if (upstreamCommit !== expectedCommit) {
  throw new Error(`Unexpected Misskey upstream commit: ${upstreamCommit}`);
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

const documents = readdirSync(themeRoot)
  .filter(name => name.endsWith('.json5'))
  .sort()
  .map(name => {
    const source = readFileSync(join(themeRoot, name), 'utf8');
    return { name, source, document: json5.parse(source) };
  });

const themesById = new Map(documents.map(entry => [entry.document.id, entry.document]));

function compileTheme(theme) {
  const base = theme.base === undefined ? null : themesById.get(theme.base);
  if (theme.base !== undefined && base === undefined) {
    throw new Error(`Theme ${theme.id} refers to missing base ${theme.base}.`);
  }

  const props = { ...(base?.props ?? {}), ...theme.props };
  const resolving = new Set();

  function resolveColor(value) {
    if (typeof value !== 'string' || value.length === 0) {
      throw new Error(`Theme ${theme.id} contains a non-string color value.`);
    }

    if (value.startsWith('@') || value.startsWith('$')) {
      const key = value.startsWith('@') ? value.slice(1) : value;
      if (!(key in props)) throw new Error(`Theme ${theme.id} refers to missing property ${key}.`);
      if (resolving.has(key)) throw new Error(`Theme ${theme.id} contains a property cycle at ${key}.`);
      resolving.add(key);
      const result = resolveColor(props[key]);
      resolving.delete(key);
      return result;
    }

    if (value.startsWith(':')) {
      const parts = value.split('<');
      const operation = parts.shift().slice(1);
      const argument = Number.parseFloat(parts.shift());
      if (!Number.isFinite(argument) || parts.length === 0) {
        throw new Error(`Theme ${theme.id} contains malformed operation ${value}.`);
      }

      const color = resolveColor(parts.join('<'));
      switch (operation) {
        case 'darken': return color.darken(argument);
        case 'lighten': return color.lighten(argument);
        case 'alpha': return color.setAlpha(argument);
        case 'hue': return color.spin(argument);
        case 'saturate': return color.saturate(argument);
        default: throw new Error(`Theme ${theme.id} contains unsupported operation ${operation}.`);
      }
    }

    const color = tinycolor(value);
    if (!color.isValid()) throw new Error(`Theme ${theme.id} contains invalid color ${value}.`);
    return color;
  }

  const compiled = {};
  for (const [name, value] of Object.entries(props)) {
    if (name.startsWith('$')) continue;
    compiled[name] = value.startsWith('"')
      ? value.replace(/^"\s*/, '')
      : resolveColor(value).toRgbString();
  }

  for (const surface of ['bg', 'panel']) {
    const color = tinycolor(compiled[surface]);
    if (!color.isValid() || color.getAlpha() !== 1) {
      throw new Error(`Theme ${theme.id} has a transparent or invalid ${surface} surface.`);
    }
  }

  return compiled;
}

const compiledThemes = documents.map(entry => ({
  entry,
  properties: compileTheme(entry.document)
}));

const catalog = {
  schemaVersion: 1,
  misskeyVersion: '12.119.2',
  upstreamCommit,
  themes: compiledThemes.map(({ entry, properties }) => ({
    id: entry.document.id,
    name: entry.document.name,
    author: entry.document.author ?? null,
    description: entry.document.desc ?? null,
    base: entry.document.base ?? entry.document.kind,
    selectable: !entry.name.startsWith('_'),
    sourceFile: entry.name,
    sourceSha256: sha256(entry.source),
    properties
  }))
};

const serialized = `${JSON.stringify(catalog, null, 2)}\n`;
const defaultLight = compiledThemes.find(({ entry }) => entry.name === 'l-light.json5');
const defaultDark = compiledThemes.find(({ entry }) => entry.name === 'd-green-lime.json5');
if (defaultLight === undefined || defaultDark === undefined) {
  throw new Error('Misskey 12.119.2 default light or dark theme is missing.');
}

function cssProperties(properties, indentation) {
  return Object.entries(properties)
    .map(([name, value]) => `${indentation}--${name}: ${value};`)
    .join('\n');
}

const defaultCss = `/* Generated from the pinned Misskey 12.119.2 ColdDeviceStorage defaults. */
:root {
  color-scheme: light;
${cssProperties(defaultLight.properties, '  ')}
}

@media (prefers-color-scheme: dark) {
  :root {
    color-scheme: dark;
${cssProperties(defaultDark.properties, '    ')}
  }
}
`;

if (checkOnly) {
  if (!existsSync(outputPath) || readFileSync(outputPath, 'utf8') !== serialized) {
    throw new Error('Blazor theme catalog is stale. Run node eng/generate-blazor-themes.mjs.');
  }
  if (!existsSync(defaultCssOutputPath) || readFileSync(defaultCssOutputPath, 'utf8') !== defaultCss) {
    throw new Error('Blazor default theme CSS is stale. Run node eng/generate-blazor-themes.mjs.');
  }
} else {
  mkdirSync(dirname(outputPath), { recursive: true });
  writeFileSync(outputPath, serialized);
  mkdirSync(dirname(defaultCssOutputPath), { recursive: true });
  writeFileSync(defaultCssOutputPath, defaultCss);
}

console.log(`Blazor theme catalog: ${catalog.themes.length} themes from Misskey ${catalog.misskeyVersion}.`);
