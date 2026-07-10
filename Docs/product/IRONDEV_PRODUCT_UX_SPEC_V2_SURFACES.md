# IronDev Product and UX Specification v2 - Product Surfaces

This module continues the [foundations and entry contract](IRONDEV_PRODUCT_UX_SPEC_V2_FOUNDATIONS.md) and covers sections 8-14 of the complete specification.

---

## 8. Board

The Board is a shared operational surface. It answers:

- What work exists?
- Where is it?
- What is blocked?
- Who or what is it waiting on?
- What needs me?

### 8.1 Pipeline

Columns:

```text
Shape | Ticket | Build | Review | Done
```

Cards show:

- work item ID and title;
- stage and concise state;
- blocker or waiting-on actor;
- assignee avatar/name when useful;
- last meaningful event time;
- repair count, finding count, or mutation lease only when relevant.

Do not show hashes, evidence paths, full readiness reports, or raw event streams.

### 8.2 Board actions and filters

One primary action: **New work**. It routes to Chat and starts a new IronDev session with ticket formation available.

Filters:

- My work
- Waiting on me
- Team

Run Queue is an expandable Board region, not a top-level destination.

### 8.3 Multi-user updates

- Board updates may stream or poll, but the visible state always maps to backend records.
- A card moving while the user watches is announced without stealing focus.
- Personal filters and scroll position persist per user and project.
- Presence does not imply ownership. A small **Robert is viewing** indicator may appear in Work Item details, not as a blocker.

### 8.4 Project-level blocked state

If project readiness blocks all governed runs, retain Board shaping capability and show one concise project-level message:

> Governed runs are blocked until project setup is complete.

Actions:

- **Complete project setup**
- **View setup details**

Do not repeat the same warning on every card.

---

## 9. Chat

Chat is the first-class space for project-aware thinking and collaboration. It is not a generic chatbot and it is not a hidden Build console.

### 9.1 Chat outcomes

Users can:

- ask project-aware questions;
- discuss work with teammates;
- ask IronDev to inspect permitted repository and project sources;
- see what IronDev inspected;
- save useful thinking as a Discussion;
- create or version a Document;
- propose a Decision;
- form and review one or more ticket drafts;
- link the conversation to an existing Work Item;
- continue discussion without creating work.

### 9.2 Chat workspace layout

```text
+----------------------+--------------------------------+----------------------+
| Channels / sessions  | Conversation                   | Working material     |
|                      |                                |                      |
| Project channels     | people, IronDev, notices       | Sources inspected    |
| Direct with IronDev  |                                | Candidate criteria   |
| Linked work items    | composer                       | Open questions       |
| Recent               |                                | Draft artifacts      |
+----------------------+--------------------------------+----------------------+
```

At narrow widths, the channel rail becomes a drawer and the working-material rail collapses behind **Show context**. The conversation remains primary.

### 9.3 Channel and session types

1. **Project channel** - shared project conversation such as General, Architecture, Tickets, Build Runs, Review, or Release.
2. **Members-only channel** - shared only with selected project members.
3. **Direct IronDev session** - one user and IronDev; implemented as a members-only channel/session and shareable through an explicit action.
4. **Linked channel** - context-specific channel for a Work Item, run, review, or release candidate.

Channel role controls moderation and visibility only:

- Owner
- Moderator
- Member
- Read only

### 9.4 Human and assistant participation

- In a direct IronDev session, every sent message is an assistant request unless the UI clearly offers a note-only mode.
- In a shared human channel, IronDev responds only when explicitly invoked with `@IronDev`, **Ask IronDev**, or a selected-message action.
- Assistant turns are visually distinct but not theatrical.
- System notices and linked events are compact, neutral rows.
- Human messages never imitate assistant or system styling.

### 9.5 Composer

The normal composer supports:

- message text;
- mentions;
- link existing context;
- upload or attach a document;
- send.

The composer does not permanently show Build controls, model selectors, tool selectors, mode confidence, or route traces.

Context actions open a controlled picker:

- Repository file or symbol
- Existing document
- Decision
- Ticket or Work Item
- Run or finding
- Upload document

### 9.6 Reading and source transparency

When IronDev inspects project material, use honest states:

- Inspecting project
- Inspection complete
- Some sources unavailable
- Project inspection unavailable

The working-material rail shows:

- source name;
- type;
- relevant section or symbol;
- why it was used;
- status;
- optional supporting excerpt;
- full retrieval/evidence details behind disclosure.

Never imply that IronDev read the entire repository unless the backend returns that fact. Do not fabricate file counts or progress.

### 9.7 Structured artifact formation

Conversation can produce a structured draft containing:

- candidate title;
- problem or desired outcome;
- proposed change;
- business rules;
- acceptance criteria;
- constraints;
- affected areas;
- assumptions;
- open questions;
- potential conflicts;
- source message and document links;
- confidence as advisory metadata, not authority.

Actions are state-dependent:

- Keep discussing
- Ask next question
- Edit draft
- Review ticket draft
- Save discussion
- Create document
- Propose decision

