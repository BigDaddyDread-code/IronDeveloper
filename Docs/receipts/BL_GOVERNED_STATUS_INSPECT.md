# BL - Governed Status Inspect

## Purpose

This slice adds a read-only inspect surface for canonical GovernedOperationStatus.

It can read and display:

- operation kind
- operation state
- subject
- blocked reasons
- missing evidence
- next safe actions
- forbidden actions
- evidence refs
- receipt refs
- validation issues
- red flags
- amber flags

It validates status through GovernedOperationStatusValidator.

## Boundary

It does not approve.
It does not satisfy policy.
It does not execute.
It does not mutate source.
It does not commit.
It does not push.
It does not create PRs.
It does not merge.
It does not release.
It does not deploy.
It does not promote memory.
It does not continue workflow.
It does not execute rollback.
It does not call provider gateways.
It does not create new authority records.

NextSafeActions are displayed as guidance only.

EvidenceRefs and ReceiptRefs are displayed as references only.

Inspect output is not authority.
Inspect output is not evidence.
Inspect output is not a receipt.

## CLI

The file-based inspect command is:

```text
irondev operation-status inspect --status <operation-status.json>
```

The command reads an existing canonical status JSON file, inspects it, prints a human-readable summary, and exits non-zero for invalid, unreadable, malformed, or missing status input.

The command does not execute next safe actions.
The command does not update operation state.
The command does not write governance events.

## Review Line

Inspecting status explains the current governed state. It does not change it.

## Killjoy

Inspect can read the sign on the locked door. It cannot open the door.
