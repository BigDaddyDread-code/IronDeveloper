# Block H Project Authority Policy Model

Block H begins with policy vocabulary only.

PR82 defines ProjectAutonomyPolicy contracts.

PR82 does not evaluate policy.
PR82 does not approve or execute anything.

Project autonomy levels are:

- Conservative
- Balanced
- Experimental

The word "free" is intentionally forbidden as an autonomy level because it suggests unbounded agent authority.

Missing policy must later fail closed.

Sensitive actions remain human-review gated, including source apply, accepted-memory promotion, destructive operations, external side effects, and release approval.

Project autonomy policy is not approval, execution permission, workflow routing, source apply, memory promotion, release approval, or model authority.

Later blocks may use this vocabulary to evaluate approval requirements, but this contract does not grant authority.
