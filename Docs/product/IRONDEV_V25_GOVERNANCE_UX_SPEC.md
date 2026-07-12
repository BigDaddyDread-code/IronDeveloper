IronDev v2.5 Governance UX Specification

Suggested file: Docs/product/IRONDEV_V25_GOVERNANCE_UX_SPEC.md
Status: Product and implementation contract
Surface: /projects/{projectId}/library/governance

1. Purpose

Replace the current Governance surface with a clear project control centre that answers four questions:

What protections currently apply?
What requires human attention?
What is degraded, exceptional, or unsafe?
Where does the authoritative decision or evidence live?

The current page is a compatibility host for 17 separate read-only viewers exposed as equal navigation choices. Those viewers mix operations, workflow runs, packages, approvals, policy results, apply evidence, rollback, tool gates, memory proposals, release evidence, and technical traces.

That structure reflects backend record types. It does not reflect the questions a project member is trying to answer.

2. Product boundary
Settings
Configures policy and permitted behaviour.

Governance
Explains effective controls, attention, exceptions, and risk.

Work Item
Owns consequential decisions and governed actions.

Audit
Records what happened, who acted, and the associated evidence.

Governance may navigate to the authoritative surface.

Governance must not directly:

approve;
disposition findings;
continue a workflow;
apply source;
execute rollback;
publish policy;
change agent configuration;
invoke a tool.

One consequential action has one authoritative product surface.

3. User outcome

Within ten seconds of opening Governance, an ordinary project member should understand:

whether required controls are active;
whether anything needs their attention;
whether any control is degraded;
whether a recovery or exception state exists;
where to go next.

The user must not need:

a Work Item ID;
a run ID;
a trace ID;
a package hash;
a correlation ID;
knowledge of internal governance record names.
4. Canonical routes
/projects/{projectId}/library/governance
/projects/{projectId}/library/governance/controls
/projects/{projectId}/library/governance/exceptions
/projects/{projectId}/library/governance/decisions
/projects/{projectId}/library/governance/technical

The root Governance route renders the overview.

Existing routes remain available as compatibility deep links. They do not remain the normal navigation model.

5. Governance overview
Header
Project Governance

Attention required · 2 items need a decision

Human approval, exact-package binding, controlled apply,
and durable execution evidence are active for this project.

[Review next item]   View audit   Open settings

Display:

project name;
overall status;
concise status explanation;
attention count;
generated time;
one backend-selected primary action;
links to Audit and Settings.

Do not place technical warnings, hashes, trace IDs, or policy prose permanently in the header.

6. Overall status vocabulary
Controls active

Required controls are available and no material degraded condition is known.

This does not claim the project or source code is defect-free.

Attention required

A human decision is waiting, while the governance system remains operational.

Examples:

findings need disposition;
approval is required;
continuation is required;
apply review is required.
Degraded

A required control, evidence source, or recovery state is incomplete or unavailable.

Examples:

missing execution evidence;
interrupted apply;
partial mutation risk;
unavailable policy evaluation;
unavailable required model provider;
receipt gap.
Unavailable

The overview could not be loaded.

The client must not reconstruct governance truth from unrelated records.

Forbidden overall labels:

Safe
Secure
Fully compliant
Approved

unless a specific backend contract supports that exact claim.

7. Primary action

The backend selects the highest-priority attention item.

Priority order:

Partial mutation or interrupted apply
Invalid or stale approval/package binding
Findings awaiting disposition
Approval required
Continuation decision required
Other blocked governance state
No action

Examples:

Review interrupted apply
Disposition 3 findings
Review approval package
Resolve missing execution evidence

The action opens the relevant Work Item.

It does not perform the decision from Governance.

8. Needs attention

Show only items requiring a human decision or recovery response.

Example:

Needs attention                                      3

WI-104  Partial apply requires review
        Source mutation may have occurred before failure.
        Waiting on: Project Owner
        Next: Inspect receipts and choose a recovery action.
        [Open WI-104]

WI-101  Three critic findings need disposition
        Review package 4 contains unresolved findings.
        Waiting on: Reviewer
        [Open WI-101]

WI-097  Approval required
        Build and test evidence is ready for review.
        Waiting on: Eligible approver
        [Open WI-097]

Each item contains:

Work Item reference and title;
attention kind;
severity;
plain-language explanation;
waiting-on person or role;
recorded time or age;
next safe action;
authoritative target route.

Empty state:

