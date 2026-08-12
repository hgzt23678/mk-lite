#!/usr/bin/env node

import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const frontendRoot = path.join(repositoryRoot, 'frontend/misskey-v12');
const frontendRequire = createRequire(path.join(frontendRoot, 'package.json'));
const { rollup } = frontendRequire('rollup');
const packageRoot = path.join(frontendRoot, 'node_modules/blurhash');
const inputPath = path.join(packageRoot, 'dist/esm/decode.js');
const outputPath = path.join(
  repositoryRoot,
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/js/vendor/blurhash-1.1.5.js');
const sourcePaths = [
  path.join(packageRoot, 'package.json'),
  path.join(packageRoot, 'dist/esm/base83.js'),
  path.join(packageRoot, 'dist/esm/decode.js'),
  path.join(packageRoot, 'dist/esm/error.js'),
  path.join(packageRoot, 'dist/esm/utils.js'),
];

const digest = createHash('sha256');
for (const sourcePath of sourcePaths) digest.update(await readFile(sourcePath));
const sourceDigest = digest.digest('hex');

const bundle = await rollup({ input: inputPath });
const generatedOutput = await bundle.generate({ format: 'es', compact: false });
await bundle.close();
const chunk = generatedOutput.output.find(output => output.type === 'chunk');
if (chunk === undefined) throw new Error('Rollup did not emit the BlurHash browser module.');

const generated = [
  '/* blurhash 1.1.5, Copyright (c) Wolt Enterprises, MIT License. */',
  '/* Generated from the exact locked frontend dependency; do not hand-edit.',
  ` * Source-set SHA-256: ${sourceDigest}`,
  ' */',
  chunk.code.trim(),
  '',
].join('\n');

if (process.argv.includes('--check')) {
  const current = await readFile(outputPath, 'utf8').catch(() => '');
  if (current !== generated) {
    process.stderr.write('Generated BlurHash 1.1.5 browser module is stale.\n');
    process.exitCode = 1;
  }
} else {
  await mkdir(path.dirname(outputPath), { recursive: true });
  await writeFile(outputPath, generated, 'utf8');
}
