---
id: PROMOTION_PACKAGE_LANGUAGE_RUNTIME_169
project: IronDev
title: Promotion Package And Language Runtime Spine 169
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T16:09:00Z
primary_retrieval_questions:
  - What is a ProposedChange?
  - How does IronDev package disposable output for promotion review?
  - How does IronDev model language runtimes for future Java, TypeScript, and Python support?
boundary: Review/package only. No apply, branch creation, real repo writes, accepted memory mutation, ticket acceptance, or self-approval.
---

# Promotion Package And Language Runtime Spine 169

## Purpose

Slice 169 creates the missing bridge between a successful disposable build and a future reviewed apply path.

It introduces two durable concepts:

- `ProposedChange`: the case file IDA tracks through review.
- `PromotionPackage`: the evidence package attached to a proposed change.

It also adds a first-class language runtime spine so future languages can be added without rewriting promotion, evidence, Critic review, or build/test planning.

## Commands

Create a package from an existing source run:

```text
promotion package create --source-run-id <run> --project Solitaire --run-id <package-run> --json
```

Run the dogfood proof end-to-end:

```text
campaign promotion-package-169 --run-id <run> --json
```

The campaign first runs the 168 loop-gated disposable build, then packages its disposable output.

## ProposedChange

`ProposedChange` is not the patch.

It is the case file:

```text
messy prompt
  -> ProposedChange
  -> source docs/tickets/runs/traces
  -> promotion package
  -> future isolated apply decision
```

It records:

- proposed change id
- project
- source goal
- source document ids
- source ticket ids
- source run ids
- source trace ids
- target runtime profile id
- current stage
- promotion package id
- approval state
- risks
- evidence refs

## PromotionPackage

`PromotionPackage` is review evidence.

It records:

- package id
- proposed change id
- source run and trace
- runtime profile
- files to promote
- blocked files
- tests passed
- risks
- human review checklist
- evidence summary
- recommendation

It does not apply files.

## Language Runtime Spine

The language/runtime abstraction is first-class.

Implemented:

- `ILanguageRuntimeRegistry`
- `LanguageRuntimeProfile`
- executable `csharp-dotnet` profile

Contract-only profiles:

- `java-maven`
- `typescript-node`
- `python-pytest`

The non-C# profiles are intentionally marked `NotExecutableYet`. They prove the shape without pretending IronDev can build those languages today.

## Validation Result

Validation proved:

- 168 source run can be packaged.
- ProposedChange id is created.
- PromotionPackage id is created.
- Runtime profile is `csharp-dotnet`.
- C# runtime profile is executable.
- Java, TypeScript, and Python profiles exist but are not executable yet.
- 17 generated source/project files are promotable.
- generated `bin/` and `obj/` files are blocked.
- build/test/quality evidence is attached.
- approval state is `NeedsHumanReview`.
- real repo mutation count remains zero.

## Boundary

169 does not:

- apply files
- create an isolated branch/worktree
- mutate accepted memory
- accept tickets
- approve promotion
- write generated files into the real repo
- claim Java/TypeScript/Python execution support

## Blunt Assessment

This is the correct bridge.

IronDev can now say:

```text
Here is the proposed change.
Here is the evidence.
Here are the files worth promoting.
Here are the files blocked.
Here is the runtime profile.
Here is what a human is being asked to review.
```

That is the step before isolated branch/worktree apply.
