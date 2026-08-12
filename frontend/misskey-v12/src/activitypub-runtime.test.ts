import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchRuntimeConfig, validateRuntimeConfig } from './activitypub-runtime';

const safeConfig = {
  enabled: true,
  instanceName: 'local.example',
  authority: 'https://identity.example/',
  clientId: 'activitypub-web',
  scopes: ['openid', 'profile', 'activitypub.read'],
  redirectUri: 'https://local.example/app/auth/callback',
  postLogoutRedirectUri: 'https://local.example/app/',
  sourceUrl: 'https://source.example/frontend',
  capabilities: {
    publicTimeline: true,
    localTimeline: true,
    homeTimeline: true,
    compose: true,
    favourite: true,
    renote: true,
    mute: true,
    mediaUpload: false,
    notifications: false,
    streaming: false,
  },
};

describe('ActivityPub frontend runtime configuration', () => {
  beforeEach(() => {
    vi.stubGlobal('window', { location: { origin: 'https://local.example' } });
  });

  it('accepts same-origin callbacks and normalizes URL fields', () => {
    const config = validateRuntimeConfig(safeConfig, 'https://local.example');

    expect(config.authority).toBe('https://identity.example');
    expect(config.sourceUrl).toBe('https://source.example/frontend');
  });

  it.each([
    ['an insecure authority', { authority: 'http://identity.example' }],
    ['a cross-origin callback', { redirectUri: 'https://attacker.example/app/auth/callback' }],
    ['an unexpected callback path', { redirectUri: 'https://local.example/callback' }],
    ['an insecure source link', { sourceUrl: 'http://source.example/frontend' }],
  ])('rejects %s', (_name, changed) => {
    expect(() => validateRuntimeConfig({ ...safeConfig, ...changed }, 'https://local.example')).toThrow();
  });

  it('fetches without credentials, cache, or redirects', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify(safeConfig), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));

    await fetchRuntimeConfig(fetcher as typeof fetch);

    expect(fetcher).toHaveBeenCalledWith('/api/frontend/config', {
      headers: { Accept: 'application/json' },
      cache: 'no-store',
      credentials: 'omit',
      redirect: 'error',
    });
  });

  it('does not accept incomplete capability declarations', () => {
    const capabilities = { ...safeConfig.capabilities } as Record<string, boolean>;
    delete capabilities.compose;

    expect(() => validateRuntimeConfig({ ...safeConfig, capabilities }, 'https://local.example')).toThrow(
      'Invalid capability: compose',
    );
  });
});
