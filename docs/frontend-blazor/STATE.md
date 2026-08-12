# Blazor frontend state

## State ownership

認証済み利用者、Timeline subscription、compose中状態、overlay stackはcircuit scopeに置く。

異なる利用者の状態をsingleton serviceへ混在させない。

投稿、follow、reaction、notification、Deliveryをcircuit状態だけで確定しない。

これらの事実はApplication commandがPostgreSQL transactionで確定する。

## Prerender handoff

初期query結果と購読開始cursorは`PersistentComponentState`へ保存する。

static SSRが生成した状態をinteractive circuitが一度だけ取り出すため、DB queryの二重実行を避ける。

永続化する値は表示modelと数値cursorだけであり、token、Cookie、秘密情報を含めない。

## Browser persistence

端末設定は`IClientStorage`を介して`localStorage`または`sessionStorage`へ保存する。

storage keyは制御文字と`token`、`authorization`、`cookie`、`secret`を含む名前を拒否する。

現在はTimeline resume cursorを`sessionStorage`へ保存し、上流互換の`lang`を`localStorage`へ保存する。

client更新判定はMisskey 12.119.2と同じraw stringの`lastVersion`を読む。runtime versionが異なる場合は新しいraw値を書き、theme再構築のため`theme`を削除する。旧versionが有効で現在versionの方が新しく、かつ実AuthenticationStateが認証済みの場合だけ`MkUpdated`を表示する。初回、同一version、downgrade、壊れたversion、guestではpopupを表示しない。tokenやaccount情報はこの境界へ渡さない。

`lang`は25件の対応localeだけを受理し、SSRで確定したsafe cookie、`html.lang`、`html.dir`、scoped localization stateへ同期する。

Vue版の`locale` JSONは削除しないが、改竄可能なbrowser stateであるため翻訳源として使用しない。

その他の既存keyとschema migrationはinventoryを基に後続sliceで実装するため、未検証keyを移行済みとは判定しない。
