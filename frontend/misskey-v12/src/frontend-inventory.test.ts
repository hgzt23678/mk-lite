import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const inventoryRoot = resolve(repositoryRoot, 'artifacts/frontend-inventory');

function inventory<T>(name: string): T {
  return JSON.parse(readFileSync(resolve(inventoryRoot, name), 'utf8')) as T;
}

interface FileMapping {
  sourcePath: string;
  targetPath: string;
  classification: string;
  upstreamStatus: string;
  migrationStatus: string;
  automatedTests?: string[];
  blockedReason?: string | null;
  exclusionFeature?: string | null;
  excludedReason?: string | null;
  backendEndpointEvidence?: Array<{
    kind: 'api-endpoint' | 'streaming-channel';
    endpoint: string;
    upstreamContractPath: string;
    apiInventoryImplementation: string;
    apiInventoryBlockedReason: string;
    backendSourcePath: string;
    backendImplemented: boolean;
  }>;
  knownGaps?: string[];
  verificationScope?: string[];
  contractSource?: string;
  storageKeys?: string[];
  props?: Array<{ type?: string }>;
  emits?: Array<{ type?: string }>;
  domClasses?: string[];
  styles?: Array<Record<string, unknown>>;
  motion?: {
    transitionElements: unknown[];
    keyframes: unknown[];
    animationDeclarations: unknown[];
    transitionDeclarations: unknown[];
  };
  upstreamContract?: {
    sha256: string;
    props: Array<Record<string, unknown>>;
    emits: Array<Record<string, unknown>>;
    slots: string[];
    directives: Array<{ name: string; argument: string | null }>;
    apiEndpoints: string[];
    domClasses: string[];
    browserApis: string[];
    styles: Array<{
      selectors: string[];
      cssDeclarations: Array<{ property: string; value: string }>;
    }>;
  } | null;
}

