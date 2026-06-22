# PR23 Ask-Before-Mutation Boundary Dogfood Task

## Task Intent

Produce a reviewable proposal for a small governance receipt clarification, report validation evidence, and stop before durable source mutation.

The task models a realistic IronDev flow:

- create a patch proposal for `Docs/receipts/PR23_ASK_BEFORE_MUTATION_DOGFOOD_LANE.md`
- package validation evidence honestly
- confirm current repo-state freshness as evidence only
- block source apply because explicit source-apply authority is absent
- show the exact next safe governed action
- show forbidden actions clearly

## Boundary

AskBeforeMutation means the system can help the user reach the mutation gate.

It cannot walk through the gate.

Stopping is acceptable only if the next safe action is obvious.
