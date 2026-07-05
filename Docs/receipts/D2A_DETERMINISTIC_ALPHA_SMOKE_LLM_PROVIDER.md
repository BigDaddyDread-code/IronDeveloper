# D2A - Deterministic Alpha-Smoke LLM Provider

## Summary

D2A adds a deterministic alpha-smoke LLM provider foundation for the governed
loop. The provider fakes model words only, so proposal, tester, critic, evidence,
workspace execution, and gates above the model seam still run through the real
services.

The provider is default-off. It is selected only when both flags are present:

- `AlphaSmoke:Enabled=true`
- `AlphaSmoke:ModelMode=Deterministic`

## Boundary

The deterministic provider is not approval, policy satisfaction, evidence,
receipt persistence, source apply authority, rollback authority, run completion,
release readiness, deployment readiness, or workflow continuation.

It does not create approval records, satisfy policy, write receipts, write
evidence hashes, mutate source, apply patches, commit, push, create pull
requests, merge, release, deploy, or continue workflow.

Its output is test input for higher-level services. It is not authority.

## Demo Containment

Runtime Infrastructure does not contain demo-specific answer material.

BookSeller deterministic response material lives under fixture data:

- `TestFixtures/BookSeller/alpha-smoke/responses/builder.json`
- `TestFixtures/BookSeller/alpha-smoke/responses/tester.json`
- `TestFixtures/BookSeller/alpha-smoke/responses/critic.json`

`DeterministicAlphaSmokeLlmService` only loads configured role responses. It
does not know the fixture name, project name, sample paths, ticket key, or answer
content.

## Gate

The provider selection gate remains two-factor:

1. `AlphaSmoke:Enabled` must be `true`.
2. `AlphaSmoke:ModelMode` must be `Deterministic`.

If either flag is absent, the resolver falls back to the configured normal
provider. Gate-negative tests use the safe fake provider fallback, so they prove
selection behavior without requiring OpenAI configuration or network access.

## Validation

Local validation to run before merge:

- `dotnet build IronDev.slnx --no-restore`
- focused deterministic provider tests
- `RuntimeRoots_ContainNoDemoNameSpecialCasing`
- skeleton-run CI lane
- governance-boundary CI lane
- `git diff --check`
- `git diff --cached --check`

## Review Line

The deterministic provider may fake model words. It must not fake the governed
loop, and runtime Infrastructure must not know the demo answer.

## Killjoy

A demo that only works because the machine already knows the answer is not a
demo. It is a shortcut wearing a lab coat.
