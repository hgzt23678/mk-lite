import { readFile } from 'node:fs/promises';

const allowed = new Set([
  '0BSD',
  'AGPL-3.0-only',
  'Apache-2.0',
  'BSD-2-Clause',
  'BSD-3-Clause',
  'BlueOak-1.0.0',
  'CC0-1.0',
  'ISC',
  'MIT',
  'MIT-0',
  'MPL-2.0',
  'OFL-1.1',
  'Python-2.0',
  '(CC-BY-4.0 AND OFL-1.1 AND MIT)',
  '(MIT AND CC-BY-4.0)',
  '(MPL-2.0 OR Apache-2.0)',
]);

// The npm metadata for these exact immutable releases omits `license`, even
// though the installed archive contains an unambiguous license declaration.
// Keep this list version-pinned: upgrades must be reviewed again instead of
// inheriting an approval intended for another release.
const auditedMissingLicenses = new Map([
  ['escape-regexp@0.0.1', 'MIT'], // The package README has an MIT License section.
  ['misskey-js@0.0.14', 'MIT'], // The installed package contains LICENSE (MIT).
]);

const lock = JSON.parse(await readFile(new URL('../frontend/misskey-v12/package-lock.json', import.meta.url), 'utf8'));
const failures = [];
const inventory = [];
for (const [path, metadata] of Object.entries(lock.packages ?? {})) {
  if (!path || !path.includes('node_modules/')) continue;
  const name = path.slice(path.lastIndexOf('node_modules/') + 'node_modules/'.length);
  const packageId = `${name}@${metadata.version ?? 'unknown'}`;
  const raw = metadata.license;
  const declaredLicense = Array.isArray(raw)
    ? `(${raw.join(' AND ')})`
    : typeof raw === 'string'
      ? raw
      : raw?.type;
  const license = declaredLicense ?? auditedMissingLicenses.get(packageId);
  inventory.push(`${name}\t${metadata.version ?? 'unknown'}\t${license ?? 'MISSING'}`);
  if (!license || !allowed.has(license)) failures.push(`${packageId}: ${license ?? 'MISSING'}`);
}

inventory.sort().forEach(line => console.log(line));
if (failures.length > 0) {
  console.error(`Unapproved frontend licenses:\n${failures.join('\n')}`);
  process.exitCode = 1;
}
