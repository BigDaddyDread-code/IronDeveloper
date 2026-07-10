# IronDev Product and UX Specification

**Version:** 2.0  
**Date:** 10 July 2026  
**Product:** IronDev desktop client (Tauri and React)  
**Status:** Product and implementation contract

IronDev is a governed, multi-user AI engineering cockpit. It helps a team connect a software repository, discuss and shape work, create clear tickets, run controlled build-and-test workflows, inspect failures and bounded repair attempts, review findings, record human decisions, approve consciously, apply changes safely, and inspect the resulting evidence.

The experience must remain simple on the surface and rigorous underneath. The backend owns truth and authority. The client presents that truth at the depth needed for the current decision.

---

## Document map

- **Product contract - sections 0-6:** decisions, objective, information depth, governance, entities, users, and information architecture.
- **Journeys and surfaces - sections 7-14:** entry, Board, Chat, Work Item, Library, Documents, Tools, and members.
- **System behavior - sections 15-21:** state language, components, matrices, writing, responsive behavior, accessibility, and visual system.
- **Handoff and acceptance - sections 22-24 and appendices:** React/API guidance, implementation order, acceptance test, and review checklist.

---

## 0. Decisions locked in this revision

This revision incorporates the product decisions made after the first UX package.

1. **Chat is a first-class product surface.** It is the home for project-aware conversation, source inspection, collaborative discussion, and the formation of documents, decisions, and ticket drafts. The product label is **Chat**, not Prompt, because IronDev already has persistent chat and project collaboration concepts.
2. **A tenant chooser appears only when the signed-in user has more than one accessible tenant.** One tenant is selected automatically. No tenant choice is shown when there is no genuine choice.
3. **Documents and Tools are real Library destinations.** They receive dedicated routes, complete states, permissions, and implementation contracts.
4. **Multi-user behavior is foundational.** Identity, membership, assignment, mentions, unread state, concurrency, authorship, decision attribution, and channel visibility are not later decoration.
5. **No fake maturity labels.** Working capabilities receive real routes. Intended but unimplemented destinations return an honest **Not implemented** state and, where applicable, an HTTP 501. The product does not use "demo", "alpha", "coming soon", or similar language to disguise missing behavior.
6. **Chat does not collapse into Build.** Chat can inspect, discuss, draft, and create governed artifacts. Build and source mutation remain separate controlled workflows.
7. **Human conversation is not authority.** A message that says "approved" is still only a message. Approval, continuation, and apply remain explicit backend-governed actions.

These decisions extend the original IronDev UX brief while preserving its governance and evidence boundaries.

---

## 1. Product objective

A new team member should be able to:

1. sign in;
2. enter the correct tenant without unnecessary selection;
3. choose or connect a project;
4. complete required project setup;
5. understand existing work on the Board;
6. ask IronDev and teammates questions in Chat;
7. see exactly which project sources IronDev inspected;
8. turn a useful conversation into a document, decision proposal, or ticket;
9. open a Work Item and understand its current gate;
10. start a governed run when the backend allows it;
11. understand why a run stopped or was repaired;
12. make the required human decision;
13. approve, continue, and apply through separate explicit actions; and
14. inspect the result and evidence.

The user should not need to understand internal event names, provider configuration, reason codes, receipt paths, or database concepts to complete ordinary work.

---

## 2. Information depth model

IronDev contains operational, technical, governance, collaboration, and evidence information. The product must place each item at the correct depth.

| Depth | Purpose | Typical information |
| --- | --- | --- |
| Front stage | Understand and move current work | project, work item, stage, status, blocker, assignment, waiting-on actor, one primary action |
| Working context | Make the current decision | criteria, sources, affected files, readiness, test results, findings, approval requirements, apply preflight |
| Backstage evidence | Prove, diagnose, or audit | raw events, provider/model details, hashes, receipts, evidence paths, complete run history, correlation IDs, policy evaluation |
| Administration | Configure access and capabilities | users, tenant membership, channel membership, project settings, tool connections, commands, providers, safety and authority policies |

**Placement rule:** Information moves forward when it becomes decision material. It moves back when it is only proof or administration.

Examples:

- A package hash is hidden on the Board but shown during the approval ceremony for that exact package.
- A tool's mutation scope is visible when the user is about to authorize a mutating action, but not repeated in every Chat message.
- A human sentence explains a blocker first. A reason code and correlation ID are available under details.
- Message authors and decision actors remain visible because attribution is decision material in a multi-user system.

---

## 3. Locked governance and collaboration principles

The following rules are product invariants.

### 3.1 Backend truth and authority

- The UI displays backend truth.
- The UI requests actions; the backend decides whether they are allowed.
- The UI never fabricates readiness, authority, success, evidence, approval, execution, membership, or access.
- Disabled actions explain the plain-language blocker and the next safe action.
- A client-side role label never grants authority.
- A stale client view cannot be used to approve or mutate a newer package.

### 3.2 Workflow boundaries

