# PLATFORM-BASELINE-1 Clean Validation

## Purpose

Provide one dependable validation path for product slices without relying on an already-running API, a developer database, or unlocked default build outputs.

## Supported local command

```powershell
.\Scripts\ci\run-platform-baseline-ci.ps1
```

The command:

1. resolves the isolated LocalTest SQL connection;
2. creates a uniquely named `*_Test` database;
3. builds the base schema, applies the migration manifest twice, and runs the migration verifier;
4. starts the real API in-process through `WebApplicationFactory` and executes endpoint contracts;
5. emits .NET build outputs under ignored `artifacts/platform-baseline`, not the live API's `bin` directory;
6. runs the frontend type/OpenAPI contract lane without deleting a live Vite process's dependency tree;
7. removes the isolated database and build output.

Use `-SkipFrontend` only when diagnosing the SQL/API half. Use `-KeepDatabase` only for explicit investigation; the database name remains test-guarded.

## CI enforcement

The full SQL lane runs clean-database migration verification and the in-process endpoint contract. The frontend workflow runs the live Swagger regeneration and dirty-tree gate. Together they reject migration, API, generated-contract, or TypeScript drift without requiring a developer stack.

## Process isolation

- API contract tests host the API in-process.
- OpenAPI generation builds the API into a unique temporary output.
- Platform .NET tests use an ignored per-run artifacts path inside the repository so source-inspection tests can still locate `IronDev.slnx`.
- Local frontend validation uses lockfile-respecting `npm install`; CI continues to use clean `npm ci`.
- The supported manual launcher stops only repository LocalTest processes and listeners before restart.

## Cleanup order

API test reset deletes Chat audit children before messages and sessions, document links before versions and documents, channel children before channels, project-scoped rows before projects, and tenant memberships before non-seeded users. Foreign keys remain enabled.

## Boundary

This path proves the selected validation contracts execute cleanly. It does not grant merge, release, deployment, workflow, approval, apply, or memory authority.
