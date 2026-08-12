# Misskey v12 client port notice

This directory contains a modified port of the Misskey web client.

- Upstream project: Misskey
- Upstream release: `12.119.2`
- Upstream commit: `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`
- Upstream source: <https://github.com/misskey-dev/misskey/tree/12.119.2/packages/client>
- Copyright: syuilo and the Misskey contributors
- License: GNU Affero General Public License version 3 only

The complete corresponding source for the deployed build must be published at
the URL configured as `Frontend:SourceUrl`. The URL must identify the exact
revision containing this frontend, its build configuration, dependency lock
file, backend adapters, and modifications.

Modifications in this port include the ASP.NET Core runtime bootstrap, OIDC
Authorization Code with PKCE integration, Bearer-token Misskey API adapter,
idempotency headers, removal of the upstream service-worker server assumptions,
same-origin media boundaries, and build-tool compatibility changes.

Third-party dependencies and bundled assets retain their own copyright and
license terms. `eng/check-frontend-licenses.mjs` inventories the exact locked
dependency graph; passing that automated gate is not a substitute for legal
review before public deployment or redistribution.
