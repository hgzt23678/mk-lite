<script setup lang="ts">
import type { Account, RuntimeConfig } from '../types';

defineProps<{
  config: RuntimeConfig;
  account: Account | null;
}>();

const today = new Date();
const dateLabel = new Intl.DateTimeFormat('ja-JP', { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' }).format(today);
</script>

<template>
  <aside class="widgets" aria-label="ウィジェット">
    <section v-if="account" class="widget profile-widget">
      <div class="profile-accent"></div>
      <span class="avatar large">{{ account.display_name.slice(0, 1).toUpperCase() }}</span>
      <b>{{ account.display_name || account.username }}</b>
      <span>@{{ account.acct }}</span>
      <dl>
        <div><dt>ノート</dt><dd>{{ account.statuses_count }}</dd></div>
        <div><dt>フォロー</dt><dd>{{ account.following_count }}</dd></div>
        <div><dt>フォロワー</dt><dd>{{ account.followers_count }}</dd></div>
      </dl>
    </section>

    <section class="widget calendar-widget">
      <span class="eyebrow">TODAY</span>
      <strong>{{ today.getDate() }}</strong>
      <span>{{ dateLabel }}</span>
    </section>

    <section class="widget">
      <header><b>連合の状態</b><span class="status-dot"></span></header>
      <p>{{ config.instanceName }}</p>
      <ul class="capability-list">
        <li><span>タイムライン</span><b>利用可能</b></li>
        <li><span>ストリーミング</span><b :class="{ pending: !config.capabilities.streaming }">{{ config.capabilities.streaming ? '利用可能' : '未実装' }}</b></li>
        <li><span>メディア投稿</span><b :class="{ pending: !config.capabilities.mediaUpload }">{{ config.capabilities.mediaUpload ? '利用可能' : '未実装' }}</b></li>
      </ul>
    </section>

    <section class="widget safety-widget">
      <b>プライバシー保護</b>
      <p>リモートメディアは同一オリジンの proxy だけを表示します。外部画像へブラウザから直接接続しません。</p>
    </section>
  </aside>
</template>
