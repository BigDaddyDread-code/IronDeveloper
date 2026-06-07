# IronDev L4 Agent Spine

## Purpose

This document defines the target architecture and quality bar for IronDev's agent system.

IronDev is not trying to become a loose collection of prompts, wrappers, scripts, and clever demos.

IronDev's agent system must become a release-quality product spine: clean, obvious, safe, traceable, and beautiful in its architecture.

Every agent, CLI, governance, memory, tool, workflow, and dogfood change should be judged against this document.

## North Star

IronDev should eventually support this flow:

```text
Rob gives IronDev a goal.
IronDev retrieves project context.
Supervisor plans.
Conscience checks boundaries.
Tester runs.
Builder works only inside a disposable workspace.
Quality validates.
Critic reviews.
IronDev produces a promotion package.
Rob approves or rejects.
Only then does real code move.
```

The system should feel like a serious AI development cockpit, not a wrapper around Codex, not prompt theatre, and not a pile of scripts.

## Target Level: Controlled L4

L4 is the goal.

But L4 does not mean uncontrolled autonomy.

L4 means:

```text
A governed agent can take a goal, plan the work, execute inside a disposable or controlled workspace, run tests, collect evidence, diagnose failures, iterate within limits, and produce a promotion package for human approval.
```

L4 may act autonomously inside the cage.

L4 may not silently mutate the real repository, memory, tickets, approvals, production state, or project truth.

## Autonomy Ladder

### L1 - Observe

The agent can read state, summarize, classify, and report.

No mutation.

### L2 - Diagnose

The agent can produce failure packages, evidence summaries, likely causes, warnings, and recommended next actions.

No mutation.

### L3 - Propose

The agent can create implementation plans, patch proposals, test plans, and risk notes.

No real apply.

### L4 - Execute in Cage

The agent can apply changes only inside a disposable workspace, run build/test commands, iterate within limits, collect evidence, and produce a promotion package.

No real repository writes.

No silent promotion.

### L5 - Real Promotion

The system may apply or merge to the real repository only after explicit human approval.

L5 is not autonomous.

## Hard Boundaries

Every serious agent path must enforce these boundaries:

```text
1. Goal intake is explicit.
2. Plan is visible before execution.
3. Workspace is disposable or controlled.
4. Real repository writes are blocked by default.
5. Tool calls are typed and governed.
6. Build/test commands are captured.
7. Every decision has trace/evidence.
8. Failures produce a structured package.
9. Iteration has limits.
10. Promotion requires human approval.
```

If a workflow does not meet these rules, it is not L4.

## Current Spine

The current direction is:

```text
CLI contract first
real report truth
agent consumes contract
product command surface
ReplayRunner demoted
failure package next
controlled L4 later
```

The product-facing path should prefer:

```text
IronDev.Cli
  -> application service
  -> governed agent/workflow
  -> run report contract
  -> evidence
  -> stable CLI envelope
```

Avoid:

```text
Agent
  -> random script
  -> raw ReplayRunner output
  -> fake green result
  -> hidden mutation
```

## ReplayRunner Boundary

ReplayRunner may remain as:

```text
internal dogfood harness
test runner
legacy replay path
```

ReplayRunner must not be treated as the product interface.

Product surfaces belong in `IronDev.Cli` and application services.

## CLI Contract Rule

Product CLI commands must return real system data using stable envelopes.

A product CLI command must not return fake success.

A product CLI command must not expose raw internal harness output as product truth.

Standard envelope:

```json
{
  "status": "succeeded | failed | blocked",
  "command": "canonical command name",
  "traceId": "optional trace id",
  "summary": "human-readable summary",
  "data": {},
  "errors": [],
  "warnings": []
}
```

## Agent Failure Rule

When an agent run fails or blocks, it must produce enough structured information for Codex or Rob to inspect before patching.

Minimum failure package:

```text
runId
status
decision
decisionReason
tester status
blocked reason
warnings
errors
evidence paths
commands run
recommended next action
```

Failure packages are diagnostic artifacts.

They must not patch, write, approve, or mutate.

## Promotion Rule

No agent may promote changes to the real repository without explicit human approval.

Promotion packages should include:

```text
goal
plan
changed files
patch summary
build evidence
test evidence
risk notes
known limitations
rollback notes
human approval status
```

## Quality Bar

The agent system must be:

```text
Clean:
No hidden hacks, accidental paths, or vague ownership.

Obvious:
It should be easy to see what owns reasoning, tools, memory, orchestration, governance, and persistence.

Safe:
Fail closed. No surprise mutation. No implicit approval. No hidden autonomy.

Traceable:
Every meaningful action should have run IDs, decisions, evidence, warnings, errors, and timestamps.

Beautiful:
Small strong boundaries. Boring names. Clear contracts. No clever sludge.
```

## Review Questions for Every Agent PR

Before merging an agent-related PR, answer:

```text
1. Does this move IronDev toward controlled L4?
2. What boundary does it strengthen?
3. What can the agent read?
4. What can the agent write?
5. What tools can it call?
6. What is logged?
7. What is replayable?
8. What fails closed?
9. What requires human approval?
10. Is any product truth coming from a harness, prompt, or hidden fallback?
```

## Anti-Patterns

Reject designs that depend on:

```text
"The agent decides."
"The prompt will handle it."
"Memory will know."
"We can add governance later."
"ReplayRunner output is good enough."
"It works in testing."
"The UI will make it clear."
```

These are not architecture.

## Immediate Roadmap

Current sequence:

```text
PR-9: supervisor failure package
PR-10: bounded recovery plan
PR-11: disposable workspace execution command
PR-12: build/test/repair loop inside disposable workspace
PR-13: promotion package
PR-14: human approval gate for real apply
```

Every step should either move toward controlled L4 or harden the path to controlled L4.

## Final Rule

Do not build a clever agent.

Build a trustworthy product spine.
