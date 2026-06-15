# PR147 - Approval/Gate/Dogfood Correlation Report

PR147 adds a read-only Approval/Gate/Dogfood Correlation Report surface for Block O.

The report correlates existing governance trace evidence around approval decisions, tool gate decisions, and dogfood receipts. It is an inspection receipt only.

## Boundary

The Approval/Gate/Dogfood Correlation Report is read-only.

It does not:

- approve or reject anything
- satisfy policy
- open or close a tool gate
- invoke tools
- mark dogfood passed
- approve release
- transition workflow state
- dispatch agents
- call models or build prompts
- create tickets
- promote memory
- activate retrieval
- apply source or patches
- create governance events
- create approval decisions
- create policy decisions
- create dogfood receipts
- expose raw payload JSON
- expose prompts, completions, tool output, source content, patch payloads, or hidden/private reasoning

Correlation is not approval. Correlation is not policy satisfaction. Dogfood receipt evidence is not release approval. Tool gate evidence is not tool execution. A conflict signal is not a verdict. A recommendation is not execution.

## What changed

- Added Core report models and validator.
- Added `IApprovalGateDogfoodCorrelationReportService`.
- Added a read-only service over `IGovernanceTraceExplorerService`.
- Added `GET /api/v1/governance/correlation-reports/approval-gate-dogfood`.
- Added focused API, boundary, and static-boundary tests.

## Review line

PR147 connects the receipts. It does not sign them, reopen the gate, or ship the release.
