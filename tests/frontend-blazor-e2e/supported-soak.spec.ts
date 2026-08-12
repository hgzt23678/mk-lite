import { expect, test } from '@playwright/test';

test.describe('supported Misskey surface soak', () => {
  test.setTimeout(180_000);

  test('repeatedly navigates supported pages without circuit, stream, or opaque-surface regressions', async ({ page }) => {
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

    // A previous Playwright test can be finishing a disconnected circuit after its
    // assertions. Drain that bounded test-host retention window before starting this
    // soak so transport diagnostics belong to this test only.
    await new Promise(resolve => setTimeout(resolve, 3_000));
    await page.request.post('/__test/reset-diagnostics');
    await page.goto('/__test/sign-in');
    await expect(page).toHaveURL(/\/$/);
    const supportedPaths = [
      '/about',
      '/about#federation',
      '/timeline/local',
      '/my/notifications',
      '/settings/profile',
      '/settings/api',
      '/admin/relays',
      '/@alice',
      '/@alice/clips',
      '/@alice/followers'
    ];

    for (let iteration = 0; iteration < 12; iteration += 1) {
      for (const path of supportedPaths) {
        await page.goto(path, { waitUntil: 'domcontentloaded' });
        const shell = page.locator('body > .mk-app, body > .dkgtipfy');
        await expect(shell, `expected app shell for ${path}`).toHaveCount(1, { timeout: 15_000 });
        await expect.poll(async () => shell.evaluate(element =>
          !(element as HTMLElement).inert)).toBe(true);
        await expect(shell).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
      }
    }

    await new Promise(resolve => setTimeout(resolve, 3_000));
    expect(failures).toEqual([]);
    const diagnostics = await (await page.request.get('/__test/diagnostics')).json() as {
      unhandledExceptions: unknown[];
    };
    const transport = await (await page.request.get('/__test/transport-diagnostics')).json() as {
      applicationNeverCompleted: unknown[];
    };
    expect(diagnostics.unhandledExceptions).toEqual([]);
    expect(transport.applicationNeverCompleted).toEqual([]);
  });
});
