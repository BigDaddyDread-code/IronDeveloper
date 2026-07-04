# J06 Local Weaviate Bootstrap/Rebuild Command Receipt

## Purpose

J06 adds a guarded local Weaviate command for developer setup.

The command may check, ensure, or rebuild a developer-local Weaviate collection only.

Local Weaviate state is a disposable derived index. Rebuilding it is setup convenience, not authority, approval, evidence, or readiness.

## Command Path

- `Scripts/local/weaviate-local.ps1`

Default invocation:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\weaviate-local.ps1
```

The default invocation behaves as `-CheckOnly`.

## Supported Switches

- `-CheckOnly`
- `-EnsureSchema`
- `-Rebuild`
- `-Endpoint`
- `-SchemaPath`
- `-CollectionName`
- `-ConfirmRebuild`
- `-NonInteractive`
- `-Verbose`

There is no force mode.

There are no API key, token, password, connection string, authorization-header, cloud-url, Docker-start, service-start, demo-seed, or smoke-run parameters.

## Default Check-Only Behavior

`-CheckOnly`:

- parses the endpoint
- classifies the endpoint as loopback/local or rejected
- classifies the collection name as safe local or rejected
- validates the optional schema path without printing it
- checks Weaviate reachability
- checks collection presence when Weaviate is reachable
- creates nothing
- deletes nothing
- imports nothing
- starts nothing
- writes no evidence

Check-only can report unavailable local Weaviate without failing when the endpoint and collection are safe.

Unsafe endpoint, malformed endpoint, credential-shaped endpoint, unsafe collection, or unsafe schema path fails closed.

## Local Endpoint Rules

Allowed endpoints are loopback only:

- `http://localhost:8080`
- `http://127.0.0.1:8080`
- `http://[::1]:8080`

J06 rejects:

- remote hostnames
- cloud Weaviate endpoints
- WCS-shaped endpoints
- LAN/private IPs
- public IPs
- endpoint user-info credentials
- credential-shaped endpoint text
- malformed URLs
- unknown hostnames
- environment-looking hostnames such as dev, test, staging, uat, accept, prod, or live

Local means loopback.

## Collection Name Rules

Allowed local collection patterns:

- `IronDeveloper_Local`
- `IronDeveloper_Local_<suffix>`
- `IronDeveloper_Dev`
- `IronDeveloper_Test`
- `IronDeveloper_J06_<suffix>`

J06 rejects production-shaped markers anywhere in collection names, including:

- Prod
- Production
- Live
- Accept
- Acceptance
- UAT
- Stage
- Staging
- Shared
- Release
- Main
- Customer
- Client

Examples rejected:

- `IronDeveloper_Prod`
- `IronDeveloper_Local_Prod1`
- `IronDeveloper_Test_Live`
- `IronDeveloper_Shared`
- `CustomerKnowledge`
- `ProductionKnowledge`

If the collection name is uncertain, J06 rejects it.

## Rebuild Confirmation Rules

`-Rebuild` is destructive.

It requires an exact confirmation phrase:

```text
REBUILD <CollectionName>
```

For example:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\weaviate-local.ps1 `
  -Endpoint http://localhost:8080 `
  -CollectionName IronDeveloper_Local `
  -Rebuild `
  -ConfirmRebuild "REBUILD IronDeveloper_Local"
```

J06 checks endpoint safety, collection safety, schema path safety, and rebuild confirmation before trying to mutate Weaviate.

Rebuild deletes only the named local collection.

J06 does not wildcard-delete schema.

J06 does not delete all collections.

J06 does not infer safety from environment labels.

## Schema Path Rules

`-SchemaPath` is optional.

If omitted, J06 uses a minimal local schema with `vectorizer = none`.

If supplied, the schema path must:

- be inside the repository
- resolve safely
- exist as a file
- have a schema class matching the requested collection name

J06 rejects:

- URLs
- network shares
- user-home paths outside the repository
- temp paths outside the repository
- traversal outside the repository
- missing files
- invalid JSON
- schema class mismatch

J06 reads schema JSON as data only.

J06 does not execute schema files as code.

## What J06 Refuses To Do

J06 does not start Docker or Weaviate.

J06 does not run docker compose.

J06 does not install Weaviate.

J06 does not load demo, BookSeller, or product data.

J06 does not run alpha smoke.

J06 does not run critic, review, approval, or workflow flows.

J06 does not write evidence.

J06 does not write memory.

J06 does not change source, SQL, authority records, receipts, workflow state, release readiness, deployment readiness, or product truth.

J06 does not claim alpha, merge, release, or deployment readiness.

## J04 / J05 Interaction

J04 does not call J06.

J05 does not call J06.

J06 remains an explicit local command.

Bootstrap scripts may mention next safe actions, but they must not silently start, ensure, rebuild, or seed Weaviate.

## Boundary Statement

Weaviate is a derived index, not source of truth.

SQL/source records remain authority.

Local vector state is disposable.

Local vector rebuild is not evidence.

Local vector rebuild is not approval.

Local vector rebuild is not policy satisfaction.

Local vector rebuild is not alpha readiness.

Local vector rebuild is not release readiness.

Local vector rebuild is not merge permission.

Local vector rebuild is not workflow continuation.

Local schema presence is not product correctness.

Local check success is not CI success.

## Tests Added

J06 adds `BlockJ06LocalWeaviateBootstrapCommandTests`.

The tests cover:

- command existence and documentation
- default check-only behavior
- mutation modes requiring explicit switches
- endpoint allow/reject classification
- credential-shaped endpoint rejection
- collection allow/reject classification
- exact rebuild confirmation
- no wildcard schema deletion
- schema path safety
- forbidden credential/service/demo/smoke parameters
- no Docker or service start behavior
- no J04/J05 auto-invocation
- non-authority receipt wording
- no production runtime authority surface

## Validation Run

- J06 focused command-contract tests: 36/36 passed.
- J04 focused local bootstrap compatibility: 10/10 passed.
- J05 focused local SQL compatibility: 42/42 passed.
- H13/H14 Weaviate boundary compatibility: 20/20 passed.
- Integration category contract: 7/7 passed.
- C11 secret scan: 9/9 passed.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed with 0 errors / 7 existing warnings.

ConfigBoundary was run and failed 118/120 because J03 flagged pre-existing literals outside the J06 changed-file set:

- `IronDev.IntegrationTests/DemoContainmentStaticBoundaryTests.cs` contains existing workstation/local SQL marker literals.
- `IronDev.IntegrationTests/BlockJ05LocalSqlBootstrapCommandTests.cs` contains existing local SQL fixture literals.

J06 does not change those files.

The J06-focused lane, J04/J05 compatibility lanes, H13/H14 Weaviate lanes, category contract, C11, restore, and build all passed.

## Known Limitations

J06 tests do not require live Weaviate.

J06 tests do not require Docker.

J06 does not prove a real local collection was created.

J06 does not validate existing indexed content.

J06 does not seed vectors.

J06 does not implement a developer environment doctor.

J06 does not replace `Scripts/weaviate-dev.ps1` for explicit Docker lifecycle operations.

## Next Intended Slice

J07-lite - Developer environment doctor / alpha smoke preflight.

Review line: J07-lite reports readiness blockers. It does not create readiness.

Killjoy: A machine that can run the loop has not proven the loop is correct.
