# PR24 Bounded Authority Draft PR Dogfood Task

## Task Intent

Produce a reviewable proposal for a narrow governance receipt clarification, report validation, apply the proposal inside a controlled fixture, package and execute a controlled commit, push through a controlled gateway, create a controlled draft PR, and then stop.

The task models the common IronDev path:

- create a patch proposal for `Docs/receipts/PR24_BOUNDED_AUTHORITY_DOGFOOD_LANE.md`
- package validation evidence honestly
- verify repo freshness evidence before mutation
- apply only inside a scoped dogfood fixture
- package commit evidence
- execute commit only through a fake controlled commit gateway
- execute push only through a fake controlled push gateway
- execute draft PR creation only through a fake controlled draft PR gateway
- stop before ready-for-review, merge, release, deployment, memory promotion, and workflow continuation

## Boundary

Bounded authority should make the common path fast without making the dangerous path possible.

A scoped key opens one door, not the building.

This dogfood task uses controlled test fixtures only. It does not mutate the real IronDev repository, call real GitHub, run shell commands, push to a real remote, mark ready for review, merge, release, deploy, promote memory, or continue workflow.
