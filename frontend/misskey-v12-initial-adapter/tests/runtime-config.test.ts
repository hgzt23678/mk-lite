import { describe, expect, it, vi } from 'vitest';
import { fetchRuntimeConfig, validateRuntimeConfig } from '../src/runtime-config';

const valid = {
  enabled: true,
  instanceName: 'social.example.com',
  authority: 'https://identity.example.com',
  clientId: 'activitypub-web',
  scopes: ['openid', 'profile', 'offline_access', 'activitypub.read'],
  redirectUri: 'https://social.example.com/app/auth/callback',
  postLogoutRedirectUri: 'https://social.example.com/app/',
  sourceUrl: 'https://git.example.com/social/frontend',
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

describe('runtime configuration', () => {
  it('accepts the public server contract', () => {
    const result = validateRuntimeConfig(valid, 'https://social.example.com');
    expect(result.clientId).toBe('activitypub-web');
    expect(result.capabilities.renote).toBe(true);
  });

  it('rejects a callback on an attacker origin', () => {
    expect(() => validateRuntimeConfig({ ...valid, redirectUri: 'https://attacker.example/callback' }, 'https://social.example.com'))
      .toThrow(/same-origin/);
  });

  it('fetches configuration without credentials or browser cache', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      ...valid,
      redirectUri: 'http://localhost:3000/app/auth/callback',
      postLogoutRedirectUri: 'http://localhost:3000/app/',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    await fetchRuntimeConfig(fetcher as typeof fetch);
    expect(fetcher).toHaveBeenCalledWith('/api/frontend/config', expect.objectContaining({
      cache: 'no-store', credentials: 'omit', redirect: 'error',
    }));
  });
});
