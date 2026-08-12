#!/usr/bin/env node
import { createRequire } from 'node:module';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const frontendRequire = createRequire(path.join(repositoryRoot, 'frontend/misskey-v12/package.json'));
const vite = await import(pathToFileURL(frontendRequire.resolve('vite')).href);
const temporaryDirectory = await mkdtemp(path.join(os.tmpdir(), 'activitypub-mfm-'));
const outputPath = path.join(
  repositoryRoot,
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/js/mfm-parser.js');

try {
  await vite.build({
    configFile: false,
    logLevel: 'error',
    build: {
      emptyOutDir: true,
      minify: 'oxc',
      outDir: temporaryDirectory,
      lib: {
        entry: path.join(repositoryRoot, 'tools/mfm-browser-entry.mjs'),
        formats: ['es'],
        fileName: () => 'mfm-parser.js',
      },
      rolldownOptions: {
        output: { codeSplitting: false },
      },
    },
  });
  const bundle = await readFile(path.join(temporaryDirectory, 'mfm-parser.js'), 'utf8');
  const generated = [
    '/* mfm-js 0.23.0 (MIT); generated from the locked Misskey 12.119.2 dependency. */',
    bundle,
  ].join('\n');
  if (process.argv.includes('--check')) {
    const current = await readFile(outputPath, 'utf8').catch(() => '');
    if (current !== generated) {
      process.stderr.write('Generated browser MFM parser is stale.\n');
      process.exitCode = 1;
    }
  } else {
    await writeFile(outputPath, generated, 'utf8');
  }
} finally {
  await rm(temporaryDirectory, { recursive: true, force: true });
}
