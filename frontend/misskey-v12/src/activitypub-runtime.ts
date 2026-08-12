export interface FrontendCapabilities {
  publicTimeline: boolean;
  localTimeline: boolean;
  homeTimeline: boolean;
  compose: boolean;
  favourite: boolean;
  renote: boolean;
  mute: boolean;
  mediaUpload: boolean;
  notifications: boolean;
  streaming: boolean;
}

export interface RuntimeConfig {
  enabled: boolean;
  instanceName: string;
  authority: string;
  clientId: string;
  scopes: string[];
  redirectUri: string;
  postLogoutRedirectUri: string;
  sourceUrl: string | null;
  capabilities: FrontendCapabilities;
}

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

export function validateRuntimeConfig(value: unknown, currentOrigin = window.location.origin): RuntimeConfig {
  if (!isRecord(value) || typeof value.enabled !== 'boolean' || !isRecord(value.capabilities)) {
    throw new Error('Invalid frontend configuration');
  }

  const authority = new URL(readString(value, 'authority'));
  const redirectUri = new URL(readString(value, 'redirectUri'));
  const postLogoutRedirectUri = new URL(readString(value, 'postLogoutRedirectUri'));
  if (authority.protocol !== 'https:' || redirectUri.origin !== currentOrigin || postLogoutRedirectUri.origin !== currentOrigin ||
      redirectUri.pathname !== '/app/auth/callback' || postLogoutRedirectUri.pathname !== '/app/') {
    throw new Error('Unsafe OIDC frontend configuration');
  }
  if (!Array.isArray(value.scopes) || value.scopes.length === 0 ||
      value.scopes.some(scope => typeof scope !== 'string' || scope.length === 0 || scope.length > 128)) {
    throw new Error('Invalid OAuth scopes');
  }

  const capabilityNames: (keyof FrontendCapabilities)[] = [
    'publicTimeline', 'localTimeline', 'homeTimeline', 'compose', 'favourite',
    'renote', 'mute', 'mediaUpload', 'notifications', 'streaming',
  ];
  const capabilities = {} as FrontendCapabilities;
  for (const name of capabilityNames) {
    if (typeof value.capabilities[name] !== 'boolean') throw new Error(`Invalid capability: ${name}`);
    capabilities[name] = value.capabilities[name] as boolean;
  }

  const sourceUrl = value.sourceUrl == null ? null : new URL(readString(value, 'sourceUrl'));
  if (sourceUrl && sourceUrl.protocol !== 'https:') throw new Error('Source URL must use HTTPS');
  return {
    enabled: value.enabled,
    instanceName: readString(value, 'instanceName'),
    authority: authority.href.replace(/\/$/, ''),
    clientId: readString(value, 'clientId'),
    scopes: value.scopes as string[],
    redirectUri: redirectUri.href,
    postLogoutRedirectUri: postLogoutRedirectUri.href,
    sourceUrl: sourceUrl?.href ?? null,
    capabilities,
  };
}

export async function fetchRuntimeConfig(fetcher: typeof fetch = fetch): Promise<RuntimeConfig> {
  const response = await fetcher('/api/frontend/config', {
    headers: { Accept: 'application/json' },
    cache: 'no-store',
    credentials: 'omit',
    redirect: 'error',
  });
  if (!response.ok) throw new Error(`Frontend configuration is unavailable (${response.status})`);
  return validateRuntimeConfig(await response.json());
}

export function runtimeConfig(): RuntimeConfig {
  const value = window.__ACTIVITYPUB_RUNTIME_CONFIG__;
  if (!value) throw new Error('Frontend runtime configuration has not been initialized');
  return value;
}