### 9.8 Ticket creation

**Review ticket draft** opens a dedicated review state on the same Chat route or a child route. The user reviews title, problem, criteria, constraints, open questions, and provenance.

Primary action: **Create ticket**

Backend-confirmed success shows the real ticket identifier and **Open work item**.

Ticket creation does not imply:

- readiness;
- approval;
- permission to run;
- confirmed affected files;
- source mutation.

A conversation may yield multiple candidate tickets. IronDev proposes the split; the user decides which drafts become real tickets.

### 9.9 Multi-user draft behavior

- Draft shows creator, contributors, and last updated time.
- Edits use revision control.
- If another user changes the draft, disable confirmation until the latest version is reviewed.
- Only one primary action appears at a time; co-editing does not become a real-time document editor unless implemented and backed by a real concurrency model.
- A user without ticket-create permission may still contribute and request that an eligible user create the ticket.

### 9.10 Message lifecycle

- Edited messages show **edited**.
- Deleted messages show a tombstone when required for thread continuity or provenance.
- Editing a message already used by an assistant answer or artifact does not rewrite history silently; mark downstream material as based on an earlier version and offer **Ask again** or **Refresh draft**.
- Read markers and unread counts are convenience, not governance evidence.
- Notification levels: All, Mentions, None.

### 9.11 Chat boundaries

Chat must not:

- run a governed build inline;
- hide sandbox/run state inside a message;
- treat a message as approval;
- silently create tickets, decisions, issues, pull requests, or source changes;
- silently use a mutating tool;
- imply that linked context is validated evidence;
- store or display hidden chain-of-thought.

---

## 10. Work Item

The Work Item remains the governed lifecycle surface.

Header:

```text
WI-42 - Add book sorting                 BUILD - Running
Back to Board                            [one primary next action]
```

Stage rail:

```text
Shape -> Ticket -> Build -> Review -> Done
```

### 10.1 Shared collaboration in a Work Item

The Work Item shows:

- assignee and waiting-on actor;
- followers;
- concise recent activity;
- linked Chat channel;
- **Discuss in Chat** action;
- actor identity on criteria changes, findings, decisions, approvals, continuation, and apply.

Do not duplicate the full Chat thread inside the Work Item. Show a compact activity or selected discussion excerpt and route to the linked channel.

### 10.2 Gate behavior

A blocked gate explains:

1. human sentence;
2. next safe action;
3. technical details on demand.

Example:

> Cannot start the run. The ticket has no acceptance criteria.

Actions:

- **Add criteria**
- **View readiness details**

### 10.3 Contract rail

Compact always-visible summary:

```text
Contract
4 criteria - 2 affected files
1 decision - 0 open questions
Show contract
```

Relevant contract material moves forward by stage. Complete provenance, history, and evidence remain one action away.

### 10.4 Build

Normal progress:

- Generating proposal
- Authoring tests
- Building
- Testing
- Preparing review

Summary:

- files changed;
- tests authored;
- repair attempts;
- elapsed time.

A repaired run must say:

> Attempt 1 failed. Attempt 2 repaired and passed. The repaired proposal is under review. The original proposal remains preserved.

### 10.5 Review and approval

Show findings requiring action first. Each finding includes severity, why it matters, required fix, disposition, reason, and actor.

Approval appears only when the backend returns current eligibility for the current package. The ceremony may require reason, scope, expiry, and exact package confirmation.

The eligible approver list may be shown for coordination, but it does not enable an ineligible user's action.

### 10.6 Continuation and apply

Approval success exposes the next separate action returned by the backend. Continuation and apply each re-evaluate current state.

Apply preflight shows decision material such as:

- current package identity;
- source/workspace state;
- affected files;
- conflicts or drift;
- mutation scope;
- required reason.

### 10.7 Done

Start with the outcome:

- Applied successfully, or the exact terminal outcome;
- files changed;
- tests passed;
- repair attempts;
- approving actor;
- applying actor;
- time.

Actions:

- View changes
- Open report
- View receipts
- Discuss outcome

State once that Apply is not commit, push, pull request, release, or deployment.

---

## 11. Library

Library contains durable knowledge, evidence, capabilities, and administration that are not needed continuously.

### 11.1 Library home

Use grouped cards/rows, not a flat wall of chips. Each group shows its purpose and current project count/state.

- Knowledge: Documents, Decisions, Discussions, Solution
- Work evidence: Reports, Runs, Audit
- Governance: Approvals, Source apply, Policies, Release and rollback
- Capabilities: Tools and agents
- Project administration: Projects, Members, Settings

### 11.2 Normal-user boundary

A normal contributor can complete work without opening Library. An expert, administrator, or auditor can still reach all evidence and configuration.

---

## 12. Documents

Documents are a first-class Library area and can also be added from Chat.

### 12.1 Unified user model

The UI presents one Documents area with two origins:

