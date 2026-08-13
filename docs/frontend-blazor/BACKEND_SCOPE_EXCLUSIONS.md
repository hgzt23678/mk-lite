# Frontend exclusions for absent backend features

Frontendのvisual oracleはMisskey v12.119.2、backend契約の判定基準は`mei23/dolphin`固定checkoutである。残タスク全体の順序と完了条件は`REMAINING_TASKS.md`を正本とする。

This document narrows the Misskey 12.119.2 frontend migration to functionality supported by the current backend. An `excluded` source is not a blocked port: it has no migration obligation while all of its required backend contracts are absent.

## Evidence rule

`eng/generate-frontend-inventory.mjs` accepts an exclusion only when all of the following are true:

1. The contract exists in the pinned `artifacts/api-inventory/misskey-12.119.2.json` inventory.
2. `artifacts/api-inventory/misskey-client-callgraph.json` ties every declared contract to a real excluded source and source line. The declared evidence must exactly cover the static API and streaming calls made by that feature, and unresolved dynamic calls are rejected.
3. The generator extracts the actual `MapGet`/`MapPost`/`MapPut`/`MapPatch`/`MapDelete` routes from `src/ActivityPub.MisskeyApi/MisskeyEndpoints.cs` and the accepted channel names from `MisskeyStreamingEndpoints.cs`. Any matching implementation makes the exclusion invalid unless the same endpoint is an explicitly declared, tested subfeature of an otherwise excluded mixed screen (for example initial account creation within the still-excluded full admin user-management page).
4. Sources without a direct call, such as the `/my/drive` wrapper and Drive dialogs, must be connected through the parsed internal import graph to a source with direct call evidence.
5. Every exclusion has a non-empty reason and endpoint evidence. A route name, directory name, or API-inventory `blocked` label alone is not evidence.

The pinned upstream inventory contains 321 API contracts and 14 streaming channels. The client statically calls 262 API endpoints and 14 channels. The current Misskey adapter source maps 40 `/api` routes and accepts five named streaming channels. Some mapped routes are only partially compatible; route presence is nevertheless enough to keep their consumers in scope.

## Feature evidence matrix

| Feature | Excluded sources | Client routes removed from the port queue | Required contracts absent from the actual adapter |
|---|---:|---|---|
| Drive management | 12 | `/my/drive`, `/my/drive/folder/:folder`, `/settings/drive`, `/admin/file/:fileId`, `/admin/files` | `@stream/drive`; `admin/drive/clean-remote-files`; `admin/drive/files`; `admin/drive/show-file`; `drive`; `drive/files`; `drive/files/delete`; `drive/files/show`; `drive/files/update`; `drive/files/upload-from-url`; `drive/folders`; `drive/folders/create`; `drive/folders/delete`; `drive/folders/show`; `drive/folders/update`; `i/update` |
| API-backed charts | 3 | No dedicated router entry; these are composed components and a widget | `charts/active-users`; `charts/ap-request`; `charts/drive`; `charts/federation`; `charts/instance`; `charts/notes`; `charts/user/drive`; `charts/user/following`; `charts/user/notes`; `charts/users`; `federation/stats` |

All 27 contract checks above resolve to a pinned upstream contract and one or more client call sites, and all resolve to `backendImplemented: false` against the current C# source. The generated, line-level evidence is stored under `exclusionFeatures` in `artifacts/frontend-inventory/vue-to-blazor-mapping.json`.

## Source exclusion matrix

| Feature | Source | Why the source belongs to the feature boundary |
|---|---|---|
| Drive management | `frontend/misskey-v12/src/components/MkDrive.vue` | Owns Drive listing, folder mutation, file mutation, URL upload, and Drive channel subscription. |
| Drive management | `frontend/misskey-v12/src/components/MkDrive.file.vue` | Owns Drive file update and deletion actions. |
| Drive management | `frontend/misskey-v12/src/components/MkDrive.folder.vue` | Owns Drive folder and contained-file mutation actions. |
| Drive management | `frontend/misskey-v12/src/components/MkDrive.navFolder.vue` | Owns Drive navigation drag-and-drop mutations. |
| Drive management | `frontend/misskey-v12/src/components/MkDriveSelectDialog.vue` | Dedicated dialog wrapper importing `MkDrive.vue`. |
| Drive management | `frontend/misskey-v12/src/components/MkDriveWindow.vue` | Dedicated window wrapper importing `MkDrive.vue`. |
| Drive management | `frontend/misskey-v12/src/components/MkFileListForAdmin.vue` | Dedicated admin Drive result list imported by the evidenced admin files page. |
| Drive management | `frontend/misskey-v12/src/pages/drive.vue` | Router surface that directly composes `MkDrive.vue`. |
| Drive management | `frontend/misskey-v12/src/pages/settings/drive.vue` | Reads Drive usage and folders and mutates the Drive upload-folder setting. |
| Drive management | `frontend/misskey-v12/src/pages/admin-file.vue` | Reads, updates, and deletes an individual Drive file through absent admin and Drive routes. |
| Drive management | `frontend/misskey-v12/src/pages/admin/files.vue` | Lists, searches, and cleans Drive files through absent admin routes. |
| Drive management | `frontend/misskey-v12/src/widgets/slideshow.vue` | Loads its slideshow exclusively from `drive/files`. |
| API-backed charts | `frontend/misskey-v12/src/components/MkChart.vue` | Fetches every supported chart mode from the absent `charts/*` family. |
| API-backed charts | `frontend/misskey-v12/src/components/MkInstanceStats.vue` | Fetches absent federation statistics and composes the API-backed chart renderer. |
| API-backed charts | `frontend/misskey-v12/src/widgets/activity.vue` | Loads activity data exclusively from `charts/user/notes`. |

## Deliberately retained sources

| Source | Why it is not excluded |
|---|---|
| `components/MkDriveFileThumbnail.vue` | Shared, data-only file presentation; it does not require a Drive endpoint. |
| `components/MkPostForm.vue` and `components/MkPostFormAttaches.vue` | The note composer is partially supported by the implemented `notes/create` route. Missing Drive attachment operations do not exclude the whole composer. |
| `components/MkMiniChart.vue` and `scripts/use-chart-tooltip.ts` | Data-only chart presentation primitives can render caller-provided data and do not require chart APIs. |
| `pages/admin/overview.vue` | Mixed screen: `stats` and `federation/instances` are mapped by the current adapter even though its chart, server, and Drive panels are absent. |
| `widgets/federation.vue` | Mixed widget: it calls the mapped `federation/instances` route as well as the absent `charts/instance` route. |
| `pages/timeline.vue` | The core timeline endpoints and supported timeline streaming channels exist; missing antenna, channel, and list selectors are partial gaps. |
| `pages/note.vue` | `notes/show` and `users/notes` exist; the missing clips call is only one subfeature. |
| `pages/share.vue` | `notes/show` and `users/show` exist; the missing Drive lookup is only the attachment branch. |

If a required Drive or chart route/channel is later added to `ActivityPub.MisskeyApi`, inventory generation fails until the exclusion is reviewed. That prevents `excluded` from silently hiding newly supported functionality.
