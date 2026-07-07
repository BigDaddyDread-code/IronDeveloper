# DOGFOOD-2 — Second Repo Runbook

**Status:** Entry criteria satisfied by the dirty-repo gate PR; cycle not yet run.
**Purpose:** walk a second, non-BookSeller repository through the full governed loop —
provisioning → ticket → run → gate → apply → report — using only the product surfaces.
This is the viability test: it measures whether the value of a governed run exceeds the
cost of its ceremony, on a repo the demo was never tuned for.

Killjoy line:

```text
BookSeller proves the loop exists. A second repo proves the loop generalizes.
Nothing about this cycle may reach into demo seeds, fixtures, or hidden local knowledge.
```

---

## 1. Entry Criteria (all must hold before the cycle starts)

```text
[x] Provisioning readiness endpoint real (PROJECT-3, PR #741)
[x] Root safety: drive/user/system roots and reparse ancestors refused (PR #741 review fix)
[x] Dirty-repo state is a real check: Dirty blocks with BlockedDirtyRepo and a named
    remedy; Unknown is named, never guessed Clean (this PR)
[ ] A second repository exists locally: small, buildable, testable, git-clean,
    outside the IronDev repository, with no IronDev-specific scaffolding
[ ] Local preflight green: API + SQL + model proof (deterministic mode acceptable;
    record which)
```

## 2. The Cycle (product surfaces only)

```text
1. Create project        POST /api/projects + PUT local-path (or the cockpit Projects UI)
2. Evaluate readiness    Library > Provisioning — expect honest blockers first
3. Answer the pointed    confirm build command, test command, proposed profile —
   questions             each via the wizard; re-evaluate until ReadyToRun
4. Shape a small ticket  one real improvement, 1-3 acceptance criteria, via Shape stage
5. Start governed run    through the gate; if readiness refuses, the refusal text is
                         itself a finding for this cycle
6. Walk the loop         build/test evidence → repair if triggered → critic review →
                         dispositions → approval ceremony → continuation → apply
7. Read the final report gaps named? boundary stated? receipts on disk?
```

## 3. Evidence to Capture (per step, no exceptions)

```text
timestamps · endpoint + response for each gate decision · every refusal reason code
encountered · ceremony minutes per human touch · repair/revision attempts · the final
report file · anything the operator had to know that the product did not tell them
```

That last item is the most valuable output of the cycle. Every instance is a UX bug:
file it as a finding with the screen and the missing next-safe-action.

## 4. Exit Criteria (maps to future-ux-product-spec §23)

```text
A second small repo was imported/provisioned through product surfaces only.
Build/test configuration was detected or asked for — never assumed.
A ticket was created, a governed run started, and the run halted at the human gate.
Critic/finding/approval requirements were visible and satisfiable in the cockpit.
Apply happened only through the backend-governed path.
The final report reconstructs the run without hiding gaps.
Ceremony cost recorded: human minutes per gate, per ticket.
```

## 5. Refusal Log Template

```text
| Step | Surface | Refusal/reason code | Was the remedy named? | UX finding filed? |
|---|---|---|---|---|
```

## 6. Boundary

```text
This runbook is procedure, not authority. Completing it approves nothing and releases
nothing. Its output is evidence: a receipts document under Docs/dogfood/ and a list of
UX findings that become the next slices.
```
