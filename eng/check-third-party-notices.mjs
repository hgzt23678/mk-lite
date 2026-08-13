import { homedir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { access, readFile, readdir } from 'node:fs/promises';

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const noticePath = join(repositoryRoot, 'THIRD_PARTY_NOTICES.md');
const notice = await readFile(noticePath, 'utf8');
const failures = [];

const noticeRows = new Map();
for (const match of notice.matchAll(/^\| `([^`]+)` \| ([^|]+?) \| ([^|]+?) \|/gm)) {
  noticeRows.set(match[1], {
    version: match[2].trim(),
    license: match[3].trim(),
  });
}
for (const match of notice.matchAll(/^\| \[`([^`]+)`\]\([^)]*\) \| ([^|]+?) \| ([^|]+?) \|/gm)) {
  noticeRows.set(match[1], {
    version: match[2].trim(),
    license: match[3].trim(),
  });
}

const directoryPackages = await readFile(join(repositoryRoot, 'Directory.Packages.props'), 'utf8');
const centralVersions = new Map(
  [...directoryPackages.matchAll(/<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"/g)]
    .map(match => [match[1], match[2]]),
);

const productionPackageReferences = new Set();
await collectPackageReferences(join(repositoryRoot, 'src'));
await collectPackageReferences(join(repositoryRoot, 'frontend', 'ActivityPub.Misskey.Blazor'));

const nugetRoot = process.env.NUGET_PACKAGES ?? join(homedir(), '.nuget', 'packages');
for (const packageName of [...productionPackageReferences].sort()) {
  const version = centralVersions.get(packageName);
  if (!version) {
    failures.push(`${packageName}: no central version in Directory.Packages.props`);
    continue;
  }

  const row = noticeRows.get(packageName);
  if (!row) {
    failures.push(`${packageName}@${version}: missing direct .NET notice row`);
    continue;
  }
  if (row.version !== version) {
    failures.push(`${packageName}: notice version ${row.version} does not match ${version}`);
  }

  const packageDirectory = join(nugetRoot, packageName.toLowerCase(), version);
  let nuspec;
  try {
    nuspec = (await readdir(packageDirectory)).find(file => file.endsWith('.nuspec'));
  } catch {
    failures.push(`${packageName}@${version}: package is not restored under ${nugetRoot}`);
    continue;
  }
  if (!nuspec) {
    failures.push(`${packageName}@${version}: restored package has no nuspec`);
    continue;
  }

  const metadata = await readFile(join(packageDirectory, nuspec), 'utf8');
  const license = metadata.match(/<license\s+type="expression">([^<]+)<\/license>/)?.[1];
  if (!license) {
    failures.push(`${packageName}@${version}: nuspec has no SPDX license expression`);
  } else if (row.license !== license) {
    failures.push(`${packageName}@${version}: notice says ${row.license}, nuspec says ${license}`);
  }
}

const frontendLock = JSON.parse(await readFile(
  join(repositoryRoot, 'frontend', 'misskey-v12', 'package-lock.json'),
  'utf8',
));
const frontendDirect = frontendLock.packages?.['']?.dependencies ?? {};
const reviewedMissingLicenses = new Map([
  ['escape-regexp@0.0.1', 'MIT'],
  ['misskey-js@0.0.14', 'MIT'],
]);

for (const packageName of Object.keys(frontendDirect).sort()) {
  const metadata = frontendLock.packages?.[`node_modules/${packageName}`];
  const version = metadata?.version;
  const rawLicense = metadata?.license;
  const license = Array.isArray(rawLicense)
    ? rawLicense.join(' AND ')
    : typeof rawLicense === 'string'
      ? rawLicense.replace(/^\((.*)\)$/, '$1')
      : reviewedMissingLicenses.get(`${packageName}@${version}`);
  const row = noticeRows.get(packageName);

  if (!metadata || !version || !license) {
    failures.push(`${packageName}: incomplete direct frontend lock metadata`);
    continue;
  }
  if (!row) {
    failures.push(`${packageName}@${version}: missing direct frontend notice row`);
    continue;
  }
  if (row.version !== version || row.license !== license) {
    failures.push(
      `${packageName}: notice ${row.version}/${row.license} does not match lock ${version}/${license}`,
    );
  }
}

const requiredLicenseFiles = [
  'LICENSE',
  'licenses/Apache-2.0.txt',
  'licenses/CC-BY-4.0.txt',
  'licenses/ISC.txt',
  'licenses/MIT.txt',
  'licenses/OFL-1.1.txt',
  'licenses/PostgreSQL.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/blurhash/LICENSE.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/fontawesome/LICENSE.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/katex/LICENSE.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/matter/LICENSE.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/mfm-js/LICENSE.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/photoswipe/LICENSE.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/prism/LICENSE.txt',
  'frontend/ActivityPub.Misskey.Blazor/wwwroot/twemoji/LICENSE.txt',
];

for (const relativePath of requiredLicenseFiles) {
  try {
    await access(join(repositoryRoot, relativePath));
  } catch {
    failures.push(`missing license file: ${relativePath}`);
  }
}

if (failures.length > 0) {
  console.error(`Third-party notice check failed:\n${failures.join('\n')}`);
  process.exitCode = 1;
} else {
  console.log(
    `Third-party notices match ${productionPackageReferences.size} direct .NET packages, ` +
    `${Object.keys(frontendDirect).length} direct frontend packages, and ` +
    `${requiredLicenseFiles.length} license files.`,
  );
}

async function collectPackageReferences(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'node_modules') continue;
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      await collectPackageReferences(path);
      continue;
    }
    if (!entry.isFile() || !entry.name.endsWith('.csproj')) continue;
    const project = await readFile(path, 'utf8');
    const references = [...project.matchAll(/<PackageReference\s+Include="([^"]+)"/g)]
      .map(match => match[1]);
    const lockPath = join(directory, 'packages.lock.json');
    let lock;
    try {
      lock = JSON.parse(await readFile(lockPath, 'utf8'));
    } catch {
      if (references.length > 0) failures.push(`${path}: missing or invalid packages.lock.json`);
      continue;
    }
    const lockedDependencies = Object.assign({}, ...Object.values(lock.dependencies ?? {}));
    for (const packageName of references) {
      productionPackageReferences.add(packageName);
      const lockedVersion = lockedDependencies[packageName]?.resolved;
      const centralVersion = centralVersions.get(packageName);
      if (!lockedVersion) {
        failures.push(`${path}: ${packageName} is absent from its checked-in lock`);
      } else if (centralVersion && lockedVersion !== centralVersion) {
        failures.push(
          `${path}: ${packageName} lock ${lockedVersion} does not match central ${centralVersion}`,
        );
      }
    }
  }
}
