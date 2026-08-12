const cachePrefix = 'activitypub-misskey-blazor-ssr-';
const cacheName = `${cachePrefix}1`;
const shellAssets = [
    '/_content/ActivityPub.Misskey.Blazor/css/app.css',
    '/_content/ActivityPub.Misskey.Blazor/manifest.webmanifest',
];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(shellAssets)));
});

self.addEventListener('activate', event => {
    event.waitUntil(caches.keys().then(keys => Promise.all(
        keys.filter(key => key.startsWith(cachePrefix) && key !== cacheName).map(key => caches.delete(key)),
    )));
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);
    if (event.request.method !== 'GET' || url.origin !== self.location.origin ||
        url.pathname.startsWith('/api/') || url.pathname.startsWith('/media/') ||
        url.pathname.startsWith('/app/_blazor')) return;
    if (event.request.mode === 'navigate') {
        event.respondWith(fetch(event.request, { cache: 'no-store' }));
        return;
    }
    if (url.pathname.startsWith('/_content/ActivityPub.Misskey.Blazor/')) {
        event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request)));
    }
});
