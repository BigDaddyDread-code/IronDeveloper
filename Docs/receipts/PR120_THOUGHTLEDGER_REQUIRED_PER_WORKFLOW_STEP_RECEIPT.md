# PR120 - ThoughtLedger Required Per Workflow Step Receipt

## Summary

PR120 requires every typed workflow step to carry a ThoughtLedger reference. The reference provides traceability to safe governance reasoning/evidence material without storing hidden/private reasoning, raw prompts, raw completions, raw tool output, or whole-patch payloads in the workflow step.

## Boundary

ThoughtLedger reference presence is not approval, policy satisfaction, execution authority, memory promotion authority, or retrieval activation.

The runner skeleton remains evaluation-only.

PR120 does not read or write ThoughtLedger content. It requires the reference only.

## What changed

- Added `WorkflowStepThoughtLedgerReference`.
- Required a ThoughtLedger reference on typed workflow step contracts.
- Updated workflow step contract validation and normalization.
- Propagated the reference into runner step evaluation output.
- Added tests proving ThoughtLedger traceability does not satisfy evidence or policy preflight.
- Added static tests proving no ThoughtLedger reader/writer, runtime, SQL, API, CLI, or authority surface was added.

## What this does not do

- Does not execute workflow steps.
- Does not transition workflow state.
- Does not approve anything.
- Does not satisfy policy.
- Does not grant authority.
- Does not read ThoughtLedger content.
- Does not write ThoughtLedger entries.
- Does not dispatch agents.
- Does not invoke tools.
- Does not mutate source.
- Does not apply patches.
- Does not promote memory.
- Does not activate retrieval.
- Does not call models.
- Does not add SQL, API, CLI, UI, or hosted runtime surface.

## Explicit limitation

PR120 requires ThoughtLedger references on workflow steps. It does not record workflow step transitions. When transition recording is added, every transition must also require ThoughtLedger traceability.

## Review line

PR120 requires every workflow step to carry a ThoughtLedger trail marker. It does not let the marker drive.
