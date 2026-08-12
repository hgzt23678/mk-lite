import { expect, test, type Page } from '@playwright/test';

const startDiagnostics = (page: Page) => {
  const failures: string[] = [];
  page.on('console', message => {
    if (message.type() === 'error') failures.push(`console:${message.text()}`);
  });
  page.on('pageerror', error => failures.push(`page:${error.name}:${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) {
      failures.push(`http:${response.status()}:${new URL(response.url()).pathname}`);
    }
  });
  return failures;
};

const assertServerDiagnosticsAreEmpty = async (page: Page) => {
  const response = await page.request.get('/__test/diagnostics');
  expect(response.ok()).toBeTruthy();
  expect((await response.json()).unhandledExceptions).toEqual([]);
};

test.beforeEach(async ({ page }) => {
  await page.request.post('/__test/reset-diagnostics');
});

test('document disposal classifies only its aborted module import as a circuit disconnect', async ({ page }) => {
  let releaseImport!: () => void;
  const importReleased = new Promise<void>(resolve => {
    releaseImport = resolve;
  });
  let reportIntercepted!: () => void;
  const importIntercepted = new Promise<void>(resolve => {
    reportIntercepted = resolve;
  });
  let heldImport = true;
  let interceptedCount = 0;

  await page.route('**/_content/ActivityPub.Misskey.Blazor/js/visitor-shell*.js', async route => {
    if (!heldImport) {
      await route.continue();
      return;
    }

    heldImport = false;
    interceptedCount += 1;
    reportIntercepted();
    await importReleased;
    try {
      await route.continue();
    } catch {
      // The request belongs to the discarded document and is expected to have no route client.
    }
  });

  await page.goto('/');
  await importIntercepted;

  try {
    const navigation = page.goto('/about-misskey');
    await page.waitForURL(/\/about-misskey$/);
    releaseImport();
    await navigation;
  } finally {
    releaseImport();
  }

  expect(interceptedCount).toBe(1);
  await expect(page.locator('.znqjceqz')).toBeVisible();
  // Diagnostics on the discarded document are asserted through the server circuit collector.
  // Attach browser diagnostics to the replacement document so Firefox's own WebSocket teardown
  // report cannot be mistaken for an application exception in the new circuit.
  const failures = startDiagnostics(page);

  // An active document's real module exception remains observable and is never translated to
  // the disposal marker. The same-origin test-only module is valid JavaScript that throws at
  // evaluation, avoiding a 404, a CSP exception, or browser-specific syntax diagnostics.
  const genuineFailure = await page.evaluate(async () => {
    const interop = (globalThis as typeof globalThis & {
      activityPubMisskeyInterop: { importModule(specifier: string): Promise<unknown> };
    }).activityPubMisskeyInterop;
    try {
      await interop.importModule('/genuine-module-failure.js');
      return null;
    } catch (error) {
      return error instanceof Error ? error.message : String(error);
    }
  });
  expect(genuineFailure).toBe('GENUINE_MODULE_FIXTURE');

  // Exercise a server event and its page-local interop on the replacement circuit before
  // checking the diagnostics collector.
  const physics = page.locator('.znqjceqz > .about');
  await expect(physics).toHaveAttribute('data-physics-prepared', 'true');
  await physics.locator(':scope > img.icon').click();
  await expect(physics).toHaveClass(/\bplaying\b/);

  expect(failures).toEqual([]);
  await assertServerDiagnosticsAreEmpty(page);
});

test('closing a sign-in circuit disposes modal, form inputs, and authentication interop without leaking the request', async ({ browser, request }) => {
  const context = await browser.newContext({ locale: 'ja-JP', timezoneId: 'UTC' });
  const page = await context.newPage();
  const failures = startDiagnostics(page);

  await page.goto('/');
  await page.locator('[data-cy-signin]').click();
  const modal = page.locator('body > .qzhlnise.dialog[role="dialog"]');
  await expect(modal).toHaveCount(1);
  await expect.poll(async () => modal.getAttribute('data-motion-state')).toBe('entered');
  await expect(modal.locator('input[name="username"]')).toBeFocused();
  const beforeClose = await (await request.get('/__test/circuit-diagnostics')).json() as {
    activeCircuits: number;
    closedCircuits: number;
  };
  expect(beforeClose.activeCircuits).toBeGreaterThan(0);

  await context.close();
  expect(failures).toEqual([]);

  // Closing a Playwright context tears down its WebSocket abruptly. Give SignalR and the
  // test host's one-second disconnected-circuit retention window and bounded interop cleanup
  // time to complete before forcing collection. Collecting immediately is itself a Kestrel
  // event-23 fixture: the transport delegate is still completing even for an otherwise empty
  // InteractiveServer page.
  await new Promise(resolve => setTimeout(resolve, 10_000));
  expect((await request.post('/__test/collect-garbage')).status()).toBe(204);
  await new Promise(resolve => setTimeout(resolve, 1_500));

  const circuitDiagnostics = await request.get('/__test/diagnostics');
  expect(circuitDiagnostics.ok()).toBeTruthy();
  expect((await circuitDiagnostics.json()).unhandledExceptions).toEqual([]);

  // SignalR intentionally owns a long-running transport request. Kestrel's event 23 is
  // therefore not a leak detector for an abruptly closed browser context. Verify the actual
  // circuit lifecycle instead: the circuit that owned MkSignin must be released after the
  // retention window and its component disposal work has completed.
  await expect.poll(async () => {
    const current = await (await request.get('/__test/circuit-diagnostics')).json() as {
      activeCircuits: number;
      closedCircuits: number;
    };
    return current.closedCircuits;
  }).toBeGreaterThan(beforeClose.closedCircuits);
  const afterClose = await (await request.get('/__test/circuit-diagnostics')).json() as {
    activeCircuits: number;
  };
  expect(afterClose.activeCircuits).toBeLessThan(beforeClose.activeCircuits);
});
