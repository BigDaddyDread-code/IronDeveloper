# Isolated Promotion Apply Proof 170

Slice 170 proves that a `PromotionPackage` can become an isolated candidate workspace without touching the active IronDev working tree.

It introduces:

- `promotion apply isolated --package-run-id <package-run> --run-id <apply-run> --json`
- `campaign isolated-promotion-apply-170 --run-id <run> --json`
- `IsolatedPromotionApplyReport`

The proof:

- consumes a `PromotionPackage`
- preserves the `ProposedChangeId`
- creates an isolated candidate workspace outside the active repo
- initializes an isolated candidate branch marker
- copies only `FilesToPromote`
- rejects `FilesBlocked`
- runs C#/.NET build/test in the isolated workspace
- writes JSON/Markdown apply reports
- proves the active repo status is unchanged before/after the apply proof

Boundary:

```text
Isolated candidate workspace only. No main writes, accepted memory mutation, ticket acceptance, auto-merge, or self-approval.
```

This does not approve real repository writes. It proves the promotion package can safely become isolated review evidence.
