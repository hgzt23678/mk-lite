# Misskey v12 Blazor port notice

This directory contains the production Blazor translation of the Misskey web
client.

- Upstream project: Misskey
- Upstream release: `12.119.2`
- Upstream commit: `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`
- Upstream source: <https://github.com/misskey-dev/misskey/tree/12.119.2/packages/client>
- Copyright: syuilo and the Misskey contributors
- License: GNU Affero General Public License version 3 only

This port replaces the Vue runtime with ASP.NET Core static SSR and Interactive
Server Blazor.
It translates reviewed DOM, CSS, component behavior, browser lifecycle,
localization, themes, and API integration into Razor, C#, and typed JavaScript
module boundaries.
Unsupported backend capabilities remain explicit rather than being replaced by
fixed data or successful no-op handlers.

The complete corresponding source for a deployed build must be available at the
URL configured as `Frontend:SourceUrl`.
That source must identify the exact revision, build configuration, dependency
lock files, backend adapters, generated assets, and local modifications.

Third-party dependencies and bundled assets retain their own copyright and
license terms.
Their license texts are stored beside the distributed files under `wwwroot/vendor`.
