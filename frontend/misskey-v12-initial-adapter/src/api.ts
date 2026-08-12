import type { Account, Status, StatusDraft, TimelineKind, TimelinePage } from './types';

export interface AccessTokenProvider {
  accessToken(): Promise<string | null>;
}

export class ApiError extends Error {
  public constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

export class MastodonApi {
  public constructor(
    private readonly tokens: AccessTokenProvider,
    private readonly fetcher: typeof fetch = fetch,
  ) {}

  public verifyCredentials(): Promise<Account> {
    return this.request<Account>('/api/v1/accounts/verify_credentials', {}, true);
  }

  public async timeline(kind: TimelineKind, maxId?: string): Promise<TimelinePage> {
    const parameters = new URLSearchParams({ limit: '20' });
    if (maxId) parameters.set('max_id', maxId);
    let path = '/api/v1/timelines/public';
    let authenticated = false;
    if (kind === 'home') {
      path = '/api/v1/timelines/home';
      authenticated = true;
    } else if (kind === 'local') {
      parameters.set('local', 'true');
    }
    const response = await this.send(`${path}?${parameters.toString()}`, {}, authenticated);
    const items = await this.readJson<Status[]>(response);
    return { items, nextMaxId: parseNextMaxId(response.headers.get('Link')) };
  }

  public createStatus(draft: StatusDraft): Promise<Status> {
    return this.request<Status>('/api/v1/statuses', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() },
      body: JSON.stringify(draft),
    }, true);
  }

  public favourite(status: Status, enabled: boolean): Promise<Status> {
    return this.statusMutation(status.id, enabled ? 'favourite' : 'unfavourite');
  }

  public renote(status: Status, enabled: boolean): Promise<Status> {
    return this.statusMutation(status.id, enabled ? 'reblog' : 'unreblog');
  }

  public async mute(accountId: string): Promise<void> {
    await this.request<unknown>(`/api/v1/accounts/${encodeURIComponent(accountId)}/mute`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ notifications: true }),
    }, true);
  }

  private statusMutation(id: string, operation: string): Promise<Status> {
    return this.request<Status>(`/api/v1/statuses/${encodeURIComponent(id)}/${operation}`, {
      method: 'POST',
      headers: { 'Idempotency-Key': crypto.randomUUID() },
    }, true);
  }

  private async request<T>(path: string, init: RequestInit, authenticated: boolean): Promise<T> {
    return this.readJson<T>(await this.send(path, init, authenticated));
  }

  private async send(path: string, init: RequestInit, authenticated: boolean): Promise<Response> {
    const token = authenticated ? await this.tokens.accessToken() : null;
    if (authenticated && !token) {
      throw new ApiError(401, 'ログインが必要です。');
    }
    const headers = new Headers(init.headers);
    headers.set('Accept', 'application/json');
    if (token) headers.set('Authorization', `Bearer ${token}`);

    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 15_000);
    try {
      const response = await this.fetcher(path, {
        ...init,
        headers,
        cache: 'no-store',
        credentials: 'omit',
        redirect: 'error',
        signal: controller.signal,
      });
      if (!response.ok) {
        throw new ApiError(response.status, response.status === 401 ? 'セッションの更新が必要です。' : `API request failed (${response.status})`);
      }
      return response;
    } catch (error) {
      if (error instanceof ApiError) throw error;
      if (error instanceof DOMException && error.name === 'AbortError') {
        throw new ApiError(408, 'サーバーからの応答がタイムアウトしました。');
      }
      throw new ApiError(0, 'サーバーへ接続できませんでした。');
    } finally {
      window.clearTimeout(timeout);
    }
  }

  private async readJson<T>(response: Response): Promise<T> {
    const contentType = response.headers.get('Content-Type') ?? '';
    if (!contentType.toLowerCase().startsWith('application/json')) {
      throw new ApiError(502, 'API response was not JSON');
    }
    return response.json() as Promise<T>;
  }
}

export function parseNextMaxId(linkHeader: string | null): string | null {
  if (!linkHeader) return null;
  for (const part of linkHeader.split(',')) {
    const match = part.match(/^\s*<([^>]+)>\s*;\s*rel="?next"?\s*$/i);
    if (!match?.[1]) continue;
    const target = new URL(match[1], window.location.origin);
    return target.searchParams.get('max_id');
  }
  return null;
}
