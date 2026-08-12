# ADR 0003: Dual HTTP signature profiles

Status: accepted

Inbound federation accepts the legacy Mastodon HTTP Signatures profile and RFC 9421 after exact-body digest validation. Outbound federation supports both profiles and defaults to the broadly compatible legacy profile unless peer capability or a bounded authentication retry selects RFC 9421.

NSign supplies RFC 9421 parsing and cryptographic providers. A narrow compatibility adapter owns legacy canonicalization. Domain and application modules do not depend on either representation.
