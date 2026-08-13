# Misskey 12.119.2 API compatibility

固定tagのendpoint registryと各endpointのmeta/paramDefをTypeScript ASTで解析した結果である。client到達性は移植済みfrontendのAST call graphと照合する。Misskey 2026.6.0のPasture結果はこの判定へ流用しない。

Upstream commit: `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`

Inventory: 321 routes; implemented 26, failed 0, blocked 294.

`implemented` は契約と永続副作用を自動試験で確認した項目だけを指す。routeだけが存在する項目はblockedである。 `client-verified` と `differential-verified` は現時点で0件であり、互換を宣言しない。

| Method | Path | Authentication | 判定 | 理由 |
| --- | --- | --- | --- | --- |
| POST | `/api/admin/abuse-user-reports` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/accounts/create` | none | implemented |  |
| POST | `/api/admin/accounts/delete` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/ad/create` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/ad/delete` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/ad/list` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/ad/update` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/announcements/create` | Misskey token + moderator | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/admin/announcements/delete` | Misskey token + moderator | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/admin/announcements/list` | Misskey token + moderator | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/admin/announcements/update` | Misskey token + moderator | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/admin/delete-account` | Misskey token + administrator | blocked | No adapter route exists. |
| POST | `/api/admin/delete-all-files-of-a-user` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/drive-capacity-override` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/drive/clean-remote-files` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/drive/cleanup` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/drive/files` | none | blocked | No adapter route exists. |
| POST | `/api/admin/drive/show-file` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/add` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/add-aliases-bulk` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/copy` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/delete` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/delete-bulk` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/import-zip` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/list` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/list-remote` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/remove-aliases-bulk` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/set-aliases-bulk` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/set-category-bulk` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/emoji/update` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/federation/delete-all-files` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/federation/refresh-remote-instance-metadata` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/federation/remove-all-following` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/federation/update-instance` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/get-index-stats` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/get-table-stats` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/get-user-ips` | Misskey token + administrator | blocked | No adapter route exists. |
| POST | `/api/admin/invite` | Misskey token + moderator | blocked | A durable, audited 130-bit invitation is implemented and tested, but the hardened 26-character code intentionally differs from Misskey 12.119.2's 8-character code, so exact differential compatibility remains blocked. |
| POST | `/api/admin/meta` | Misskey token + administrator | blocked | No adapter route exists. |
| POST | `/api/admin/moderators/add` | Misskey token + administrator | blocked | No adapter route exists. |
| POST | `/api/admin/moderators/remove` | Misskey token + administrator | blocked | No adapter route exists. |
| POST | `/api/admin/promo/create` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/queue/clear` | Misskey token + moderator | excluded | Destructive Bull queue clearing is incompatible with the PostgreSQL audit and recovery contract; use pause, domain cancel, and per-dead-letter replay. |
| POST | `/api/admin/queue/deliver-delayed` | Misskey token + moderator | implemented |  |
| POST | `/api/admin/queue/inbox-delayed` | Misskey token + moderator | implemented |  |
| POST | `/api/admin/queue/stats` | Misskey token + moderator | blocked | The fixed Dolphin UI deliver/inbox fields are PostgreSQL-backed and tested. The absent db and objectStorage queues are not represented by fabricated zero values, so the full 12.119.2 response remains blocked. |
| POST | `/api/admin/relays/add` | Misskey token + moderator | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/admin/relays/list` | Misskey token + moderator | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/admin/relays/remove` | Misskey token + moderator | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/admin/reset-password` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/resolve-abuse-user-report` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/send-email` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/server-info` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/show-moderation-logs` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/show-user` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/show-users` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/silence-user` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/suspend-user` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/unsilence-user` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/unsuspend-user` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/update-meta` | Misskey token + administrator | blocked | No adapter route exists. |
| POST | `/api/admin/update-user-note` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/admin/vacuum` | Misskey token + moderator | blocked | No adapter route exists. |
| POST | `/api/announcements` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/antennas/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/antennas/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/antennas/list` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/antennas/notes` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/antennas/show` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/antennas/update` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/ap/get` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/ap/show` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/app/create` | none | blocked | No adapter route exists. |
| POST | `/api/app/show` | none | blocked | No adapter route exists. |
| POST | `/api/auth/accept` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/auth/session/generate` | none | blocked | No adapter route exists. |
| POST | `/api/auth/session/show` | none | blocked | No adapter route exists. |
| POST | `/api/auth/session/userkey` | none | blocked | No adapter route exists. |
| POST | `/api/blocking/create` | Misskey token | implemented |  |
| POST | `/api/blocking/delete` | Misskey token | implemented |  |
| POST | `/api/blocking/list` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/channels/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/channels/featured` | none | blocked | No adapter route exists. |
| POST | `/api/channels/follow` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/channels/followed` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/channels/owned` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/channels/show` | none | blocked | No adapter route exists. |
| POST | `/api/channels/timeline` | none | blocked | No adapter route exists. |
| POST | `/api/channels/unfollow` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/channels/update` | Misskey token | blocked | No adapter route exists. |
| GET, POST | `/api/charts/active-users` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/ap-request` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/drive` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/federation` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/hashtag` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/instance` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/notes` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/user/drive` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/user/following` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/user/notes` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/user/reactions` | none | blocked | No adapter route exists. |
| GET, POST | `/api/charts/users` | none | blocked | No adapter route exists. |
| POST | `/api/clips/add-note` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/clips/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/clips/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/clips/list` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/clips/notes` | none | blocked | No adapter route exists. |
| POST | `/api/clips/remove-note` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/clips/show` | none | blocked | No adapter route exists. |
| POST | `/api/clips/update` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/files` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/files/attached-notes` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive/files/check-existence` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive/files/create` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/files/delete` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/files/find` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive/files/find-by-hash` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive/files/show` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/files/update` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/files/upload-from-url` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive/folders` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/folders/create` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/folders/delete` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/folders/find` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive/folders/show` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/drive/folders/update` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/drive/stream` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/email-address/available` | none | implemented |  |
| POST | `/api/endpoint` | none | blocked | No adapter route exists. |
| POST | `/api/endpoints` | none | blocked | No adapter route exists. |
| POST | `/api/export-custom-emojis` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/federation/followers` | none | blocked | No adapter route exists. |
| POST | `/api/federation/following` | none | blocked | No adapter route exists. |
| POST | `/api/federation/instances` | none | blocked | The public projection, durable Misskey IDs, welcome-client query, host filter, and validation are tested, but all filter/sort combinations and a fixed Misskey 12.119.2 differential fixture remain blocked. |
| POST | `/api/federation/show-instance` | none | blocked | No adapter route exists. |
| GET, POST | `/api/federation/stats` | none | blocked | No adapter route exists. |
| POST | `/api/federation/update-remote-user` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/federation/users` | none | blocked | No adapter route exists. |
| GET, POST | `/api/fetch-rss` | none | blocked | No adapter route exists. |
| POST | `/api/following/create` | Misskey token | implemented |  |
| POST | `/api/following/delete` | Misskey token | implemented |  |
| POST | `/api/following/invalidate` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/following/requests/accept` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/following/requests/cancel` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/following/requests/list` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/following/requests/reject` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/gallery/featured` | none | blocked | No adapter route exists. |
| POST | `/api/gallery/popular` | none | blocked | No adapter route exists. |
| POST | `/api/gallery/posts` | none | blocked | No adapter route exists. |
| POST | `/api/gallery/posts/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/gallery/posts/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/gallery/posts/like` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/gallery/posts/show` | none | blocked | No adapter route exists. |
| POST | `/api/gallery/posts/unlike` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/gallery/posts/update` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/get-online-users-count` | none | blocked | No adapter route exists. |
| POST | `/api/hashtags/list` | none | blocked | No adapter route exists. |
| POST | `/api/hashtags/search` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/hashtags/show` | none | blocked | No adapter route exists. |
| POST | `/api/hashtags/trend` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/hashtags/users` | none | blocked | No adapter route exists. |
| POST | `/api/i` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/i/2fa/done` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/2fa/key-done` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/2fa/password-less` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/2fa/register` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/2fa/register-key` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/2fa/remove-key` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/2fa/unregister` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/apps` | Misskey token | implemented |  |
| POST | `/api/i/authorized-apps` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/change-password` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/delete-account` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/export-blocking` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/export-following` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/export-mute` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/export-notes` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/export-user-lists` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/favorites` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/gallery/likes` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/gallery/posts` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/get-word-muted-notes-count` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/import-blocking` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/import-following` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/import-muting` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/import-user-lists` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/notifications` | Misskey token | blocked | Durable dual-API projection, filtering, read state, and untilId handling exist, but fixed-server differential fixtures and complete pagination edge cases remain blocked. |
| POST | `/api/i/page-likes` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/pages` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/pin` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/read-all-messaging-messages` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/read-all-unread-notes` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/read-announcement` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/i/regenerate-token` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/get` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/get-all` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/get-detail` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/keys` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/keys-with-type` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/remove` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/scopes` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/registry/set` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/revoke-token` | Misskey token | implemented |  |
| POST | `/api/i/signin-history` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/unpin` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/update` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/i/update-email` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/user-group-invites` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/webhooks/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/webhooks/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/webhooks/list` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/webhooks/show` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/i/webhooks/update` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/messaging/history` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/messaging/messages` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/messaging/messages/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/messaging/messages/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/messaging/messages/read` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/meta` | none | blocked | Core server identity and capability-disable fields are available, but persisted ads, custom emoji, themes, policies, and the complete 12.119.2 response contract remain blocked. |
| POST | `/api/miauth/:session/check` | one-time MiAuth session | implemented |  |
| POST | `/api/miauth/gen-token` | Misskey token | implemented |  |
| POST | `/api/mute/create` | Misskey token | implemented |  |
| POST | `/api/mute/delete` | Misskey token | implemented |  |
| POST | `/api/mute/list` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/my/apps` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes` | none | blocked | No adapter route exists. |
| POST | `/api/notes/children` | none | blocked | No adapter route exists. |
| POST | `/api/notes/clips` | none | blocked | No adapter route exists. |
| POST | `/api/notes/conversation` | none | blocked | No adapter route exists. |
| POST | `/api/notes/create` | Misskey token | implemented |  |
| POST | `/api/notes/delete` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/notes/favorites/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/favorites/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/featured` | none | blocked | No adapter route exists. |
| POST | `/api/notes/global-timeline` | none | implemented |  |
| POST | `/api/notes/hybrid-timeline` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/notes/local-timeline` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/notes/mentions` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/polls/recommendation` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/polls/vote` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| GET, POST | `/api/notes/reactions` | none | implemented |  |
| POST | `/api/notes/reactions/create` | Misskey token | implemented |  |
| POST | `/api/notes/reactions/delete` | Misskey token | implemented |  |
| POST | `/api/notes/renotes` | none | blocked | No adapter route exists. |
| POST | `/api/notes/replies` | none | blocked | No adapter route exists. |
| POST | `/api/notes/search` | none | blocked | No adapter route exists. |
| POST | `/api/notes/search-by-tag` | none | blocked | No adapter route exists. |
| POST | `/api/notes/show` | none | implemented |  |
| POST | `/api/notes/state` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/thread-muting/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/thread-muting/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/timeline` | Misskey token | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/notes/translate` | none | blocked | No adapter route exists. |
| POST | `/api/notes/unrenote` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/user-list-timeline` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/watching/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notes/watching/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notifications/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/notifications/mark-all-as-read` | Misskey token | implemented |  |
| POST | `/api/notifications/read` | Misskey token | implemented |  |
| POST | `/api/page-push` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/pages/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/pages/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/pages/featured` | none | blocked | No adapter route exists. |
| POST | `/api/pages/like` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/pages/show` | none | blocked | No adapter route exists. |
| POST | `/api/pages/unlike` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/pages/update` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/ping` | none | blocked | No adapter route exists. |
| POST | `/api/pinned-users` | none | blocked | No adapter route exists. |
| POST | `/api/promo/read` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/request-reset-password` | none | blocked | No adapter route exists. |
| POST | `/api/reset-db` | none | blocked | No adapter route exists. |
| POST | `/api/reset-password` | none | blocked | No adapter route exists. |
| POST | `/api/server-info` | none | blocked | No adapter route exists. |
| POST | `/api/signin` | credentials | implemented |  |
| POST | `/api/signup` | none | blocked | No adapter route exists. |
| POST | `/api/signup-pending` | signup session | blocked | No adapter route exists. |
| POST | `/api/stats` | none | implemented |  |
| POST | `/api/sw/register` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/sw/unregister` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/test` | none | blocked | No adapter route exists. |
| POST | `/api/username/available` | none | implemented |  |
| POST | `/api/users` | none | blocked | No adapter route exists. |
| POST | `/api/users/clips` | none | blocked | No adapter route exists. |
| POST | `/api/users/followers` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/users/following` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/users/gallery/posts` | none | blocked | No adapter route exists. |
| POST | `/api/users/get-frequently-replied-users` | none | blocked | No adapter route exists. |
| POST | `/api/users/groups/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/invitations/accept` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/invitations/reject` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/invite` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/joined` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/leave` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/owned` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/pull` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/show` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/transfer` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/groups/update` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/lists/create` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/lists/delete` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/lists/list` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/lists/pull` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/lists/push` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/lists/show` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/lists/update` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/notes` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/users/pages` | none | blocked | No adapter route exists. |
| POST | `/api/users/reactions` | none | blocked | No adapter route exists. |
| POST | `/api/users/recommendation` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/relation` | Misskey token | implemented |  |
| POST | `/api/users/report-abuse` | Misskey token | blocked | No adapter route exists. |
| POST | `/api/users/search` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/users/search-by-username-and-host` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/users/show` | none | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/users/stats` | none | blocked | No adapter route exists. |
| GET | `/api/v1/instance/peers` | none | blocked | No adapter route exists. |
| GET | `/streaming` | optional Misskey token in query, redacted before logging | blocked | Timeline and reaction Note Capture slices are tested, but main, drive, antenna, channel, messaging, and remaining protocol messages are not implemented. |


## Streaming channels

| Channel | Authentication | 判定 | 理由 |
| --- | --- | --- | --- |
| `antenna` | optional | blocked | No channel adapter exists. |
| `channel` | optional | blocked | No channel adapter exists. |
| `drive` | Misskey token | blocked | No channel adapter exists. |
| `globalTimeline` | optional | blocked | Wire handling exists, but the complete channel contract is not yet covered by automated tests. |
| `homeTimeline` | Misskey token | implemented |  |
| `hybridTimeline` | Misskey token | blocked | Wire handling exists, but the complete channel contract is not yet covered by automated tests. |
| `localTimeline` | optional | blocked | Wire handling exists, but the complete channel contract is not yet covered by automated tests. |
| `main` | Misskey token | blocked | Wire handling exists, but the complete channel contract is not yet covered by automated tests. |
| `messaging` | optional | blocked | No channel adapter exists. |
| `messagingIndex` | optional | blocked | No channel adapter exists. |
| `note-capture` | optional | blocked | Wire handling exists, but the complete channel contract is not yet covered by automated tests. |
| `queueStats` | optional | blocked | No channel adapter exists. |
| `serverStats` | optional | blocked | No channel adapter exists. |
| `userList` | optional | blocked | No channel adapter exists. |
