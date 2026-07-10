# IronDev Project Documents and Tools Specification

**Version:** 1.0  
**Date:** 10 July 2026  
**Scope:** Library/Documents, project uploads, document versions, Library/Tools, tool configuration, and Chat integration

Related project material:

- [Versioned project document models](../../IronDev.Core/Models/ProjectDocumentModels.cs)
- [Governed tool contracts](../../IronDev.Core/Tools/GovernedToolContracts.cs)
- [Chat and artifact boundaries](CHAT_DISCUSSION_TICKET_BUILD_BOUNDARIES.md)

---

## 1. Product decisions

- Documents and Tools are dedicated Library destinations.
- Both are project-scoped in normal use.
- Tenant administration is exposed only where a tenant-level connection or membership action is required.
- Uploading a file does not automatically make it usable context.
- Connecting a tool does not automatically authorize its use.
- Working routes show real functionality and complete states.
- Unbuilt destinations return Not implemented and are not promoted in primary navigation.

---

## 2. Documents information model

### 2.1 User-visible document origins

| Origin | Description | Version rule |
| --- | --- | --- |
| Created in IronDev | Markdown-based architecture, discussion summary, build plan, decision log, or other supported project document | Every edit creates an immutable version |
| Uploaded source | A local file uploaded to the project for reading/retrieval | Replacement creates a new version and retains prior source/evidence |

The UI can present both origins in one Documents area while preserving origin, type, processing, and version provenance.

### 2.2 Document status

Document identity status:

- Active
- Archived
- Deleted/tombstoned according to retention policy

Current version/processing status:

- Uploading
- Processing
- Draft
- Ready
- Approved, only when the backend supports and returns that status
- Processing failed
- Unsupported
- Superseded
- Unavailable

Do not collapse Uploading, Processing, and Ready into one optimistic success state.

### 2.3 Authority boundary

A document is durable project context. It is not automatically:

- an accepted decision;
- a ticket contract;
- readiness evidence;
- approval;
- policy satisfaction;
- tool authority;
- permission to mutate source.

Links preserve provenance and navigation. They do not grant access to restricted content.

---

## 3. Documents routes and navigation

```text
/projects/:projectId/library/documents
/projects/:projectId/library/documents/upload
/projects/:projectId/library/documents/:documentId
/projects/:projectId/library/documents/:documentId/versions/:versionId
```

Library home links to Documents only when the route has functional list/read behavior. Direct access to an intended but unbuilt child action returns Not implemented.

---

## 4. Document list

### 4.1 Primary action

**Upload document**

An optional **Create document** secondary action appears only when the native document editor/creation flow is implemented.

### 4.2 List content

Show:

- title/name;
- origin;
- document type;
- current status;
- current version label;
- updated by and time;
- visibility;
- links/use count when useful.

Do not show content hashes, storage paths, parser names, raw indexing events, or tenant/project numeric IDs.

### 4.3 Filters and search

- All
- Ready
- Processing
- Created in IronDev
- Uploaded
- Archived
- Search by title/name

A filtered empty state is distinct from no documents and from service failure.

### 4.4 Multi-user list behavior

- Results are tenant/project scoped.
- Restricted documents do not appear as discoverable titles to unauthorized users unless policy explicitly allows that metadata.
- User preferences such as filters persist per user/project.
- Processing updates may stream or poll and reconcile against canonical state.

---

## 5. Upload document

### 5.1 Fields

- File
- Display name, prefilled from filename
- Document type/category when implemented
- Optional description
- Visibility: Project or Members only, when supported
- Members, when Members only is selected

### 5.2 File selection

- Tauri uses the native file chooser.
- Drag-and-drop is supported only when it has a complete keyboard alternative.
- The client validates obvious type/size limits but treats the backend as authoritative.
- The UI never reads sensitive content into logs or analytics.

### 5.3 Flow

```text
Choose file
-> validate request
-> upload
-> backend accepts file
-> process/extract
-> index/retrieval readiness
-> Ready
```

Each transition is based on backend state.

### 5.4 Failure handling

| Failure | UI behavior |
| --- | --- |
| Request rejected | Preserve metadata and show reason |
| Network interruption | Show retry; do not say uploaded |
| Backend accepted but processing failed | Show Processing failed and retry/details |
| Unsupported type | Show Unsupported before or after backend validation as authoritative |
| Permission removed | Stop action and show access message |
| Duplicate content | Follow backend result: link existing, create version, or reject; do not guess |

