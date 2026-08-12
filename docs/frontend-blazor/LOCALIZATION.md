# Misskey 12.119.2 localization

## 固定した翻訳源

対応言語は`frontend/misskey-v12/locales/index.js`が列挙する25件に固定する。

build時のgeneratorはlocalの`index.js`と各YAMLを固定commit `a5a74f4434b179cdb1f97af98bf294c8b18de0e2`のGit objectへbyte単位で照合する。

照合後のraw localeを`catalog.json`へ生成し、Blazor assemblyへ埋め込む。

本番実行経路はVue、Vite、YAML parser、外部locale endpointを必要としない。

ブラウザーの`localStorage.locale`は旧Vue版との移行互換のため削除しない。

ただし、`localStorage.locale`は利用者が改竄できるため、翻訳源として解析しない。

## fallbackとlookup

effective localeはMisskey 12.119.2と同じ順序でdeep mergeする。

`ja-JP`は日本語だけを使用する。

`en-US`は日本語へ英語を上書きする。

`ja-KS`は日本語へ関西弁を上書きする。

その他の言語は日本語、英語、言語primary、選択localeの順で上書きする。

中国語では`zh-CN`をprimaryとしてから`zh-TW`を上書きする。

dot区切りkeyは生成時にflattenし、存在しないkeyは上流`I18n.t`と同じくkey自体を返す。

interpolationは引数の列挙順に進め、各`{name}`の最初の一件だけを置換する。

25言語はfallback後に各1632個のstring leafを持つことを起動時と自動試験で検査する。

## SSRとinteractive circuit

document requestでは、検証済み`misskey.lang` cookieを`Accept-Language`より優先する。

cookieが無効な場合はquality付き`Accept-Language`を評価し、exact locale、primary language、`ja-JP`の順に解決する。

Host、PublicBaseUri、Tailscale hostnameはlocale選択へ使用しない。

document middlewareはSSR前にrequest cultureを選択localeへ設定し、同じlocaleを`Path=/app`、`SameSite=Lax`、一年期限のcookieへ確定する。

HTTPSではcookieへ`Secure`を付ける。

このcookieを最初のSignalR接続も送信するため、Firefoxでdocumentとcircuitの`Accept-Language`が異なる場合もhydrate後に言語が反転しない。

## 旧Vue storageの移行

hydrate後の型付きES moduleは`localStorage.lang`だけをlocale IDとして読む。

値が25言語のどれかに正規化できる場合だけ、scoped state、cookie、`html.lang`、`html.dir`へ反映する。

別tabの`storage` eventも同じallowlistを通す。

unsupported valueは現在の有効localeを変更しない。

module、storage listener、`DotNetObjectReference`、JavaScript handleはcomponent破棄時にdisposeする。

この境界はtoken、authorization code、Cookie値の読出し、旧locale JSONの解析を行わない。

## 検証

`LocalizationTests`は25言語の完全性、fallback chain、dot-path、単一置換、RTL、cookieとAccept-Languageを検証する。

`LocalizationHostTests`はhydrate時のstate変更、descendant rerender、JavaScript handleのdisposeを検証する。

`localization-parity.spec.ts`はChromium、Firefox、WebKitでSSR、API、storage移行、cookie、`html.lang`、`html.dir`、`MkContainer`の`showMore`を検証する。

この基盤の完了は、全画面のhard-coded labelが移植済みであることを意味しない。

各componentは移植時に上流keyへ接続し、個別のlocale visual testを追加する。
