if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/_content/ActivityPub.Misskey.Blazor/service-worker.js', {
      scope: '/',
      updateViaCache: 'none'
    });
  }, { once: true });
}