- Findings are advisory review evidence, not automatic vetoes.
- Finding disposition is not approval.
- Approval is not continuation.
- Continuation is not source apply.
- Apply is controlled source mutation, not commit, push, pull request, release, or deployment.
- Repair attempts remain visible. A repaired run never appears to have passed cleanly on its first attempt.
- Governance friction appears where authority is consumed, not as permanent wallpaper.

### 3.3 Collaboration boundaries

- A channel message is conversation, not approval or evidence validation.
- An assistant answer is advisory project context, not authority.
- A context link is a navigation and grounding pointer, not permission to mutate the linked object.
- Channel ownership or moderation controls collaboration behavior, not workflow authority.
- Tenant roles decide visibility and administrative eligibility; action-specific backend gates remain authoritative.
- Every consequential human action records the actual actor, tenant, project, object revision, time, and reason required by policy.

### 3.4 Product honesty

- A real capability has a real route and complete states.
- A temporarily broken capability is **Unavailable**, not Not implemented.
- A route whose capability does not exist is **Not implemented**.
- A capability with missing setup is **Setup required**.
- A user without access sees **Permission required** or **You do not have access**, not a fake empty state.
- Product chrome does not advertise destinations that are not usable.

---

## 4. Core product entities

| Entity | Meaning | Important boundary |
| --- | --- | --- |
| Tenant | Security and administration boundary containing users and projects | Tenant selection does not select a project or grant workflow authority |
| Project | Repository-backed engineering context | Project data must never bleed into another project or tenant |
| User | Authenticated human actor | Identity must be retained on messages, edits, decisions, approvals, and apply actions |
| Channel | Shared or members-only collaboration space | Channel role is not workflow authority |
| Chat session | A project-scoped conversation with IronDev, private or shared according to channel membership | Assistant output is advisory until promoted through an explicit artifact flow |
| Discussion | Saved project thinking | Discussion is not a decision or ticket |
| Document | Versioned durable project context | Upload or creation does not automatically make content approved truth |
| Decision | Accepted project guardrail with provenance | A draft decision is not accepted until the backend records acceptance |
| Ticket | Structured work contract | Ticket creation does not mean readiness or permission to run |
| Work Item | Ticket plus governed lifecycle state | Current backend state controls available actions |
| Run | Governed build/test attempt and evidence | Repair history remains visible |
| Finding | Critic observation requiring review/disposition as configured | Disposition is not approval |
| Approval | Explicit authority decision for a specific scope and package | Approval does not continue or apply |
| Tool | Governed capability with declared scopes and evidence | Configuration or availability does not grant invocation authority |
| Receipt/report | Durable proof and audit material | Evidence does not grant authority |

---

## 5. User, tenancy, and membership model

IronDev is multi-user even when one person is currently using a project.

### 5.1 Tenant entry rules

```text
Sign in
  -> zero tenants: Access unavailable
  -> one tenant: select automatically -> Projects
  -> multiple tenants: Choose tenant -> Projects
```

- Explicit sign-in always leads to project selection after tenant resolution.
- A returning valid session may safely restore the last tenant and project only when access is still valid.
- Switching tenant clears the active project, work item, Chat channel, draft, cached documents, and route history before the new tenant loads.
- **Switch tenant** appears in the user menu only when more than one tenant is accessible.

### 5.2 Canonical tenant roles

The client presents the repository's canonical tenant role vocabulary:

- Owner
- Tenant admin
- Approver
- Reviewer
- Operator
- Viewer
- Member

Role descriptions must state what the role generally allows the user to see or administer. They must not imply that a role alone can approve, run, continue, use a tool, or apply source. The backend evaluates each action against its complete policy and object state.

### 5.3 Project and work assignment

The Board and Work Item support:

- assignee or assignees;
- waiting-on user or role;
- watchers/followers;
- recent contributors;
- explicit reviewer or approver requirement when returned by the backend;
- user-specific filters: **My work**, **Waiting on me**, **Team**.

Assignment is coordination metadata. It does not grant authority.

### 5.4 Attribution

Always show human identity for:

- Chat messages and edits;
- document upload and versions;
- ticket creation and material contract edits;
- finding dispositions;
- approval, denial, continuation, and apply requests;
- tool configuration changes;
- membership and role changes.

Use display names front stage. Email, numeric IDs, and audit metadata belong in administration or details.

### 5.5 Concurrency

Every mutable shared object uses a backend revision or ETag.

- The client submits the revision it reviewed.
- A 409/conflict response never gets converted into optimistic success.
- The user sees: **This changed while you were working. Review the latest version before saving.**
- The UI shows a concise comparison when possible.
- Approval and apply screens always reload the current package and eligibility before enabling the primary action.

---

## 6. Primary information architecture

IronDev has four primary product surfaces:

1. **Board** - shared work state and run queue.
2. **Chat** - project conversation, source inspection, discussion, and artifact formation.
3. **Work Item** - governed lifecycle for one item.
4. **Library** - durable knowledge, evidence, tools, administration, and settings.

