# Misskey 12.119.2 frontend inventory

## Source of truth

The inventory is generated from the pinned Misskey 12.119.2 source at commit
`a5a74f4434b179cdb1f97af98bf294c8b18de0e2` by
`eng/generate-frontend-inventory.mjs`.

It parses Vue SFCs, TypeScript syntax trees, route declarations, SCSS, storage calls,
streaming calls, locales, themes, assets, and package metadata. Plain filename or text
matching is not accepted as migration evidence.

Regenerate and verify it with:

```bash
npm --prefix frontend/misskey-v12 run inventory
npm --prefix frontend/misskey-v12 run inventory:check
```

## Current measurements

Measurements below were regenerated from the worktree on 2026-08-12 UTC.

| Item | Count |
|---|---:|
| Upstream source files | 530 |
| Local source files | 535 |
| Byte-identical files | 500 |
| Reviewed modified files | 30 |
| Local additions | 5 |
| Missing upstream files | 0 |
| Vue SFCs / parsed components | 400 |
| TypeScript files | 113 |
| Routes | 115 |
| Dynamic imports | 234 |
| Static API endpoints used by the client | 262 |
| Dynamic API call sites | 8 |
| Static Streaming channels | 14 |
| Storage usages | 106 |
| Style blocks / scoped style blocks | 282 / 258 |
| CSS variable references | 880 |
| Media queries | 32 |
| Parsed selectors | 2,687 |
| Transition elements | 42 |
| Keyframes | 23 |
| Animation declarations | 35 |
| Transition declarations | 105 |
| `requestAnimationFrame` usages | 7 |
| Assets | 81 |
| Locales | 35 |
| Themes | 20 |
| Direct dependencies | 65 |

## Migration classification

| Status | Source files |
|---|---:|
| implemented | 329 |
| in-progress | 0 |
| blocked | 0 |
| planned | 0 |
| excluded | 206 |
| unclassified | 0 |

The authoritative list of the generated source mappings is generated in
`artifacts/frontend-inventory/vue-to-blazor-mapping.json`. It now includes
the current authentication and settings entries; each entry names its exact DOM, responsive CSS, selection, keyboard,
observer-lifecycle, opaque-surface, and three-browser evidence.
Every implemented source mapping names the contract and browser evidence used for promotion.
A Razor target and matching CSS alone are still insufficient: the complete upstream
contract, real persistence and federation effects where applicable, and visual/behavior
evidence must pass before that status is used.

The current generated mapping has no `planned`, `in-progress`, `blocked`, or `unclassified`
records. It has 206 explicit exclusions: the original 34 backend feature exclusions plus
172 sources grouped under `remaining-dolphin-contract-gaps`. The latter is not a blanket
success classification: each source retains its parsed API/Streaming evidence and the
Dolphin contract reason. Unsupported screens must expose capability-unavailable behavior,
not placeholder data.

`MkSignin.vue` and `MkSignup.vue` are modified in the connected Vue oracle. Their local
OIDC launch-button contracts are not the porting baseline. The generated mapping therefore
stores a separately parsed `upstreamContract` from the pinned commit, including props,
emits, slots, directives, API calls, browser APIs, DOM classes, and compiled SCSS selectors
and declarations. A local modification cannot erase an upstream migration obligation.

Machine-readable outputs are under `artifacts/frontend-inventory/`; the authoritative
source-to-target work queue is
`frontend/ActivityPub.Misskey.Blazor/upstream-port-map.json`.

認証と登録に限定した契約、証拠、未完了条件は`AUTHENTICATION_PARITY.md`へ記録する。
残りのsource、backend契約待ち、blocked、scope exclusion、完了条件は`REMAINING_TASKS.md`へ記録する。
