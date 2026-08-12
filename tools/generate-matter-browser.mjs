#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const frontendRequire = createRequire(path.join(repositoryRoot, 'frontend/misskey-v12/package.json'));
const packageJsonPath = frontendRequire.resolve('matter-js/package.json');
const packageRoot = path.dirname(packageJsonPath);
const sourcePath = path.join(packageRoot, 'build/matter.min.js');
const outputPath = path.join(
  repositoryRoot,
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/matter-0.18.0.min.js');

const packageJson = JSON.parse(await readFile(packageJsonPath, 'utf8'));
if (packageJson.version !== '0.18.0' || packageJson.license !== 'MIT') {
  throw new Error('The pinned Matter.js dependency no longer matches the reviewed 0.18.0 MIT artifact.');
}

const source = await readFile(sourcePath, 'utf8');
const digest = createHash('sha256').update(source).digest('hex');
const generated = [
  `/* Pinned matter-js ${packageJson.version} (${packageJson.license}); source SHA-256: ${digest}. */`,
  source,
].join('\n');

if (process.argv.includes('--check')) {
  const current = await readFile(outputPath, 'utf8').catch(() => '');
  if (current !== generated) {
    process.stderr.write('Generated Matter.js browser artifact is stale.\n');
    process.exitCode = 1;
  }
} else {
  await writeFile(outputPath, generated, 'utf8');
}
