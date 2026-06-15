# PR148 - Agent Run Health Summary

## Purpose

PR148 adds a read-only Agent Run Health Summary report for Block O operational inspection.

The report summarizes existing safe governance trace evidence for an agent run or run selector. It is designed to help a human reviewer see whether visible evidence suggests a run is healthy, blocked, failed, incomplete, or needs review.

## Boundary

This is a read-only report.

It does not:

- restart, retry, rerun, resume, continue, or dispatch an agent
- invoke tools
- call models
- build prompts
- transition workflow state
- satisfy approval
- satisfy policy
- approve release
- create tickets
- promote memory
- activate retrieval
- apply source
- apply patches
- create governance events
- create approval decisions
- create policy decisions
- create tool requests
- create dogfood receipts
- expose raw payload JSON
- expose raw prompts
- expose raw completions
- expose raw tool output
- expose source content
- expose patch payloads
- expose hidden or private reasoning

## Evidence sources

The summary reads existing governance trace explorer output only.

The trace explorer already exposes safe trace fields and does not return raw payload JSON. PR148 keeps that boundary and projects those safe fields into health signals, missing-evidence markers, trace references, and operational recommendations.

## API routes

- `GET /api/v1/agents/runs/health-summary`
- `GET /api/v1/agents/runs/{agentRunId}/health-summary`

Both routes are read-only and require authentication.

## Health categories

- `ObservedHealthy`
- `ObservedWarning`
- `ObservedBlocked`
- `ObservedFailed`
- `EvidenceIncomplete`
- `NeedsHumanReview`

These categories are operational labels only. They are not approval, policy satisfaction, release readiness, execution permission, or workflow state transitions.

## Review line

PR148 checks the agent gauges. It does not grab the controls.