Do not add Batch, Runs, Governance, Settings, or Tools as equal top-level surfaces.

### 6.1 Quiet global header

```text
IronDev / BookSeller      Board  Chat  Work Item  Library      health  Robert
```

Rules:

- Project identity is visible.
- Work Item is enabled only when an item is open; otherwise it routes to the most recent accessible item or explains that no item is selected.
- The health indicator opens a details drawer.
- The user menu contains tenant identity, switch tenant when applicable, profile, and sign out.
- Do not permanently show provider, environment, API, version, governance, or connection prose.
- Presence is subtle: a compact avatar stack or **3 active** control opens project presence details. It must not compete with the primary task.

### 6.2 Library structure

```text
Library
  Projects
  Solution
  Documents
  Decisions
  Discussions
  Reports
  Governance
  Audit
  Tools
  Members and administration
  Settings
```

Governance is grouped by user intent:

- Runs and evidence
- Approvals
- Source apply
- Policies
- Tools and agents
- Release and rollback

### 6.3 Route contract

The target routes are project-scoped unless noted.

| Route | Purpose |
| --- | --- |
| `/sign-in` | Explicit sign-in |
| `/tenants/select` | Conditional tenant selection only when multiple tenants exist |
| `/projects` | Project tiles and Connect tile |
| `/projects/connect` | Connect a local repository |
| `/projects/:projectId/setup` | Project setup and readiness |
| `/projects/:projectId/board` | Shared pipeline and run queue |
| `/projects/:projectId/chat` | Chat landing and recent channels/sessions |
| `/projects/:projectId/chat/channels/:channelId` | Shared or members-only project channel |
| `/projects/:projectId/chat/sessions/:sessionId` | Direct IronDev session |
| `/projects/:projectId/work-items/:workItemId` | Governed Work Item |
| `/projects/:projectId/library` | Library home |
| `/projects/:projectId/library/documents` | Document list |
| `/projects/:projectId/library/documents/upload` | Upload document |
| `/projects/:projectId/library/documents/:documentId` | Document detail and versions |
| `/projects/:projectId/library/tools` | Project tool catalogue |
| `/projects/:projectId/library/tools/add` | Add or request a tool connection |
| `/projects/:projectId/library/tools/:toolId` | Tool detail, scope, health, and usage |
| `/projects/:projectId/library/members` | Project-visible member directory and collaboration settings |
| `/library/administration/users` | Tenant user administration for eligible users |

Compatibility redirects may preserve current routes, but new navigation uses the target structure. A route must not claim success merely because a component rendered.

### 6.4 Route outcome semantics

| Situation | UI | Suggested API outcome |
| --- | --- | --- |
| Capability works | Real screen and complete states | 2xx |
| User lacks access | Permission explanation and safe navigation | 403 |
| Object no longer exists | Not found with return path | 404 |
| Capability is defined but not built | Not implemented | 501 |
| Backend/service temporarily unavailable | Unavailable with retry | 503 |
| Current object revision changed | Conflict review | 409 |
| Action blocked by readiness/policy | Plain-language blocker and remedy | 409 or 422 according to API contract |

---

## 7. Entry journey

### 7.1 Sign in

**User goal:** Establish a valid session.

Front stage:

- Email
- Password
- Sign in
- One concise inline error
- Small expandable **Connection details**

Do not show token controls, tenant/project selectors, environment tables, fallback IDs, or raw API metadata in the main form.

Expired session copy:

> Your session has expired. Sign in again.

Valid credentials replace a rejected token without asking the user to clear local state manually.

### 7.2 Tenant resolution

- One tenant: do not render a chooser; select it and load Projects.
- Multiple tenants: show complete clickable tenant rows/cards with tenant name and optional organization description.
- Zero tenants: show **No tenant access** and the signed-in identity; offer **Sign out** and any real support path.
- Tenant selection failure preserves the screen and explains the failure.

### 7.3 Project tiles

Every project tile is the action. Show only:

- project name;
- repository path;
- Ready, Setup required, or Status unavailable;
- setup item count when useful;
- optional compact member/activity signal only when it helps distinguish shared projects.

The final tile is **Connect another project** and remains visible when projects exist.

Tile outcomes:

- Ready -> Board
- Setup required -> Project setup
- Status unavailable -> Project setup
- Connect another project -> Connect Project

### 7.4 Connect Project

Dedicated screen fields:

- Local repository
- Project name

Tauri uses the native folder chooser. Browser testing may allow typing a full path. Selecting a folder suggests the directory name without locking it.

Primary action: **Connect project**  
Secondary action: **Back to projects**

Creation of a project record always routes to Project setup. It never implies readiness.

### 7.5 Project setup

Show one current task, completed compact rows, and full details behind disclosure.

An incomplete project may offer **Open Board for shaping**, but governed runs remain blocked by backend readiness.

When setup becomes complete, show the confirmed state and an explicit **Open Board** action. Do not auto-redirect before the user can see completion.

---
