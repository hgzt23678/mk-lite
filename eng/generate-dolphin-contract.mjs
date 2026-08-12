#!/usr/bin/env node

import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';

const root = resolve(dirname(new URL(import.meta.url).pathname), '..');
const dolphinRoot = join(root, '.cache/meidolphin');
const dolphinEndpoints = join(dolphinRoot, 'src/server/api/endpoints');
const localEndpoint = join(root, 'src/ActivityPub.MisskeyApi/MisskeyEndpoints.cs');
const output = join(root, 'artifacts/backend-contract/dolphin-misskey-12.json');
const checkOnly = process.argv.includes('--check');
const dolphinCommit = execFileSync('git', ['-C', dolphinRoot, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
const generatedAt = execFileSync('git', ['-C', dolphinRoot, 'show', '-s', '--format=%cI', dolphinCommit], { encoding: 'utf8' }).trim();
const expectedCommit = '3ce200269f814547dc7dfc6b246abadf8a9c00ed';
if (dolphinCommit !== expectedCommit) throw new Error(`Unexpected Dolphin commit: ${dolphinCommit}`);

function walk(directory) {
  const files = [];
  for (const name of readdirSync(directory).sort()) {
    const file = join(directory, name);
    if (statSync(file).isDirectory()) files.push(...walk(file));
    else if (name.endsWith('.ts') && name !== 'endpoint.ts' && name !== 'endpoints.ts') files.push(file);
  }
  return files;
}

function field(text, name) {
  const match = text.match(new RegExp(`\\b${name}\\s*:\\s*([^,\\n}]+)`));
  return match?.[1]?.trim() ?? null;
}

function namesInBlock(text, name) {
  const start = text.indexOf(`${name}:`);
  if (start < 0) return [];
  const end = text.indexOf('},', start);
  const block = text.slice(start, end < 0 ? Math.min(text.length, start + 4000) : end);
  return [...block.matchAll(/^\s*([A-Za-z0-9_]+)\s*:/gm)].map(match => match[1]);
}

const dolphin = walk(dolphinEndpoints).map(file => {
  const source = readFileSync(file, 'utf8');
  const name = relative(dolphinEndpoints, file).split(sep).join('/').replace(/\.ts$/, '');
  return {
    name,
    sourcePath: relative(root, file).split(sep).join('/'),
    requireCredential: /requireCredential\s*:\s*true/.test(source),
    requireAdmin: /requireAdmin\s*:\s*true/.test(source),
    secure: /secure\s*:\s*true/.test(source),
    kind: field(source, 'kind'),
    parameters: namesInBlock(source, 'params'),
    errors: namesInBlock(source, 'errors')
  };
}).sort((a, b) => a.name.localeCompare(b.name));

const local = readFileSync(localEndpoint, 'utf8');
const groups = new Map();
for (const match of local.matchAll(/RouteGroupBuilder\s+(\w+)\s*=\s*endpoints\.MapGroup\(\s*"([^"]+)"/g)) {
  groups.set(match[1], match[2].replace(/\/$/, ''));
}
const routes = [];
for (const match of local.matchAll(/(\w+)\.Map(Get|Post|Put|Patch|Delete)\(\s*"([^"]+)"/g)) {
  const base = groups.get(match[1]) ?? '';
  const path = (`${base}/${match[3]}`).replaceAll('//', '/');
  routes.push({ method: match[2].toUpperCase(), path, endpoint: path.replace(/^\/api\//, '') });
}

const localNames = new Set(routes.map(route => route.endpoint));
const contracts = dolphin.map(endpoint => {
  const matches = routes.filter(route => route.endpoint === endpoint.name);
  return {
    ...endpoint,
    localRoutes: matches,
    status: matches.length > 0 ? 'supported-route' : 'missing-adapter-route',
    persistenceEvidence: matches.length > 0 ? 'requires endpoint/application test review' : 'none',
    differentialEvidence: 'not-run'
  };
});

const artifact = {
  schemaVersion: 1,
  target: 'Misskey v12 backend contract',
  source: 'mei23/dolphin',
  sourcePath: '.cache/meidolphin',
  commit: dolphinCommit,
  generatedAt,
  dolphinEndpointCount: dolphin.length,
  localRouteCount: routes.length,
  supportedDolphinRouteCount: contracts.filter(x => x.status === 'supported-route').length,
  missingDolphinAdapterCount: contracts.filter(x => x.status !== 'supported-route').length,
  dolphinEndpoints: contracts,
  localRoutes: routes,
  localOnlyRoutes: routes.filter(route => !dolphin.some(endpoint => endpoint.name === route.endpoint)),
  requiredScreenContracts: {
    supported: [
      'i', 'i/update', 'i/apps', 'i/revoke-token', 'notes/timeline', 'notes/global-timeline',
      'notes/local-timeline', 'notes/show', 'notes/create', 'notes/delete', 'notes/reactions/create',
      'notes/reactions/delete', 'i/notifications', 'notifications/mark-all-as-read', 'admin/invite',
      'admin/announcements/list', 'admin/announcements/create', 'admin/announcements/update',
      'admin/announcements/delete', 'admin/relays/list', 'admin/relays/add', 'admin/relays/remove',
      'users/show', 'users/notes', 'users/search', 'users/followers', 'users/following'
    ],
    excludedUntilBackendContract: [
      'drive/files/create', 'charts/*', 'antennas/*', 'channels/*', 'clips/*', 'gallery/*', 'i/2fa/*'
    ]
  }
};

const serialized = JSON.stringify(artifact, null, 2) + '\n';
if (checkOnly) {
  const current = existsSync(output) ? readFileSync(output, 'utf8') : null;
  if (current !== serialized) process.exitCode = 1;
} else {
  writeFileSync(output, serialized);
}
console.log(`Dolphin contract: ${dolphin.length} endpoints, ${routes.length} local routes, ${artifact.missingDolphinAdapterCount} missing adapters.`);