### 5.5 Success

After upload acceptance:

> Document uploaded. Processing is in progress.

After Ready:

> Document ready.

Actions:

- Open document
- Use in Chat

Do not call the document Ready immediately after transport success.

---

## 6. Document detail and versions

### 6.1 Front stage

- title;
- origin/type;
- status;
- current version;
- uploader/creator;
- last editor;
- visibility;
- concise description;
- where used;
- primary action based on state and permission.

### 6.2 Version history

Each version shows:

- version label;
- status;
- creator;
- created time;
- change summary;
- parent version;
- use/link summary.

Immutable versions are never edited in place. A change creates a new version.

### 6.3 Backstage details

Behind **Document details**:

- content hash;
- original filename and media type;
- byte size;
- parser/extractor;
- indexing status;
- evidence/storage reference;
- processing timeline;
- correlation ID;
- complete artifact links.

### 6.4 Conflict handling

New versions are created from a reviewed parent/current revision. A stale update returns conflict:

> A newer document version exists. Review it before creating another version.

The UI may let the user branch from an older version only when the backend explicitly supports that model.

---

## 7. Documents in Chat and Work Items

### 7.1 Attach existing document

The context picker shows only accessible documents and the exact version to be attached.

### 7.2 Upload from Chat

The upload becomes a project document first. When Ready, it can be attached to the current Chat request/session.

### 7.3 Source disclosure

An IronDev answer cites the exact document/version used. If a new version appears later, the answer remains linked to the original and may show **Newer version available**.

### 7.4 Ticket and decision links

A ticket or decision proposal records source document/version links. Link creation does not convert the document into accepted truth.

---

## 8. Tools information model

### 8.1 Governed definition

A tool definition includes declared capability material such as:

- name and description;
- input/output contract;
- allowed callers;
- mutates state;
- nested calls;
- file write;
- process execution;
- network access;
- workspace mutation;
- evidence kinds;
- boundary statement.

The product translates this into understandable effective scope without hiding the exact definition from experts.

### 8.2 Tenant connection and project enablement

```text
Tenant connection
  credentials/installation/connection health

Project enablement
  effective capabilities and policy scope for this project
```

A connected tenant tool may still be disabled or restricted in a project.

### 8.3 Status vocabulary

- Connected
- Setup required
- Unavailable
- Disabled
- Permission required
- Not implemented

Execution results:

- Succeeded
- Rejected
- Failed

Rejected means policy or preflight stopped the tool before the body ran. Failed means the body was allowed but did not complete successfully. The UI explains this in human language first.

---

## 9. Tools routes and navigation

```text
/projects/:projectId/library/tools
/projects/:projectId/library/tools/add
/projects/:projectId/library/tools/:toolId
/library/administration/tools/:toolId/connection
```

The tenant connection route is shown only to eligible users and only for connectors that actually require tenant-level configuration.

---

## 10. Tool catalogue

### 10.1 Primary action

- Eligible administrator: **Add tool**
- Other user when request workflow exists: **Request tool**
- Otherwise no fake action; explain who manages tools under details.

### 10.2 Categories

- Development
- Testing and validation
- Repositories and source control
- Issue tracking
- Documentation and knowledge
- Communication
- Deployment and operations
- Custom tools

### 10.3 Tool card

Show:

- name;
- one-sentence purpose;
- status;
- effective project scope summary;
- health;
- connection owner/manager when useful.

Example:

```text
GitHub
Repository and pull-request access
Connected
Project scope: Read repository
```

Example:

```text
Playwright
Browser testing
Setup required
Executable not detected
```

Do not expose secret values, full policy text, raw environment variables, or evidence IDs on the card.

---

## 11. Add/configure tool

### 11.1 Flow

```text
Choose connector
-> review declared capabilities
-> configure tenant connection when required
-> test connection
-> choose project enablement/scope
-> backend policy evaluation
-> enabled or blocked result
```

### 11.2 Capability review

Bring risk forward before a mutating scope is enabled:

- Read project/repository
- Network access
- Run process
- Write files
- Mutate workspace
- Create external objects
- Send external communication

The action states exactly what is being enabled for which project.

### 11.3 Credentials

- Secret fields are write-only.
- After save, show credential owner/type, last updated time, and connection result, not the secret.
- Failed connection tests do not clear non-secret form fields.
- Credential changes are security-audited and fail closed when required audit is unavailable.

