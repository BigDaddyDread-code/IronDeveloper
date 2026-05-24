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

Slice 169 creates the bridge between a successful disposable build and a future reviewed apply path.

It introduces:

- `ProposedChange`, the IDA case file for a possible change.
- `PromotionPackage`, the review evidence attached to that case file.
- `ILanguageRuntimeRegistry` and `LanguageRuntimeProfile`.
- executable `csharp-dotnet` support.
- contract-only `java-maven`, `typescript-node`, and `python-pytest` profiles marked `NotExecutableYet`.

Commands:

```text
promotion package create --source-run-id <run> --project Solitaire --run-id <package-run> --json
campaign promotion-package-169 --run-id <run> --json
```

Validation proved a 168 source run can be packaged into a ProposedChange and PromotionPackage with 17 promotable generated source/project files, generated `bin/` and `obj/` files blocked, build/test/quality evidence attached, approval state `NeedsHumanReview`, and real repo mutation count zero.

Boundary: review/package only. No apply, branch creation, real repo writes, accepted memory mutation, ticket acceptance, self-approval, or fake multi-language execution support.
