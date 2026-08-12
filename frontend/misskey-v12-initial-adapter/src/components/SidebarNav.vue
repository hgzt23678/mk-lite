<script setup lang="ts">
import type { Account, RuntimeConfig, TimelineKind } from '../types';
import UiIcon from './UiIcon.vue';

defineProps<{
  config: RuntimeConfig;
  account: Account | null;
  timeline: TimelineKind;
}>();

defineEmits<{
  selectTimeline: [kind: TimelineKind];
  compose: [];
  login: [];
  logout: [];
  toggleTheme: [];
}>();
</script>

<template>
  <aside class="sidebar" aria-label="メインナビゲーション">
    <div class="brand" :title="config.instanceName">
      <span class="brand-mark">M</span>
      <span class="brand-name">{{ config.instanceName }}</span>
    </div>

    <nav class="nav-list">
      <button v-if="account" :class="{ active: timeline === 'home' }" @click="$emit('selectTimeline', 'home')">
        <UiIcon name="home" /><span>ホーム</span>
      </button>
      <button :class="{ active: timeline === 'local' }" @click="$emit('selectTimeline', 'local')">
        <UiIcon name="local" /><span>ローカル</span>
      </button>
      <button :class="{ active: timeline === 'global' }" @click="$emit('selectTimeline', 'global')">
        <UiIcon name="global" /><span>グローバル</span>
      </button>
      <button disabled title="通知 API の実装後に有効になります">
        <UiIcon name="bell" /><span>通知</span><small>準備中</small>
      </button>
      <button disabled title="検索 API の実装後に有効になります">
        <UiIcon name="search" /><span>みつける</span><small>準備中</small>
      </button>
      <button v-if="account" disabled title="プロフィール画面は次の垂直スライスです">
        <UiIcon name="user" /><span>プロフィール</span><small>準備中</small>
      </button>
    </nav>

    <button v-if="account" class="compose-button" @click="$emit('compose')">
      <UiIcon name="compose" />ノート
    </button>

    <div class="sidebar-bottom">
      <button class="quiet-button" @click="$emit('toggleTheme')">テーマ切替</button>
      <button v-if="account" class="account-chip" @click="$emit('logout')">
        <span class="avatar">{{ account.display_name.slice(0, 1).toUpperCase() }}</span>
        <span><b>{{ account.display_name || account.username }}</b><small>@{{ account.acct }}</small></span>
        <UiIcon name="logout" />
      </button>
      <button v-else class="login-button" @click="$emit('login')">
        <UiIcon name="login" />ログイン
      </button>
      <a class="source-link" :href="config.sourceUrl" target="_blank" rel="noopener noreferrer">
        <UiIcon name="source" />このクライアントのソース
      </a>
      <small class="license">Misskey 12.119.2 を基に改変 · AGPL-3.0 · 無保証</small>
    </div>
  </aside>
</template>
