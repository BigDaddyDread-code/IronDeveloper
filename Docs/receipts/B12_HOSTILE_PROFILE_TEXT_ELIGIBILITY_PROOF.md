# B12 - Hostile Profile Text Eligibility Proof

Hostile profile text cannot become eligibility.

## Boundary

ProfileId text is not profile kind.
Subject text is not authority.
OperationId text is not authority.
Evidence ref text is not downstream authority.
Receipt ref text is not eligibility.
HumanReadableIntent is not authority.
ValidationKind wording is not approval.
Validation evidence wording is not approval.
Accepted apply approval prefixes do not authorize later lanes.
Bounded grant evidence prefixes do not authorize mismatched operations.
Operation eligibility evidence prefixes do not authorize mismatched operations.
Eligibility comes from structured governance fields only.
No production behavior changed.
No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.

## Scope

Changed:

- `IronDev.IntegrationTests/BlockB12HostileProfileTextEligibilityProofTests.cs`
- `Docs/receipts/B12_HOSTILE_PROFILE_TEXT_ELIGIBILITY_PROOF.md`

Not changed:

- production code
- `AuthorityGlossary.cs`
- `AuthorityProfileStatusMapper.cs`
- `AuthorityProfileStatusReason.cs`
- `RunAuthorityProfileValidator.cs`
- `RunAuthorityProfileEvaluator.cs`
- `OperationEligibilityEvaluator.cs`
- `BoundedRunAuthorityGrantValidator.cs`
- `SourceApplyAuthorityEvaluator.cs`
- executors
- API, CLI, UI, SQL, durable store, or generated client paths
- operation sets
- status state enums
- status reason enums

## Proofs

B12 proves that hostile words in freeform and reference fields do not create authority:

- ProfileId text cannot change profile kind.
- ProfileId text cannot widen allowed operations.
- Subject text cannot become authority.
- OperationId text cannot become authority.
- EvidenceRef text cannot become downstream authority.
- ReceiptRef text cannot become eligibility.
- HumanReadableIntent cannot widen operation kind.
- HumanReadableIntent cannot widen file scope.
- HumanReadableIntent cannot override stop-before.
- ValidationKind hostile wording cannot satisfy unrelated validation.
- EvidenceRef hostile suffix cannot override operation mismatch.
- Receipt text cannot replace operation eligibility decision.
- Subject text cannot replace operation eligibility decision.
- Production eligibility/status code does not parse hostile freeform authority words.

Evidence-ref prefixes remain narrow: they may satisfy only the explicit prefix checks already present in the contract. Text after the prefix is still not authority.

## Validation

- Focused B12: 13/13 passed.
- B11 compatibility: 8/8 passed.
- B10 compatibility: 8/8 passed.
- B09 compatibility: 14/14 passed.
- B08 compatibility: 15/15 passed.
- B07 compatibility: 10/10 passed.
- B06 compatibility: 14/14 passed.
- B05/B04/B03/B01 compatibility: 51/51 passed.
- BQ/BR/BS/BT/BU compatibility: 81/81 passed.
- Stable governance/status corridor: 1943/1943 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Line

Text can describe intent. It cannot become eligibility.

## Killjoy

Text can lie. Gates must not listen.
