# Actor Attribution Audit

**Status:** Canonical cleanup contract

**Programme slice:** CLN-12

## Contract

Every authenticated `POST`, `PUT`, `PATCH`, or `DELETE` request is attributed at the API boundary. The durable attempt is written before endpoint dispatch. If that write fails, dispatch does not occur.

Each record carries:

- actor user ID;
- tenant ID when a tenant context exists;
- route project ID when the route is project scoped;
- correlation ID and optional `X-Causation-ID`;
- UTC timestamp;
- source surface and client;
- method, route, phase, and terminal HTTP status.

Clients may identify themselves with `X-IronDev-Source-Surface` and `X-IronDev-Source-Client`. Safe defaults are `api` plus `tauri`, `browser`, or `api-client` inferred from the user agent. Client claims are provenance labels, never authority.

## Lifecycle

The ledger is append-only. A request writes `Attempted` before dispatch and then one of `Completed`, `Refused`, or `Failed`. A terminal-write failure cannot erase the durable attempt and is logged as critical.

The project audit ledger projects terminal attribution rows when their integer project and tenant resolve to a visible project. The attribution ledger grants no visibility, approval, continuation, apply, or administrative authority.

Anonymous entry operations are outside user attribution because no authenticated actor exists yet. Authentication security events remain owned by the security audit contract.
