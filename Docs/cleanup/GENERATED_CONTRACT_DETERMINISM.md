# Generated Contract Determinism

**Status:** Canonical cleanup contract

**Programme slice:** CLN-14

## Source And Pins

The running `IronDev.Api` Swagger document is the API source. Contract generation runs from an isolated Test-environment API using the SDK pinned by `global.json`. The frontend pins Node, npm, and `openapi-typescript` in `package.json` and `package-lock.json`.

## Proof

`tools/contracts/update-openapi-contract.ps1 -Check -VerifyDeterminism`:

1. builds and starts one isolated API;
2. generates the OpenAPI snapshot and TypeScript types;
3. generates both artifacts again from the same process;
4. compares first and second SHA-256 hashes;
5. compares the final hashes with the checked-in artifacts.

The frontend contract CI lane runs this proof. Nondeterministic generation, stale artifacts, or manual edits therefore fail the lane.

TypeScript enums are generated from the checked-in OpenAPI schema by the pinned generator; they are not maintained as a separate handwritten contract.

Planned routes remain explicit. A route that exists only to report unavailable behavior must advertise `501` and its honest response schema in OpenAPI. It must not masquerade as `200` or disappear from generation.
