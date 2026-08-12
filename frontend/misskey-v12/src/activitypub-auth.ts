import { UserManager, WebStorageStateStore, type User, type UserManagerSettings } from 'oidc-client-ts';
import type { RuntimeConfig } from './activitypub-runtime';

let manager: UserManager | null = null;

export function configureAuthentication(config: RuntimeConfig): void {
  if (manager) return;
  const settings: UserManagerSettings = {
    authority: config.authority,
    client_id: config.clientId,
    redirect_uri: config.redirectUri,
    post_logout_redirect_uri: config.postLogoutRedirectUri,
    response_type: 'code',
    response_mode: 'query',
    scope: config.scopes.join(' '),
    automaticSilentRenew: true,
    monitorSession: false,
    revokeTokensOnSignout: true,
    loadUserInfo: true,
    filterProtocolClaims: true,
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
  };
  manager = new UserManager(settings);
}

function requireManager(): UserManager {
  if (!manager) throw new Error('OIDC authentication is not configured');
  return manager;
}

export async function completeCallbackIfPresent(): Promise<void> {
  if (window.location.pathname !== '/app/auth/callback') return;
  await requireManager().signinRedirectCallback();
  window.history.replaceState({}, document.title, '/app/');
}

export async function currentUser(): Promise<User | null> {
  const oidc = requireManager();
  const user = await oidc.getUser();
  if (user && !user.expired) return user;
  if (!user?.refresh_token) return null;
  try {
    return await oidc.signinSilent();
  } catch {
    await oidc.removeUser();
    return null;
  }
}

export async function getAccessToken(): Promise<string | null> {
  return (await currentUser())?.access_token ?? null;
}

export async function beginSignIn(): Promise<void> {
  await requireManager().signinRedirect({ state: { returnTo: '/app/' } });
}

export async function endSession(): Promise<void> {
  const oidc = requireManager();
  const user = await oidc.getUser();
  if (user?.id_token) {
    await oidc.signoutRedirect({ id_token_hint: user.id_token });
    return;
  }
  await oidc.removeUser();
  window.location.assign('/app/');
}
