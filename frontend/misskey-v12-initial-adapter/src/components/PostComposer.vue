<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import type { Account, Status, StatusDraft } from '../types';
import UiIcon from './UiIcon.vue';

const props = defineProps<{
  account: Account;
  submitting: boolean;
  replyTo: Status | null;
  expanded: boolean;
}>();

const emit = defineEmits<{
  submit: [draft: StatusDraft];
  cancelReply: [];
}>();

const text = ref('');
const spoilerText = ref('');
const useCw = ref(false);
const visibility = ref<Status['visibility']>('public');
const textarea = ref<HTMLTextAreaElement | null>(null);
const remaining = computed(() => 5000 - [...text.value].length);
const canSubmit = computed(() => !props.submitting && text.value.trim().length > 0 && remaining.value >= 0);

watch(() => props.expanded, async expanded => {
  if (expanded) {
    await nextTick();
    textarea.value?.focus();
  }
});

watch(() => props.replyTo, async status => {
  if (!status) return;
  const mention = `@${status.account.acct} `;
  if (!text.value.startsWith(mention)) text.value = mention + text.value;
  await nextTick();
  textarea.value?.focus();
}, { immediate: true });

function submit(): void {
  if (!canSubmit.value) return;
  emit('submit', {
    status: text.value.trim(),
    visibility: visibility.value,
    spoiler_text: useCw.value ? spoilerText.value.trim() : '',
    sensitive: useCw.value,
    ...(props.replyTo ? { in_reply_to_id: props.replyTo.id } : {}),
  });
}

function clear(): void {
  text.value = '';
  spoilerText.value = '';
  useCw.value = false;
  emit('cancelReply');
}

defineExpose({ clear, focus: () => textarea.value?.focus() });
</script>

<template>
  <section class="composer panel-block" aria-label="ノートを作成">
    <div v-if="replyTo" class="reply-banner">
      <UiIcon name="reply" />@{{ replyTo.account.acct }} への返信
      <button type="button" @click="$emit('cancelReply')">解除</button>
    </div>
    <header>
      <span class="avatar">{{ account.display_name.slice(0, 1).toUpperCase() }}</span>
      <div class="composer-controls">
        <span :class="['text-count', { over: remaining < 0 }]">{{ remaining }}</span>
        <select v-model="visibility" aria-label="公開範囲">
          <option value="public">🌐 パブリック</option>
          <option value="unlisted">⌂ ホーム</option>
          <option value="private">🔒 フォロワー</option>
          <option value="direct">✉ ダイレクト</option>
        </select>
        <button class="submit-button" :disabled="!canSubmit" type="button" @click="submit">
          {{ replyTo ? '返信' : 'ノート' }}
        </button>
      </div>
    </header>
    <input v-if="useCw" v-model="spoilerText" class="cw-input" maxlength="500" placeholder="内容の注釈" aria-label="内容の注釈" />
    <textarea
      ref="textarea"
      v-model="text"
      maxlength="5100"
      :disabled="submitting"
      :placeholder="replyTo ? '返信を書きます…' : 'いまどうしてる？'"
      aria-label="ノート本文"
      @keydown.ctrl.enter.prevent="submit"
      @keydown.meta.enter.prevent="submit"
    />
    <footer>
      <span class="unavailable-tool" title="メディア upload API の実装後に有効になります"><UiIcon name="image" />メディア</span>
      <button type="button" :class="{ active: useCw }" @click="useCw = !useCw"><UiIcon name="eye" />CW</button>
      <span class="keyboard-hint">Ctrl / ⌘ + Enter</span>
    </footer>
  </section>
</template>
