# Routing contract

The production frontend is a standalone Blazor WebAssembly application served by ASP.NET Core at
`/app/`. Browser history and route evaluation remain client-side after the initial static shell is
loaded. The Interactive Server test host is an oracle-only fixture and is not a production route.

The route inventory is generated from the pinned Misskey v12.119.2 source and nirax.ts. It is not
replaced by a list of links. Blazor's route table owns the component route, while query and hash
state is parsed by the page that owns the corresponding upstream contract.

Current verified rules:

- /about#federation is handled by AboutPage and preserves the selected tab through browser navigation.
- /authorize-follow?acct=... is handled by FollowPage; it parses acct from the explicit URI and uses the
  shared users/show/following/create presentation boundary.
- /settings/{section} and /admin/{section} keep the v12 menu shell. Unsupported sections expose
  data-capability-state="false" instead of returning a fabricated page.
- /timeline/{kind} maps only home, local, hybrid and global timelines currently exposed by the
  Dolphin contract.
- /notes/{id} keeps load/error state transitions and generation-based cancellation when the
  parameter changes.

Host headers and Tailscale hostnames are not used to construct authentication authorities or public
IRIs. Runtime configuration supplies PublicBaseUri and Authority.

The remaining 325 source mappings are classified in artifacts/frontend-inventory/; routes requiring
missing Dolphin endpoints remain planned or excluded until their backend contract, authorization and
durable side effects exist.
