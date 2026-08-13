# Misskey v12 comparison source notice

This directory contains a fixed, modified copy of the Misskey web client used
as the visual and behavioral reference for the Blazor port and as input to the
generated migration inventory. It is not the production frontend runtime.

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

Modifications in this copy include inventory instrumentation, parity fixtures,
build-tool compatibility changes, generated-data checks, and the removal or
isolation of assumptions that require the original Misskey server runtime.

Third-party dependencies and bundled assets retain their own copyright and
license terms. `eng/check-frontend-licenses.mjs` inventories the exact locked
dependency graph; passing that automated gate is not a substitute for legal
review before public deployment or redistribution.
