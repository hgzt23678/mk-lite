import { describe, expect, it, vi } from 'vitest';
import { ApiError, MastodonApi, parseNextMaxId } from '../src/api';

const status = {
  id: 'a5f1d7bf-a15a-4c62-9a54-f58de35642af',
  created_at: '2026-08-03T00:00:00Z',
  in_reply_to_id: null,
  sensitive: false,
  spoiler_text: '',
  visibility: 'public',
  uri: 'https://social.example/objects/1',
  url: 'https://social.example/objects/1',
  replies_count: 0,
  reblogs_count: 0,
  favourites_count: 0,
  favourited: false,
  reblogged: false,
  muted: false,
  content: '<p>hello</p>',
  reblog: null,
  account: { id: 'account', username: 'alice', acct: 'alice', display_name: 'Alice' },
  media_attachments: [],
};

describe('Mastodon API adapter', () => {
  it('loads a public timeline without sending a bearer token', async () => {
    const fetcher = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      expect(headers.has('Authorization')).toBe(false);
      return new Response(JSON.stringify([status]), {
        status: 200,
        headers: {
          'Content-Type': 'application/json',
          Link: '</api/v1/timelines/public?max_id=next-page>; rel="next"',
        },
      });
    });
    const api = new MastodonApi({ accessToken: async () => 'not-sent' }, fetcher as typeof fetch);
    const page = await api.timeline('global');
    expect(page.items).toHaveLength(1);
    expect(page.nextMaxId).toBe('next-page');
  });

  it('uses the access token and an idempotency key for mutations', async () => {
    const fetcher = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      expect(headers.get('Authorization')).toBe('Bearer access-token');
      expect(headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
      return new Response(JSON.stringify(status), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    const api = new MastodonApi({ accessToken: async () => 'access-token' }, fetcher as typeof fetch);
    await api.createStatus({ status: 'hello', visibility: 'public', spoiler_text: '', sensitive: false });
  });

  it('does not call the network when authentication is missing', async () => {
    const fetcher = vi.fn();
    const api = new MastodonApi({ accessToken: async () => null }, fetcher as typeof fetch);
    await expect(api.createStatus({ status: 'hello', visibility: 'public', spoiler_text: '', sensitive: false }))
      .rejects.toEqual(expect.objectContaining({ status: 401 }));
    expect(fetcher).not.toHaveBeenCalled();
  });
});

describe('pagination parser', () => {
  it('reads max_id only from the next relation', () => {
    expect(parseNextMaxId('</before?max_id=bad>; rel="prev", </after?max_id=good>; rel="next"')).toBe('good');
  });
});
