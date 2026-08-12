<script setup lang="ts">
import type { User } from 'oidc-client-ts';
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { ApiError, MastodonApi } from './api';
import { OidcSession } from './auth';
import NoteCard from './components/NoteCard.vue';
import PostComposer from './components/PostComposer.vue';
import RightWidgets from './components/RightWidgets.vue';
import SidebarNav from './components/SidebarNav.vue';
import UiIcon from './components/UiIcon.vue';
import { fetchRuntimeConfig } from './runtime-config';
import type { Account, RuntimeConfig, Status, StatusDraft, TimelineKind } from './types';

const config = ref<RuntimeConfig | null>(null);
const account = ref<Account | null>(null);
const statuses = ref<Status[]>([]);
const timeline = ref<TimelineKind>('global');
const nextMaxId = ref<string | null>(null);
const loading = ref(true);
const loadingMore = ref(false);
const submitting = ref(false);
const composerExpanded = ref(false);
const replyTo = ref<Status | null>(null);
const errorMessage = ref<string | null>(null);
const actionBusy = ref(new Set<string>());
const composer = ref<InstanceType<typeof PostComposer> | null>(null);
let session: OidcSession | null = null;
let api: MastodonApi | null = null;
let unsubscribe: (() => void) | null = null;

const timelineTitle = computed(() => ({ home: 'ホーム', local: 'ローカル', global: 'グローバル' })[timeline.value]);
const authenticated = computed(() => account.value !== null);

onMounted(async () => {
  document.addEventListener('keydown', onKeydown);
  applyStoredTheme();
  try {
    config.value = await fetchRuntimeConfig();
    document.title = `${config.value.instanceName} | Misskey v12 client`;
    if (!config.value.enabled) throw new Error('Frontend is disabled by the server configuration');
    session = new OidcSession(config.value);
    unsubscribe = session.subscribe(user => void synchronizeUser(user));
    await session.completeCallbackIfPresent();
    const user = await session.currentUser();
    api = new MastodonApi(session);
    await synchronizeUser(user);
    timeline.value = user ? 'home' : 'global';
    await loadTimeline();
  } catch (error) {
    errorMessage.value = presentError(error);
  } finally {
    loading.value = false;
  }
});

onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown);
  unsubscribe?.();
});

async function synchronizeUser(user: User | null): Promise<void> {
  if (!user || !api) {
    account.value = null;
    return;
  }
  try {
    account.value = await api.verifyCredentials();
  } catch (error) {
    account.value = null;
    errorMessage.value = presentError(error);
  }
}

async function loadTimeline(append = false): Promise<void> {
  if (!api) return;
  errorMessage.value = null;
  if (append) loadingMore.value = true;
  else loading.value = true;
  try {
    const page = await api.timeline(timeline.value, append ? nextMaxId.value ?? undefined : undefined);
    statuses.value = append ? [...statuses.value, ...page.items] : page.items;
    nextMaxId.value = page.nextMaxId;
  } catch (error) {
    errorMessage.value = presentError(error);
  } finally {
    loading.value = false;
    loadingMore.value = false;
  }
}

async function selectTimeline(kind: TimelineKind): Promise<void> {
  if (kind === 'home' && !authenticated.value) {
    await login();
    return;
  }
  timeline.value = kind;
  window.scrollTo({ top: 0, behavior: 'smooth' });
  await loadTimeline();
}

async function createStatus(draft: StatusDraft): Promise<void> {
  if (!api) return;
  submitting.value = true;
  errorMessage.value = null;
  try {
    const created = await api.createStatus(draft);
    statuses.value = [created, ...statuses.value];
    composer.value?.clear();
    composerExpanded.value = false;
    replyTo.value = null;
  } catch (error) {
    errorMessage.value = presentError(error);
  } finally {
    submitting.value = false;
  }
}

async function toggleFavourite(status: Status): Promise<void> {
  if (!api) return;
  await runStatusAction(status, 'favourite', () => api!.favourite(status, !status.favourited));
}

async function toggleRenote(status: Status): Promise<void> {
  if (!api) return;
  await runStatusAction(status, 'renote', () => api!.renote(status, !status.reblogged));
}

async function runStatusAction(status: Status, action: string, operation: () => Promise<Status>): Promise<void> {
  const key = `${status.id}:${action}`;
  if (actionBusy.value.has(key)) return;
  actionBusy.value = new Set(actionBusy.value).add(key);
  errorMessage.value = null;
  try {
    const updated = await operation();
    statuses.value = statuses.value.map(item => {
      if (item.id === status.id) return updated;
      return item.reblog?.id === status.id ? { ...item, reblog: updated } : item;
    });
  } catch (error) {
    errorMessage.value = presentError(error);
  } finally {
    const next = new Set(actionBusy.value);
    next.delete(key);
    actionBusy.value = next;
  }
}

async function mute(status: Status): Promise<void> {
  if (!api || !window.confirm(`@${status.account.acct} をミュートしますか？`)) return;
  const key = `${status.id}:mute`;
  actionBusy.value = new Set(actionBusy.value).add(key);
  try {
    await api.mute(status.account.id);
    statuses.value = statuses.value.filter(item => (item.reblog ?? item).account.id !== status.account.id);
  } catch (error) {
    errorMessage.value = presentError(error);
  } finally {
    const next = new Set(actionBusy.value);
    next.delete(key);
    actionBusy.value = next;
  }
}

async function reply(status: Status): Promise<void> {
  replyTo.value = status;
  composerExpanded.value = true;
  await nextTick();
  composer.value?.focus();
}

