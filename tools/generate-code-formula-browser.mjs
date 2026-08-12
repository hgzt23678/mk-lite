#!/usr/bin/env node
import { createRequire } from 'node:module';
import { cp, mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const frontendRequire = createRequire(path.join(repositoryRoot, 'frontend/misskey-v12/package.json'));
const vite = await import(pathToFileURL(frontendRequire.resolve('vite')).href);
const outputRoot = path.join(repositoryRoot, 'frontend/ActivityPub.Misskey.Blazor/wwwroot');
const check = process.argv.includes('--check');

const packages = [
  {
    name: 'prismjs',
    version: '1.30.0',
    entry: 'tools/prism-browser-entry.mjs',
    output: 'js/prism-highlight.js',
  },
  {
    name: 'katex',
    version: '0.16.25',
    entry: 'tools/katex-browser-entry.mjs',
    output: 'js/katex-renderer.js',
  },
];

async function compareFile(source, target) {
  const [expected, actual] = await Promise.all([
    readFile(source),
    readFile(target).catch(() => null),
  ]);
  return actual !== null && expected.equals(actual);
}

async function copyOrCheck(source, target) {
  if (check) {
    if (!await compareFile(source, target)) {
      process.stderr.write(`Generated browser dependency is stale: ${path.relative(repositoryRoot, target)}\n`);
      process.exitCode = 1;
    }
    return;
  }

  await mkdir(path.dirname(target), { recursive: true });
  await cp(source, target, { recursive: true, force: true });
}

for (const dependency of packages) {
  const packageJsonPath = frontendRequire.resolve(`${dependency.name}/package.json`);
  const packageRoot = path.dirname(packageJsonPath);
  const packageJson = JSON.parse(await readFile(packageJsonPath, 'utf8'));
  if (packageJson.version !== dependency.version || packageJson.license !== 'MIT') {
    throw new Error(`${dependency.name} no longer matches the reviewed ${dependency.version} MIT dependency.`);
  }

  const temporaryDirectory = await mkdtemp(path.join(os.tmpdir(), `activitypub-${dependency.name}-`));
  try {
    await vite.build({
      configFile: false,
      logLevel: 'error',
      build: {
        emptyOutDir: true,
        minify: 'oxc',
        outDir: temporaryDirectory,
        lib: {
          entry: path.join(repositoryRoot, dependency.entry),
          formats: ['es'],
          fileName: () => path.basename(dependency.output),
        },
        rolldownOptions: {
          output: { codeSplitting: false },
        },
      },
    });
    const bundle = await readFile(path.join(temporaryDirectory, path.basename(dependency.output)), 'utf8');
    const generated = `/* ${dependency.name} ${dependency.version} (MIT); generated from the locked Misskey 12.119.2 dependency. */\n${bundle}`;
    const outputPath = path.join(outputRoot, dependency.output);
    if (check) {
      const current = await readFile(outputPath, 'utf8').catch(() => '');
      if (current !== generated) {
        process.stderr.write(`Generated browser module is stale: ${dependency.output}\n`);
        process.exitCode = 1;
      }
    } else {
      await mkdir(path.dirname(outputPath), { recursive: true });
      await writeFile(outputPath, generated, 'utf8');
    }
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
}

const prismRoot = path.dirname(frontendRequire.resolve('prismjs/package.json'));
await copyOrCheck(
  path.join(prismRoot, 'themes/prism-okaidia.css'),
  path.join(outputRoot, 'vendor/prism/prism-okaidia.css'));
await copyOrCheck(
  path.join(prismRoot, 'LICENSE'),
  path.join(outputRoot, 'vendor/prism/LICENSE.txt'));

const katexRoot = path.dirname(frontendRequire.resolve('katex/package.json'));
await copyOrCheck(
  path.join(katexRoot, 'dist/katex.min.css'),
  path.join(outputRoot, 'vendor/katex/katex.min.css'));
await copyOrCheck(
  path.join(katexRoot, 'LICENSE'),
  path.join(outputRoot, 'vendor/katex/LICENSE.txt'));

const sourceFonts = path.join(katexRoot, 'dist/fonts');
const targetFonts = path.join(outputRoot, 'vendor/katex/fonts');
if (check) {
  for (const file of await readdir(sourceFonts)) {
    if (!await compareFile(path.join(sourceFonts, file), path.join(targetFonts, file))) {
      process.stderr.write(`Generated KaTeX font is stale: ${file}\n`);
      process.exitCode = 1;
    }
  }
} else {
  await mkdir(path.dirname(targetFonts), { recursive: true });
  await cp(sourceFonts, targetFonts, { recursive: true, force: true });
}
