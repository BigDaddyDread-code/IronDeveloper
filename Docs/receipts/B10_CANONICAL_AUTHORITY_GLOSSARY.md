# B10 — Canonical Authority Glossary

This PR adds canonical authority glossary constants.

## Boundary

The glossary is not authority.
The glossary is not approval.
The glossary is not policy satisfaction.
The glossary is not execution permission.
The glossary does not create or widen any profile.
The glossary does not change status mapping behavior.
The glossary does not change operation eligibility behavior.
The glossary does not add executors.
The glossary does not add UI, API, CLI, SQL, durable store, or generated client paths.

## Scope

Added:

- `IronDev.Core/Governance/AuthorityGlossary.cs`
- `IronDev.IntegrationTests/BlockB10CanonicalAuthorityGlossaryTests.cs`
- `Docs/receipts/B10_CANONICAL_AUTHORITY_GLOSSARY.md`

Not added:

- executor paths
- API, CLI, UI, SQL, durable store, or generated client paths
- profile operation changes
- status state changes
- operation eligibility behavior changes
- approval, policy satisfaction, memory promotion, or workflow continuation paths

## Contract

The glossary is a language-only constants holder. It is not a policy engine, mapper, bridge, evaluator, translator, authority source, approval source, or execution source.

The focused tests lock exact constant values and bind key existing governance outputs to those constants:

- profile allowance remains necessary but not sufficient
- eligibility remains necessary but not sufficient
- eligible status remains not execution
- AskBeforeMutation remains one guarded door
- BoundedRunAuthority remains bounded at the next authority boundary

## Validation

- Focused B10: 8/8 passed.
- B09/B08/B07/B06 compatibility: 53/53 passed.
- B05/B04/B03/B01 compatibility: 51/51 passed.
- BQ/BR/BS/BT/BU compatibility: 81/81 passed.
- Stable governance/status corridor: 1922/1922 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Line

If the vocabulary drifts, the boundary drifts.

## Killjoy

Words are not gates, but bad words rot gates.
