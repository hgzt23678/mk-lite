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
const packageJsonPath = path.join(packageRoot, 'package.json');
const inputPath = path.join(packageRoot, 'dist/esm/decode.js');
const outputPath = path.join(
  repositoryRoot,
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/js/vendor/blurhash-1.1.5.js');
const licenseOutputPath = path.join(
  repositoryRoot,
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/blurhash/LICENSE.txt');
const sourcePaths = [
  packageJsonPath,
  path.join(packageRoot, 'dist/esm/base83.js'),
  path.join(packageRoot, 'dist/esm/decode.js'),
  path.join(packageRoot, 'dist/esm/error.js'),
  path.join(packageRoot, 'dist/esm/utils.js'),
];

const packageJson = JSON.parse(await readFile(packageJsonPath, 'utf8'));
if (packageJson.version !== '1.1.5' || packageJson.license !== 'MIT') {
  throw new Error('The pinned BlurHash dependency no longer matches the reviewed 1.1.5 MIT artifact.');
}

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

const license = [
  'MIT License',
  '',
  'Copyright (c) 2018 Wolt Enterprises',
  '',
  'Permission is hereby granted, free of charge, to any person obtaining a copy',
  'of this software and associated documentation files (the "Software"), to deal',
  'in the Software without restriction, including without limitation the rights',
  'to use, copy, modify, merge, publish, distribute, sublicense, and/or sell',
  'copies of the Software, and to permit persons to whom the Software is',
  'furnished to do so, subject to the following conditions:',
  '',
  'The above copyright notice and this permission notice shall be included in all',
  'copies or substantial portions of the Software.',
  '',
  'THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR',
  'IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,',
  'FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE',
  'AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER',
  'LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,',
  'OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE',
  'SOFTWARE.',
  '',
].join('\n');

if (process.argv.includes('--check')) {
  const current = await readFile(outputPath, 'utf8').catch(() => '');
  const currentLicense = await readFile(licenseOutputPath, 'utf8').catch(() => '');
  if (current !== generated) {
    process.stderr.write('Generated BlurHash 1.1.5 browser module is stale.\n');
    process.exitCode = 1;
  }
  if (currentLicense !== license) {
    process.stderr.write('Bundled BlurHash license is stale.\n');
    process.exitCode = 1;
  }
} else {
  await mkdir(path.dirname(outputPath), { recursive: true });
  await mkdir(path.dirname(licenseOutputPath), { recursive: true });
  await writeFile(outputPath, generated, 'utf8');
  await writeFile(licenseOutputPath, license, 'utf8');
}
