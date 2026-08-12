import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import pluginVue from '@vitejs/plugin-vue';
import reactivityTransform from '@vue-macros/reactivity-transform/vite';
import { defineConfig } from 'vite';

import meta from './package.json' with { type: 'json' };
import pluginJson5 from './vite.json5.ts';

const root = path.dirname(fileURLToPath(import.meta.url));
const languageKeys = fs.readdirSync(path.join(root, 'locales'))
  .filter(name => /^[a-z]{2,3}-[A-Z][A-Za-z]+\.yml$/.test(name))
  .map(name => name.slice(0, -4))
  .sort();
const extensions = ['.ts', '.tsx', '.js', '.jsx', '.mjs', '.json', '.json5', '.svg', '.sass', '.scss', '.css', '.vue'];

export default defineConfig(({ mode }) => ({
  base: '/app/',
  publicDir: path.join(root, 'public'),
  plugins: [reactivityTransform(), pluginVue(), pluginJson5()],
  resolve: {
    extensions,
    alias: { '@/': path.join(root, 'src/') },
  },
  define: {
    _VERSION_: JSON.stringify(meta.version),
    _LANGS_: JSON.stringify(languageKeys.map(key => [key, key])),
    _ENV_: JSON.stringify(mode),
    _DEV_: mode !== 'production',
    _PERF_PREFIX_: JSON.stringify('Misskey:'),
    _DATA_TRANSFER_DRIVE_FILE_: JSON.stringify('mk_drive_file'),
    _DATA_TRANSFER_DRIVE_FOLDER_: JSON.stringify('mk_drive_folder'),
    _DATA_TRANSFER_DECK_COLUMN_: JSON.stringify('mk_deck_column'),
    __VUE_OPTIONS_API__: true,
    __VUE_PROD_DEVTOOLS__: false,
  },
  build: {
    target: ['chrome100', 'firefox100', 'safari15', 'es2017'],
    manifest: true,
    rolldownOptions: {
      input: path.join(root, 'index.html'),
      // Plugin timing diagnostics vary with CI host contention and are not a
      // correctness signal; bundle content and duration are measured separately.
      checks: { pluginTimings: false },
      output: { manualChunks: id => id.includes('/node_modules/vue/') ? 'vue' : undefined },
    },
    cssCodeSplit: true,
    outDir: path.join(root, 'dist'),
    assetsDir: 'assets',
    emptyOutDir: true,
    sourcemap: true,
    reportCompressedSize: false,
  },
  server: { fs: { strict: true } },
}));
