# Workspace Cleanup and Retention Policy

**Status:** Canonical operations contract

## Vocabulary

- An **active workspace** is in use and never cleanup-eligible.
- A **failed workspace** is preserved until its failure evidence is archived and remains inspectable.
- An **applied workspace** has completed its governed apply lifecycle; application alone does not make it cleanup-eligible.
- **Archived evidence** is retained outside the derived workspace in an inspectable evidence store.
- **Cleanup eligibility** means eligibility for a later governed human review, never delete permission.
- The **retention period** starts at last activity. A **quota** compares total retained workspace usage with the configured limit and may prioritize already eligible candidates, but cannot shorten retention.
- A **manual hold**, **legal hold**, or **audit hold** blocks eligibility. Legal and audit holds cannot be bypassed by quota.

Only identified, derived workspaces with a known lifecycle state and non-negative policy inputs can become eligible. Invalid or unknown inputs fail closed. Required receipts and failed-run evidence must remain inspectable before the workspace can be considered. The evaluator returns no delete command and creates no cleanup, apply, approval, or workflow authority.