async function login(): Promise<void> {
  try {
    await session?.signIn();
  } catch (error) {
    errorMessage.value = presentError(error);
  }
}

async function logout(): Promise<void> {
  try {
    await session?.signOut();
  } catch (error) {
    errorMessage.value = presentError(error);
  }
}

function toggleTheme(): void {
  const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
  document.documentElement.dataset.theme = next;
  window.localStorage.setItem('misskey-v12-theme', next);
}

function applyStoredTheme(): void {
  const stored = window.localStorage.getItem('misskey-v12-theme');
  document.documentElement.dataset.theme = stored === 'dark' || stored === 'light'
    ? stored
    : window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 't' && !event.metaKey && !event.ctrlKey && !(event.target instanceof HTMLInputElement) && !(event.target instanceof HTMLTextAreaElement)) {
    event.preventDefault();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
  if (event.key === 'n' && authenticated.value && !(event.target instanceof HTMLInputElement) && !(event.target instanceof HTMLTextAreaElement)) {
    event.preventDefault();
    composerExpanded.value = true;
    composer.value?.focus();
  }
}

function openComposer(): void {
  if (!account.value) {
    void login();
    return;
  }
  composerExpanded.value = true;
  void nextTick(() => composer.value?.focus());
}

function reloadPage(): void {
  window.location.reload();
}

function presentError(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  return error instanceof Error && error.message.startsWith('Frontend')
    ? error.message
    : '画面を読み込めませんでした。しばらくしてから再試行してください。';
}
</script>

<template>
  <div v-if="config" class="app-shell">
    <SidebarNav
      :config="config"
      :account="account"
      :timeline="timeline"
      @select-timeline="selectTimeline"
      @compose="openComposer"
      @login="login"
      @logout="logout"
      @toggle-theme="toggleTheme"
    />

    <main class="main-column">
      <header class="timeline-header">
        <div>
          <span class="eyebrow">TIMELINE</span>
          <h1>{{ timelineTitle }}</h1>
        </div>
        <button type="button" title="更新" :disabled="loading" @click="loadTimeline()"><UiIcon name="refresh" /></button>
      </header>

      <nav class="timeline-tabs" aria-label="タイムライン種別">
        <button v-if="account" :class="{ active: timeline === 'home' }" @click="selectTimeline('home')"><UiIcon name="home" /><span>ホーム</span></button>
        <button :class="{ active: timeline === 'local' }" @click="selectTimeline('local')"><UiIcon name="local" /><span>ローカル</span></button>
        <button :class="{ active: timeline === 'global' }" @click="selectTimeline('global')"><UiIcon name="global" /><span>グローバル</span></button>
      </nav>

      <PostComposer
        v-if="account && config.capabilities.compose"
        ref="composer"
        :account="account"
        :submitting="submitting"
        :reply-to="replyTo"
        :expanded="composerExpanded"
        @submit="createStatus"
        @cancel-reply="replyTo = null"
      />
      <section v-else-if="!account" class="login-callout panel-block">
        <div><b>ホームタイムラインと投稿を利用する</b><span>OIDC Authorization Code + PKCE で安全にログインします。</span></div>
        <button type="button" @click="login"><UiIcon name="login" />ログイン</button>
      </section>

      <div v-if="errorMessage" class="error-banner" role="alert">
        <span>{{ errorMessage }}</span><button type="button" @click="errorMessage = null">閉じる</button>
      </div>

      <section class="timeline" aria-live="polite" :aria-busy="loading">
        <div v-if="loading && statuses.length === 0" class="loading-state"><span></span><p>タイムラインを読み込んでいます</p></div>
        <div v-else-if="statuses.length === 0" class="empty-state"><b>まだノートがありません</b><span>このタイムラインに最初のノートが届くと、ここに表示されます。</span></div>
        <NoteCard
          v-for="status in statuses"
          :key="status.id"
          :status="status"
          :authenticated="authenticated"
          :busy="[...actionBusy].some(key => key.startsWith(status.id + ':') || (status.reblog && key.startsWith(status.reblog.id + ':')))"
          @reply="reply"
          @favourite="toggleFavourite"
          @renote="toggleRenote"
          @mute="mute"
          @login="login"
        />
      </section>

      <button v-if="nextMaxId" class="load-more" :disabled="loadingMore" type="button" @click="loadTimeline(true)">
        {{ loadingMore ? '読み込み中…' : 'もっと見る' }}
      </button>
    </main>

    <RightWidgets :config="config" :account="account" />

    <nav class="mobile-nav" aria-label="モバイルナビゲーション">
      <button v-if="account" :class="{ active: timeline === 'home' }" @click="selectTimeline('home')"><UiIcon name="home" /><span>ホーム</span></button>
      <button :class="{ active: timeline === 'local' }" @click="selectTimeline('local')"><UiIcon name="local" /><span>ローカル</span></button>
      <button class="mobile-compose" @click="openComposer"><UiIcon name="compose" /></button>
      <button :class="{ active: timeline === 'global' }" @click="selectTimeline('global')"><UiIcon name="global" /><span>グローバル</span></button>
      <button @click="account ? logout() : login()"><UiIcon :name="account ? 'user' : 'login'" /><span>{{ account ? 'アカウント' : 'ログイン' }}</span></button>
    </nav>
  </div>

  <div v-else class="boot-screen">
    <span class="brand-mark">M</span>
    <div v-if="loading" class="loading-state"><span></span><p>クライアントを起動しています</p></div>
    <div v-else-if="errorMessage" class="fatal-error" role="alert"><b>起動できませんでした</b><p>{{ errorMessage }}</p><button @click="reloadPage">再読み込み</button></div>
  </div>
</template>
