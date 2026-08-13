# License texts

This directory contains standard license texts referenced by direct .NET and
frontend dependencies in [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md).

| SPDX identifier | Local text |
| --- | --- |
| AGPL-3.0-only | [`../LICENSE`](../LICENSE) |
| Apache-2.0 | [`Apache-2.0.txt`](Apache-2.0.txt) |
| CC-BY-4.0 | [`CC-BY-4.0.txt`](CC-BY-4.0.txt) |
| ISC | [`ISC.txt`](ISC.txt) |
| MIT | [`MIT.txt`](MIT.txt) |
| OFL-1.1 | [`OFL-1.1.txt`](OFL-1.1.txt) |
| PostgreSQL | [`PostgreSQL.txt`](PostgreSQL.txt) |

The texts are the plain-text forms published by the SPDX License List. Package
copyright notices and component-specific terms remain in the package archive
or the license file distributed beside the browser asset.

Run `node eng/check-third-party-notices.mjs` after a locked restore to verify
that every direct package and required license file remains represented.
