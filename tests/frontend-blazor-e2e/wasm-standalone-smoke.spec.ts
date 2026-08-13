import { expect, test } from '@playwright/test';

type Diagnostics = {
  sessionCookieObserved: boolean;
  sessionMarkerObserved: boolean;
  protectedRequestCount: number;
  invalidProtectedRequestCount: number;
  csrfProbeSucceeded: boolean;
  cursorRequestSucceeded: boolean;
  webSocketConnected: boolean;
  webSocketChannel: string | null;
};

test('real standalone WASM visitor, cookie CSRF, opaque backgrounds, and streaming transport', async ({ page }) => {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const notFound: string[] = [];
  const requestedResources: string[] = [];

  page.on('console', message => {
    if (message.type() === 'error' || message.text().includes('Content Security Policy')) {
      consoleErrors.push(message.text());
    }
  });
  page.on('pageerror', error => pageErrors.push(error.message));
  page.on('response', response => {
    if (response.status() === 404) notFound.push(new URL(response.url()).pathname);
  });
  page.on('request', request => {
    requestedResources.push(new URL(request.url()).pathname);
  });
  await page.addInitScript(() => {
    window.addEventListener('securitypolicyviolation', event => {
      console.error(`Content Security Policy violation: ${event.violatedDirective}`);
    });
  });

  await page.goto('/__wasm/bootstrap');
  await expect(page).toHaveURL(/\/app\/$/);
  await expect(page.locator('.mk-app .rsqzvsbo > .top > .main')).toBeVisible({ timeout: 20_000 });

  expect(requestedResources).toContain('/app/_framework/blazor.webassembly.js');
  expect(requestedResources.some(path =>
    path.includes('/_blazor') ||
    path.endsWith('/blazor.web.js') ||
    /(?:^|\/)@vite(?:\/|$)/.test(path) ||
    /(?:^|\/)vue(?:\.runtime)?(?:\.|\/)/i.test(path))).toBe(false);

  const surfaces = await page.evaluate(() => {
    function styleFor(selector: string) {
      const element = document.querySelector(selector);
      if (!(element instanceof HTMLElement)) throw new Error(`Missing surface: ${selector}`);
      const style = getComputedStyle(element);
      return { selector, color: style.backgroundColor, image: style.backgroundImage };
    }
    return [
      styleFor('html'),
      styleFor('body'),
      styleFor('.mk-app'),
      styleFor('.rsqzvsbo > .top > .main')
    ];
  });
  for (const surface of surfaces) {
    const color = surface.color.trim().toLowerCase();
    const components = color.match(/[\d.]+/g)?.map(Number) ?? [];
    const alpha = color === 'transparent'
      ? 0
      : color.startsWith('rgba') || color.includes('/')
        ? components.at(-1) ?? 0
        : 1;
    expect(alpha > 0 || surface.image !== 'none', `${surface.selector} must be opaque`).toBe(true);
  }

  const browserBoundary = await page.evaluate(async () => {
    const security = await import('/app/_content/ActivityPub.Misskey.Blazor/js/frontend-request-security.js');
    const probeResponse = await fetch('/__wasm/csrf-probe', {
      method: 'POST',
      credentials: 'include',
      headers: security.frontendRequestHeaders('/__wasm/csrf-probe', true)
    });
    if (!probeResponse.ok) throw new Error('CSRF probe failed');

    const cursorResponse = await fetch('/api/streaming/cursor', {
      method: 'POST',
      credentials: 'include',
      headers: {
        ...security.frontendRequestHeaders('/api/streaming/cursor', true),
        'Content-Type': 'application/json'
      },
      body: '{}'
    });
    if (!cursorResponse.ok) throw new Error('Cursor bootstrap failed');
    const { cursor } = await cursorResponse.json();

    const streamModule = await import('/app/streaming.js');
    let connection: { dispose(): void } | undefined;
    const checkpoint = new Promise<number>((resolve, reject) => {
      const timeout = window.setTimeout(() => reject(new Error('WebSocket checkpoint timeout')), 5000);
      const receiver = {
        invokeMethodAsync(method: string, value: string) {
          if (method === 'ReceiveFrameAsync') {
            const frame = JSON.parse(value);
            if (frame.type === 'checkpoint') {
              window.clearTimeout(timeout);
              resolve(frame.cursor);
            }
          }
          return Promise.resolve();
        }
      };
      const endpoint = new URL('/streaming', window.location.origin);
      endpoint.protocol = endpoint.protocol === 'https:' ? 'wss:' : 'ws:';
      connection = streamModule.createMisskeyStream(endpoint.href, receiver, 32);
      connection.subscribe('wasm-smoke', 'globalTimeline', cursor);
    });
    try {
      return { checkpoint: await checkpoint };
    } finally {
      connection?.dispose();
    }
  });
  expect(browserBoundary.checkpoint).toBe(43);

  const diagnosticsResponse = await page.request.get('/__wasm/diagnostics');
  expect(diagnosticsResponse.ok()).toBe(true);
  const diagnostics = await diagnosticsResponse.json() as Diagnostics;
  expect(diagnostics).toMatchObject({
    sessionCookieObserved: true,
    sessionMarkerObserved: true,
    invalidProtectedRequestCount: 0,
    csrfProbeSucceeded: true,
    cursorRequestSucceeded: true,
    webSocketConnected: true,
    webSocketChannel: 'globalTimeline'
  });
  expect(diagnostics.protectedRequestCount).toBeGreaterThanOrEqual(6);

  expect(notFound, 'unclassified 404 responses').toEqual([]);
  expect(consoleErrors, 'console/CSP errors').toEqual([]);
  expect(pageErrors, 'uncaught page errors').toEqual([]);

  await page.goto('/__wasm/bootstrap-failure');
  const initializationFailure = page.locator(
    '.mk-initialization-error[data-error-code="FRONTEND_INITIALIZATION_FAILED"]');
  await expect(initializationFailure).toBeVisible();
  await expect(initializationFailure).toContainText('クライアントを安全に初期化できませんでした。');
  await expect(initializationFailure.getByRole('button', { name: '再読み込み' })).toBeVisible();
  expect(consoleErrors, 'console/CSP errors after failed bootstrap').toEqual([]);
  expect(pageErrors, 'uncaught errors after failed bootstrap').toEqual([]);
});
