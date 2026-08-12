#!/usr/bin/env node

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const upstreamRoot = join(repositoryRoot, '.cache/upstream/misskey-12.119.2');
const sourcePath = join(upstreamRoot, 'packages/client/src/emojilist.json');
const twemojiRoot = join(repositoryRoot, 'frontend/misskey-v12/node_modules/@discordapp/twemoji/dist/svg');
const twemojiPackagePath = join(repositoryRoot, 'frontend/misskey-v12/node_modules/@discordapp/twemoji/package.json');
const outputDataPath = join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/wwwroot/data/emojilist.json');
const outputManifestPath = join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/wwwroot/data/emoji-assets-manifest.json');
const outputTwemojiRoot = join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/wwwroot/twemoji');
const expectedCommit = 'a5a74f4434b179cdb1f97af98bf294c8b18de0e2';
const checkOnly = process.argv.includes('--check');

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

function fileNameForEmoji(value) {
  let codePoints = Array.from(value, character => character.codePointAt(0).toString(16));
  if (!codePoints.includes('200d')) codePoints = codePoints.filter(code => code !== 'fe0f');
  return `${codePoints.join('-')}.svg`;
}

const upstreamCommit = execFileSync('git', ['-C', upstreamRoot, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
if (upstreamCommit !== expectedCommit) throw new Error(`Unexpected Misskey upstream commit: ${upstreamCommit}`);
if (!existsSync(sourcePath) || !existsSync(twemojiRoot) || !existsSync(twemojiPackagePath)) {
  throw new Error('Pinned emoji data or Twemoji package is unavailable. Run npm ci for the Vue oracle first.');
}

const source = readFileSync(sourcePath);
const definitions = JSON.parse(source.toString('utf8'));
if (!Array.isArray(definitions) || definitions.length !== 1782) {
  throw new Error(`Expected 1782 Misskey 12.119.2 emoji definitions, found ${definitions.length}.`);
}

const assetNames = [...new Set(definitions.map(definition => fileNameForEmoji(definition.char)))].sort();
const assets = assetNames.map(name => {
  const path = join(twemojiRoot, name);
  if (!existsSync(path)) throw new Error(`Twemoji asset ${name} is missing.`);
  return { file: name, sha256: sha256(readFileSync(path)) };
});
const twemojiPackage = JSON.parse(readFileSync(twemojiPackagePath, 'utf8'));
if (twemojiPackage.version !== '14.0.2') {
  throw new Error(`Unexpected Twemoji version: ${twemojiPackage.version}`);
}

const manifest = `${JSON.stringify({
  schemaVersion: 1,
  misskeyVersion: '12.119.2',
  upstreamCommit,
  sourceSha256: sha256(source),
  definitionCount: definitions.length,
  twemojiVersion: twemojiPackage.version,
  assetCount: assets.length,
  assets
}, null, 2)}\n`;

if (checkOnly) {
  if (!existsSync(outputDataPath) || !readFileSync(outputDataPath).equals(source)) {
    throw new Error('Generated Blazor emoji data is stale.');
  }
  if (!existsSync(outputManifestPath) || readFileSync(outputManifestPath, 'utf8') !== manifest) {
    throw new Error('Generated Blazor emoji asset manifest is stale.');
  }
  for (const asset of assets) {
    const output = join(outputTwemojiRoot, asset.file);
    if (!existsSync(output) || sha256(readFileSync(output)) !== asset.sha256) {
      throw new Error(`Generated Blazor Twemoji asset ${asset.file} is stale.`);
    }
  }
} else {
  mkdirSync(dirname(outputDataPath), { recursive: true });
  mkdirSync(outputTwemojiRoot, { recursive: true });
  writeFileSync(outputDataPath, source);
  writeFileSync(outputManifestPath, manifest);
  for (const asset of assets) copyFileSync(join(twemojiRoot, asset.file), join(outputTwemojiRoot, asset.file));
}

console.log(`Blazor emoji data: ${definitions.length} definitions and ${assets.length} Twemoji ${twemojiPackage.version} assets.`);
