# B11 - Status Authority Glossary Adoption

This PR replaces exact status authority strings with AuthorityGlossary constants where possible.

## Boundary

The emitted status text did not change.
The glossary is still language only.
The glossary is not authority.
The glossary is not approval.
The glossary is not policy satisfaction.
The glossary is not execution permission.
Status remains explanation, not permission.
No profile operation set changed.
No status state changed.
No operation eligibility behavior changed.
No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.

## Scope

Changed:

- `IronDev.Core/Governance/AuthorityProfiles/AuthorityProfileStatusMapper.cs`
- `IronDev.IntegrationTests/BlockB11StatusAuthorityGlossaryAdoptionTests.cs`
- `Docs/receipts/B11_STATUS_AUTHORITY_GLOSSARY_ADOPTION.md`

Not changed:

- `AuthorityGlossary.cs`
- run authority profile operation sets
- status state or reason enums
- operation eligibility evaluator behavior
- profile validator behavior
- executors
- API, CLI, UI, SQL, durable store, or generated client paths

## Adopted Constants

`AuthorityProfileStatusMapper` now references existing glossary constants for exact existing emitted phrases:

- `AuthorityGlossary.DoNotExecuteFromStatusAlone`
- `AuthorityGlossary.DoNotTreatEligibleStatusAsApproval`
- `AuthorityGlossary.DoNotTreatEligibleStatusAsPolicySatisfaction`
- `AuthorityGlossary.DoNotApplySourceFromStatusAlone`
- `AuthorityGlossary.ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree`
- `AuthorityGlossary.DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority`
- `AuthorityGlossary.BoundedProfileAllowanceIsNotLaterStageAuthority`

No near-match wording was changed to fit the glossary.

## Validation

- Focused B11: 8/8 passed.
- B10 compatibility: 8/8 passed.
- B09/B08/B07/B06 compatibility: 53/53 passed.
- B05/B04/B03/B01 compatibility: 51/51 passed.
- BQ/BR/BS/BT/BU compatibility: 81/81 passed.
- Stable governance/status corridor: 1930/1930 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed with normal LF/CRLF warning.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.

## Review Line

Use the glossary where the status already says the exact same thing.

## Killjoy

Using the right words does not grant the right authority.