No governance action is waiting. Required controls are active and no recovery decision is currently recorded.

9. Effective controls

Show the project’s effective control floor in plain language.

Human authority
human approval requirement;
self-approval restriction;
separation of duties;
eligible reviewer and approver rules;
solo-user exception state.
Source mutation
active-repository mutation restriction;
controlled apply mode;
exact-package binding;
dry-run requirement;
apply recovery policy.
Agents and tools
agent authority boundary;
Critic independence;
tool invocation policy;
tool scope enforcement;
effective agent configuration state.
Evidence and memory
execution-proof requirement;
receipt preservation;
audit recording;
memory proposal and promotion separation;
document and source provenance.

Example:

Human approval          Required
Source                  Tenant policy

Self-approval           Prohibited
Source                  IronDev invariant

Source mutation         Controlled apply only
Source                  Project configuration

Critic independence     Enforced
Source                  IronDev invariant

Execution proof         Durable events required
Source                  IronDev invariant

Each control exposes:

effective value;
short explanation;
source;
configurable or locked state;
detail route;
remedy route where degraded.

Allowed source labels:

IronDev invariant
Tenant policy
Project policy
Project configuration
Runtime evidence
Not configured
Unavailable
10. Exceptions and degraded states

Show only material deviations from normal operation.

Example:

Exceptions and degraded states                       2

Critical · Interrupted apply
WI-104 may have partial source mutation.
Recorded 14 minutes ago.
[Inspect recovery]

Warning · Critic provider unavailable
New Critic reviews cannot begin.
Connection: Local review model
[Open AI connection settings]

Supported categories include:

policy exception;
solo-user exception;
disabled control;
missing execution evidence;
missing receipt;
partial mutation risk;
interrupted run;
interrupted apply;
stale approval;
superseded review package;
provider unavailable;
tool unavailable;
project readiness degraded;
audit persistence issue;
configuration conflict.

Severity vocabulary:

Critical
Action required
Warning
Information

Empty state:

No active governance exceptions or degraded controls are recorded.

11. Recent consequential decisions

Show the latest five to ten material decisions.

Example:

Recent decisions

Brenda O'Shea approved review package 4
WI-104 · 10:42 AM

Robert O'Shea requested continuation
WI-103 · Yesterday

Controlled apply was refused
WI-101 · Missing exact-package evidence · Monday

Include:

finding dispositions;
approvals accepted or refused;
policy satisfaction outcomes;
continuation accepted or refused;
apply started, completed, failed, or refused;
recovery decisions;
policy exceptions;
consequential configuration changes;
membership changes affecting authority.

The existing Audit ledger owns full actor, Work Item, event, outcome, and evidence history. Governance displays a concise subset and links to View full audit.

12. Technical evidence

Technical evidence remains accessible through progressive disclosure.

Default action:

View technical evidence

It may contain:

correlation ID;
causation ID;
operation ID;
run ID;
trace ID;
package hash;
policy reference;
receipt references;
source component;
safe technical summary.

The existing Governance Timeline begins with technical filters for project reference, run, workflow step, correlation, causation, subject, event kind, source component, date range, and count. That belongs under Technical Evidence or Audit, not on the primary Governance page.

Technical evidence groups:

Traces
Runs and operations
Packages and artifacts
Approvals and policy
Apply and recovery
Tools and memory
13. Legacy viewer disposition
Operation status → Board or Work Item
Action requests → Work Item current action
Workflow runs and steps → Work Item execution evidence
Patch packages and artifacts → Work Item evidence
Approval packages → Work Item review
Accepted approvals → Governance decision detail and Audit
Policy satisfaction → Governance effective controls
Continuation evidence → Work Item review or outcome
Source apply review and receipts → Work Item apply
Rollback evidence → Work Item recovery
Tool gate decisions → Governance controls or Work Item evidence
Memory proposals → Library memory area or Governance control detail
Release readiness evidence → future Release surface
Governance timeline → Audit technical traces
Dogfood receipts → Advanced developer evidence

Compatibility URLs remain functional.

The 17-chip strip is removed from normal navigation.

14. Backend contract
GET /api/projects/{projectId}/governance/overview

The response owns:

ProjectGovernanceOverview
- ProjectId
- ProjectName
- OverallStatus
- StatusSummary
- GeneratedUtc
- Version
- PrimaryAction
- AttentionItems
- Controls
- Exceptions
- RecentDecisions
- Navigation
- SectionIssues
- Boundary

