import {
  UserManager,
  WebStorageStateStore,
  type User,
  type UserManagerSettings,
} from 'oidc-client-ts';
import type { RuntimeConfig } from './types';

type SessionListener = (user: User | null) => void;

export class OidcSession {
  private readonly manager: UserManager;
  private readonly listeners = new Set<SessionListener>();

  public constructor(config: RuntimeConfig) {
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
    this.manager = new UserManager(settings);
    this.manager.events.addUserLoaded(user => this.notify(user));
    this.manager.events.addUserUnloaded(() => this.notify(null));
    this.manager.events.addAccessTokenExpired(() => this.notify(null));
  }

  public subscribe(listener: SessionListener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public async completeCallbackIfPresent(): Promise<boolean> {
    if (window.location.pathname !== '/app/auth/callback') {
      return false;
    }
    await this.manager.signinRedirectCallback();
    window.history.replaceState({}, document.title, '/app/');
    return true;
  }

  public async currentUser(): Promise<User | null> {
    const user = await this.manager.getUser();
    if (user && !user.expired) {
      return user;
    }
    if (user?.refresh_token) {
      try {
        return await this.manager.signinSilent();
      } catch {
        await this.manager.removeUser();
      }
    }
    return null;
  }

  public async accessToken(): Promise<string | null> {
    return (await this.currentUser())?.access_token ?? null;
  }

  public signIn(): Promise<void> {
    return this.manager.signinRedirect({ state: { returnTo: '/app/' } });
  }

  public async signOut(): Promise<void> {
    const user = await this.manager.getUser();
    if (user?.id_token) {
      await this.manager.signoutRedirect({ id_token_hint: user.id_token });
      return;
    }
    await this.manager.removeUser();
    this.notify(null);
  }

  private notify(user: User | null): void {
    for (const listener of this.listeners) {
      listener(user);
    }
  }
}
