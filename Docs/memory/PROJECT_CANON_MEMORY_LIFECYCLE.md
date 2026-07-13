# Project Canon Memory Lifecycle

**Status:** Canonical memory lifecycle contract

**Last reviewed:** 14 July 2026

**Programme slice:** CLN-25

## Identity and Version Contract

Project Canon uses a stable identity plus immutable versions. A title is display metadata, never identity and never an overwrite key.

Every version carries `StableMemoryId`, `VersionId`, `ContentHash`, `Status`, `CreatedByUserId`, `CreatedAtUtc`, `SourceEvidence`, `SupersedesVersionId`, `EffectiveFromUtc`, `RetiredAtUtc`, and `PromotionReceiptId`.

## Lifecycle

- A current version must have effective time and a promotion receipt.
- A replacement is appended with `SupersedesVersionId`; the predecessor is never updated or deleted.
- Archive/supersede is an appended lifecycle version with retirement time and promotion receipt.
- The current-truth view returns only `Current` versions with no successor.
- Archived/superseded heads are excluded from current truth.
- History queries return every version and lifecycle record.
- Stable identities and versions reject update/delete operations.
- A superseded version must belong to the same stable identity, tenant, and project.
- The database enforces that same-scope rule with a composite self-reference even when SQL is called outside the stored procedure.
- The first version is the only permitted root and must be `Current`.
- Every later append must name the single current leaf; branching from historical versions and adding a second successor are refused.
- Append validation and insertion run in one transaction under update/serializable locks on the stable identity and chain.

`PromotionReceiptId` is an opaque governed-evidence reference in this slice. CLN-25 requires it but does not create promotion authority or infer approval from possession of a GUID.

## SQL Authority

SQL owns identity, lifecycle integrity, and current/history projection. Semantic/vector indexes may project current versions later; they cannot rewrite or replace this ledger.

## Killjoy Line

Changing the title does not change the identity, and changing an embedding does not create a new truth version.