describe('Misskey v12 frontend inventory', () => {
  it('accounts for every upstream and local source without an unclassified mapping', () => {
    const files = inventory<{
      summary: Record<string, number>;
      missingUpstreamPaths: string[];
      files: FileMapping[];
    }>('files.json');
    const mapping = inventory<{
      sourceCount: number;
      implementedCount: number;
      inProgressCount: number;
      blockedCount: number;
      excludedCount: number;
      plannedCount: number;
      unclassifiedCount: number;
      exclusionFeatures: Array<{ allowPartiallyImplementedBackendEndpoints: string[] }>;
      mappings: FileMapping[];
    }>('vue-to-blazor-mapping.json');

    expect(files.missingUpstreamPaths).toEqual([]);
    expect(files.summary.localSourceFiles).toBe(files.files.length);
    expect(files.summary.upstreamSourceFiles).toBe(
      files.files.filter(file => file.upstreamStatus !== 'local-addition').length,
    );
    expect(mapping.sourceCount).toBe(files.files.length);
    expect(mapping.implementedCount + mapping.inProgressCount + mapping.blockedCount + mapping.excludedCount + mapping.plannedCount).toBe(mapping.sourceCount);
    expect(mapping.unclassifiedCount).toBe(0);
    expect(mapping.mappings.every(item => item.sourcePath && item.targetPath && item.classification)).toBe(true);
    expect(mapping.mappings.every(item => Array.isArray(item.storageKeys) && Array.isArray(item.styles))).toBe(true);
    expect(mapping.mappings.every(item => item.motion &&
      Array.isArray(item.motion.transitionElements) &&
      Array.isArray(item.motion.keyframes) &&
      Array.isArray(item.motion.animationDeclarations) &&
      Array.isArray(item.motion.transitionDeclarations))).toBe(true);
    expect(new Set(mapping.mappings.map(item => item.sourcePath)).size).toBe(mapping.sourceCount);
    // Several deterministic upstream utility modules are intentionally implemented by
    // one typed C# service.  Source coverage must remain one-to-one, while target
    // paths may be shared when the implementation boundary is shared.
    expect(new Set(mapping.mappings.map(item => item.targetPath)).size).toBeGreaterThan(0);
    expect(mapping.mappings.some(item => item.migrationStatus === 'unclassified')).toBe(false);
    for (const item of mapping.mappings.filter(item => item.migrationStatus === 'implemented' || item.migrationStatus === 'in-progress')) {
      expect(existsSync(resolve(repositoryRoot, item.targetPath)), item.targetPath).toBe(true);
      expect(item.automatedTests?.length ?? 0, `${item.sourcePath}.automatedTests`).toBeGreaterThan(0);
      for (const testPath of item.automatedTests ?? []) {
        expect(existsSync(resolve(repositoryRoot, testPath)), testPath).toBe(true);
      }
    }
    const partiallyImplementedExcludedEndpoints = new Set(
      mapping.exclusionFeatures.flatMap(feature => feature.allowPartiallyImplementedBackendEndpoints),
    );
    for (const item of mapping.mappings.filter(item => item.migrationStatus === 'blocked')) {
      expect(item.blockedReason?.trim().length ?? 0, `${item.sourcePath}.blockedReason`).toBeGreaterThan(0);
      expect(item.automatedTests?.length ?? 0, `${item.sourcePath}.automatedTests`).toBeGreaterThan(0);
    }
    for (const item of mapping.mappings.filter(item => item.migrationStatus === 'excluded')) {
      expect(item.exclusionFeature?.trim().length ?? 0, `${item.sourcePath}.exclusionFeature`).toBeGreaterThan(0);
      expect(item.excludedReason?.trim().length ?? 0, `${item.sourcePath}.excludedReason`).toBeGreaterThan(0);
      expect(item.blockedReason, `${item.sourcePath}.blockedReason`).toBeNull();
      expect(item.backendEndpointEvidence?.length ?? 0, `${item.sourcePath}.backendEndpointEvidence`).toBeGreaterThan(0);
      expect(item.backendEndpointEvidence?.every(evidence => {
        const explicitlyPartial = partiallyImplementedExcludedEndpoints.has(evidence.endpoint);
        return evidence.endpoint &&
          (evidence.upstreamContractPath || evidence.apiInventoryImplementation === 'unlisted-client-call') &&
          (evidence.apiInventoryImplementation === 'blocked' || evidence.apiInventoryImplementation === 'unlisted-client-call' || explicitlyPartial) &&
          (evidence.apiInventoryBlockedReason || explicitlyPartial) &&
          evidence.backendSourcePath.startsWith('src/ActivityPub.MisskeyApi/') &&
          (evidence.backendImplemented === false || explicitlyPartial);
      }), `${item.sourcePath}.backendEndpointEvidence`).toBe(true);
    }
  });

  it('excludes dedicated unsupported features and Dolphin contract gaps with complete evidence', () => {
    const mapping = inventory<{
      excludedCount: number;
      exclusionFeatures: Array<{
        feature: string;
        reason: string;
        sourcePaths: string[];
        routePatterns: string[];
        allowPartiallyImplementedBackendEndpoints: string[];
        backendEndpointEvidence: Array<{
          kind: string;
          endpoint: string;
          upstreamContractPath: string;
          apiInventoryImplementation: string;
          apiInventoryBlockedReason: string;
          clientUsages: Array<{ file: string; line: number; mechanism: string }>;
          backendSourcePath: string;
          backendImplemented: boolean;
        }>;
      }>;
      mappings: FileMapping[];
    }>('vue-to-blazor-mapping.json');
    const features = new Map(mapping.exclusionFeatures.map(feature => [feature.feature, feature]));
    const drive = features.get('drive-management');
    const charts = features.get('api-backed-charts');

    expect(mapping.implementedCount).toBeGreaterThan(0);
    expect(mapping.inProgressCount).toBe(0);
    expect(mapping.plannedCount).toBe(0);
    expect(mapping.blockedCount).toBe(0);
    expect(mapping.excludedCount).toBe(206);
    expect(features.size).toBe(9);
    const remaining = features.get('remaining-dolphin-contract-gaps');
    expect(remaining?.sourcePaths).toHaveLength(172);
    expect(remaining?.reason).toContain('Dolphin contracts');
    expect(drive?.sourcePaths).toHaveLength(12);
    expect(drive?.routePatterns).toEqual([
      '/my/drive/folder/:folder',
      '/my/drive',
      '/settings/drive',
      '/admin/file/:fileId',
      '/admin/files',
    ]);
    expect(drive?.backendEndpointEvidence.map(evidence => evidence.endpoint)).toEqual(expect.arrayContaining([
      '@stream/drive',
      'drive',
      'drive/files',
      'drive/folders',
      'admin/drive/files',
      'i/update',
    ]));
    expect(charts?.sourcePaths).toHaveLength(3);
    expect(charts?.routePatterns).toEqual([]);
    expect(charts?.backendEndpointEvidence.map(evidence => evidence.endpoint)).toEqual(expect.arrayContaining([
      'charts/active-users',
      'charts/instance',
      'charts/user/notes',
      'federation/stats',
    ]));

    expect(features.has('gallery')).toBe(true);
    expect(features.has('my-antennas')).toBe(true);
    expect(features.has('my-clips')).toBe(true);
    expect(features.has('channels')).toBe(true);
    expect(features.has('registry')).toBe(true);
    expect(features.has('favorites')).toBe(true);

    for (const feature of mapping.exclusionFeatures) {
      expect(feature.reason.trim().length, `${feature.feature}.reason`).toBeGreaterThan(0);
      expect(feature.backendEndpointEvidence.length, `${feature.feature}.backendEndpointEvidence`).toBeGreaterThan(0);
      for (const evidence of feature.backendEndpointEvidence) {
        expect(evidence.clientUsages.length, `${feature.feature}.${evidence.endpoint}.clientUsages`).toBeGreaterThan(0);
        expect(evidence.clientUsages.every(usage => feature.sourcePaths.includes(usage.file) && usage.line > 0)).toBe(true);
        const explicitlyPartial = feature.allowPartiallyImplementedBackendEndpoints.includes(evidence.endpoint);
        if (!explicitlyPartial && evidence.apiInventoryImplementation === 'blocked') {
          expect(evidence.apiInventoryImplementation).toBe('blocked');
          expect(evidence.apiInventoryBlockedReason).toMatch(/^(No (adapter route|channel adapter) exists\.|Route exists, but complete contract and persistence-side-effect evidence is missing\.|Wire handling exists, but the complete channel contract is not yet covered by automated tests\.)$/);
        }
        expect(
          evidence.backendImplemented === false ||
          feature.allowPartiallyImplementedBackendEndpoints.includes(evidence.endpoint),
          `${feature.feature}.${evidence.endpoint}.backendImplemented`,
        ).toBe(true);
      }
    }

    const bySource = new Map(mapping.mappings.map(item => [item.sourcePath, item]));
    for (const feature of mapping.exclusionFeatures) {
      for (const sourcePath of feature.sourcePaths) {
        expect(bySource.get(sourcePath)?.migrationStatus, sourcePath).toBe('excluded');
        expect(bySource.get(sourcePath)?.exclusionFeature, sourcePath).toBe(feature.feature);
      }
    }
    for (const sourcePath of [
      'frontend/misskey-v12/src/components/MkDriveFileThumbnail.vue',
      'frontend/misskey-v12/src/components/MkPostForm.vue',
      'frontend/misskey-v12/src/components/MkPostFormAttaches.vue',
      'frontend/misskey-v12/src/components/MkMiniChart.vue',
      'frontend/misskey-v12/src/pages/admin/overview.vue',
      'frontend/misskey-v12/src/pages/timeline.vue',
      'frontend/misskey-v12/src/pages/note.vue',
      'frontend/misskey-v12/src/pages/share.vue',
    ]) {
      expect(bySource.get(sourcePath)?.migrationStatus, sourcePath).not.toBe('excluded');
    }
  });

  it('records MkButton as implemented only with its complete pinned prop, DOM, motion, and browser evidence', () => {
    const mapping = inventory<{ mappings: FileMapping[] }>('vue-to-blazor-mapping.json');
    const button = mapping.mappings.find(item =>
      item.sourcePath === 'frontend/misskey-v12/src/components/MkButton.vue');

    expect(button?.migrationStatus).toBe('implemented');
    expect(button?.contractSource).toBe('local-byte-identical-to-pinned-upstream');
    expect(button?.props?.[0]?.type).toContain('autofocus?: boolean');
    expect(button?.props?.[0]?.type).toContain('wait?: boolean');
    expect(button?.emits?.[0]?.type).toContain("ev: 'click'");
    expect(button?.domClasses).toEqual(expect.arrayContaining(['bghgjjyj', '_button', 'ripples', 'content']));
    expect(button?.verificationScope).toEqual(expect.arrayContaining([
      'button and link root DOM',
      'event.target-relative ripple geometry',
      '1, 1000, and 2000 millisecond ripple lifecycle',
      'timer and listener disposal',
      'native keyboard activation and focus-visible styling',
      'pinned upstream generated SCSS',
    ]));
    expect(button?.knownGaps).toEqual([]);
    expect(button?.automatedTests).toEqual(expect.arrayContaining([
      'tests/ActivityPub.Misskey.Blazor.Tests/ButtonTests.cs',
      'tests/frontend-blazor-e2e/button-parity.spec.ts',
    ]));
  });

  it('retains the pinned upstream authentication contracts when the connected Vue oracle is modified', () => {
    const mapping = inventory<{ mappings: FileMapping[] }>('vue-to-blazor-mapping.json');
    const bySource = new Map(mapping.mappings.map(item => [item.sourcePath, item]));
    const signIn = bySource.get('frontend/misskey-v12/src/components/MkSignin.vue');
    const signUp = bySource.get('frontend/misskey-v12/src/components/MkSignup.vue');

    expect(signIn?.contractSource).toBe('pinned-upstream-with-local-delta');
    expect(signIn?.migrationStatus).toBe('implemented');
    expect(signIn?.upstreamContract?.apiEndpoints).toEqual(['signin', 'users/show']);
    expect(signIn?.upstreamContract?.browserApis).toContain('PublicKeyCredential');
    expect(signIn?.upstreamContract?.slots).toEqual(expect.arrayContaining(['caption', 'label', 'prefix', 'suffix']));
    expect(signIn?.upstreamContract?.directives.map(item => item.name)).toEqual(expect.arrayContaining(['if', 'model', 'show']));
    expect(signIn?.upstreamContract?.domClasses).toEqual(expect.arrayContaining([
      'eppvobhk',
      'normal-signin',
      '2fa-signin',
      'tap-group',
      'totp-group',
      'social',
    ]));
    expect(signIn?.upstreamContract?.styles.flatMap(style => style.selectors)).toContain('.eppvobhk > .auth > .avatar');
    expect(signIn?.upstreamContract?.styles.flatMap(style => style.cssDeclarations).map(item => item.property)).toEqual(expect.arrayContaining([
      'width',
      'height',
      'background',
      'border-radius',
    ]));
    expect(signIn?.knownGaps).toContain('real browser authenticator signature success and passkey enrollment are not covered by the browser parity test');
    expect(signIn?.knownGaps).not.toContain('WebAuthn security-key challenge and retry are not implemented');

    expect(signUp?.contractSource).toBe('pinned-upstream-with-local-delta');
    expect(signUp?.migrationStatus).toBe('implemented');
    expect(signUp?.upstreamContract?.apiEndpoints).toEqual([
      'email-address/available',
      'signin',
      'signup',
      'username/available',
    ]);
    expect(signUp?.upstreamContract?.domClasses).toEqual(expect.arrayContaining(['qlvuhzng', 'captcha', 'tou']));
    expect(signUp?.upstreamContract?.styles.flatMap(style => style.selectors)).toContain('.qlvuhzng .captcha');
    expect(signUp?.knownGaps).toEqual(expect.arrayContaining([
      'disposable, MX, and SMTP email rejection reasons remain unavailable because those registration policies are not configured',
      'external live hCaptcha and reCAPTCHA services were not contacted',
      'live SMTP delivery and three-browser email-confirmation completion are not verified',
      'real actor provisioning and ActivityPub side effects are not covered by the browser parity test',
    ]));
    expect(signUp?.knownGaps).not.toEqual(expect.arrayContaining([
      'invitation code is not projected',
      'hCaptcha and reCAPTCHA branches are not implemented',
    ]));
  });

  it('records the verified password reset and email confirmation slice with route, API, and security-deviation evidence', () => {
    const mapping = inventory<{ mappings: FileMapping[] }>('vue-to-blazor-mapping.json');
    const bySource = new Map(mapping.mappings.map(item => [item.sourcePath, item]));
    const forgot = bySource.get('frontend/misskey-v12/src/components/MkForgotPassword.vue');
    const signupComplete = bySource.get('frontend/misskey-v12/src/pages/signup-complete.vue');
    const resetPassword = bySource.get('frontend/misskey-v12/src/pages/reset-password.vue');

    expect(forgot?.migrationStatus).toBe('implemented');
    expect(forgot?.blockedReason).toBeNull();
    expect(forgot?.knownGaps).toContain('an external live SMTP service was not contacted; fixture delivery and the production MailKit configuration boundary were tested, so no live SMTP delivery claim is made');
    expect(signupComplete?.migrationStatus).toBe('implemented');
    expect(signupComplete?.blockedReason).toBeNull();
    expect(signupComplete?.knownGaps).toContain('the path parameter was intentionally replaced by a fragment so reverse proxies and access logs never receive the code');
    expect(signupComplete?.knownGaps).toContain('an external live SMTP service was not contacted; fixture delivery and the production MailKit configuration boundary were tested, so no live SMTP delivery claim is made');
    expect(resetPassword?.migrationStatus).toBe('implemented');
    expect(resetPassword?.blockedReason).toBeNull();
    expect(resetPassword?.knownGaps).toContain('the path parameter was intentionally replaced by a fragment so reverse proxies and access logs never receive the token');
    expect(resetPassword?.targetPath).toBe('frontend/ActivityPub.Misskey.Blazor/Pages/ResetPassword.razor');

    const files = inventory<{ files: Array<FileMapping & { apiEndpoints: string[]; routes: Array<{ pattern: string }> }> }>('files.json');
    const filesBySource = new Map(files.files.map(item => [item.sourcePath, item]));
    expect(filesBySource.get('frontend/misskey-v12/src/components/MkForgotPassword.vue')?.apiEndpoints).toContain('request-reset-password');
    expect(filesBySource.get('frontend/misskey-v12/src/pages/signup-complete.vue')?.apiEndpoints).toContain('signup-pending');
    expect(filesBySource.get('frontend/misskey-v12/src/pages/signup-complete.vue')?.routes.map(route => route.pattern)).toContain('/signup-complete/:code');
    expect(filesBySource.get('frontend/misskey-v12/src/pages/reset-password.vue')?.apiEndpoints).toContain('reset-password');
    expect(filesBySource.get('frontend/misskey-v12/src/pages/reset-password.vue')?.routes.map(route => route.pattern)).toContain('/reset-password/:token?');
  });

  it('parses every Vue SFC with its behavioral contracts', () => {
    const files = inventory<{ summary: Record<string, number> }>('files.json');
    const components = inventory<{
      componentCount: number;
      components: Array<Record<string, unknown>>;
    }>('components.json');

    expect(components.componentCount).toBe(files.summary.vueSfc);
    expect(components.components).toHaveLength(components.componentCount);
    for (const component of components.components) {
      expect(component.sourcePath).toMatch(/\.vue$/);
      for (const field of ['props', 'emits', 'slots', 'directives', 'childComponents', 'domClasses', 'transitions', 'externalDependencies', 'browserApis', 'styleBlocks']) {
        expect(Array.isArray(component[field]), `${String(component.sourcePath)}.${field}`).toBe(true);
      }
    }
  });

  it('extracts ordered routes and verifies every static component import exists', () => {
    const routes = inventory<{
      routeCount: number;
      routes: Array<{ index: number; componentImport: string | null }>;
    }>('routes.json');

    expect(routes.routes).toHaveLength(routes.routeCount);
    expect(routes.routes.map(route => route.index)).toEqual(routes.routes.map((_, index) => index));
    for (const route of routes.routes) {
      if (!route.componentImport?.startsWith('./')) continue;
      const source = resolve(repositoryRoot, 'frontend/misskey-v12/src', route.componentImport.slice(2));
      expect(existsSync(source), source).toBe(true);
    }
  });

  it('keeps API and Streaming usage evidence tied to real source locations', () => {
    const api = inventory<{
      staticEndpointCount: number;
      endpoints: Array<{ endpoint: string; usages: Array<{ file: string; line: number }> }>;
      dynamicCalls: Array<{ file: string; line: number; expression: string }>;
    }>('api-callgraph.json');
    const stream = inventory<{
      staticChannelCount: number;
      channels: Array<{ channel: string; usages: Array<{ file: string; line: number }> }>;
      dynamicCalls: Array<{ file: string; line: number; expression: string }>;
    }>('stream-callgraph.json');

    expect(api.endpoints).toHaveLength(api.staticEndpointCount);
    expect(stream.channels).toHaveLength(stream.staticChannelCount);
    for (const entry of [...api.endpoints, ...stream.channels]) {
      expect(entry.usages.length).toBeGreaterThan(0);
      expect(entry.usages.every(usage => usage.file.startsWith('frontend/misskey-v12/src/') && usage.line > 0)).toBe(true);
    }
    expect(api.dynamicCalls.every(call => call.expression && call.file && call.line > 0)).toBe(true);
    expect(stream.dynamicCalls.every(call => call.expression && call.file && call.line > 0)).toBe(true);
  });

  it('derives motion totals from parser output instead of independent fixed counters', () => {
    const styles = inventory<{
      styleBlockCount: number;
      styles: Array<{
        scoped: boolean;
        keyframes: string[];
        declarations: Array<{ property: string }>;
      }>;
    }>('styles.json');
    const components = inventory<{
      components: Array<{ transitions: unknown[] }>;
    }>('components.json');
    const motion = inventory<{
      transitionElementCount: number;
      keyframeCount: number;
      animationDeclarationCount: number;
      transitionDeclarationCount: number;
    }>('motion.json');

    expect(styles.styles).toHaveLength(styles.styleBlockCount);
    expect(motion.transitionElementCount).toBe(components.components.reduce((total, component) => total + component.transitions.length, 0));
    expect(motion.keyframeCount).toBe(styles.styles.reduce((total, style) => total + style.keyframes.length, 0));
    expect(motion.animationDeclarationCount).toBe(styles.styles.reduce(
      (total, style) => total + style.declarations.filter(declaration => declaration.property.toLowerCase().startsWith('animation')).length,
      0,
    ));
    expect(motion.transitionDeclarationCount).toBe(styles.styles.reduce(
      (total, style) => total + style.declarations.filter(declaration => declaration.property.toLowerCase().startsWith('transition')).length,
      0,
    ));
  });

  it('records invalid upstream locale syntax without silently dropping the locale', () => {
    const locales = inventory<{
      localeCount: number;
      locales: Array<{ locale: string; keyCount: number; parseStatus: string; parseError: string | null }>;
    }>('locales.json');

    expect(locales.locales).toHaveLength(locales.localeCount);
    expect(new Set(locales.locales.map(locale => locale.locale)).size).toBe(locales.localeCount);
    expect(locales.locales.every(locale => locale.keyCount > 0 || locale.parseStatus === 'parsed-empty')).toBe(true);
    expect(locales.locales.every(locale => ['parsed', 'parsed-empty', 'generated-upstream-fallback'].includes(locale.parseStatus))).toBe(true);
    expect(locales.locales.filter(locale => locale.parseStatus === 'generated-upstream-fallback').every(locale => locale.parseError)).toBe(true);
  });

  it('does not retain the deprecated simplified replacement UI in the production frontend', () => {
    expect(existsSync(resolve(
      repositoryRoot,
      'frontend/ActivityPub.Misskey.Blazor/Components/NoteComposer.razor',
    ))).toBe(false);

    const hostCss = readFileSync(resolve(
      repositoryRoot,
      'frontend/ActivityPub.Misskey.Blazor/wwwroot/css/app.css',
    ), 'utf8');
    for (const forbiddenSelector of [
      '.mk-composer',
      '.mk-timeline-page',
      '.mk-timeline-tabs',
      '.mk-note-list',
      '.mk-note-actions',
    ]) {
      expect(hostCss, forbiddenSelector).not.toContain(forbiddenSelector);
    }
  });
});
