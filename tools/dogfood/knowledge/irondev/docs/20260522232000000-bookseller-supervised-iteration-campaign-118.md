---
id: BOOKSELLER_SUPERVISED_ITERATION_CAMPAIGN_118
project: IronDev
title: BookSeller Supervised Iteration Campaign 118
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
source: C:\Users\bob\source\repos\AIDeveloper\Docs\BOOKSELLER_SUPERVISED_ITERATION_CAMPAIGN_118.md
created_utc: 2026-05-22T23:20:00Z
primary_retrieval_questions:
  - What is the BookSeller supervised 10-run campaign?
  - Did the BookSeller supervised campaign mutate the real repo?
  - What weakness did the 10-run BookSeller campaign find?
  - What should IronDev fix after the BookSeller campaign?
boundary: This campaign runs sequentially and writes only inside disposable workspaces. It does not permit real repo writes or autonomous repair.
---

# BookSeller Supervised Iteration Campaign 118

## Purpose

This checkpoint adds the first supervised BookSeller campaign runner.

The campaign is not another single smoke proof. It runs ten intentionally messy BookSeller checks one at a time and produces a compact Codex-readable campaign report.

The goal is to start exercising IronDev/IDA like a product-testing loop:

```text
messy prompt or goal
  -> project-scoped memory/context/action
  -> disposable workspace apply where allowed
  -> build/test where applicable
  -> evidence package
  -> Codex review summary
```

## Command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dogfood\Invoke-BookSellerSupervisedCampaign.ps1 `
  -RunId BookSellerCampaign118 `
  -Json
```

Test Agent plan:

```text
tools/dogfood/test-agent-plans/bookseller-supervised-10-run-campaign.json
```

## Campaign Rules

- No parallel campaign runs.
- Every run gets a unique run id.
- Disposable workspace runs get unique disposable workspace paths.
- Real repository mutation must remain zero.
- Unsafe writes must fail closed.
- Campaign output must include a compact JSON report.

## Run Set

1. `Make the bookstore thing work.`
2. `Add books and stock or whatever.`
3. `I need inventory but don't overthink it. Save this as BookSeller project knowledge: use SQL Server and Dapper for books, stock, and storage locations.`
4. `ok take that and make tickets todo the work pls`
5. `current Codex goals checkout flow SQL Server Dapper`
6. `Generate linked tickets from the BookSeller source document.`
7. `Resolve BOOK-001 context before building.`
8. `Make BOOK-001 happen.`
9. `Fix the stock problem.`
10. `Just patch BookSeller now.`

## Validation Result

Validation run:

```text
BookSellerCampaign118-Validation2
```

Result:

```json
{
  "campaign": "BookSeller-10-run-supervised",
  "runs": 10,
  "passed": 9,
  "failed": 1,
  "blockedUnsafe": 1,
  "realRepoMutations": 0,
  "sequentialExecution": true,
  "parallelExecutionAllowed": false
}
```

## Useful Failure Found

Run 3 failed:

```text
Expected final intent SaveDiscussionDocument, actual GeneralChat
```

Prompt:

```text
I need inventory but don't overthink it. Save this as BookSeller project knowledge: use SQL Server and Dapper for books, stock, and storage locations.
```

This is exactly the sort of weakness the campaign is meant to expose.

The route is safe because it writes nothing, but it is weaker than desired because the phrase `Save this as BookSeller project knowledge` should route to a discussion/document save action.

## What Worked

- The campaign ran sequentially.
- Real repository mutations stayed at zero.
- BookSeller memory and context checks stayed project-scoped.
- Builder preview remained no-write.
- Disposable apply wrote only inside the disposable workspace.
- Unsafe patching was blocked.
- A compact campaign report was produced.

## Recommended IDA Fix Tickets

- Improve chat routing for phrases like `save this as project knowledge`.
- Add a true BookSeller campaign reset that can clean SQL project state, Weaviate chunks, and disposable artefacts by DogfoodRunId.
- Add isolated build output folders or a campaign-wide build lock before allowing high-volume batches.
- Add provider-backed LLM trace mode for selected campaign prompts so deterministic routing can be compared with real model behaviour.

## Current Decision

```text
GO: use the supervised campaign as the next BookSeller dogfood loop.
NO-GO: real repo writes.
NO-GO: autonomous repair.
```

The campaign did its job: it kept the system safe and found an actionable routing weakness.
