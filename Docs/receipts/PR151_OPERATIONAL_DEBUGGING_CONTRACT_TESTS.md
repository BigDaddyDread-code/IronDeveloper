# PR151 - Operational Debugging Contract Tests

PR151 adds cross-surface operational debugging contract tests for Block O.

Operational debugging surfaces are read-only.

Observation is not authority.

Diagnosis is not repair.

Health is not release readiness.

Correlation is not approval.

Recommendation is not execution.

Retention rule is not cleanup execution.

Traceability is not mutation permission.

## Surfaces covered

- Governance Trace Explorer API
- Failed Workflow Diagnosis Report
- Approval/Gate/Dogfood Correlation Report
- Agent Run Health Summary
- Backend Operational Health Checks
- Governance Data Retention and Cleanup Rules

## What this does not do

This PR does not add API endpoints, CLI commands, SQL migrations, stores, runners, executors, hosted services, background workers, schedulers, cleanup jobs, repair paths, restart paths, approval paths, policy satisfaction paths, workflow transition paths, source apply paths, model calls, tool invocation, agent dispatch, memory promotion, retrieval activation, or raw/private payload exposure.

## Review line

PR151 locks the debugger in observation mode. It does not hand it the wrench.
