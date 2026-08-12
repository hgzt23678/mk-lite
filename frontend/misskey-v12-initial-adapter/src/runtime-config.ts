import type { FrontendCapabilities, RuntimeConfig } from './types';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readString(record: Record<string, unknown>, name: string): string {
  const value = record[name];
  if (typeof value !== 'string' || value.length === 0 || value.length > 2048) {
    throw new Error(`Invalid frontend configuration field: ${name}`);
  }

  return value;
}

function readCapabilities(value: unknown): FrontendCapabilities {
  if (!isRecord(value)) {
    throw new Error('Invalid frontend capabilities');
  }

  const names: (keyof FrontendCapabilities)[] = [
    'publicTimeline',
    'localTimeline',
    'homeTimeline',
    'compose',
    'favourite',
    'renote',
    'mute',
    'mediaUpload',
    'notifications',
    'streaming',
  ];
  const result = {} as FrontendCapabilities;
  for (const name of names) {
    if (typeof value[name] !== 'boolean') {
      throw new Error(`Invalid frontend capability: ${name}`);
    }
    result[name] = value[name];
  }
  return result;
}

export function validateRuntimeConfig(value: unknown, currentOrigin = window.location.origin): RuntimeConfig {
  if (!isRecord(value) || typeof value.enabled !== 'boolean') {
    throw new Error('Invalid frontend configuration');
  }

  const authority = new URL(readString(value, 'authority'));
  const redirectUri = new URL(readString(value, 'redirectUri'));
  const postLogoutRedirectUri = new URL(readString(value, 'postLogoutRedirectUri'));
  const sourceUrl = new URL(readString(value, 'sourceUrl'));
  if (authority.protocol !== 'https:' || sourceUrl.protocol !== 'https:') {
    throw new Error('OIDC authority and corresponding source must use HTTPS');
  }
  if (redirectUri.origin !== currentOrigin || postLogoutRedirectUri.origin !== currentOrigin) {
    throw new Error('OIDC redirect URI must be same-origin');
  }
  if (redirectUri.pathname !== '/app/auth/callback' || postLogoutRedirectUri.pathname !== '/app/') {
    throw new Error('OIDC redirect path is not recognized');
  }
  if (!Array.isArray(value.scopes) || value.scopes.length === 0 ||
      value.scopes.some(scope => typeof scope !== 'string' || scope.length === 0 || scope.length > 128)) {
    throw new Error('Invalid frontend OAuth scopes');
  }

  return {
    enabled: value.enabled,
    instanceName: readString(value, 'instanceName'),
    authority: authority.href.replace(/\/$/, ''),
    clientId: readString(value, 'clientId'),
    scopes: value.scopes as string[],
    redirectUri: redirectUri.href,
    postLogoutRedirectUri: postLogoutRedirectUri.href,
    sourceUrl: sourceUrl.href,
    capabilities: readCapabilities(value.capabilities),
  };
}

export async function fetchRuntimeConfig(fetcher: typeof fetch = fetch): Promise<RuntimeConfig> {
  const response = await fetcher('/api/frontend/config', {
    headers: { Accept: 'application/json' },
    cache: 'no-store',
    credentials: 'omit',
    redirect: 'error',
  });
  if (!response.ok) {
    throw new Error(`Frontend configuration is unavailable (${response.status})`);
  }
  return validateRuntimeConfig(await response.json());
}
