# Mastodon 4.6.2 API compatibility

固定tagのRails route DSLを機械解析し、OAuth discoveryとDoorkeeper routeを追加した結果である。controller/serializer契約の未抽出項目はblockedとして扱う。

Upstream commit: `70d39d364ba6183a2b6e2f763204fe2c21e0ca42`

Inventory: 331 routes; implemented 23, failed 0, blocked 308.

`implemented` は契約と永続副作用を自動試験で確認した項目だけを指す。routeだけが存在する項目はblockedである。 `client-verified` と `differential-verified` は現時点で0件であり、互換を宣言しない。

| Method | Path | Authentication | 判定 | 理由 |
| --- | --- | --- | --- | --- |
| GET | `/.well-known/oauth-authorization-server` | endpoint-specific/public | implemented |  |
| GET | `/api/oembed` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1_alpha/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1_alpha/accounts/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/accounts/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1_alpha/accounts/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1_alpha/accounts/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/accounts/:id/collections` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/accounts/:id/edit` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/accounts/:id/in_collections` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/accounts/new` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/async_refreshes/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1_alpha/collections` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1_alpha/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1_alpha/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1_alpha/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1_alpha/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1_alpha/collections/:id/items` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1_alpha/collections/:id/items/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1_alpha/collections/:id/items/:id/revoke` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id` | OAuth 2.0 Bearer | implemented |  |
| POST | `/api/v1/accounts/:id/block` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/accounts/:id/collections` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts/:id/email_subscriptions` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts/:id/endorse` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id/endorsements` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id/featured_tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts/:id/follow` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/accounts/:id/followers` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id/following` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id/identity_proofs` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id/in_collections` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id/lists` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts/:id/mute` | OAuth 2.0 Bearer | implemented |  |
| POST | `/api/v1/accounts/:id/note` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts/:id/pin` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts/:id/remove_from_followers` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/:id/statuses` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/v1/accounts/:id/unblock` | OAuth 2.0 Bearer | implemented |  |
| POST | `/api/v1/accounts/:id/unendorse` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/accounts/:id/unfollow` | OAuth 2.0 Bearer | implemented |  |
| POST | `/api/v1/accounts/:id/unmute` | OAuth 2.0 Bearer | implemented |  |
| POST | `/api/v1/accounts/:id/unpin` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/familiar_followers` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/lookup` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/accounts/relationships` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/accounts/search` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/accounts/update_credentials` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/accounts/verify_credentials` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| GET | `/api/v1/admin/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/accounts/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/accounts/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/accounts/:id/action` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/accounts/:id/approve` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/accounts/:id/enable` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/accounts/:id/reject` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/accounts/:id/unsensitive` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/accounts/:id/unsilence` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/accounts/:id/unsuspend` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/canonical_email_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/canonical_email_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/canonical_email_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/canonical_email_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/canonical_email_blocks/test` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/dimensions` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/domain_allows` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/domain_allows` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/domain_allows/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/domain_allows/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/domain_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/domain_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/domain_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/domain_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/admin/domain_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/admin/domain_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/email_domain_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/email_domain_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/email_domain_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/email_domain_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/ip_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/ip_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/ip_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/ip_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/admin/ip_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/admin/ip_blocks/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/measures` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/reports` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/reports/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/admin/reports/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/admin/reports/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/reports/:id/assign_to_self` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/reports/:id/reopen` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/reports/:id/resolve` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/reports/:id/unassign` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/retention` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/admin/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/admin/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/trends/approve` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/links` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/trends/links` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/trends/links/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/links/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/admin/trends/links/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/admin/trends/links/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/links/:id/edit` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/links/new` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/links/publishers` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/trends/links/publishers/:id/approve` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/trends/links/publishers/:id/reject` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/trends/reject` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/statuses` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/trends/statuses` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/trends/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/admin/trends/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/admin/trends/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/statuses/:id/edit` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/statuses/new` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/admin/trends/tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/admin/trends/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/admin/trends/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/admin/trends/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/tags/:id/edit` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/admin/trends/tags/new` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/announcements` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/announcements/:id/dismiss` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/announcements/:id/reactions/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/announcements/:id/reactions/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/announcements/:id/reactions/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/annual_reports` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/annual_reports/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/annual_reports/:id/generate` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/annual_reports/:id/read` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/annual_reports/:id/state` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/apps` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/apps/verify_credentials` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/bookmarks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/collections` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/collections/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/collections/:id/items` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/collections/:id/items/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/collections/:id/items/:id/revoke` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/conversations` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/conversations/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/conversations/:id/read` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/conversations/:id/unread` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/custom_emojis` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/directory` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/domain_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/domain_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/domain_blocks` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/domain_blocks/preview` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/donation_campaigns` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/emails/check_confirmation` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/emails/confirmations` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/endorsements` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/favourites` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/featured_tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/featured_tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/featured_tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/featured_tags/suggestions` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/filters` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/filters` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/follow_requests` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/follow_requests/:id/authorize` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/follow_requests/:id/reject` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/followed_tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/instance` | endpoint-specific/public | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| GET | `/api/v1/instance/activity` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/domain_blocks` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/extended_description` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/languages` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/peers` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/privacy_policy` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/rules` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/terms_of_service` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/terms_of_service/:date` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/instance/translation_languages` | endpoint-specific/public | blocked | No adapter route exists. |
| GET | `/api/v1/lists` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/lists` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/lists/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/lists/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/lists/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/lists/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/lists/:id/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/lists/:id/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/lists/:id/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/markers` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/markers` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/media` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/media/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/media/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/media/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/media/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/mutes` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/notifications` | OAuth 2.0 Bearer | blocked | Durable projection, type filtering, max_id, and mutations are tested, but since_id/min_id and exact Link preservation remain blocked. |
| GET | `/api/v1/notifications/:id` | OAuth 2.0 Bearer | implemented |  |
| POST | `/api/v1/notifications/:id/dismiss` | OAuth 2.0 Bearer | implemented |  |
| POST | `/api/v1/notifications/clear` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/notifications/policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/notifications/policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/notifications/policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/notifications/requests` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/notifications/requests/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/notifications/requests/:id/accept` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/notifications/requests/:id/dismiss` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/notifications/requests/accept` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/notifications/requests/dismiss` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/notifications/requests/merged` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/notifications/unread_count` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/peers/search` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/polls/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/polls/:id/votes` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/preferences` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/profile` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/profile` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/profile` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/profile/avatar` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/profile/header` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/push/subscription` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/push/subscription` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/push/subscription` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/push/subscription` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/push/subscription` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/reports` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/scheduled_statuses` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/scheduled_statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/scheduled_statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/scheduled_statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/scheduled_statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/statuses` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses` | OAuth 2.0 Bearer | implemented |  |
| DELETE | `/api/v1/statuses/:id` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| GET | `/api/v1/statuses/:id` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| PATCH | `/api/v1/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/bookmark` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/statuses/:id/context` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/favourite` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| GET | `/api/v1/statuses/:id/favourited_by` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/statuses/:id/history` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v1/statuses/:id/interaction_policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v1/statuses/:id/interaction_policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/mute` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/pin` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/statuses/:id/quotes` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/quotes/:id/revoke` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/reblog` | OAuth 2.0 Bearer | implemented |  |
| GET | `/api/v1/statuses/:id/reblogged_by` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/statuses/:id/source` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/translate` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/unbookmark` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/unfavourite` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| POST | `/api/v1/statuses/:id/unmute` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/unpin` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/statuses/:id/unreblog` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| GET | `/api/v1/streaming` | OAuth 2.0 Bearer | blocked | WebSocket user/public/local, notification delivery, and SSE cursor recovery are tested, but hashtag, list, and direct stream contracts remain blocked. |
| GET | `/api/v1/streaming/(*any)` | OAuth 2.0 Bearer | blocked | The catch-all route exists, but only user/public/local stream variants have contract tests. |
| GET | `/api/v1/suggestions` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v1/suggestions/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/tags/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/tags/:id/feature` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/tags/:id/follow` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/tags/:id/unfeature` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v1/tags/:id/unfollow` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/timelines/home` | OAuth 2.0 Bearer | blocked | Route exists, but complete contract and persistence-side-effect evidence is missing. |
| GET | `/api/v1/timelines/link` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/timelines/list/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/timelines/public` | endpoint-specific/public | implemented |  |
| GET | `/api/v1/timelines/tag/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/trends` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/trends/links` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/trends/statuses` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v1/trends/tags` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/admin/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/filters` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v2/filters` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v2/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v2/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v2/filters/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/filters/:id/keywords` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v2/filters/:id/keywords` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/filters/:id/statuses` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v2/filters/:id/statuses` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v2/filters/keywords/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/filters/keywords/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v2/filters/keywords/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v2/filters/keywords/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/v2/filters/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/filters/statuses/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/instance` | endpoint-specific/public | blocked | Database-backed usage counts are tested, but the complete 4.6.2 entity, rules, contact account, configuration, and differential headers remain blocked. |
| POST | `/api/v2/media` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/notifications` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/notifications/:group_key` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/notifications/:group_key/accounts` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v2/notifications/:group_key/dismiss` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/v2/notifications/clear` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/notifications/policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/v2/notifications/policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/v2/notifications/policy` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/notifications/unread_count` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/search` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/v2/suggestions` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/api/web/embeds/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| POST | `/api/web/push_subscriptions` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| DELETE | `/api/web/push_subscriptions/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/web/push_subscriptions/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/web/push_subscriptions/:id` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PATCH | `/api/web/settings` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| PUT | `/api/web/settings` | OAuth 2.0 Bearer | blocked | No adapter route exists. |
| GET | `/oauth/authorize` | endpoint-specific/public | implemented |  |
| POST | `/oauth/authorize` | endpoint-specific/public | implemented |  |
| POST | `/oauth/revoke` | endpoint-specific/public | implemented |  |
| POST | `/oauth/token` | endpoint-specific/public | implemented |  |
| GET | `/oauth/token/info` | endpoint-specific/public | blocked | No adapter route exists. |