### 11.4 Multi-user eligibility

Tenant role can make a user eligible to administer a connection, but backend authority remains decisive. UI copy:

> Your tenant role allows tool administration, subject to the current security policy.

Avoid copy such as **You have full tool access**.

---

## 12. Tool detail

### 12.1 Front stage

- connection and project status;
- effective scope;
- health;
- last checked;
- primary action: Complete setup, Test connection, Enable for project, Disable, or none according to backend.

### 12.2 Working context

- declared capabilities;
- allowed project uses;
- mutating vs read-only boundary;
- evidence produced;
- recent use summary;
- current blockers/remedies.

### 12.3 Backstage details

- definition version;
- exact allowed callers;
- evidence kinds;
- policy decisions;
- request IDs;
- execution duration;
- evidence references;
- failure/rejection details;
- complete use timeline.

---

## 13. Tool use in Chat and Work Items

### 13.1 Read-only use

IronDev may use an available read-only tool when the current request and backend policy allow it. The answer shows a compact disclosure:

> Used repository search.

The user can inspect request/result details.

### 13.2 Mutating use

A proposed mutating action is a separate control with explicit object, scope, and consequence. Examples:

- Create external issue
- Send message
- Open pull request
- Trigger deployment
- Change project configuration

Chat never executes such an action merely because it described it.

### 13.3 Attribution

Record:

- requesting human;
- effective caller/agent;
- tool;
- project;
- request reason;
- policy result;
- execution result;
- evidence refs.

### 13.4 Failure copy

Rejected:

> The tool did not run because the current policy does not allow this request.

Failed:

> The tool was allowed to run but did not complete successfully.

The technical boundary and blocked actions remain available under details.

---

## 14. State matrices

### 14.1 Documents

| State | List | Detail | Chat attachment |
| --- | --- | --- | --- |
| Loading | skeleton rows | loading surface | disabled with loading label |
| Empty | upload guidance | n/a | no documents available |
| Uploading | progress row | upload progress | not selectable |
| Processing | processing badge | extraction/index progress | not used by IronDev |
| Ready | normal row | content/version/use | selectable |
| Failed | retry badge | reason/remedy | unavailable |
| Restricted | hidden or permission row per policy | permission explanation | absent |
| Conflict | latest-version banner | compare/reload | exact older version remains linked |
| Not implemented | destination state | action-specific state | no advertised action |

### 14.2 Tools

| State | Catalogue | Detail | Invocation |
| --- | --- | --- | --- |
| Loading | skeleton cards | loading | unavailable |
| Connected | effective scope | health/config | backend-evaluated |
| Setup required | blocker | current setup task | rejected/not offered |
| Disabled | disabled badge | enable action if eligible | unavailable |
| Unavailable | health issue | retry/details | failed closed |
| Permission required | read-only explanation | no config controls | backend decides use |
| Rejected | n/a | use event details | no body ran |
| Failed | n/a | use event details | body ran and failed |
| Not implemented | honest card only if directly reached | Not implemented | not callable |

---

## 15. Accessibility

- Upload supports keyboard file selection.
- Progress has text and programmatic value.
- Processing changes are announced once per meaningful transition.
- Document and tool cards are complete controls or contain clearly separated controls, never nested competing click targets.
- Capability/risk state is conveyed by text and icon, not color alone.
- Secret fields support password-manager and accessible-label conventions.
- Tables reflow into labelled rows at narrow widths.
- Details drawers return focus to their invoking controls.

---

## 16. Acceptance tests

### Documents

1. User can upload a supported file to the current project.
2. Transport success shows Processing, not Ready.
3. IronDev cannot cite the document before Ready.
4. A processing failure preserves the document record and offers a real retry/remedy.
5. A new version preserves the previous immutable version.
6. Two users cannot silently overwrite the current version.
7. Source links identify the exact version used.
8. Restricted documents do not leak content or unauthorized metadata.

### Tools

1. User can distinguish tenant connection from project enablement.
2. Effective read/write/network/process/workspace scopes are understandable.
3. Secret values are never displayed after save.
4. Ineligible users do not receive decorative configuration controls.
5. A read-only Chat tool use is disclosed.
6. A mutating action requires a separate explicit backend-governed action.
7. Rejected and Failed results are not conflated.
8. Prior evidence remains accessible after a tool is disabled.
9. Channel role, assignment, or tenant role alone never grants invocation authority.
