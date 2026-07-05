# J08 - Fail-Safe Config Summary With Secret Redaction

## Purpose

J08 adds a safe, redacted configuration summary contract for local/development diagnostics.

The summary helps a developer understand what IronDev thinks is configured without printing raw secrets, local override contents, raw connection strings, full user-local paths, or authority-shaped readiness claims.

## Summary Fields

The summary includes:

- environment name and development/production-like status
- configuration source presence/order
- redacted key/value summaries
- SQL configuration derived metadata
- AI provider/model status and base URL host/port only
- Weaviate enabled/auth status and endpoint host/port only
- local root configuration status
- feature flag state as status only
- warnings
- boundary statement

## Redaction Policy

Raw values are never emitted for sensitive keys.

Sensitive keys include password, pwd, secret, token, API key, access key, private key, JWT, bearer, authorization, connection string, and client secret shapes.

Connection strings may be parsed into safe derived fields:

- configured yes/no
- provider shape
- database name
- server kind
- authentication mode
- whether a credential key exists
- winning source name

The raw connection string is never emitted. Password values are never emitted, and password length, prefix, and suffix are never emitted.

If redaction is uncertain, the value is redacted. Debug convenience loses to secret safety.

## Path Redaction Policy

User-local path segments are redacted.

Examples:

- `C:\Users\<you>\.irondev\workspaces`
- `/home/<you>/.irondev/evidence`
- `/Users/<you>/.irondev/logs`

The summary may report a redacted path shape, but it must not print the raw local user name.

## Config Source Precedence

J08 does not change configuration precedence.

The API host precedence remains:

1. `appsettings.json`
2. `appsettings.Development.json`
3. `appsettings.Development.Local.json`
4. API user secrets, when available
5. environment variables
6. command-line arguments

The summary can report the winning source name when a caller supplies effective values in precedence order. It does not print the winning raw value when the key is sensitive.

## Root Safety Integration

J10 root-safety validation is implemented as a separate Core contract after J08.

Configured root entries are reported as `NotEvaluated` unless a root-safety result is supplied by a validator.

J08 does not reimplement root safety, create roots, check roots, clean roots, write evidence, or treat root existence as proof of safety.

## Endpoint / Logging Behavior

No endpoint was added.

No startup logging was added.

The J08 surface is a Core-only read-only diagnostic contract and service. A later developer-environment doctor or gated diagnostics endpoint can consume it.

## Tests Added

Added `IronDev.IntegrationTests/BlockJ08FailSafeConfigSummaryTests.cs`.

Coverage includes:

- raw connection strings are never emitted
- SQL credentials are reduced to safe derived metadata
- sensitive keys are redacted
- user-local paths are redacted
- local override presence is reported without contents
- environment-variable precedence can be represented without printing values
- root safety is `NotEvaluated` when no validator result is supplied
- supplied future root-safety results can be reported without revalidation
- unknown sensitive keys fail closed
- summary output avoids authority-shaped status language
- receipt boundary text is present
- no bootstrap or mutation surface is introduced

## Validation Run

Local validation to run before merge:

- J08 focused tests
- J01/J02/J03 focused tests
- C11 secret scan
- solution build
- `git diff --check`
- `git diff --cached --check`

GitHub CI must pass before merge.

## Boundary Statement

A config summary is diagnostic evidence for a human. It is not approval, authority, policy satisfaction, root safety proof, or permission to mutate anything.

## Out Of Scope

J08 does not add:

- SQL connectivity checks
- SQL bootstrap or rebuild commands
- SQL schema or migrations
- Weaviate bootstrap or rebuild commands
- environment doctor command
- root safety validation implementation
- endpoint exposure
- startup config logging
- production deployment config
- secret management redesign
- source apply behavior
- rollback behavior
- approval behavior
- critic authority behavior
- workflow continuation behavior
- release or deployment behavior
- UI configuration editor

Review line: a config summary helps humans debug setup. It does not bless the setup.

Killjoy: printing config is how secrets become receipts.
