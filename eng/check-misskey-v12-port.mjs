import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

const root = new URL('../frontend/misskey-v12/', import.meta.url);
const manifestText = await readFile(new URL('UPSTREAM_FILES.sha256', root), 'utf8');
const manifest = manifestText.trimEnd().split('\n').map(line => {
  const [hash, path] = line.split('\t');
  if (!/^[0-9a-f]{64}$/.test(hash) || !path) throw new Error(`Invalid upstream manifest line: ${line}`);
  return { hash, path };
});

// These files are intentionally adapted for OIDC, the same-origin API/media
// boundary, current build tooling, or removal of upstream remote ghost images.
// Every other upstream byte is required to remain identical to 12.119.2.
const modified = new Set([
  '.npmrc',
  '@types/global.d.ts',
  'package.json',
  'src/account.ts',
  'src/components/MkInstanceTicker.vue',
  'src/components/MkNotes.vue',
  'src/components/MkNotifications.vue',
  'src/components/MkPagination.vue',
  'src/components/MkSignin.vue',
  'src/components/MkSignup.vue',
  'src/components/MkSuperMenu.vue',
  'src/components/MkUserList.vue',
  'src/components/MkWindow.vue',
  'src/components/global/MkError.vue',
  'src/components/page/page.vue',
  'src/os.ts',
  'src/pages/_error_.vue',
  'src/pages/about.vue',
  'src/pages/about-misskey.vue',
  'src/pages/admin/index.vue',
  'src/pages/favorites.vue',
  'src/pages/follow-requests.vue',
  'src/pages/messaging/index.vue',
  'src/pages/messaging/messaging-room.vue',
  'src/pages/not-found.vue',
  'src/pages/page-editor/page-editor.vue',
  'src/pages/settings/apps.vue',
  'src/pages/welcome.entrance.a.vue',
  'src/scripts/initialize-sw.ts',
  'src/ui/_common_/navbar-for-mobile.vue',
  'src/ui/_common_/navbar.vue',
  'src/ui/_common_/statusbars.vue',
  'src/ui/classic.sidebar.vue',
  'tsconfig.json',
  'vite.config.ts',
  'yarn.lock',
]);

if (manifest.length !== 573) throw new Error(`Expected 573 upstream files, found ${manifest.length}.`);
const paths = new Set(manifest.map(entry => entry.path));
for (const path of modified) {
  if (!paths.has(path)) throw new Error(`Modified-file allow-list entry is not in the upstream manifest: ${path}`);
}

const failures = [];
let exact = 0;
for (const entry of manifest) {
  const localPath = entry.path.startsWith('assets/')
    ? `public/client-assets/${entry.path.slice('assets/'.length)}`
    : entry.path;
  let bytes;
  try {
    bytes = await readFile(new URL(localPath, root));
  } catch {
    failures.push(`${entry.path}: missing (expected at ${localPath})`);
    continue;
  }
  if (modified.has(entry.path)) continue;
  const actual = createHash('sha256').update(bytes).digest('hex');
  if (actual !== entry.hash) failures.push(`${entry.path}: changed without review allow-list entry`);
  else exact++;
}

async function verifyExactManifest(manifestName, directory, expectedCount) {
  const text = await readFile(new URL(manifestName, root), 'utf8');
  const entries = text.trimEnd().split('\n').map(line => {
    const [hash, path] = line.split('\t');
    if (!/^[0-9a-f]{64}$/.test(hash) || !path) throw new Error(`Invalid ${manifestName} line: ${line}`);
    return { hash, path };
  });
  if (entries.length !== expectedCount) {
    throw new Error(`Expected ${expectedCount} entries in ${manifestName}, found ${entries.length}.`);
  }
  for (const entry of entries) {
    try {
      const bytes = await readFile(new URL(`${directory}/${entry.path}`, root));
      const actual = createHash('sha256').update(bytes).digest('hex');
      if (actual !== entry.hash) failures.push(`${directory}/${entry.path}: differs from upstream`);
    } catch (error) {
      if (!failures.some(failure => failure.startsWith(`${directory}/${entry.path}:`))) {
        failures.push(`${directory}/${entry.path}: missing (${error.code ?? 'read error'})`);
      }
    }
  }
  return entries.length;
}

const localeFiles = await verifyExactManifest('UPSTREAM_LOCALES.sha256', 'locales', 38);
const staticAssetFiles = await verifyExactManifest('UPSTREAM_STATIC_ASSETS.sha256', 'public/static-assets', 25);

if (failures.length > 0) {
  console.error(`Misskey v12 source parity failed:\n${failures.join('\n')}`);
  process.exitCode = 1;
} else {
  const sourceFiles = manifest.filter(entry => entry.path.startsWith('src/')).length;
  console.log(`Misskey 12.119.2 parity: ${manifest.length}/${manifest.length} client files present; ${exact} byte-identical; ${modified.size} reviewed modifications; ${sourceFiles} upstream src files, ${localeFiles} locales, and ${staticAssetFiles} server assets covered.`);
}