- **Created in IronDev** - versioned Markdown project documents such as architecture, discussion summaries, build plans, and decision logs.
- **Uploaded source** - local files added to project context and processed for retrieval.

The backend may store these through different models. The UI must not hide provenance or version history.

### 12.2 Routes

```text
/projects/:projectId/library/documents
/projects/:projectId/library/documents/upload
/projects/:projectId/library/documents/:documentId
```

### 12.3 Document list

Primary action: **Upload document**

List columns/cards:

- name/title;
- type and origin;
- status;
- current version;
- updated by and time;
- where used count when useful.

Filters:

- All
- Ready
- Processing
- Created in IronDev
- Uploaded
- Archived

### 12.4 Upload flow

Fields:

- File
- Project location/category when real
- Display name, prefilled from filename
- Optional description
- Visibility when the project supports members-only documents

Tauri uses a native file chooser and may support drag-and-drop.

Primary action: **Upload document**

The client preserves the selected file and metadata on recoverable failure when safe to do so.

### 12.5 Honest document states

- Uploading
- Processing
- Ready
- Processing failed
- Unsupported
- Unavailable
- Archived

Upload completion does not equal processing completion. A document is available to Chat retrieval only after the backend reports Ready.

### 12.6 Detail and versions

Show:

- title and type;
- origin and original filename where applicable;
- status;
- current version;
- uploader/creator;
- last editor;
- project visibility;
- used by Chat, ticket, decision, or report links;
- version list and change summaries;
- **Add new version** when allowed.

Backstage details:

- content hash;
- parser/extractor information;
- storage/evidence path;
- processing events;
- indexing state;
- correlation IDs.

### 12.7 Multi-user document behavior

- Version creation is attributed to a user.
- Existing immutable versions are never rewritten.
- Replacing an uploaded file creates a new version.
- Concurrent edits produce a conflict review, not last-write-wins success.
- Archive and delete actions use the backend's retention and reference rules.
- A user who can read a linked ticket does not automatically gain access to a restricted document.

### 12.8 Document authority boundary

An uploaded or authored document is project context. It may be cited and used to draft work, but it is not automatically an accepted decision, approved contract, or permission source.

---

## 13. Tools

Tools are governed capabilities. They belong in Library, not in a permanent model playground.

### 13.1 Two-layer model

```text
Tenant
  -> connection/installation and credential ownership
Project
  -> enabled capability and effective permission scope
```

A tenant may connect GitHub once. A project may allow repository read but not pull-request creation. Another project may have a different scope.

### 13.2 Routes

```text
/projects/:projectId/library/tools
/projects/:projectId/library/tools/add
/projects/:projectId/library/tools/:toolId
```

Tenant-wide connection administration may use a Library administration route available only to eligible users.

### 13.3 Tool list

Primary action: **Add tool** or **Request tool**, depending on eligibility returned by the backend.

Useful categories:

- Development
- Testing and validation
- Repositories and source control
- Issue tracking
- Documentation and knowledge
- Communication
- Deployment and operations
- Custom tools

Tool card:

- name and purpose;
- Connected, Setup required, Unavailable, Disabled, or Not implemented;
- effective project scopes;
- health;
- who manages the connection;
- recent usage count only when useful.

### 13.4 Tool detail

Show:

- description;
- tenant connection status;
- project enablement status;
- effective declared capabilities;
- mutates state, file write, process, network, or workspace scope;
- allowed callers when user-facing;
- evidence produced;
- health and last checked time;
- recent governed use;
- configuration actions allowed to the current user.

Credentials and secret values are never displayed after entry.

### 13.5 Chat and tools

Chat normally selects an appropriate permitted read-only capability and reports the result in compact language:

> Used repository search to locate four relevant files.

Full tool request, policy decision, evidence references, and timing remain behind details.

For mutating external actions, Chat proposes a separate explicit action. It never silently:

- creates an external issue;
- changes source;
- sends a message;
- opens a pull request;
- triggers deployment;
- changes configuration.

The action receives its own confirmation and backend authority evaluation.

### 13.6 Multi-user tool administration

- Tenant Owner or Tenant admin may be eligible to create or manage connections, subject to backend policy.
- Project users see only effective project capability and safe health information.
- Tool usage is attributed to the requesting human and the effective caller/agent.
- Disabling a tool does not erase prior evidence.
- A channel role or project assignment does not grant tool authority.

---

## 14. Members and administration

Tenant user administration belongs in Library administration.

### 14.1 Members list

Show:

- display name;
- email only when appropriate;
- tenant role;
- active/inactive;
- project/channel membership summary;
- last active information only when actually available and policy permits.

Primary action for eligible users: **Add member**

### 14.2 Role changes

Role change ceremony explains the visibility and administrative effect, while explicitly avoiding promises of workflow authority.

Protect the last tenant Owner. Backend rejection copy:

> The tenant's last owner cannot be demoted or removed.

### 14.3 Removal

Removing a tenant membership does not erase the person's authored history. Messages, versions, decisions, and receipts retain the actor identity required for audit.

---
