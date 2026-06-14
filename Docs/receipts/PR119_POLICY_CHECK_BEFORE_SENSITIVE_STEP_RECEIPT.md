# PR119 - Policy Check Before Sensitive Step Receipt

## Summary

PR119 adds policy preflight checks before sensitive workflow steps can be considered eligible for future execution. It classifies sensitive steps, validates required policy evidence references, and blocks missing policy evidence.

## Boundary

Policy evidence is not approval.

Policy evidence does not satisfy policy.

Policy evidence does not execute or transition workflow state.

The runner skeleton remains evaluation-only.

## What changed

- Added typed workflow step sensitivity classification.
- Added policy requirement and policy evidence reference models.
- Added policy preflight request/result/status/block-reason models.
- Added `IWorkflowStepPolicyPreflightChecker`.
- Added deterministic `WorkflowStepPolicyPreflightChecker`.
- Integrated policy preflight into the PR118 runner skeleton as an evaluation blocker only.
- Added focused checker, runner integration, and static boundary tests.

## What this does not do

- Does not execute workflow steps.
- Does not transition workflow state.
- Does not approve anything.
- Does not satisfy policy.
- Does not grant authority.
- Does not dispatch agents.
- Does not invoke tools.
- Does not mutate source.
- Does not apply patches.
- Does not promote memory.
- Does not activate retrieval.
- Does not call models.
- Does not add SQL, API, CLI, UI, or hosted runtime surface.
- Does not store hidden/private reasoning, raw prompts, raw completions, raw tool output, or whole-patch payloads.

## Review line

PR119 adds the policy preflight lock. It checks sensitive steps; it does not grant the key.
