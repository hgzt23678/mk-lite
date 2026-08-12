# fediverse-pastureによるローカル連合開発

## 採用方針

連合機能の日常開発、回帰確認、相互運用調査には[fediverse-pasture](https://codeberg.org/funfedidev/fediverse-pasture)を標準環境として使う。

Pastureは各実装をHTTP化し、既知のローカルユーザーを作成した開発専用コンテナである。

本番構成、TLS適合、公開インターネット上のSSRF防御、性能、バックアップ、障害復旧の証拠には使わない。

採用理由と境界は[ADR 0004](adr/0004-fediverse-pasture-development-localverse.md)に固定する。

## 固定した構成

| ノード | 固定version | Docker内origin | ホストからのUI | 初期ユーザー |
| --- | --- | --- | --- | --- |
| この.NET実装 | 作業treeのimage | `http://activitypub` | `http://localhost:2971` | `eng/pasture.sh create-actor`で明示作成 |
| Mastodon | `v4.6.2` | `http://mastodon` | `http://localhost:2970` | `hippo` / `password` |
| Misskey | `2026.6.0` | `http://misskey` | `http://localhost:2973` | `kitty` / `password` |
| Pleroma | `v2.10.0` | `http://pleroma` | `http://localhost:2972` | `full` / `password` |

Pasture composeはcommit `fecd397782757dd24400b7549a5a67cf0f074b6c`へ固定している。

固定値の権威は`deploy/pasture/versions.env`である。

version更新では、container digest、公開version、変更点、全相互運用項目を記録してから固定値を変更する。

## 起動

前提はGit、Docker Engine、`!override` tagをサポートするDocker Compose 2.24.4以降である。

最初に`.env.example`を`.env`へコピーし、ローカル専用のPostgreSQL、MinIO、Vault値へ置換する。

`AP_VAULT_TOKEN_FILE`には、`AP_VAULT_TOKEN`と同じ値を格納したrepository外のmode `0400`ファイルを絶対pathで指定する。

自動化環境で別のenv fileを使う場合だけ`ACTIVITYPUB_ENV_FILE`で絶対pathを指定できる。

ホスト側の`2971`が使用中の場合は、初回起動前に`.env`の`ACTIVITYPUB_PASTURE_PORT`を変更する。

この値はブラウザーからの入口だけを変更し、永続IRI `http://activitypub`は変更しない。

その後、次を実行する。

```bash
bash eng/pasture.sh fetch
bash eng/pasture.sh config
bash eng/pasture.sh up
bash eng/pasture.sh create-actor alice "Alice"
```

作成されるActor IRIは`http://activitypub/users/alice`である。

Actor作成コマンドはVault TransitへRSA鍵を作成し、LocalActor、ActorKey、改ざん検知監査イベントを同じ管理ユースケースで永続化する。

固定の秘密鍵やDB seedを配布しない。

状態確認と停止は次のとおりである。

```bash
bash eng/pasture.sh status
bash eng/pasture.sh logs api worker mastodon_sidekiq misskey pleroma
bash eng/pasture.sh down
```

`down`は名前付きPostgreSQL、MinIO、Caddy volumeを削除しない。

Pasture側DBは上流方針どおり既定で揮発性であり、相互運用fixtureを毎回既知状態から開始する。

## ネットワークと安全境界

`eng/pasture.sh up`は外部Docker network `fediverse-pasture`を`--internal`、`172.29.0.0/24`で作成する。

同名networkが外向き通信可能、または固定subnetと異なる状態で既に存在するときは、既存containerや利用者データを勝手に停止せず、起動を拒否する。

.NETノードのPasture overrideは次を強制する。

- `ASPNETCORE_ENVIRONMENT=Development`
- `Federation:PublicBaseUri=http://activitypub`
- `Federation:RequireHttps=false`
- `Federation:DevelopmentRestrictToAllowedHosts=true`
- private接続許可は`mastodon`、`misskey`、`pleroma`の完全一致だけ
- 許可hostでもRFC1918またはIPv6 ULAだけを追加許可する
- loopback、link-local、cloud metadata、multicast、unspecified addressは引き続き拒否する
- APIとWorkerの両方が同じ許可リストを使う
- container health probeもcanonical `Host: activitypub`でHost filteringを通す
- 起動時にdevelopment security exceptionを構造化warningとして出力する

ProductionではHTTP、loopback、development host allow-list、restrict modeのどれかが有効なら起動に失敗する。

この境界は本番のSafe Federation HTTPポリシーを緩和しない。

## 開発ループ

機能ごとに最低限、次の順で双方向確認する。

1. 対象ノードから`alice@activitypub`をWebFinger検索する。
2. この.NETノードから対象Actorを解決する。
3. FollowとAcceptまたはRejectを双方向に実行する。
4. Activityを生成し、送信側Activity、Delivery、DeliveryAttemptと受信側表示を照合する。
5. 同じactivity IDの再送で副作用が増えないことを確認する。
6. Update、Undo、Delete後の両側状態を確認する。
7. worker killまたは対象ノード停止後、lease回収と失敗先だけの再送を確認する。

## Tailnetからfrontendを試験する

別端末のブラウザーから移植済みMisskey frontendを確認する場合は、Tailscale FunnelではなくTailnet限定のServeを使う。

```bash
bash eng/pasture-tailscale.sh up
bash eng/pasture-tailscale.sh status
```

scriptはTailscaleのMagicDNS名を取得し、既定ではHTTPS port `9443`を`127.0.0.1:2971`へ接続する。
ブラウザーへ公開するOIDC issuer/authorization endpointはこのTailnet HTTPS originを使う一方、APIからKeycloakへのmetadata、JWKS、token backchannelは`identity:8080`の内部経路を使う。
これによりTailnet ServeをAPI自身が折り返して参照せず、初回login challengeも外向き経路の状態に依存しない。
portを変える場合は`ACTIVITYPUB_TAILSCALE_PORT`を明示する。
同じportが別serviceへ割り当て済みなら、既存設定を上書きせず起動を拒否する。

このoverlayでは次を分離する。

- Actor、Activity、Objectの永続IRIはPasture内の`http://activitypub`を維持する。
- browser callbackとlogout return URIだけを明示的なTailnet HTTPS originへ向ける。
- KeycloakのfrontchannelはTailnet HTTPS origin、backchannelはDocker内originを使用する。
- 外部から公開するKeycloak pathは`pasture` realmと認証resourceに限定し、管理pathは公開しない。
- redirect URIとWeb Originは生成したrealm設定へ完全一致で登録し、wildcardを使わない。
- Funnelを有効化せず、実Fediverseや公開Internetへ公開しない。

再検証とServeだけの停止は次のとおりである。

```bash
bash eng/pasture-tailscale.sh test
bash eng/pasture-tailscale.sh stop
```

`stop`はPasture containerとvolumeを停止・削除しない。
containerも停止する場合だけ`down`を使うが、名前付きvolumeは削除しない。

Release candidateでは、Actor検索、Follow、Accept、Reject、Create、Reply、Update、Delete、Like、Announce、Undo、Block、Mention、Hashtag、Media、Poll、Followers-only、Mentioned-only、鍵更新、signed GET、sharedInbox、Misskey reaction、LitePub EmojiReactを実装ごとに記録する。

実施日時、server version、image digest、commit、成功項目、既知差異、関連ログを[適合・相互運用表](CONFORMANCE.md)とrelease artifactへ残す。

## 2026-08-03実測で得た運用上の注意

- 非公開ActivityはsharedInboxへ集約せず、各remote Actorのpersonal inboxへ配送する。followers collectionを本文の`to`へ残し、全follower IRIを本文へ展開しない。
- 配送Workerはclaimしたbatchを直ちに並行開始し、domain leaseでdomain別並列数を制限する。逐次処理すると後方Deliveryのleaseが処理開始前に失効する。
- transport中はdelivery leaseとdomain leaseの両方を`Workers:HeartbeatInterval`で更新する。
- DNS `SocketException`を含むnetwork failureはRetry対象のDeliveryAttemptとして保存する。
- pinned Pleroma imageではoffline起動時にUI assetを取得できないため、UI表示項目はauthenticated APIまたはDB projectionを使い、UI成功とは表現しない。
- 2 CPUの検証hostではMastodon Rails development-mode HTTPが長時間試験中に無応答になった。DB、Redis、media volume、networkを保持したapp container再作成で復旧した。このfixture事象を.NET製品障害と混同しない。

実測artifactは`artifacts/interop/pasture/20260803T061125Z/`にあり、raw本文、Cookie、token、秘密鍵、署名全体を含めない。

## Misskey v12の扱い

PastureのMisskey nodeは現行実装との連合回帰を目的とし、移植元frontend `12.119.2`とは別物として扱う。

Misskey v12 frontend/API互換確認は既存fixtureとfrontend testを継続し、現行MisskeyとのActivityPub相互運用をPastureで確認する。

実Misskey v12 serverが必要な試験は、古い依存と既知脆弱性を現在のlocalverseへ混在させず、version、image source、ネットワーク境界を固定した別compose projectとして追加する。

Pasture全サービスへ同じ`VERSION`環境変数を渡す方法は、各実装のtag体系が異なるため使用しない。

## 拡張

GoToSocialはPastureの`gotosocial.yml`を同じ方式でversion固定して追加する。

PeerTubeは現在のPasture標準構成に含まれないため、公式imageを隔離networkへ接続する別adapterを用意する。

どちらもcompose追加だけで成功扱いにせず、相互運用表に実測結果を残す。
