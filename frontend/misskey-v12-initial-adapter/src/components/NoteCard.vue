<script setup lang="ts">
import { computed, ref } from 'vue';
import { safeMediaUrl, sanitizeStatusHtml } from '../sanitize';
import type { Status } from '../types';
import UiIcon from './UiIcon.vue';

const props = defineProps<{
  status: Status;
  authenticated: boolean;
  busy: boolean;
}>();

const emit = defineEmits<{
  reply: [status: Status];
  favourite: [status: Status];
  renote: [status: Status];
  mute: [status: Status];
  login: [];
}>();

const showSensitive = ref(false);
const displayStatus = computed(() => props.status.reblog ?? props.status);
const content = computed(() => sanitizeStatusHtml(displayStatus.value.content));
const media = computed(() => displayStatus.value.media_attachments
  .map(attachment => ({ ...attachment, safeUrl: safeMediaUrl(attachment.preview_url || attachment.url) }))
  .filter(attachment => attachment.safeUrl !== null));
const created = computed(() => {
  const date = new Date(displayStatus.value.created_at);
  return new Intl.DateTimeFormat('ja-JP', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
});
const visibilityName = computed(() => ({
  public: 'パブリック',
  unlisted: 'ホーム',
  private: 'フォロワー限定',
  direct: 'ダイレクト',
})[displayStatus.value.visibility]);

function requireLogin(action: () => void): void {
  if (props.authenticated) action();
  else emit('login');
}

</script>

<template>
  <article class="note" :aria-label="`${displayStatus.account.display_name || displayStatus.account.username} のノート`">
    <div v-if="status.reblog" class="renote-line">
      <UiIcon name="renote" />{{ status.account.display_name || status.account.username }} が Renote
    </div>
    <div class="note-layout">
      <span class="avatar large">{{ displayStatus.account.display_name.slice(0, 1).toUpperCase() }}</span>
      <div class="note-main">
        <header class="note-header">
          <div class="account-name">
            <b>{{ displayStatus.account.display_name || displayStatus.account.username }}</b>
            <span>@{{ displayStatus.account.acct }}</span>
          </div>
          <time :datetime="displayStatus.created_at" :title="created">{{ created }}</time>
          <span class="visibility" :title="visibilityName">
            <UiIcon :name="displayStatus.visibility === 'private' ? 'lock' : displayStatus.visibility === 'direct' ? 'mail' : 'global'" />
          </span>
        </header>

        <div v-if="displayStatus.spoiler_text" class="content-warning">
          <span>{{ displayStatus.spoiler_text }}</span>
          <button type="button" @click="showSensitive = !showSensitive">{{ showSensitive ? '隠す' : 'もっと見る' }}</button>
        </div>
        <div v-if="!displayStatus.spoiler_text || showSensitive" class="note-content" v-html="content"></div>

        <div v-if="media.length && (!displayStatus.sensitive || showSensitive)" class="media-grid">
          <template v-for="attachment in media" :key="attachment.id">
            <img v-if="attachment.type === 'image'" :src="attachment.safeUrl!" :alt="attachment.description || '添付画像'" loading="lazy" decoding="async" />
            <video v-else-if="attachment.type === 'video'" :src="attachment.safeUrl!" controls preload="metadata"></video>
            <audio v-else-if="attachment.type === 'audio'" :src="attachment.safeUrl!" controls preload="none"></audio>
          </template>
        </div>

        <footer class="note-actions">
          <button :disabled="busy" type="button" title="返信" @click="requireLogin(() => $emit('reply', displayStatus))">
            <UiIcon name="reply" /><span>{{ displayStatus.replies_count || '' }}</span>
          </button>
          <button :class="{ active: displayStatus.reblogged }" :disabled="busy" type="button" title="Renote" @click="requireLogin(() => $emit('renote', displayStatus))">
            <UiIcon name="renote" /><span>{{ displayStatus.reblogs_count || '' }}</span>
          </button>
          <button :class="{ active: displayStatus.favourited }" :disabled="busy" type="button" title="お気に入り" @click="requireLogin(() => $emit('favourite', displayStatus))">
            <UiIcon name="star" /><span>{{ displayStatus.favourites_count || '' }}</span>
          </button>
          <details class="note-menu">
            <summary title="その他"><UiIcon name="more" /></summary>
            <div>
              <a :href="displayStatus.url" target="_blank" rel="noopener noreferrer">元のページを開く</a>
              <button v-if="authenticated" :disabled="busy" type="button" @click="$emit('mute', displayStatus)">@{{ displayStatus.account.acct }} をミュート</button>
            </div>
          </details>
        </footer>
      </div>
    </div>
  </article>
</template>
