<div align="center">
<a href="https://github.com/hgzt23678/mkdotnet">
	<img src="./frontend/misskey-v12/public/static-assets/splash.png" alt="mkdotnet logo" style="border-radius:50%" width="400"/>
</a>

**🌎 mkdotnet is an open-source, federated social server built with .NET and the Misskey 12.119.2 interface. 🚀**

---

<a href="./docs/DEPLOYMENT.md">
	<img src="https://img.shields.io/badge/create_an_instance-FBD53C?logoColor=FBD53C&style=for-the-badge&logo=server&labelColor=363B40" alt="create an instance"/></a>

<a href="./docs/CONFORMANCE.md">
	<img src="https://img.shields.io/badge/check_compatibility-acea31?logoColor=acea31&style=for-the-badge&logo=activitypub&labelColor=363B40" alt="check compatibility"/></a>

<a href="https://github.com/hgzt23678/mkdotnet">
	<img src="https://img.shields.io/badge/view_the_source-A371F7?logoColor=A371F7&style=for-the-badge&logo=git&labelColor=363B40" alt="view the source"/></a>

<a href="./LICENSE">
	<img src="https://img.shields.io/badge/AGPL--3.0--only-5865F2?logoColor=5865F2&style=for-the-badge&logo=gnu&labelColor=363B40" alt="AGPL-3.0-only"/></a>

---

</div>

<div>

<a href="https://github.com/misskey-dev/misskey/tree/12.119.2"><img src="https://raw.githubusercontent.com/misskey-dev/misskey/a5a74f4434b179cdb1f97af98bf294c8b18de0e2/assets/ai.png" alt="Misskey Ai" align="right" height="320px"/></a>

## ✨ Features

- **ActivityPub federation**\
  Follow, Create, Update, Delete, Like, Announce, Undo and other supported activities are exchanged with remote Fediverse servers. HTTP signatures, inbox deduplication and private visibility checks are applied at the federation boundary.
- **Emoji reactions**\
  Notes can carry Unicode and custom emoji reactions without collapsing every reaction into a single favourite state.
- **Misskey v12 interface**\
  The supported Misskey 12.119.2 UI is translated to Razor and server-side Interactive Blazor. The production path does not load Vue, an iframe or a replacement mock UI.
- **Durable delivery**\
  PostgreSQL is the source of truth for inbox and outbound jobs, leases, retries and dead letters. Optional Redis acceleration never replaces the durable queue.
- **Operations and media boundaries**\
  The server provides health checks, OpenTelemetry, moderation controls and S3-compatible media storage behind explicit production configuration.

</div>

<div style="clear: both;"></div>

> [!WARNING]
> This project is under active development. It does not claim complete Mastodon API, Misskey API or Misskey frontend compatibility. Supported and excluded behavior is recorded in the [conformance](./docs/CONFORMANCE.md) and [verification](./docs/VERIFICATION.md) documents.

## Documentation

Start with the [deployment guide](./docs/DEPLOYMENT.md). The [Cloudflare guide](./docs/CLOUDFLARE.md), [production checklist](./docs/PRODUCTION_CHECKLIST.md), [operations runbooks](./docs/runbooks/README.md), [threat model](./docs/THREAT_MODEL.md) and [architecture decisions](./docs/adr/README.md) cover the corresponding operational boundaries.

The frontend design source is Misskey `12.119.2` at commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`. Its appearance and interaction are the UI reference; backend behavior is implemented through the repository's Application, Domain, Persistence, Federation and Media boundaries.

## Installation

The local Docker Compose environment is intended for development and verification. A public deployment must use the production settings and runbooks linked above.

Requirements:

- Git
- Docker Engine with Docker Compose `2.24.4` or later
- OpenSSL
- .NET SDK `10.0.302` when building or testing outside containers

```bash
git clone https://github.com/hgzt23678/mkdotnet.git
cd mkdotnet
cp .env.example .env
```

Replace every placeholder in `.env` with a different local secret. Keep the Vault token file outside the repository and make it readable only by its owner:

```bash
mkdotnet_secret_dir=/absolute/path/outside/the/repository/mkdotnet
install -d -m 0700 "$mkdotnet_secret_dir"
openssl rand -hex 32 > "$mkdotnet_secret_dir/vault-token"
chmod 0400 "$mkdotnet_secret_dir/vault-token"
```

Copy the generated value to the local-only `AP_VAULT_TOKEN` setting and set
`AP_VAULT_TOKEN_FILE` to that absolute file path. Then validate and start the
environment:

```bash
docker compose config --quiet
docker compose up --build --detach --wait --wait-timeout 300
docker compose ps
```

The dedicated `migrate` service applies database migrations before the API and workers start. Open `https://localhost:8443/app/` after the services become healthy. The local Caddy certificate may require trusting its development CA; do not disable TLS verification in product code.

Stop the environment without deleting PostgreSQL or object storage data:

```bash
docker compose down
```

Do not add `--volumes` unless you intend to destroy all local test data.

## Development

```bash
dotnet restore ActivityPubServer.slnx --locked-mode
dotnet format ActivityPubServer.slnx --verify-no-changes --no-restore
dotnet build ActivityPubServer.slnx --configuration Release --no-restore
dotnet test ActivityPubServer.slnx --configuration Release --no-build
```

The fixed Vue source is retained only as a migration oracle and inventory input:

```bash
npm --prefix frontend/misskey-v12 ci --ignore-scripts
npm --prefix frontend/misskey-v12 run verify:upstream
npm --prefix frontend/misskey-v12 run inventory:check
```

## License

mkdotnet and the Misskey-derived frontend are distributed under the [GNU Affero General Public License version 3 only](./LICENSE). Misskey attribution, corresponding-source information and modification notices are in [NOTICE.md](./NOTICE.md). Direct dependency licenses and browser-distributed license files are indexed in [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md).
