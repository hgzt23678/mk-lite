# Third-party open-source notices

This file identifies the direct open-source libraries used to build or run this
repository. It is an attribution and inventory document, not a replacement for
the terms of any dependency's license.

The repository itself, including the Misskey-derived Razor port, is licensed
under `AGPL-3.0-only`; see [`LICENSE`](LICENSE) and [`NOTICE.md`](NOTICE.md).
Each third-party component remains subject to its own license.

## Direct .NET dependencies

Versions come from `Directory.Packages.props` and the checked-in
`packages.lock.json` files. Packages used only by test projects are not included
in this production/build inventory.

| Package | Version | License | Upstream |
| --- | ---: | --- | --- |
| `AWSSDK.S3` | 4.0.102.1 | Apache-2.0 | [aws/aws-sdk-net](https://github.com/aws/aws-sdk-net/) |
| `HtmlSanitizer` | 9.2.995 | MIT | [mganss/HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) |
| `MailKit` | 4.17.0 | MIT | [jstedfast/MailKit](https://github.com/jstedfast/MailKit) |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.Components.Authorization` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.Components.Web` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.Components.WebAssembly.Authentication` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.Components.WebAssembly.Server` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.10 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.AspNetCore.WebUtilities` | 10.0.11 | MIT | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| `Microsoft.EntityFrameworkCore` | 10.0.11 | MIT | [dotnet/efcore](https://github.com/dotnet/efcore) |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.10 | MIT | [dotnet/efcore](https://github.com/dotnet/efcore) |
| `Microsoft.Extensions.Http.Resilience` | 10.9.0 | MIT | [dotnet/extensions](https://github.com/dotnet/extensions) |
| `NSign.Abstractions` | 1.2.4 | MIT | [Unisys/NSign](https://github.com/Unisys/NSign) |
| `NSign.AspNetCore` | 1.2.4 | MIT | [Unisys/NSign](https://github.com/Unisys/NSign) |
| `NSign.Client` | 1.2.4 | MIT | [Unisys/NSign](https://github.com/Unisys/NSign) |
| `NSign.SignatureProviders` | 1.2.4 | MIT | [Unisys/NSign](https://github.com/Unisys/NSign) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | PostgreSQL | [npgsql/efcore.pg](https://github.com/npgsql/efcore.pg) |
| `OpenIddict.EntityFrameworkCore` | 7.6.0 | Apache-2.0 | [openiddict/openiddict-core](https://github.com/openiddict/openiddict-core) |
| `OpenIddict.Server.AspNetCore` | 7.6.0 | Apache-2.0 | [openiddict/openiddict-core](https://github.com/openiddict/openiddict-core) |
| `OpenIddict.Validation.AspNetCore` | 7.6.0 | Apache-2.0 | [openiddict/openiddict-core](https://github.com/openiddict/openiddict-core) |
| `OpenIddict.Validation.ServerIntegration` | 7.6.0 | Apache-2.0 | [openiddict/openiddict-core](https://github.com/openiddict/openiddict-core) |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.17.0 | Apache-2.0 | [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) |
| `OpenTelemetry.Extensions.Hosting` | 1.17.0 | Apache-2.0 | [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.17.0 | Apache-2.0 | [open-telemetry/opentelemetry-dotnet-contrib](https://github.com/open-telemetry/opentelemetry-dotnet-contrib) |
| `OpenTelemetry.Instrumentation.Http` | 1.17.0 | Apache-2.0 | [open-telemetry/opentelemetry-dotnet-contrib](https://github.com/open-telemetry/opentelemetry-dotnet-contrib) |
| `OpenTelemetry.Instrumentation.Runtime` | 1.17.0 | Apache-2.0 | [open-telemetry/opentelemetry-dotnet-contrib](https://github.com/open-telemetry/opentelemetry-dotnet-contrib) |
| `StackExchange.Redis` | 3.1.3 | MIT | [StackExchange/StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) |

The standard texts are included locally for
[MIT](licenses/MIT.txt),
[Apache-2.0](licenses/Apache-2.0.txt), and
[PostgreSQL](licenses/PostgreSQL.txt).
Package archives restored by NuGet also contain their declared license metadata
and any package-specific notice files.

## Direct frontend dependencies

This is the complete direct runtime dependency set from
`frontend/misskey-v12/package-lock.json`. The fixed Vue client is retained as
the Misskey 12.119.2 visual/behavior oracle and generation input; Vue itself is
not loaded by the production Blazor execution path.

| Package | Version | Declared license |
| --- | ---: | --- |
| [`@discordapp/twemoji`](https://www.npmjs.com/package/@discordapp/twemoji/v/14.0.2) | 14.0.2 | MIT AND CC-BY-4.0 |
| [`@fortawesome/fontawesome-free`](https://www.npmjs.com/package/@fortawesome/fontawesome-free/v/6.1.2) | 6.1.2 | CC-BY-4.0 AND OFL-1.1 AND MIT |
| [`@syuilo/aiscript`](https://www.npmjs.com/package/@syuilo/aiscript/v/0.11.1) | 0.11.1 | MIT |
| [`autobind-decorator`](https://www.npmjs.com/package/autobind-decorator/v/2.4.0) | 2.4.0 | MIT |
| [`autosize`](https://www.npmjs.com/package/autosize/v/5.0.1) | 5.0.1 | MIT |
| [`blurhash`](https://www.npmjs.com/package/blurhash/v/1.1.5) | 1.1.5 | MIT |
| [`broadcast-channel`](https://www.npmjs.com/package/broadcast-channel/v/7.3.0) | 7.3.0 | MIT |
| [`browser-image-resizer`](https://www.npmjs.com/package/browser-image-resizer/v/2.4.1) | 2.4.1 | MIT |
| [`chart.js`](https://www.npmjs.com/package/chart.js/v/3.9.1) | 3.9.1 | MIT |
| [`chartjs-adapter-date-fns`](https://www.npmjs.com/package/chartjs-adapter-date-fns/v/2.0.0) | 2.0.0 | MIT |
| [`chartjs-plugin-gradient`](https://www.npmjs.com/package/chartjs-plugin-gradient/v/0.5.1) | 0.5.1 | MIT |
| [`chartjs-plugin-zoom`](https://www.npmjs.com/package/chartjs-plugin-zoom/v/1.2.1) | 1.2.1 | MIT |
| [`compare-versions`](https://www.npmjs.com/package/compare-versions/v/5.0.1) | 5.0.1 | MIT |
| [`cropperjs`](https://www.npmjs.com/package/cropperjs/v/2.0.0-beta) | 2.0.0-beta | MIT |
| [`date-fns`](https://www.npmjs.com/package/date-fns/v/2.29.2) | 2.29.2 | MIT |
| [`escape-regexp`](https://www.npmjs.com/package/escape-regexp/v/0.0.1) | 0.0.1 | MIT |
| [`eventemitter3`](https://www.npmjs.com/package/eventemitter3/v/4.0.7) | 4.0.7 | MIT |
| [`idb-keyval`](https://www.npmjs.com/package/idb-keyval/v/6.2.0) | 6.2.0 | Apache-2.0 |
| [`insert-text-at-cursor`](https://www.npmjs.com/package/insert-text-at-cursor/v/0.3.0) | 0.3.0 | MIT |
| [`json5`](https://www.npmjs.com/package/json5/v/2.2.3) | 2.2.3 | MIT |
| [`katex`](https://www.npmjs.com/package/katex/v/0.16.25) | 0.16.25 | MIT |
| [`matter-js`](https://www.npmjs.com/package/matter-js/v/0.18.0) | 0.18.0 | MIT |
| [`mfm-js`](https://www.npmjs.com/package/mfm-js/v/0.23.0) | 0.23.0 | MIT |
| [`misskey-js`](https://www.npmjs.com/package/misskey-js/v/0.0.14) | 0.0.14 | MIT |
| [`oidc-client-ts`](https://www.npmjs.com/package/oidc-client-ts/v/3.5.0) | 3.5.0 | Apache-2.0 |
| [`photoswipe`](https://www.npmjs.com/package/photoswipe/v/5.3.2) | 5.3.2 | MIT |
| [`prismjs`](https://www.npmjs.com/package/prismjs/v/1.30.0) | 1.30.0 | MIT |
| [`punycode`](https://www.npmjs.com/package/punycode/v/2.3.1) | 2.3.1 | MIT |
| [`rndstr`](https://www.npmjs.com/package/rndstr/v/1.0.0) | 1.0.0 | MIT |
| [`s-age`](https://www.npmjs.com/package/s-age/v/1.1.2) | 1.1.2 | MIT |
| [`seedrandom`](https://www.npmjs.com/package/seedrandom/v/3.0.5) | 3.0.5 | MIT |
| [`strict-event-emitter-types`](https://www.npmjs.com/package/strict-event-emitter-types/v/2.0.0) | 2.0.0 | ISC |
| [`stringz`](https://www.npmjs.com/package/stringz/v/2.1.0) | 2.1.0 | MIT |
| [`syuilo-password-strength`](https://www.npmjs.com/package/syuilo-password-strength/v/0.0.1) | 0.0.1 | MIT |
| [`textarea-caret`](https://www.npmjs.com/package/textarea-caret/v/3.1.0) | 3.1.0 | MIT |
| [`three`](https://www.npmjs.com/package/three/v/0.144.0) | 0.144.0 | MIT |
| [`throttle-debounce`](https://www.npmjs.com/package/throttle-debounce/v/5.0.2) | 5.0.2 | MIT |
| [`tinycolor2`](https://www.npmjs.com/package/tinycolor2/v/1.6.0) | 1.6.0 | MIT |
| [`twemoji-parser`](https://www.npmjs.com/package/twemoji-parser/v/14.0.0) | 14.0.0 | MIT |
| [`uuid`](https://www.npmjs.com/package/uuid/v/14.0.1) | 14.0.1 | MIT |
| [`vanilla-tilt`](https://www.npmjs.com/package/vanilla-tilt/v/1.8.1) | 1.8.1 | MIT |
| [`vue`](https://www.npmjs.com/package/vue/v/3.5.40) | 3.5.40 | MIT |
| [`vue-prism-editor`](https://www.npmjs.com/package/vue-prism-editor/v/2.0.0-alpha.2) | 2.0.0-alpha.2 | MIT |
| [`vuedraggable`](https://www.npmjs.com/package/vuedraggable/v/4.1.0) | 4.1.0 | MIT |

The `escape-regexp@0.0.1` and `misskey-js@0.0.14` npm metadata omit a
machine-readable license value. Their installed immutable archives contain MIT
license declarations; `eng/check-frontend-licenses.mjs` records this reviewed,
version-specific exception.

## Browser-distributed license files

The current Blazor product copies or generates browser assets from the
following fixed packages. Their license notices are shipped beside the files:

| Component | License file in this repository |
| --- | --- |
| BlurHash | [`wwwroot/vendor/blurhash/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/blurhash/LICENSE.txt) |
| Font Awesome Free | [`wwwroot/vendor/fontawesome/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/fontawesome/LICENSE.txt) |
| KaTeX | [`wwwroot/vendor/katex/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/katex/LICENSE.txt) |
| Matter.js | [`wwwroot/vendor/matter/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/matter/LICENSE.txt) |
| mfm-js | [`wwwroot/vendor/mfm-js/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/mfm-js/LICENSE.txt) |
| PhotoSwipe | [`wwwroot/vendor/photoswipe/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/photoswipe/LICENSE.txt) |
| Prism | [`wwwroot/vendor/prism/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/vendor/prism/LICENSE.txt) |
| Twemoji graphics | [`wwwroot/twemoji/LICENSE.txt`](frontend/ActivityPub.Misskey.Blazor/wwwroot/twemoji/LICENSE.txt) |

## Reproducing and updating this inventory

1. Restore only from the checked-in locks:

   ```bash
   dotnet restore ActivityPubServer.slnx --locked-mode
   npm --prefix frontend/misskey-v12 ci --ignore-scripts
   ```

2. Reproduce the complete resolved-license inventories:

   ```bash
   bash eng/check-licenses.sh > dependency-licenses.tsv
   node eng/check-frontend-licenses.mjs > frontend-dependency-licenses.tsv
   node eng/check-third-party-notices.mjs
   ```

3. Compare the direct package references in production `.csproj` files and the
   root `dependencies` object in `frontend/misskey-v12/package-lock.json` with
   the two direct-package tables above.
4. For every upgrade, review the exact restored archive's license and notices,
   update the version and SPDX expression here, and refresh any copied browser
   license file using its existing generator under `tools/` or `eng/`.
5. Run all three license checks again. Do not approve a new or missing license merely
   by widening the allowlist.

The generated TSV files are review artifacts and need not be committed. The
lock files, this notice, and all browser-distributed license files are the
repository records.
