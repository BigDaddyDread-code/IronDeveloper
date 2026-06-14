# PR117 Typed Workflow Step Contract Receipt

PR117 adds the typed workflow step contract.

It makes workflow step intent, input references, expected output references, expected actor kind, allowed transitions, evidence requirements, and boundary flags explicit.

Typed workflow step contract validation is Core-only.

The contract is not workflow runtime.

The contract is not execution permission.

The contract is not workflow continuation.

The contract is not workflow resume.

The contract is not workflow retry.

The contract is not agent dispatch.

The contract is not tool invocation.

The contract is not LangGraph runtime.

The contract is not scheduler or orchestrator wiring.

The contract is not source apply.

The contract is not memory promotion.

The contract is not retrieval activation.

The contract is not approval satisfaction.

The contract is not release approval.

Input references are references only.

Expected output references are expectations only.

Allowed transitions are review-state transitions only.

Evidence requirements are requirements only.

Memory proposal artifacts remain review material only.

Approval policy references remain requirements only.

Boundary flags must remain false.

Raw prompts, raw completions, raw tool outputs, private reasoning, scratchpads, and entire patch payloads are rejected.

Workflow step contracts make the clipboard labels sharper.

They do not add the control panel.
