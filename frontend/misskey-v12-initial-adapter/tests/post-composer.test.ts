import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import PostComposer from '../src/components/PostComposer.vue';

const account = {
  id: 'account', username: 'alice', acct: 'alice', display_name: 'Alice', locked: false, bot: false,
  url: 'https://social.example/users/alice', uri: 'https://social.example/users/alice', avatar: '',
  followers_count: 0, following_count: 0, statuses_count: 0,
};

describe('PostComposer', () => {
  it('submits a production Mastodon status contract', async () => {
    const wrapper = mount(PostComposer, { props: { account, submitting: false, replyTo: null, expanded: true } });
    await wrapper.get('textarea').setValue('hello fediverse');
    await wrapper.get('.submit-button').trigger('click');
    expect(wrapper.emitted('submit')?.[0]?.[0]).toEqual({
      status: 'hello fediverse', visibility: 'public', spoiler_text: '', sensitive: false,
    });
  });

  it('carries the reply target and mention into a reply', async () => {
    const reply = {
      id: 'status', account, content: '<p>source</p>', created_at: '2026-08-03T00:00:00Z',
      in_reply_to_id: null, sensitive: false, spoiler_text: '', visibility: 'public' as const,
      uri: 'https://social.example/objects/status', url: 'https://social.example/objects/status', replies_count: 0,
      reblogs_count: 0, favourites_count: 0, favourited: false, reblogged: false, muted: false,
      reblog: null, media_attachments: [],
    };
    const wrapper = mount(PostComposer, { props: { account, submitting: false, replyTo: reply, expanded: true } });
    await wrapper.get('.submit-button').trigger('click');
    expect(wrapper.emitted('submit')?.[0]?.[0]).toEqual(expect.objectContaining({ in_reply_to_id: 'status', status: '@alice' }));
  });
});
