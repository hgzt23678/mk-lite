(() => {
  'use strict';

  if (globalThis.activityPubMisskeyInterop !== undefined) return;

  const pageDisposalMarker = 'MISSKEY_INTEROP_PAGE_DISPOSAL';
  let pageDisposing = false;
  let cancelledNavigationTimer = 0;

  const beginProvisionalDisposal = () => {
    pageDisposing = true;
    if (cancelledNavigationTimer !== 0) window.clearTimeout(cancelledNavigationTimer);
    cancelledNavigationTimer = window.setTimeout(() => {
      cancelledNavigationTimer = 0;
      if (document.visibilityState === 'visible') pageDisposing = false;
    }, 1000);
  };
  const confirmDisposal = () => {
    pageDisposing = true;
    if (cancelledNavigationTimer !== 0) {
      window.clearTimeout(cancelledNavigationTimer);
      cancelledNavigationTimer = 0;
    }
  };
  const resetDisposal = () => {
    pageDisposing = false;
    if (cancelledNavigationTimer !== 0) {
      window.clearTimeout(cancelledNavigationTimer);
      cancelledNavigationTimer = 0;
    }
  };

  // WebKit can reject in-flight imports after beforeunload but before pagehide. Keep a bounded
  // provisional window for that gap; pagehide confirms disposal, while a cancelled navigation
  // leaves the document visible and is reset without permanently masking later import failures.
  window.addEventListener('beforeunload', beginProvisionalDisposal, { capture: true });
  window.addEventListener('pagehide', confirmDisposal, { capture: true });
  window.addEventListener('pageshow', resetDisposal, { capture: true });

  const api = Object.freeze({
    async importModule(specifier) {
      if (typeof specifier !== 'string' || specifier.length === 0 || specifier.length > 512) {
        throw new TypeError('A bounded Misskey interop module specifier is required.');
      }

      try {
        // Dynamic import resolves relative strings against the JavaScript file containing this
        // expression. The former Blazor `import` interop resolved them against document.baseURI;
        // preserve that contract so `./_content/...` remains beneath the configured `/app/` base.
        const resolvedSpecifier = new URL(specifier, document.baseURI).href;
        return await import(resolvedSpecifier);
      } catch (error) {
        if (pageDisposing) throw new Error(pageDisposalMarker);
        throw error;
      }
    },
  });

  Object.defineProperty(globalThis, 'activityPubMisskeyInterop', {
    configurable: false,
    enumerable: false,
    writable: false,
    value: api,
  });
})();
