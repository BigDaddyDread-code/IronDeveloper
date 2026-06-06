# IronDev CLI Contract (PR-2)

This document defines the contract for IronDev’s product CLI and the boundary against `ReplayRunner`.

## 1) Purpose and scope

The CLI must be a stable, machine-consumable interface for product workflows.

- Product CLI commands are expected to operate on real system state and return real data.
- ReplayRunner is explicitly an internal dogfood/test harness and may not host product command semantics.
- This contract applies to public CLI commands invoked as `irondev ...` from `IronDev.Cli`.

## 2) Contract terms

1. Product CLI commands must return real system data.
   - Command handlers must query live state (e.g., build/run/ticket/scenario/chat sources) and return results from that state.
2. No fake success output.
   - A response must not report success when no underlying work actually completed.
3. `--json` flag is required.
   - Every user-facing product command must support a machine-readable JSON output mode.
4. Stable JSON envelope.
   - JSON responses must follow:

      ```json
      {
        "status": "succeeded | failed | blocked",
        "command": "ticket build",
        "traceId": "optional-trace-id",
        "summary": "Human-readable summary.",
        "data": {},
        "errors": [],
        "warnings": []
      }
      ```

   - `command` is the canonical command string used for invocation.
   - `status` reflects outcome and must align with process exit code.
5. Non-zero exit for failed/blocked/error states.
   - `succeeded` => `0`
   - `failed`/`blocked`/error states => non-zero
6. Mutating commands must be explicit.
   - Create/update/delete/approval/report-triggering operations must be unambiguous and visible by command naming and help text.
7. Governed commands include traceability fields where applicable.
   - Any governed action must return trace ids and governance evidence linkage where available.
8. ReplayRunner cannot host product CLI commands.
   - Dispatching of product command families in ReplayRunner is disallowed.
9. ReplayRunner is internal only.
   - Commands under ReplayRunner are for harness/test/legacy replay paths and are not customer product contracts.
10. Legacy/delete-later commands are tracked.
   - Known non-product commands are explicitly tagged and have a cleanup/removal plan.

## 3) Current command ownership map

### Product CLI (real, user-facing)
- `ticket` family
- `runs` family
- `scenario` family
- `chat-probe` commands
- `exercise` command

### ReplayRunner (internal/test/legacy)
- Harness and replay-only command handling
- Compatibility aliases and one-off test entrypoints

## 4) Enforcement rule for this contract

This contract is currently enforced by:
- `CliCommandInventoryTests`: `CliCommandInventory_ProductCliCommandsMustNotBeHandledByReplayRunner`

That test must remain and expand with future command introductions.

## 5) Change process

- Any new CLI command intended as product surface must:
  - be added under IronDev.Cli, not ReplayRunner,
  - support `--json`,
  - emit the envelope above,
  - include stable governance fields where applicable.
- Any command added as legacy/test-only:
  - must be categorized in the boundary inventory,
  - must remain internal-only,
  - must be linked to cleanup tracking.

## 6) Out of scope

- Moving command implementations between hosts (next PRs).
- Full replacement of ReplayRunner with product CLI handlers.
- Introducing additional command families outside the product contract.
