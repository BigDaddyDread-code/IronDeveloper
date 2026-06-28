# E17 - PR URL Is Not Release-Candidate Ref Regression

## Review line

A pull request points at review work. It is not a release candidate.

## Killjoy line

PR-shaped evidence is review evidence, not release-candidate evidence.

## Scope

E17 is a regression hard-stop. It adds no release-candidate creation, release-readiness, release-execution, or deployment path.

This slice adds:

- `IronDev.IntegrationTests/BlockE17PrUrlIsNotReleaseCandidateRefRegressionTests.cs`
- `Docs/receipts/E17_PR_URL_IS_NOT_RELEASE_CANDIDATE_REF_REGRESSION.md`
- a narrow `IronDev.Core/Governance/MergeReleaseSeparation.cs` hard-stop for PR-shaped release-candidate refs

The production touch is limited to the existing release-readiness evidence packager and only rejects PR-shaped `ReleaseCandidateRef` values as evidence gaps. It does not add a release-candidate classifier subsystem.

## Boundary

A pull request reference proves only that there is review work associated with a pull request.

It explicitly does not grant or satisfy:

- release-candidate authority
- release readiness
- release execution
- deployment readiness
- deployment execution
- workflow continuation
- merge readiness as release evidence
- PR URL as release-candidate ref
- PR number as release-candidate ref
- PR provider id as release-candidate ref
- PR receipt as release-candidate ref
- PR head branch as release-candidate ref
- PR base branch as release-candidate ref
- PR state evidence as release-candidate ref
- approval
- policy satisfaction
- source safety
- mutation authority

## Regression proof

E17 proves that release readiness fails closed with `NeedsMoreReleaseEvidence` when `ReleaseCandidateRef` is PR-shaped:

- pull request URL
- pull request number
- provider PR id
- draft, non-draft, and merged PR receipt refs
- merge-readiness evidence refs
- PR head branch variants
- PR base branch variants
- PR head SHA when supplied as PR evidence
- PR base SHA when supplied as PR evidence
- PR status/read-model/provider-state refs

E17 also proves valid release-candidate-shaped refs are not over-rejected:

- `release-candidate:*`
- `release-candidate-package:*`
- `release-candidate-ref:*`
- `rc-package:*`
- `release-artifact:*`
- `release-decision-candidate:*`

## Static proof

The E17 test file statically verifies that the slice adds no:

- GitHub calls
- provider calls
- process execution
- executor path
- API, CLI, persistence, SQL, OpenAPI, worker, or UI surface
- release execution
- deployment execution
- workflow continuation path

## Validation

- Focused E17: 39/39 passed
- E16/E17 compatibility lane: 75/75 passed
- E01-E17 corridor: 1378/1378 passed
- Combined A02/A05 + D01-D20 + E01-E17 corridor: 2878/2878 passed
- Governance boundary CI local script: passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warning
- `git diff --cached --check`: passed with normal LF/CRLF warning
