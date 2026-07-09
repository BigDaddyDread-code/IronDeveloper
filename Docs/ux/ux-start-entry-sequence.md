# UX-START — The Session Front Door and Project Cockpit

**Status:** Committed with the UX-START-0/1 implementation.
**Origin:** DOGFOOD-2 cycle 002 findings — the shell rendered an empty board with a mute
"API error" chip when the API was unreachable, showed no sign-in, and had no entry
sequence; provisioning-ready vs run-ready confusion; no project boundary at entry.

**Core rule:** the project is the authority boundary. No project, no work item;
no readiness, no run — and no lock the backend didn't compute.

## The entry sequence (FlowShell, in order)

```text
1. API unreachable/error   -> Preflight: the URL that failed, the likely cause, the
                              startup command, [Retry], [Connection settings].
                              Never a mute chip over an empty board.
2. 401 / no token          -> Sign-in route (existing).
3. No tenant               -> Sign-in route (tenant selection lives there).
4. No project selected     -> Project chooser. No work-item flow exists out here.
5. Project selected        -> The Board, wearing the project cockpit header.
Settings stays reachable from every gate — the escape hatch to fix connection config.
```

## Project chooser

- Cards from `GET /api/projects`; each card lazily fetches
  `GET /api/projects/{id}/provisioning/readiness` and renders the backend's own badge:
  `Ready to run` / `Setup incomplete · N blocker(s)` — **truth or a spinner, never
  frontend inference**.
- Known cost: readiness runs detection when anything is unconfirmed. Acceptable at
  local scale; a stored-truth-only summary endpoint is the named follow-up before
  project lists grow.
- `+ Create new project`: name + local repo path only. A created project is a shell —
  it lands on the **readiness screen** (Library > Provisioning), never the Board.
  A repo path is not safe until checked; a detected command is not confirmed truth.

## Project cockpit (the Board's header — not a fourth surface)

- **Readiness badge + blocked-check rows** from live `provisioning/readiness` — the
  same truth the run start enforces (one readiness truth, F-E). Every blocked row
  shows the backend's evidence and remedy verbatim.
- **One primary action**, priority-ordered from backend facts:
  gate-waiting item exists → `Review waiting item`;
  readiness blocked → `Complete project setup`;
  else → `Start new work item`.
  The human's queue outranks new work. The cockpit never makes the user hunt.
- **Needs attention**: review-stage items with their status named and a click-through.
- Shape/draft stays available on unready projects, with the boundary stated:
  *you can shape work and draft tickets now; governed runs unlock when backend
  readiness is satisfied.*

## Boundaries

```text
Selecting a project changes context, not authority.
A ready badge is evidence, not approval.
Every fix button is a request the backend may refuse.
Preflight is read-only reporting; a reachable API grants nothing.
```

## Manual test script (against the live local stack)

1. Stop the API. Load the UI → the **preflight panel** names the API URL and the
   startup command; Retry re-checks. No empty board, no mute chip.
2. Start the API. Retry → **sign-in** appears (bob@irondev.local / documented local
   password, tenant 1).
3. Signed in with no project selected → **chooser** lists projects with live badges
   (spinner → backend verdict per card).
4. Select BookSeller → cockpit: `Ready to run` badge, `search-by-author` under
   **Needs your attention** with its status, primary button = **Review waiting item**.
5. Select ParcelTracker (or any unready project) → badge counts blockers, blocked
   checks listed with remedies, primary button = **Complete project setup** → lands
   in provisioning.
6. Create a new project (name + path) → lands on the **readiness screen**, not the
   Board; the wizard's pointed questions drive to ReadyToRun.
7. Confirm "New work item" appears **only** inside a selected project — the chooser
   offers no work-item affordance.
