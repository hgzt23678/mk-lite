# ADR 0005: API dialectと永続外部ID

## Status

Accepted

## Context

一つのActor、Post、Activity、MediaをMastodon APIとMisskey APIへ投影する。

内部UUIDを公開すると、Mastodonの数値cursorとMisskey 12.119.2のAID順序を満たせない。

一方のAPI IDを他方で解釈すると、entity typeが異なる同値文字列を誤って解決する可能性もある。

## Decision

`external_entity_ids`にdialect、entity type、internal UUID、external ID、sort ordinal、作成日時、失効日時を保存する。

Mastodon IDはPostgreSQL sequenceから10進文字列として割り当てる。

Misskey IDは12.119.2の既定AID形式で割り当て、noise部の入力にPostgreSQL sequenceを使う。

一意制約はdialectとentity typeを含め、dialect間の解釈を禁止する。

API adapterは外部IDをApplication境界で解決し、Domainへ渡す前に内部UUIDへ変換する。

## Migration

Expand releaseでは独立tableとsequenceだけを作成する。

既存tableの列追加やmigration transaction内のbackfillは行わない。

Migrate phaseでは専用commandがbatch単位で既存entityのmappingを作成する。

新adapterを有効にする前に件数と一意性を検証する。

Contract phaseで削除する旧columnはない。

Down migrationはmappingを失うため、本番rollback手段として使わない。

問題が発生した場合は旧binaryへ切り戻し、schemaを保持したまま修正版へロールフォワードする。

## Consequences

ID解決にはindex lookupが一回増える。

bulk queryではmappingをまとめて読み、N+1 queryを避ける。

バックアップではtableだけでなくsequence値も復元対象になる。

API公開後にID生成方式を変更する場合、既存mappingを再計算してはならない。
