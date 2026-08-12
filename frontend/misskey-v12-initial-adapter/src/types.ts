export interface FrontendCapabilities {
  publicTimeline: boolean;
  localTimeline: boolean;
  homeTimeline: boolean;
  compose: boolean;
  favourite: boolean;
  renote: boolean;
  mute: boolean;
  mediaUpload: boolean;
  notifications: boolean;
  streaming: boolean;
}

export interface RuntimeConfig {
  enabled: boolean;
  instanceName: string;
  authority: string;
  clientId: string;
  scopes: string[];
  redirectUri: string;
  postLogoutRedirectUri: string;
  sourceUrl: string;
  capabilities: FrontendCapabilities;
}

export interface Account {
  id: string;
  username: string;
  acct: string;
  display_name: string;
  locked: boolean;
  bot: boolean;
  url: string;
  uri: string;
  avatar: string;
  followers_count: number;
  following_count: number;
  statuses_count: number;
}

export interface MediaAttachment {
  id: string;
  type: 'image' | 'video' | 'audio' | 'unknown';
  url: string;
  preview_url: string;
  description: string | null;
  blurhash: string | null;
}

export interface Status {
  id: string;
  created_at: string;
  in_reply_to_id: string | null;
  sensitive: boolean;
  spoiler_text: string;
  visibility: 'public' | 'unlisted' | 'private' | 'direct';
  uri: string;
  url: string;
  replies_count: number;
  reblogs_count: number;
  favourites_count: number;
  favourited: boolean;
  reblogged: boolean;
  muted: boolean;
  content: string;
  reblog: Status | null;
  account: Account;
  media_attachments: MediaAttachment[];
}

export type TimelineKind = 'home' | 'local' | 'global';

export interface TimelinePage {
  items: Status[];
  nextMaxId: string | null;
}

export interface StatusDraft {
  status: string;
  visibility: Status['visibility'];
  spoiler_text: string;
  sensitive: boolean;
  in_reply_to_id?: string;
}
