# API/CLI Contract Matrix

## Purpose

This matrix records the Block F API/CLI contract boundary. It is a testable inventory, not an exposure request.

## Matrix

| CLI command | API endpoint | Durable | Approval | Execution | Source apply | Memory promotion | Required boundary text |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `api ping` | `GET /health` | false | false | false | false | false | API ping is health inspection only. |
| `agent-runs list` | `GET /api/v1/agent-runs` | true | false | false | false | false | Audit is evidence, not approval. |
| `agent-runs get` | `GET /api/v1/agent-runs/{agentRunId}` | true | false | false | false | false | CLI inspection is not execution permission. |
| `agent-runs audit` | `GET /api/v1/agent-runs/{agentRunId}/audit` | true | false | false | false | false | Evidence is not permission. |
| `critic review create` | `POST /api/v1/manual-critic/reviews` | true | false | false | false | false | Critic review is not approval. |
| `critic review get` | `GET /api/v1/manual-critic/reviews/{agentRunId}` | true | false | false | false | false | Critic is not governance. |
| `memory-improvements create` | `POST /api/v1/manual-memory-improvements` | true | false | false | false | false | Memory proposal is not promotion. |
| `memory-improvements get` | `GET /api/v1/manual-memory-improvements/{agentRunId}` | true | false | false | false | false | Candidate is not memory. |
| `tool-requests create` | `POST /api/v1/tool-requests` | false | false | false | false | false | Tool request is request form, not execution permission. |
| `tool-requests get` | `GET /api/v1/tool-requests/{toolRequestId}` | false | false | false | false | false | Tool execution is separate. |
| `tool-gate evaluate` | `POST /api/v1/tool-gates/evaluate` | false | false | false | false | false | Gate evaluation is not execution. |
| `dogfood-loops create` | `POST /api/v1/dogfood-loops` | false | false | false | false | false | Dogfood receipt is evidence, not release approval. |
| `dogfood-loops get` | `GET /api/v1/dogfood-loops/{dogfoodLoopId}` | false | false | false | false | false | Dogfood loop is not autonomous workflow. |

## Frozen Boundary Language

The API/CLI contract suite treats these phrases as boundary anchors:

- Audit is not approval.
- Evidence is not permission.
- CLI inspection is not execution permission.
- Critic is not governance.
- Critic review is not approval.
- Memory proposal is not promotion.
- Memory safe is not approval.
- Candidate is not memory.
- Tool request is request form, not execution permission.
- Request approval is separate.
- Tool execution is separate.
- Gate evaluation is not execution.
- Dogfood receipt is not release approval.
- Dogfood loop is not autonomous workflow.
- Human review remains required for source apply and memory promotion.

- Retrieval match is not memory candidate.

## Freeze Notes

The matrix intentionally records temporary non-durable API-local inspection caches for tool requests, tool gate evaluations, and dogfood loops.

Those caches are not SQL source of truth, not approval, not execution permission, not audit authority, not source apply evidence, and not memory promotion evidence.


