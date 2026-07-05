# REL-5 - Chat to Confirmed Ticket to Governed Run

## Purpose

Prove the SQL/API path from a chat turn to a confirmed ticket and then into the existing governed skeleton run.

REL-5 covers:

- authenticated chat session persistence
- authenticated chat message persistence
- formalization response with ticket creation available
- draft ticket creation from the chat turn
- explicit draft confirmation into a persisted ticket
- server-verified chat provenance persisted on the ticket
- skeleton run started from the confirmed ticket
- run halted at `PausedForApproval`
- run report reconstructs the critic package and approval halt
- SQL contains chat, ticket, run, event, and provenance evidence

## Command

```powershell
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Deterministic -RunUntil Gate -StartFromChat
```

The command uses the SQL/API in-process test host. It does not use the live model path.

## Boundary

Chat formalization is not approval.

Draft confirmation persists a ticket only. It uses a backend-owned draft status and verifies chat session/message ownership before writing source references. It does not start a run by itself, approve the run, continue workflow, apply source, commit, push, merge, release, or deploy.

The governed run still halts at the existing human approval gate.

## Evidence

REL-5 writes `run-receipt.json` with the common alpha-smoke fields plus:

- `chatSessionId`
- `chatMessageId`
- `draftConfirmed`
- `chatTurnPersisted`
- `sourceMessageLinked`

## CI

`Scripts/ci/run-full-sql-integration-ci.ps1` includes the REL-5 API smoke lane:

```text
AlphaSmokeApiPersistenceTests.Rel5_ChatConfirmedTicket_StartsGovernedRun_ThroughSqlBackedApi
```

## Review Line

A confirmed ticket can start the governed path. It cannot skip the gate.
