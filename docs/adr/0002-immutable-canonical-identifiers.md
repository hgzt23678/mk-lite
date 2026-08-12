# ADR 0002: Immutable canonical identifiers

Status: accepted

All public IRIs are generated from a configured canonical HTTPS origin and stored as immutable values. Request host headers and untrusted forwarded headers never determine identity.

A domain change is an actor migration, not a runtime configuration edit. Internal UUID keys permit database changes without changing public identity.
