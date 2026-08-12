# ADR 0004: fediverse-pastureを連合開発の標準localverseにする

- Status: Accepted
- Date: 2026-08-03

## Context

ActivityPub相互運用は、単一process内fixtureだけではDNS、HTTP署名、非同期配送、実装固有Activity、表示上の差異を検証できない。

各実装の公式production composeを個別に統合すると、TLS、DNS、初期ユーザー、異なる依存DB、queueの構築が日常開発の律速になる。

一方で、localhostやprivate addressを無条件に許可するとSafe Federation HTTPのSSRF境界を破壊し、誤って実Fediverseへ配送する危険がある。

## Decision

Mastodon、Misskey、Pleromaとの日常的な相互運用開発にはfediverse-pastureを使う。

上流compose commitと各application image versionを別々に固定する。

この.NETノードはPasture専用compose overrideから同じexternal Docker networkへ接続する。

Pasture networkは`--internal`とし、.NETのSafe Federation HTTPも完全一致host allow-listへ制限する。

HTTP originとprivate network例外はDevelopmentだけで許可し、Productionではfail closedにする。

Pastureの結果はプロトコル相互運用の証拠として記録できるが、production TLS、public DNS、network egress、容量、HA、backup/restoreの証拠にはしない。

## Consequences

- 開発者は4ノードを一つのコマンドで再現できる。
- 実装versionを固定した回帰結果を比較できる。
- Pasture固有のHTTP patchに依存する差異は、公開環境で別途検証する必要がある。
- Pasture初期ユーザーと揮発DBはテスト専用であり、本番設定へ昇格できない。
- Misskey v12 frontend互換と現行Misskey federation互換は別のtest laneになる。
- GoToSocialとPeerTubeは追加adapterが完了するまで従来どおり未検証である。

## Rejected alternatives

各公式production composeだけを直接連結する案は、日常のinner loopとしてDNS、TLS、初期化負担が大きいため標準にしない。

全private addressまたはDocker subnet全体を許可する案は、侵害時の到達範囲が広すぎるため採用しない。

公開tunnelだけで試験する案は、外部依存、再現性、誤配送の面から標準にしない。
