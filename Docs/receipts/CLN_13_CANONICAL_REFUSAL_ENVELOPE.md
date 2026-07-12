# CLN-13 Canonical Governed Refusal Envelope Receipt

**Recorded:** 13 July 2026

## Delivered

- Added the shared `GovernedRefusalEnvelope` and normalizing factory.
- Applied it directly to route/body scope refusals.
- Added compatible canonical refusal members to governed continuation and release-readiness actions.
- Added factory behavior and static controller-boundary tests.

## Boundary

The envelope explains a backend refusal. It cannot authorize, approve, continue, apply, release, or mutate state.
