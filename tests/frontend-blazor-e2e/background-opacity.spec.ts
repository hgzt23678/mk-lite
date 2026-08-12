import { expect, test, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

type GeneratedTheme = {
  id: string;
  base: 'light' | 'dark';
  sourceFile: string;
  properties: Record<string, string>;
};

const themeCatalog = JSON.parse(readFileSync(
  resolve(__dirname, '../../frontend/ActivityPub.Misskey.Blazor/wwwroot/themes/catalog.json'),
  'utf8'
)) as { themes: GeneratedTheme[] };

const browserErrors = new WeakMap<Page, string[]>();

test.beforeEach(async ({ page }) => {
  const errors: string[] = [];
  browserErrors.set(page, errors);
  page.on('console', message => {
    if (message.type() === 'error') errors.push(`console: ${message.text()}`);
  });
  page.on('pageerror', error => errors.push(`pageerror: ${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400) {
      errors.push(`http ${response.status()}: ${new URL(response.url()).pathname}`);
    }
  });
});

test.afterEach(async ({ page }) => {
  expect(browserErrors.get(page) ?? [], 'browser errors or unclassified HTTP failures').toEqual([]);
});

const viewports = [
  { width: 360, height: 800 },
  { width: 390, height: 844 },
  { width: 768, height: 1024 },
  { width: 1440, height: 900 },
  { width: 1920, height: 1080 }
];

async function expectOpaqueApplicationSurfaces(page: Page): Promise<void> {
  const surfaces = await page.evaluate(() => {
    const alpha = (color: string): number | null => {
      const canvas = document.createElement('canvas');
      canvas.width = 1;
      canvas.height = 1;
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (context === null) return null;
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = color;
      context.fillRect(0, 0, 1, 1);
      return context.getImageData(0, 0, 1, 1).data[3];
    };
    return ['html', 'body', '.mk-app'].map(selector => {
      const element = document.querySelector(selector);
      if (!(element instanceof HTMLElement)) return { selector, color: null, alpha: null };
      const color = getComputedStyle(element).backgroundColor;
      return { selector, color, alpha: alpha(color) };
    });
  });

  for (const surface of surfaces) {
    expect(surface.color, `${surface.selector} has no computed background`).not.toBeNull();
    expect(surface.alpha, `${surface.selector} background is ${surface.color}`).toBe(255);
  }
}

async function expectOpaqueAuthenticatedSurfaces(page: Page): Promise<void> {
  const surfaces = await page.evaluate(() => {
    const alpha = (color: string): number | null => {
      const canvas = document.createElement('canvas');
      canvas.width = 1;
      canvas.height = 1;
      const context = canvas.getContext('2d', { willReadFrequently: true });
      if (context === null) return null;
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = color;
      context.fillRect(0, 0, 1, 1);
      return context.getImageData(0, 0, 1, 1).data[3];
    };
    return ['html', 'body', '.dkgtipfy', '.dkgtipfy > .contents', '.cmuxhskf > .tl'].map(selector => {
      const element = document.querySelector(selector);
      if (!(element instanceof HTMLElement)) return { selector, color: null, alpha: null };
      const color = getComputedStyle(element).backgroundColor;
      return { selector, color, alpha: alpha(color) };
    });
  });

  for (const surface of surfaces) {
    expect(surface.color, `${surface.selector} has no computed background`).not.toBeNull();
    expect(surface.alpha, `${surface.selector} background is ${surface.color}`).toBe(255);
  }
}

async function expectOpaqueAboutPanel(page: Page): Promise<void> {
  const currentShell = page.locator('.mk-app, .dkgtipfy').first();
  if (await currentShell.count() > 0) {
    // A full navigation while Interactive Server is still importing its first-render modules
    // aborts those requests in Firefox/WebKit.  That is not a user-reachable click because the
    // prerendered shell is inert until attachment completes, so wait for the same boundary here.
    await expect.poll(() => currentShell.getAttribute('inert')).toBeNull();
    await page.waitForLoadState('networkidle');
  }

  await page.goto('/about-misskey', { waitUntil: 'networkidle' });
  const about = page.locator('.znqjceqz > .about');
  await expect(about).toBeVisible();
  const alpha = await about.evaluate(element => {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (context === null) return null;
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(alpha, 'Misskey about panel background is transparent').toBe(255);
}

test('SSR theme bootstrap and the RCL scoped stylesheet never expose a transparent frame', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.addInitScript(() => {
    localStorage.setItem('theme', JSON.stringify({
      bg: 'rgb(24, 31, 28)',
      panel: 'rgb(35, 43, 39)',
      popup: 'rgb(42, 52, 47)'
    }));
    localStorage.setItem('colorSchema', 'dark');
  });

  let releaseTheme!: () => void;
  const themeGate = new Promise<void>(resolve => {
    releaseTheme = resolve;
  });
  await page.route('**/js/theme*.js', async route => {
    await themeGate;
    await route.continue();
  });

  await page.goto('/', { waitUntil: 'commit' });
  await expect(page.locator('.mk-app')).toBeAttached();
  await expect.poll(() => page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--bg').trim())).not.toBe('');

  const beforeHydration = await page.evaluate(() => {
    const alpha = (color: string): number => {
      const context = document.createElement('canvas').getContext('2d');
      if (context === null) throw new Error('Canvas 2D context unavailable');
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = color;
      context.fillRect(0, 0, 1, 1);
      return context.getImageData(0, 0, 1, 1).data[3];
    };
    const read = (selector: string) => {
      const element = document.querySelector(selector);
      if (!(element instanceof HTMLElement)) throw new Error(`${selector} is unavailable`);
      const color = getComputedStyle(element).backgroundColor;
      return { selector, color, alpha: alpha(color) };
    };
    return ['html', 'body', '.mk-app'].map(read);
  });
  expect(beforeHydration.map(sample => sample.alpha)).toEqual([255, 255, 255]);

  await page.evaluate(() => {
    type OpacityAuditWindow = Window & {
      __backgroundOpacitySamples?: Array<Array<{ selector: string; color: string; alpha: number }>>;
      __backgroundOpacitySampling?: boolean;
    };
    const auditWindow = window as OpacityAuditWindow;
    auditWindow.__backgroundOpacitySamples = [];
    auditWindow.__backgroundOpacitySampling = true;
    const alpha = (color: string): number => {
      const context = document.createElement('canvas').getContext('2d');
      if (context === null) throw new Error('Canvas 2D context unavailable');
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = color;
      context.fillRect(0, 0, 1, 1);
      return context.getImageData(0, 0, 1, 1).data[3];
    };
    const sample = () => {
      if (!auditWindow.__backgroundOpacitySampling) return;
      auditWindow.__backgroundOpacitySamples!.push(['html', 'body', '.mk-app'].map(selector => {
        const element = document.querySelector(selector);
        if (!(element instanceof HTMLElement)) return { selector, color: 'missing', alpha: 0 };
        const color = getComputedStyle(element).backgroundColor;
        return { selector, color, alpha: alpha(color) };
      }));
      requestAnimationFrame(sample);
    };
    // Capture the pre-hydration state synchronously as the baseline. WebKit can finish the
    // small theme module before its first animation frame is dispatched, so relying on the
    // first RAF alone would leave an empty audit despite no transparent frame being rendered.
    sample();
  });

  releaseTheme();
  await expect(page.locator('html')).toHaveAttribute('data-theme-bootstrap', 'applied');
  await page.waitForLoadState('load');
  await page.waitForTimeout(50);
  const hydration = await page.evaluate(() => {
    type OpacityAuditWindow = Window & {
      __backgroundOpacitySamples?: Array<Array<{ selector: string; color: string; alpha: number }>>;
      __backgroundOpacitySampling?: boolean;
    };
    const auditWindow = window as OpacityAuditWindow;
    auditWindow.__backgroundOpacitySampling = false;
    return {
      samples: auditWindow.__backgroundOpacitySamples ?? [],
      background: getComputedStyle(document.documentElement).getPropertyValue('--bg').trim()
    };
  });
  expect(hydration.samples.length).toBeGreaterThan(0);
  expect(hydration.samples.flat().every(sample => sample.alpha === 255)).toBeTruthy();
  expect(hydration.background).toBe('rgb(24, 31, 28)');

  const scopedStylesheetHref = await page.locator('link[rel="stylesheet"][href*="bundle.scp.css"]')
    .getAttribute('href');
  expect(scopedStylesheetHref).not.toBeNull();
  const scopedStylesheet = await page.request.get(new URL(scopedStylesheetHref!, page.url()).href);
  expect(scopedStylesheet.status()).toBe(200);
  expect(scopedStylesheet.headers()['content-type']).toContain('text/css');
  expect(await scopedStylesheet.text()).toMatch(/\.mk-acct\s*>\s*\.host\[b-[a-z0-9]+\]/u);
});

test('login and registration overlays preserve the oracle backdrop and opaque panel surfaces', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/', { waitUntil: 'networkidle' });
  await expect.poll(() => page.locator('.mk-app').getAttribute('inert')).toBeNull();

  const assertAuthenticationOverlay = async (trigger: string, label: string): Promise<void> => {
    await page.locator(trigger).click();
    const modal = page.locator(`body > .qzhlnise.dialog[aria-label="${label}"]`);
    const panel = modal.locator(':scope > .content > .ebkgoccj > .body');
    await expect(panel).toBeVisible();
    const surfaces = await modal.evaluate(element => {
      const alpha = (color: string): number => {
        const context = document.createElement('canvas').getContext('2d');
        if (context === null) throw new Error('Canvas 2D context unavailable');
        context.clearRect(0, 0, 1, 1);
        context.fillStyle = color;
        context.fillRect(0, 0, 1, 1);
        return context.getImageData(0, 0, 1, 1).data[3];
      };
      const backdrop = element.querySelector(':scope > .bg');
      const body = element.querySelector(':scope > .content > .ebkgoccj > .body');
      if (!(backdrop instanceof HTMLElement) || !(body instanceof HTMLElement)) {
        throw new Error('Authentication overlay is incomplete');
      }
      const backdropColor = getComputedStyle(backdrop).backgroundColor;
      const panelColor = getComputedStyle(body).backgroundColor;
      const app = document.querySelector('.mk-app');
      if (!(app instanceof HTMLElement)) throw new Error('Visitor shell is unavailable');
      const appColor = getComputedStyle(app).backgroundColor;
      return {
        backdropColor,
        backdropAlpha: alpha(backdropColor),
        panelColor,
        panelAlpha: alpha(panelColor),
        appAlpha: alpha(appColor),
        modalVariable: getComputedStyle(document.documentElement).getPropertyValue('--modalBg').trim(),
        panelVariable: getComputedStyle(document.documentElement).getPropertyValue('--panel').trim()
      };
    });
    expect(surfaces.backdropColor).toBe(surfaces.modalVariable);
    expect(surfaces.backdropAlpha).toBe(128);
    expect(surfaces.panelColor).toBe(surfaces.panelVariable);
    expect(surfaces.panelAlpha).toBe(255);
    expect(surfaces.appAlpha).toBe(255);

    await modal.locator(':scope > .content > .ebkgoccj > .header > button[aria-label="閉じる"]').click();
    await expect(modal).toHaveCount(0);
  };

  await assertAuthenticationOverlay('[data-cy-signin]', 'ログイン');
  await assertAuthenticationOverlay('[data-cy-signup]', '新規登録');
});

test('enhanced navigation keeps every rendered frame opaque', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' });
  await expect.poll(() => page.locator('.mk-app').getAttribute('inert')).toBeNull();
  await page.evaluate(() => {
    type NavigationAuditWindow = Window & {
      __navigationOpacitySamples?: Array<{ html: number; body: number; app: number | null }>;
      __navigationOpacitySampling?: boolean;
    };
    const auditWindow = window as NavigationAuditWindow;
    auditWindow.__navigationOpacitySamples = [];
    auditWindow.__navigationOpacitySampling = true;
    const alpha = (element: Element): number => {
      const context = document.createElement('canvas').getContext('2d');
      if (context === null) throw new Error('Canvas 2D context unavailable');
      context.clearRect(0, 0, 1, 1);
      context.fillStyle = getComputedStyle(element).backgroundColor;
      context.fillRect(0, 0, 1, 1);
      return context.getImageData(0, 0, 1, 1).data[3];
    };
    const sample = () => {
      if (!auditWindow.__navigationOpacitySampling) return;
      const app = document.querySelector('.mk-app');
      auditWindow.__navigationOpacitySamples!.push({
        html: alpha(document.documentElement),
        body: alpha(document.body),
        app: app === null ? null : alpha(app)
      });
      requestAnimationFrame(sample);
    };
    requestAnimationFrame(sample);
  });

  await page.locator('.rsqzvsbo > .top > .main > button.menu').click();
  const aboutLink = page.locator('body > .qzhlnise.popup a[href="/about-misskey"]');
  await expect(aboutLink).toBeVisible();
  await aboutLink.click();
  await expect(page.locator('.znqjceqz > .about')).toBeVisible();
  await page.waitForTimeout(100);
  const samples = await page.evaluate(() => {
    type NavigationAuditWindow = Window & {
      __navigationOpacitySamples?: Array<{ html: number; body: number; app: number | null }>;
      __navigationOpacitySampling?: boolean;
    };
    const auditWindow = window as NavigationAuditWindow;
    auditWindow.__navigationOpacitySampling = false;
    return auditWindow.__navigationOpacitySamples ?? [];
  });
  expect(samples.length).toBeGreaterThan(0);
  expect(samples.every(sample =>
    sample.html === 255 && sample.body === 255 && (sample.app === null || sample.app === 255))).toBeTruthy();
  await expectOpaqueApplicationSurfaces(page);
  const aboutAlpha = await page.locator('.znqjceqz > .about').evaluate(element => {
    const context = document.createElement('canvas').getContext('2d');
    if (context === null) throw new Error('Canvas 2D context unavailable');
    context.clearRect(0, 0, 1, 1);
    context.fillStyle = getComputedStyle(element).backgroundColor;
    context.fillRect(0, 0, 1, 1);
    return context.getImageData(0, 0, 1, 1).data[3];
  });
  expect(aboutAlpha).toBe(255);
});

for (const viewport of viewports) {
  test(`light background is opaque at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.emulateMedia({ colorScheme: 'light' });
    const response = await page.goto('/');
    expect(response?.status()).toBe(200);
    await expect(page.locator('.mk-app')).toBeVisible();
    await expectOpaqueApplicationSurfaces(page);
    await expectOpaqueAboutPanel(page);
  });

  test(`dark background is opaque at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto('/');
    await expect(page.locator('.mk-app')).toBeVisible();
    await expectOpaqueApplicationSurfaces(page);
    await expectOpaqueAboutPanel(page);
  });
}

test('opaque custom theme remains opaque in every engine', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('theme', JSON.stringify({
      accent: 'rgb(151, 191, 39)',
      bg: 'rgb(24, 31, 28)',
      fg: 'rgb(236, 241, 232)',
      panel: 'rgb(35, 43, 39)',
      popup: 'rgb(42, 52, 47)',
      divider: 'rgba(255, 255, 255, 0.12)',
      error: 'rgb(236, 65, 55)'
    }));
    localStorage.setItem('colorSchema', 'dark');
  });
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('data-theme-bootstrap', 'applied');
  await expectOpaqueApplicationSurfaces(page);
  await expectOpaqueAboutPanel(page);
});

test('transparent custom surface is rejected and cannot make the page transparent', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('theme', JSON.stringify({
      bg: 'rgba(0, 0, 0, 0)',
      panel: 'rgba(0, 0, 0, 0)'
    }));
    localStorage.setItem('colorSchema', 'dark');
  });
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('data-theme-bootstrap', 'rejected');
  await expectOpaqueApplicationSurfaces(page);
  await expectOpaqueAboutPanel(page);
});

test('transparent custom popup is rejected before a modal can expose the page background', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('theme', JSON.stringify({
      bg: 'rgb(24, 31, 28)',
      panel: 'rgb(35, 43, 39)',
      popup: 'rgba(35, 43, 39, 0)'
    }));
    localStorage.setItem('colorSchema', 'dark');
  });
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('data-theme-bootstrap', 'rejected');
  await expectOpaqueApplicationSurfaces(page);
  await expectOpaqueAboutPanel(page);
});

for (const theme of themeCatalog.themes) {
  test(`Misskey 12 theme ${theme.sourceFile} has opaque root surfaces`, async ({ page }) => {
    await page.addInitScript(({ definition }) => {
      localStorage.setItem('theme', JSON.stringify(definition.properties));
      localStorage.setItem('colorSchema', definition.base);
      localStorage.setItem('themeId', definition.id);
    }, { definition: theme });
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('data-theme-bootstrap', 'applied');
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme.base);
    await expect(page.locator('html')).toHaveAttribute('data-theme-id', theme.id);
    await expectOpaqueApplicationSurfaces(page);
    await expectOpaqueAboutPanel(page);
  });

  test(`authenticated Misskey 12 theme ${theme.sourceFile} has opaque Universal surfaces`, async ({ page }) => {
    await page.addInitScript(({ definition }) => {
      localStorage.setItem('theme', JSON.stringify(definition.properties));
      localStorage.setItem('colorSchema', definition.base);
      localStorage.setItem('themeId', definition.id);
    }, { definition: theme });
    await page.goto('/__test/sign-in');
    await expect(page.locator('html')).toHaveAttribute('data-theme-bootstrap', 'applied');
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme.base);
    await expect(page.locator('html')).toHaveAttribute('data-theme-id', theme.id);
    await expect(page.locator('.dkgtipfy')).toBeVisible();
    await expectOpaqueAuthenticatedSurfaces(page);
  });
}
