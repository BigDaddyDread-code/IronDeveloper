---
id: TINY_REST_API_DISPOSABLE_BUILD_185
project: IronDev
title: Tiny REST API Disposable Build 185
document_type: DogfoodProof
authority: Accepted
status: Current
created_utc: 2026-05-24T08:20:00Z
primary_retrieval_questions:
  - Can IronDev build a disposable ASP.NET Core REST API?
  - Can BuilderAgent generalise beyond WPF game spikes?
  - Does the disposable build loop keep generated REST API files inside the cage?
boundary: Disposable workspace proof only. No real repository writes, accepted memory mutation, ticket acceptance, guardrail mutation, or self-approval.
---

# Tiny REST API Disposable Build 185

Tiny REST API is the third disposable product target and the first non-WPF target in the governed build loop.

The useful question is:

```text
Can IDA take a messy request for a small ASP.NET Core API and build/test it inside the disposable cage without reusing WPF/game assumptions?
```

## What This Proves

- `build disposable run --project TinyRestApi --goal "i want build tiny rest api"` preserves the requested product.
- Run-scoped documents use `TINYRESTAPI_*` IDs, not `SOLITAIRE_*` or `MINESWEEPER_*`.
- Retriever context rejects WPF game product scope.
- BuilderAgent generates `TinyRestApi.Api` and `TinyRestApi.Tests` inside the disposable workspace.
- The generated app uses ASP.NET Core minimal API endpoints, DTOs, an in-memory store, and deterministic console tests.
- The repair loop intentionally breaks endpoint registration, repairs it, intentionally breaks create validation, repairs it, then reruns build/tests.
- QualityAgent/Killjoy runs as part of the full loop-gated command.
- Real repo mutation count remains zero.

## Generated Disposable Shape

```text
TinyRestApi.Api
  Program.cs
  TodoDtos.cs
  TodoStore.cs
  TodoEndpoints.cs

TinyRestApi.Tests
  Program.cs
```

The generated API includes:

- `GET /todos`
- `POST /todos`
- `PATCH /todos/{id:int}/complete`
- DTOs for create, complete, item, and typed API result evidence.
- Tests for empty list, create/trim, empty-title rejection, completion, and missing item handling.

## Boundary

This proof does not promote Tiny REST API into the real repository.

It does not make BuilderAgent a mature production engineer.

It proves another product shape can pass through the same governed disposable loop while remaining caged and traceable.

## Validation

```text
build disposable run --project TinyRestApi --goal "i want build tiny rest api" --run-id <run> --json
test run-plan --plan tools/dogfood/test-agent-plans/irondev-tiny-rest-api-disposable-build-185.json --run-id <run> --json
```

## Blunt Assessment

Solitaire proved a WPF game.

Minesweeper proved the game path was not hardcoded to Solitaire.

Tiny REST API proves the loop can leave WPF behind and build a different application type with endpoints, DTOs, validation, and tests.
