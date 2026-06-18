# PR267-271 Safe Memory From Real Runs

## Purpose

Block AE lets IronDev create safe memory proposals from real patch-run evidence, inspect them, gate their keys, and append accepted memory versions through explicit Conscience and ThoughtLedger evidence.

This is file-backed memory only.

## Boundary

This block does not add SQL, API, UI, scheduler, worker, background runtime, autonomous learning, retrieval activation, accepted-memory authority, source apply, rollback, release approval, workflow continuation, policy satisfaction, tool authority, agent dispatch, model authority, or automatic memory promotion.

A memory proposal is review material.

An accepted memory version is retained evidence.

Accepted memory is not authority.

Memory can inform later review. It cannot approve, execute, promote itself, satisfy policy, continue workflow, release software, mutate source, activate retrieval, or override human review.

## What changed

- Added Core memory proposal, key gate, promotion request, promotion receipt, and accepted-memory version models.
- Added a deterministic proposal builder that reads safe patch-run artifacts.
- Added a memory key gate that blocks unsafe, broad, secret-shaped, authority-shaped, path-shaped, raw-source, and portable-project-leaking memory.
- Added a file-backed accepted memory store with append-only version files and an accepted-memory index.
- Added CLI commands:
  - `irondev memory propose`
  - `irondev memory proposals`
  - `irondev memory promote`
  - `irondev memory list`
  - `irondev memory show`
- Added governed action spine entries for memory proposal, inspection, key gate, promotion request, promotion block, promotion acceptance, accepted-memory version append, and accepted-memory inspection.
- Added Block AE regression tests.

## Required human evidence

Memory promotion requires:

- a memory proposal,
- a key gate allow result,
- a Conscience allow decision,
- a ThoughtLedger reference,
- proposal evidence references,
- human review.

Missing Conscience or ThoughtLedger evidence fails closed.

Agent self-promotion is blocked.

Portable memory that contains project-specific details is blocked.

## Storage shape

The accepted memory store writes only file-backed artifacts:

- `accepted-memory-index.json`
- `accepted-memory.jsonl`
- `accepted-memory-versions/<memory-id>/<version>.json`
- `accepted-memory-receipts.jsonl`

No SQL-backed memory table is added in this block.

## Validation

Validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "BlockAESafeMemoryFromRealRuns" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "IronDevCliTests|BlockZManualPatchProposalProduct|BlockAAPatchLoopUsability|BlockABThinGovernedActionSpine|BlockACGovernedWorkspaceTools|BlockADAiPatchAssistance|BlockAESafeMemoryFromRealRuns" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
```

Results:

- `BlockAESafeMemoryFromRealRuns`: 10/10 passed.
- Patch CLI / Block Z-AA-AB-AC-AD regression band: 204/204 passed.
- `dotnet build IronDev.slnx --no-restore -v:minimal`: passed with 0 errors and existing warnings.
- `git diff --check`: passed with LF/CRLF warnings only.

## Review line

PR267-271 creates safe memory from real patch-run evidence. It proposes, gates, and appends memory but does not make memory authoritative.

## Killjoy line

Block AE is finished when memory can be remembered safely, not when memory can start making decisions.
