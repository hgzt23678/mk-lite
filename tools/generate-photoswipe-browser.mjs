#!/usr/bin/env node
import { createRequire } from 'node:module';
import { cp, mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const frontendRequire = createRequire(path.join(repositoryRoot, 'frontend/misskey-v12/package.json'));
const packageRoot = path.dirname(path.dirname(frontendRequire.resolve('photoswipe')));
const packageJson = JSON.parse(await readFile(path.join(packageRoot, 'package.json'), 'utf8'));
const outputRoot = path.join(
  repositoryRoot,
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/photoswipe');
const check = process.argv.includes('--check');

if (packageJson.version !== '5.3.2' || packageJson.license !== 'MIT') {
  throw new Error('photoswipe no longer matches the reviewed 5.3.2 MIT dependency.');
}

const files = [
  ['dist/photoswipe.esm.min.js', 'photoswipe.esm.min.js'],
  ['dist/photoswipe-lightbox.esm.min.js', 'photoswipe-lightbox.esm.min.js'],
  ['dist/photoswipe.css', 'photoswipe.css'],
  ['LICENSE', 'LICENSE.txt'],
];

for (const [sourceName, targetName] of files) {
  const source = path.join(packageRoot, sourceName);
  const target = path.join(outputRoot, targetName);
  if (check) {
    const [expected, current] = await Promise.all([
      readFile(source),
      readFile(target).catch(() => null),
    ]);
    if (current === null || !expected.equals(current)) {
      process.stderr.write(`Generated PhotoSwipe asset is stale: ${targetName}\n`);
      process.exitCode = 1;
    }
  } else {
    await mkdir(outputRoot, { recursive: true });
    await cp(source, target, { force: true });
  }
}
