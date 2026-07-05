# REL-4 - Live Model Single-Ticket Smoke

## Purpose

Prove that an explicitly configured live model can produce one bounded BookSeller ticket draft artifact.

REL-4 is an external-dependency smoke. It is not part of normal CI, and it never falls back to deterministic mode.

## Command

```powershell
$env:IRONDEV_ALPHA_SMOKE_LIVE_MODEL = "1"
$env:IRONDEV_ALPHA_SMOKE_LIVE_PROVIDER = "OpenAI"
$env:IRONDEV_ALPHA_SMOKE_LIVE_MODEL_NAME = "<model>"
$env:OPENAI_API_KEY = "<set outside repo>"
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Live -RunUntil TicketDraft
```

Local/OpenAI-compatible providers may use:

```powershell
$env:IRONDEV_ALPHA_SMOKE_LIVE_PROVIDER = "LocalOpenAI"
$env:IRONDEV_ALPHA_SMOKE_LIVE_BASE_URL = "http://localhost:1234/v1"
```

## Boundary

The live model output is draft evidence only.

REL-4 does not persist a ticket, start a run, request critic review, create or consume approval, continue workflow, apply source, commit, push, merge, release, or deploy.

## Receipt

The smoke writes `run-receipt.json` under the safe alpha-smoke output directory. The receipt includes provider/model metadata, a response hash, bounded title, acceptance-criteria count, proposed files, and proof/limitation statements.

The receipt must not include secrets, credentials, raw logs, or private reasoning.

## Failure Modes

- `LiveModelOptInMissing`: explicit live-model opt-in is missing.
- `LiveModelNotConfigured`: provider/model/key/base URL configuration is incomplete.
- `LiveModelRunUntilUnsupported`: live model mode was requested for anything beyond `TicketDraft`.

## Review Line

Live model output is useful evidence. It is not permission to persist, run, approve, apply, or release.
