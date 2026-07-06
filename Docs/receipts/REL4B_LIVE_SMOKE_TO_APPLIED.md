# REL-4b Receipt - Live Smoke to Applied (One Operator Command)

## Purpose

Close the REL-4 wall. Live mode was limited to a bounded ticket draft; the live
governed run existed only as an opt-in test invocation. REL-4b makes the
self-repairing live hero walk one operator command:

```powershell
$env:IRONDEV_ALPHA_SMOKE_LIVE_MODEL = "1"
$env:IRONDEV_ALPHA_SMOKE_LIVE_PROVIDER = "OpenAI"
$env:IRONDEV_ALPHA_SMOKE_LIVE_MODEL_NAME = "<model>"
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket bulk-discount `
  -ModelMode Live -RunUntil Applied -RequireExistingAcceptedApproval
```

One command: real builder/tester/critic on the configured live model, bounded
repair armed (budget 1), live critic findings dispositioned inside the proof,
hash-bound approval recorded through the governed API, controlled apply,
receipt verification — the same field contract REL-3 verifies.

## Executed (2026-07-07, OpenAI/gpt-4o)

```text
Status: Passed — 22/22 stages
SkeletonRunStart -> CriticPackageFetch -> CriticReviewRecorded -> GateStateVerified
-> AcceptedApprovalPersisted -> ContinuationUnblocked -> Applied
SelfRepairCheck: LiveFirstAttemptClean (budget armed, unused — consistent with
gpt-4o's 4/4 clean record since the LinkedFilePaths context fix)
```

The self-repair path itself was captured live in HERO-3 (gpt-4o-mini, run
`4f2ebde6`, receipt HERO3_SELF_REPAIRING_LIVE_WALK); the `SelfRepairCheck` stage
reports whichever story a given run earns.

## Command contract (explicit, never silent)

```text
-ModelMode Live supports exactly: -RunUntil TicketDraft (REL-4 bounded draft)
                                  -RunUntil Applied (REL-4b hero walk)
Applied demands -RequireExistingAcceptedApproval (the smoke never creates
  approval by default; the proof owns its governed API approval request)
Applied demands -Ticket bulk-discount (LiveModelTicketUnsupported otherwise —
  no silent ticket swap under the operator)
Anything else: LiveModelRunUntilUnsupported. Never a deterministic fallback.
New stage: SelfRepairCheck — LiveFirstAttemptClean | LiveSelfRepairOccurred
  (with repair attempts, initial vs gate proposal ids)
```

## Boundaries

- A live smoke run is diagnostic evidence, not model reliability, approval, or
  release readiness.
- Self-repair is proposal-shaped work, never authority — the gate is unchanged
  and the stage message says so.
- Live opt-in envs required; secrets stay in the environment; blocked runs name
  their reason.

## Files Changed

- `Scripts/smoke/alpha-smoke.ps1`
- `IronDev.IntegrationTests/Smoke/AlphaSmokeScriptContractTests.cs`
- `Docs/alpha-smoke/README.md`, `Docs/alpha-smoke/reason-codes.md`
- `Docs/receipts/REL4B_LIVE_SMOKE_TO_APPLIED.md`

## Validation

- Live end-to-end script run: Passed, 22/22 stages (above).
- `AlphaSmokeScriptContractTests` 12/12, including the new wiring contract that
  executes the script for both refusal modes (missing approval mode, wrong ticket).
- Existing live TicketDraft path and deterministic paths unchanged.

## Review Line

The inventory's live-model blocker asked for "live model path reaches gate safely
or explicit deterministic-only scope." This closes it beyond the ask: the live
path reaches Applied, one command, self-repair reported honestly.

## Killjoy Line

One green operator command is repeatability for the author. The next developer
running it without help is the proof that matters — that is DOGFOOD's job.
