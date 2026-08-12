# ADR 0007: Browser JavaScript interop boundary

## Status

Accepted on 2026-08-04.

## Context

Misskey 12.119.2はobserver、pointer capture、animation frame、Canvas、MFM、AiScript、Matter.jsなど、DOMまたは既存JavaScript engineに依存する。

これらを精度の低いC#再実装へ置換すると、UIとanimationの互換性が失われる。一方、Vue component lifecycleや画面描画をJavaScriptへ残すとBlazor完全移植にならない。

## Decision

Razor ComponentsがDOMと画面状態を所有する。

JavaScriptはbrowser primitive、または固定versionの既存parser/engineだけをES moduleとして提供し、moduleごとにC# interfaceを定義する。

`IJSObjectReference`、`DotNetObjectReference`、observer、event listener、timer、animation frame、Blob URL、外部engineは所有componentが確実に破棄する。

package由来artifactはlockfile、license、source digestを生成時に検証し、同一originから配信する。CDN、任意origin import、`eval`は使用しない。

Vue、Vue Router、Pizzax、Vue directive、Vue lifecycle、SFC renderingはこの境界へ持ち込まない。

## Consequences

複雑なbrowser挙動を上流fixtureと比較しつつ、production execution pathからVueを除去できる。

interopの非同期取消とresource ownershipが新たな故障点になるため、route離脱、連続操作、circuit切断、再接続の試験を各moduleに要求する。