Boundary statement:

Governance reports effective controls, pending decisions, exceptions, and evidence. It grants no approval, continues no workflow, and applies no source.

The backend owns:

overall status;
action priority;
effective control values;
source labels;
exception classification;
severity;
recent-decision selection;
safe navigation targets.

The browser must not derive posture by inspecting:

ticket status text;
run summaries;
trace messages;
receipt names;
package names;
button presence;
CSS state.
15. Failure rules

When a required control cannot be evaluated:

Control: Unavailable
Overall status: Degraded

Missing evidence is rendered as missing evidence, never success.

When the whole endpoint fails:

Governance unavailable

IronDev could not load the project governance overview.
No control state has been inferred.

[Retry]

Partial section failure:

Recent decisions unavailable

Effective controls and attention state were loaded successfully.
16. Responsive layout

Desktop:

Header

Needs attention        Effective controls
Exceptions             Recent decisions

Narrow viewport:

Header
Needs attention
Exceptions
Effective controls
Recent decisions

Mobile priority:

Overall status
Primary action
Needs attention
Exceptions
Effective controls
Recent decisions

Technical references must wrap and must not create horizontal scrolling.

17. Accessibility

Required:

status communicated through text, not colour alone;
clear section headings;
primary action first after the header;
aria-expanded for technical details;
live announcements for refresh results;
descriptive links such as Open WI-104;
keyboard-operable controls;
minimum target sizes;
no chip-only navigation;
no horizontal overflow at 390px.
18. Permissions

Any project member may view:

overall posture;
non-sensitive controls;
permitted attention items;
non-sensitive exceptions;
recent decisions;
redacted evidence links.

Technical evidence may require elevated permission.

Redaction occurs on the backend. The UI must not receive sensitive content and merely hide it.

19. Required tests
Projection tests
healthy controls produce Controls active;
waiting decision produces Attention required;
missing required evidence produces Degraded;
interrupted apply becomes highest priority;
partial mutation produces Critical exception;
self-approval source is correct;
solo exception is explicit;
unavailable policy is not treated as active;
cross-project and cross-tenant records are excluded;
removed-user attribution remains visible;
primary action targets the authoritative Work Item.
UX tests
controls-active state;
attention-required state;
degraded state;
no-attention state;
partial-section failure;
full endpoint failure and retry;
Work Item navigation;
Settings navigation;
Audit navigation;
technical evidence expansion;
direct-route refresh;
back and forward navigation;
keyboard operation;
390px viewport;
no duplicate authority actions.
Boundary tests

Governance frontend code must not execute:

Approve
ContinueAsync
ApplyAsync
RollbackAsync
TransitionAsync
SatisfyPolicy
AcceptedApproval

References and explanatory text are permitted. Mutation ownership is not.

20. Delivery slices
GOV-UX-0 — governance information architecture

Classify every current viewer, define its canonical product home, preserve compatibility routes, and remove the flat-viewer model from the target design.

GOV-READ-1 — project governance overview

Build the backend read model, projection service, attention priority, effective-control source resolution, exception classification, recent decisions, generated contract, and tests.

GOV-UX-1 — governance overview

Implement the header, posture, attention, controls, exceptions, recent decisions, responsive states, and authoritative navigation.

GOV-DETAIL-1 — governance drill-down

Add control details, exception details, source explanations, remedy navigation, redacted technical evidence, and permission handling.

GOV-CLEAN-1 — legacy viewer containment

Remove the 17-viewer strip, preserve direct links, group technical viewers, route trace investigation toward Audit, and move Work Item-owned evidence to the Work Item.

21. Acceptance criteria

The specification is complete when:

Governance opens as a project overview rather than a viewer catalogue.
A user understands protections, attention, abnormal state, and next destination without knowing an internal ID.
Overall posture is backend-owned.
Every effective control identifies its source.
Attention links to Work Item.
Policy changes link to Settings.
History links to Audit.
Technical evidence is progressively disclosed.
Existing deep links remain functional.
Missing evidence never appears successful.
Endpoint failure causes no client inference.
Governance owns no approval, continuation, apply, rollback, or policy mutation.
Desktop and narrow viewport journeys pass.
A normal user can explain the project governance posture without reading architecture documentation.

Review line: Governance tells the user what protects the project, what needs them, what is abnormal, and where the real decision belongs.

Killjoy line: A wall of governance records proves that records exist. It does not prove that a human can govern the project.
