const cacheNamePrefix = 'misskey-blazor-ssr-';
const cacheName = `${cacheNamePrefix}1`;
const shellAssets = [
  '/_content/ActivityPub.Misskey.Blazor/css/app.css',
  '/_content/ActivityPub.Misskey.Blazor/manifest.webmanifest'
];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(shellAssets)));
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(
    keys.filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName).map(key => caches.delete(key))
  )));
});

self.addEventListener('fetch', event => {
  const requestUrl = new URL(event.request.url);
  if (event.request.method !== 'GET' || requestUrl.origin !== self.location.origin ||
      requestUrl.pathname.startsWith('/api/') || requestUrl.pathname.startsWith('/media/') ||
      requestUrl.pathname.startsWith('/app/_blazor')) {
    return;
  }

  if (event.request.mode === 'navigate') {
    event.respondWith(fetch(event.request));
    return;
  }

  event.respondWith(caches.match(event.request).then(cached => cached || fetch(event.request)));
});
