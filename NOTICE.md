# Notices for mk-lite

`mk-lite` is licensed under the GNU Affero General Public License version 3 only.

Copyright (C) 2026 mk-lite contributors.

## Misskey-derived frontend

The frontend contains modified and translated portions of the Misskey web client.

- Upstream project: Misskey
- Upstream release: `12.119.2`
- Upstream commit: `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`
- Upstream source: <https://github.com/misskey-dev/misskey/tree/12.119.2/packages/client>
- Copyright: syuilo and the Misskey contributors
- License: GNU Affero General Public License version 3 only

The Misskey-derived source and its translations are located in
`frontend/misskey-v12`, `frontend/misskey-v12-initial-adapter`, and
`frontend/ActivityPub.Misskey.Blazor`.
The production frontend replaces the Vue runtime with ASP.NET Core static SSR
and Interactive Server Blazor while retaining the reviewed DOM, CSS, assets,
interaction behavior, and generated compatibility data needed for the port.

The complete corresponding source for a deployed version must identify the
exact revision, build configuration, dependency lock files, backend adapters,
and local modifications.
The deployed interface uses `Frontend:SourceUrl` for this source link.

## Third-party software and assets

Third-party dependencies and bundled assets retain their own copyright and
license terms.
License texts for browser-distributed dependencies are stored beside the
corresponding files under `frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor`.
Twemoji graphics are stored under
`frontend/ActivityPub.Misskey.Blazor/wwwroot/twemoji` with their attribution and
license notice.
The complete direct dependency inventory is recorded in
`THIRD_PARTY_NOTICES.md`.
The locked NuGet and npm dependency inventories are checked by
`eng/check-licenses.sh` and `eng/check-frontend-licenses.mjs`.

See `docs/DEPENDENCIES.md` for the reviewed dependency and license boundaries.
