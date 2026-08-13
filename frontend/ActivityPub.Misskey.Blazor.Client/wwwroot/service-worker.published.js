importScripts('./service-worker-assets.js');

const cachePrefix = 'activitypub-misskey-blazor-wasm-';
const cacheName = `${cachePrefix}${self.assetsManifest.version}`;
const forbiddenPrefixes = [
    '/api',
    '/auth',
    '/streaming',
    '/media',
    '/objects',
    '/activities',
    '/users',
];

function isSafeStaticAsset(url) {
    if (url.origin !== self.location.origin ||
        forbiddenPrefixes.some(prefix => url.pathname === prefix || url.pathname.startsWith(`${prefix}/`))) {
        return false;
    }

    return url.pathname.startsWith('/app/_framework/') ||
        url.pathname.startsWith('/app/_content/ActivityPub.Misskey.Blazor/css/') ||
        url.pathname.startsWith('/app/_content/ActivityPub.Misskey.Blazor/vendor/fontawesome/') ||
        url.pathname.startsWith('/app/_content/ActivityPub.Misskey.Blazor/vendor/prism/') ||
        url.pathname.startsWith('/app/_content/ActivityPub.Misskey.Blazor/vendor/katex/') ||
        url.pathname.startsWith('/app/_content/ActivityPub.Misskey.Blazor/vendor/photoswipe/') ||
        url.pathname.startsWith('/app/_content/ActivityPub.Misskey.Blazor/js/') ||
        url.pathname === '/app/streaming.js' ||
        url.pathname === '/app/ActivityPub.Misskey.Blazor.Client.styles.css' ||
        url.pathname === '/app/index.html' ||
        url.pathname === '/app/manifest.webmanifest' ||
        url.pathname === '/app/service-worker-registration.js';
}

const safeAssets = self.assetsManifest.assets
    .map(asset => new URL(asset.url, self.registration.scope))
    .filter(isSafeStaticAsset);

self.addEventListener('install', event => {
    event.waitUntil((async () => {
        const cache = await caches.open(cacheName);
        await Promise.all(safeAssets.map(async url => {
            const request = new Request(url, {
                cache: 'no-cache',
                credentials: 'omit',
                mode: 'same-origin',
            });
            const response = await fetch(request);
            if (response.ok && response.type === 'basic') {
                await cache.put(request, response);
            }
        }));
        await self.skipWaiting();
    })());
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys
            .filter(key => key.startsWith(cachePrefix) && key !== cacheName)
            .map(key => caches.delete(key)));
        await self.clients.claim();
    })());
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);
    if (event.request.method !== 'GET' ||
        event.request.mode === 'navigate' ||
        !isSafeStaticAsset(url)) {
        return;
    }

    event.respondWith((async () => {
        const cache = await caches.open(cacheName);
        const cached = await cache.match(new Request(url, { credentials: 'omit' }));
        return cached ?? fetch(event.request);
    })());
});
