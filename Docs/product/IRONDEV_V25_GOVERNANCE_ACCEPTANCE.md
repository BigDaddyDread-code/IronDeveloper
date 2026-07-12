# IronDev v2.5 Governance Acceptance

**Contract:** `IRONDEV_V25_GOVERNANCE_UX_SPEC.md`

**Baseline:** `f8987b07` (`main` after GOV-CLEAN-1)

**Qualified:** 2026-07-12, LocalTest

**Result:** PASS for the v2.5 Governance UX contract

## Delivered slices

| Slice | Pull request | Result |
|---|---:|---|
| GOV-UX-0 information architecture | #810 | Merged |
| GOV-READ-1 backend overview contract | #811 | Merged |
| GOV-UX-1 project control centre | #812 | Merged |
| GOV-DETAIL-1 canonical drilldowns | #813 | Merged |
| GOV-CLEAN-1 legacy containment | #814 | Merged |

## Acceptance evidence

| Contract outcome | Evidence |
|---|---|
| Governance opens as a project overview | Canonical project route renders `flow.governance.overview`; legacy host is absent. |
| Posture and next action are backend-owned | `GET /api/projects/{projectId}/governance/overview` supplies status, summary, primary action, target route, controls, exceptions, decisions, navigation, and section issues. |
| Effective controls identify their source | Every control renders its backend source; projector tests cover invariant and tenant-policy labels. |
| Attention returns to the authoritative Work Item | Attention and primary-action target routes are project-scoped Work Item routes. Cross-project evidence is rejected by the projector. |
| Policy and history have canonical homes | Governance links to project Settings and Audit using backend navigation values. |
| Technical evidence is progressively disclosed | The canonical Technical Evidence route groups compatibility viewers by investigative purpose. |
| Existing deep links remain functional | Legacy timeline and evidence viewer smoke tests remain green; the 17-viewer strip is removed. |
| Missing or partial evidence is not success | Missing execution proof becomes an exception; section read failures force `Degraded` and render an explicit issue. |
| Endpoint failure causes no client inference | The unavailable state offers Retry and performs no action. |
| Governance owns no consequential mutation | The overview and compatibility host are covered by the UI authority firewall; browser tests find no approve, continue, apply-source, or rollback controls. |
| Desktop and narrow viewport journeys pass | Browser coverage includes keyboard focus and a 390px no-overflow assertion; live visual inspection passed. |

## Validation

- `dotnet build IronDev.Api/IronDev.Api.csproj --no-restore`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore --filter FullyQualifiedName~ProjectGovernanceOverviewProjectorTests`
- `npm run build`
- Governance information architecture, overview, drilldown, legacy, and UI authority Playwright suites
- Supported LocalTest startup and live flow-shell smoke
- Deterministic OpenAPI and TypeScript generation, repeated with identical SHA-256 output

## Manual product result

**Mode:** BrowserOnly

**Environment:** LocalTest

**Database reset:** No; the stable seeded fixtures were already present

**Project:** IronDev Local Test Project

**Login:** `bob@irondev.local`

Journey exercised through the visible UI:

1. Signed in and selected the seeded project.
2. Opened Library, then Governance.
3. Confirmed `Attention required`, the backend-selected controlled-apply review, WI-3002, six sourced controls, and the missing-execution-evidence exception.
4. Opened Technical Evidence and confirmed grouped read-only viewer links.
5. Opened the preserved Governance Timeline deep link and returned through Back to Governance.
6. Rechecked the overview at 390px with no horizontal overflow or action overlap.

**Manual result:** PASS

## Known limits

- Recent decisions are a concise subset of Work Item evidence; Audit remains the complete history.
- Advanced viewer URLs remain compatibility routes while their evidence contracts are gradually absorbed by Work Item, Audit, and future Release surfaces.
- Static UI authority scanning is regression protection, not runtime authority. Backend action boundaries remain authoritative.

## Review line

Governance now tells a project member what protects the project, what needs attention, what is abnormal, and where the real decision belongs without requiring internal record identifiers.
