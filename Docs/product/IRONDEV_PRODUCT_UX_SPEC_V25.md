# IronDev v2.5 Product Specification

**Status:** Planning baseline
**Scope:** Workshop, durable Work Items, and configurable agents
**Authority:** Product direction only. This document does not claim current support.

## Product Objective

IronDev v2.5 makes the product understandable, configurable, and recoverable for someone who did not build it.

A new user should be able to:

1. Connect an AI provider securely.
2. Use IronDev immediately with built-in agent defaults.
3. Review and adjust an agent's model, skill, and personality.
4. Test the effective configuration before using it for real work.
5. Restore one field, one agent, one project, or all configuration to a known-good default.
6. Use Workshop to shape an idea.
7. Create a durable Work Item.
8. Carry that Work Item through build, review, approval, and controlled apply.
9. Inspect which configuration was used.
10. Recover after a poor configuration change without database or filesystem intervention.

## North-Star Proof

A person who did not build IronDev can install or open it, connect a model provider, use the built-in defaults, shape and complete one real governed change, deliberately alter an agent, restore it, and repeat the journey without help from the author.

## Product Structure

The v2.5 primary navigation is:

```text
Board | Workshop | Work Item | Library
```

### Board

Shows what work exists, where it is, who or what it is waiting on, and the next safe action.

### Workshop

The collaborative space where users ask IronDev questions, investigate project context, use documents and source context, discuss with other humans, shape ideas, resolve open questions, review candidate acceptance criteria, and create Work Items.

Chat remains an internal capability and component. It is not the product purpose.

### Work Item

The durable governed case that owns the journey from confirmed intent to final outcome.

### Library

Holds documents, decisions, evidence, tools, members, governance history, and configuration.

## Core Design Principles

### Useful Immediately

A new installation ships with good built-in profiles. Users do not need to write prompts before IronDev works.

### Configurable Where Preference Matters

Users may configure AI provider connections, model selection, token budgets, timeouts, skills, personalities, tenant defaults, project overrides, enabled tools, retrieval sources, and human review preferences where policy permits.

### Strict Where Authority Matters

Configuration cannot remove tenant isolation, project isolation, approval requirements, evidence preservation, exact-package binding, critic independence, secret protection, controlled source mutation, code-owned output contracts, version history, or audit history.

### Recoverable By Design

Every configurable value has a known source, history, comparison view, and reset action. No configuration failure should require direct SQL, file editing, or deleting profile directories.

### Configuration Is Provenance, Not Authority

An agent profile changes how an agent performs its assigned role. It does not grant capabilities, satisfy gates, approve work, or permit source mutation.

## Scope Model

Configuration is resolved through four layers:

```text
Built-in IronDev default
Tenant default
Project override
Effective run snapshot
```

### Built-In Default

Versioned configuration shipped with IronDev. It is always available, read-only, cannot be deleted, and is updated only through an IronDev release.

### Tenant Default

Optional organisation-wide configuration managed by Owner or Tenant Administrator. It applies to projects without an override and may select an AI connection available to the tenant.

### Project Override

Optional project-specific values. They apply only to one project, display which fields differ from the tenant or built-in default, and can be reset field by field or entirely.

### Effective Run Snapshot

The fully resolved non-secret configuration captured when an operation starts. Later configuration changes do not rewrite historical runs.

## AI Connection Model

Agent profiles must not contain provider credentials.

```text
AiConnection
- Id
- TenantId
- Name
- ProviderKind
- ControlledEndpointId
- SecretReference
- CredentialStatus
- CreatedByUserId
- CreatedUtc
- UpdatedByUserId
- UpdatedUtc
- Version
- IsEnabled
```

Connections may expose display name, provider, controlled endpoint, credential configured state, last successful test, last failed test, available model list, enabled state, tenant/project availability, and credential rotation date.

Connections must never expose raw API keys, bearer tokens, decrypted credentials, credential values in audit payloads, credential values in support exports, or credential values in model prompts.

Users may choose only administrator-approved provider endpoints. Arbitrary per-agent URLs are forbidden. Custom or self-hosted providers require a controlled endpoint record first.

Supported storage:

- Windows local mode: Windows Credential Manager or DPAPI-protected storage.
- Team-host mode: encrypted host secret store or external vault.
- Database: secret reference and metadata only.

Credential lifecycle actions are configure credential, replace credential, revoke credential, test connection, disable connection, and view non-secret history. After entry, the credential is never shown again.

## Agent Roles

### Analyst

User-facing name: **Workshop guide**.

Purpose: inspect project context, ask materially useful questions, separate facts from assumptions, create candidate acceptance criteria, preserve provenance, and prepare a Work Item proposal.

Cannot approve, start a governed build without a separate action, disposition findings, continue workflow, or apply source.

### Builder

Purpose: inspect the confirmed contract and linked context, propose the smallest coherent implementation, produce bounded code changes, and report uncertainty and scope drift.

Cannot approve its own work, alter the Work Item contract silently, claim tests executed when they did not, or apply source without the governed apply chain.

### Tester

Purpose: independently derive tests from the contract and implementation, exercise success and refusal paths, distinguish authored/discovered/executed tests, and produce test evidence.

Cannot approve, suppress failed evidence, treat compilation as test proof, or inherit Builder conclusions as fact.

### Critic

Purpose: independently review the exact sealed package, verify claims against contract/code/build/test evidence, identify defects and risks, and state no material objection when warranted.

Cannot share agent memory, receive hidden Builder reasoning, approve, modify the package, or disposition its own findings.

### Orchestrator

Purpose: deterministically coordinate the governed workflow, compose bounded tasks, enforce stages and gates, and preserve correlation and evidence links.

The Orchestrator runs no LLM, has no personality, has no editable skill, has no provider connection, and has no approval authority.

## Built-In Default Profiles

Defaults are versioned, named, and visible.

Example version identifier:

```text
IronDev Agent Defaults 2.5.0
```

### Analyst Default

Skill: inspect available project context before making claims. Ask only questions that materially improve the work contract. Separate confirmed facts, assumptions, constraints, dependencies, and unresolved questions. Prefer precise acceptance criteria that can be tested. Preserve exact provenance for documents, source files, messages, and decisions. Do not imply that a draft is approved work.

Personality: curious, structured, practical, and plain-speaking. Avoid generic consultancy language. Ask one useful question rather than five weak ones.

### Builder Default

Skill: read the confirmed contract, linked files, architecture context, and current project structure before proposing changes. Prefer the smallest coherent implementation that satisfies the acceptance criteria. Do not invent file paths, APIs, types, dependencies, or test outcomes. Identify scope expansion explicitly. Preserve existing conventions unless the contract requires change.

Personality: calm, precise, pragmatic, and economical. State uncertainty instead of bluffing. Explain decisions in terms of the contract and evidence.

### Tester Default

Skill: derive tests independently from the acceptance criteria and actual implementation. Cover normal behavior, failure paths, boundaries, regressions, and relevant security conditions. Distinguish tests that were authored, discovered, compiled, and executed. Never present a green build as proof that tests ran.

Personality: methodical, sceptical, patient, and evidence-oriented. Prefer reproducible results over optimistic interpretation.

### Critic Default

Skill: review the exact current sealed package independently. Verify claims against source, build evidence, executed tests, requirement coverage, and scope. Attack the weakest material claim first. Separate defects, risks, unsupported claims, missing evidence, and subjective preferences. Do not manufacture objections when the evidence is sufficient.

Personality: blunt, concise, independent, and unimpressed by confident wording. Comfortable saying either "blocked" or "no material objection."

## Agent Profile Contract

```text
AgentProfile
- Id
- ScopeKind
- TenantId
- ProjectId
- Role
- AiConnectionId
- Model
- TimeoutSeconds
- InputTokenLimit
- OutputTokenLimit
- Temperature
- SkillOverride
- PersonalityOverride
- BuiltInDefaultVersion
- CreatedByUserId
- CreatedUtc
- UpdatedByUserId
- UpdatedUtc
- Version
```

The backend returns effective profile values with provenance:

```text
EffectiveAgentProfile
- Role
- Connection
- Provider
- Model
- Timeout
- InputTokenLimit
- OutputTokenLimit
- Temperature
- EffectiveSkill
- EffectivePersonality
- FieldSources
- BuiltInDefaultVersion
- TenantProfileVersion
- ProjectProfileVersion
- EffectiveHash
```

Refuse unknown providers, disabled connections, inaccessible connections, missing credentials, unsupported models, invalid timeouts, invalid token budgets, secret-like skill/personality content, authority or capability fields, arbitrary outbound URLs, invalid scope, and stale writes.

Skill and personality are advisory framing. The code-owned role instruction, evidence package, boundaries, and output contract remain authoritative and are appended after configurable content. Conflicting profile instructions are ignored and recorded as configuration warnings.

## Reset and Recovery Model

Reset must be explicit, granular, previewable, and reversible.

Reset levels:

- Reset one field.
- Reset one agent.
- Reset all project agents.
- Reset tenant defaults.
- Restore a previous profile version.
- Restore IronDev defaults.

Before reset, the product shows before value, after value, source after reset, and the fact that existing run evidence is unchanged.

Field reset uses normal confirmation. Agent reset uses a confirm button with an impact summary. Tenant-wide reset requires stronger confirmation. Credential removal is separate and names affected agents and projects.

After reset, the UI offers restore previous configuration. The action creates another version; history is never deleted or rewritten.

When an effective profile cannot run, the product names the exact invalid field, names its source layer, and offers edit override, reset field, reset agent, choose another connection, or restore last working version.

## Draft, Test, and Publish Flow

Agent changes should not become effective one keystroke at a time.

```text
Current
Draft changed
Validate
Test
Publish
```

Validation checks permitted fields, connection access, credential presence, model compatibility, token limits, secret detection, profile boundary violations, and inherited-value resolution.

A test agent run invokes the selected model with a bounded, non-authoritative sample task. It shows latency, token usage, effective skill/personality version, and response. It creates no Work Item, creates no run, modifies no source, and grants no authority.

Publishing requires administrator eligibility, creates a new profile version, records actor and reason, changes future effective configurations, and does not alter running or historical operations.

## Profile History and Comparison

Each role provides current effective configuration, project override, tenant default, built-in default, version history, and run usage history.

Compare view shows fields changed, actor, reason, and usage. A profile version in use may not be physically deleted.

## Run Configuration Snapshot

Every model-driven operation captures a non-secret snapshot:

```text
AgentConfigurationSnapshot
- SnapshotId
- WorkItemId
- RunId
- Role
- ConnectionId
- Provider
- ControlledEndpointIdentity
- Model
- TimeoutSeconds
- InputTokenLimit
- OutputTokenLimit
- Temperature
- SkillVersion
- SkillHash
- PersonalityVersion
- PersonalityHash
- EffectiveProfileHash
- CreatedUtc
```

Reports expose the non-secret snapshot. They never include API keys, bearer tokens, decrypted credentials, or secret reference values suitable for retrieval.

## Settings Information Architecture

Canonical route:

```text
/projects/{projectId}/library/settings
```

Settings sections:

- Project: repository, workspace root, build/test command, architecture profile, retrieval readiness, project overrides.
- AI connections: list, add, configure credential, test, replace, disable, affected agents, audit history.
- Agents: Workshop guide, Builder, Tester, Critic, Orchestrator.
- Safety and approval: effective approval policy, intervention rules, separation-of-duties policy, solo exception status, locked invariant explanation.
- Runtime: API, worker, database, model-provider, workspace, and software version health.
- Advanced: endpoint details, diagnostics, support bundle, configuration export, validation report.

The current local-browser policy draft must either become a backend-owned versioned policy or remain visibly unavailable. It must never appear effective merely because it was saved locally.

## Workshop Product Changes

Primary navigation changes from Chat to Workshop.

Canonical route:

```text
/projects/{projectId}/workshop
```

Compatibility routes:

```text
/projects/{projectId}/chat
/projects/{projectId}/chat/sessions/{sessionId}
/projects/{projectId}/chat/channels/{channelId}
```

Compatibility routes redirect to equivalent Workshop routes.

Workshop conversation types are direct with Workshop guide, project channel, Work Item discussion, and document-led investigation.

Creating a Work Item records Workshop session, source messages, exact document versions, inspected source references, creator, participants, candidate contract version, and open questions at creation. Workshop evidence is provenance, not approval.

## Durable Work Item Requirement

v2.5 must not rely on `ProjectTicket` being renamed as a Work Item.

A durable Work Item owns:

```text
WorkItem
- Id
- TenantId
- ProjectId
- Title
- OriginKind
- OriginReference
- CurrentContractId
- CurrentRunId
- CurrentStage
- CurrentState
- AssigneeUserId
- WaitingOnKind
- WaitingOnReference
- CreatedByUserId
- CreatedUtc
- UpdatedUtc
- Version
```

The Ticket becomes the versioned contract associated with the Work Item. The Run becomes an attempt associated with the Work Item. A failed run, repair, revision, or apply retry remains within the same Work Item.

The detailed migration and compatibility contract is [V25-00 Work Item Contract Map](V25_00_WORK_ITEM_CONTRACT_MAP.md).

## Permissions

Any permitted project member may read effective profiles, view built-in defaults, view non-secret run snapshots, see connection health, and view history.

Project administrators may create and publish project overrides, reset project overrides, test agents, and select tenant-approved connections.

Tenant Owners and Tenant Administrators may manage tenant defaults, create AI connections, store or rotate credentials, disable connections, restore tenant configuration, and control project access to connections.

Nobody may read a stored credential, put a secret in an agent profile, configure approval authority through skill/personality, redirect an agent to an arbitrary endpoint, alter historical snapshots, or configure the Orchestrator as a model-driven agent.

## Configuration Export and Import

Export is non-secret only: tenant or project profile overrides, model identifiers, token budgets, skills, personalities, connection logical names, and built-in default versions.

Import creates a draft. It maps logical connection names, refuses inaccessible connections, validates all values, shows differences, requires explicit publish, and preserves source provenance. Import is a configuration proposal, not active configuration.

## First-User Journey

The primary usability test for v2.5:

1. User signs in.
2. User selects a project.
3. Settings reports no AI connection.
4. User creates an OpenAI or supported local-provider connection.
5. User enters a credential.
6. User tests the connection.
7. Built-in Analyst, Builder, Tester, and Critic defaults become runnable.
8. User opens Workshop.
9. User shapes a real change.
10. User reviews the proposed Work Item contract.
11. User creates the Work Item.
12. User starts a governed run.
13. User reviews the package and findings.
14. User records approval.
15. User requests continuation.
16. User applies the exact reviewed change.
17. User changes the Builder personality.
18. User tests it.
19. User decides the change is poor.
20. User resets Builder to the previous or built-in default.
21. User repeats a small run successfully.
22. The final report identifies the exact profile versions used by both runs.

No author help. No SQL. No editing configuration files.

## Delivery Slices

| Slice | Name | Purpose |
| --- | --- | --- |
| V25-00 | Work Item contract map | Lock current-to-target ownership before schema work. |
| V25-01 | Work Item core identity | Add durable Work Item storage and links to existing tickets and runs. |
| V25-02 | Work Item migration and compatibility | Create Work Items for existing tickets and preserve current URLs and identifiers. |
| V25-03 | Workshop rename | Add canonical Workshop navigation and compatibility redirects. |
| V25-04 | Analyst role | Add the Workshop guide profile and code-owned role boundary. |
| V25-05 | Built-in defaults | Ship versioned Analyst, Builder, Tester, and Critic defaults. |
| V25-06 | AI connection contract | Add tenant-scoped connections, controlled endpoints, and non-secret metadata. |
| V25-07 | Secure credentials | Add write-only credential storage, rotation, revocation, and redaction. |
| V25-08 | Effective profile inheritance | Resolve built-in, tenant, and project layers with per-field provenance. |
| V25-09 | Profile draft and publish | Add validation, test execution, versioning, publish reason, and stale-write handling. |
| V25-10 | Reset and restore | Add field, agent, project, tenant, previous-version, and built-in reset flows. |
| V25-11 | Settings hub | Replace the long Settings page with Project, AI Connections, Agents, Safety, Runtime, and Advanced sections. |
| V25-12 | Run snapshots | Persist exact non-secret effective agent configurations per run. |
| V25-13 | Configuration history | Add compare, usage, actor, reason, and restore views. |
| V25-14 | Export/import | Add non-secret configuration packs as draft-only imports. |
| V25-15 | Non-author usability qualification | Run the complete first-user journey on a fresh installation. |

## Required Tests

Security tests prove credentials never return from reads, logs, reports, audit payloads, exports, or exceptions; cross-tenant and cross-project access is refused; arbitrary endpoint injection is refused; secret-looking skill/personality content is refused; and unauthorized profile writes are refused.

Authority tests prove skill cannot grant capability, personality cannot satisfy approval, profile cannot move a gate, Analyst cannot create an accepted approval, Builder cannot apply source directly, Critic remains independent, and Orchestrator remains deterministic.

Versioning tests prove stale writes conflict, published versions are immutable, restore creates a new version, historical runs retain old snapshot hashes, reset affects future work only, and built-in defaults remain available.

UX tests cover first-run missing connection, connection setup, failed connection test, draft profile validation, test-agent invocation, publish, field reset, agent reset, project reset, restore previous version, credential rotation, narrow viewport, keyboard operation, and screen-reader operation.

## Exit Criteria

IronDev v2.5 is complete when:

- Workshop is the primary work-formation surface.
- A durable Work Item owns the lifecycle.
- Built-in defaults allow immediate use.
- Users can configure agents without editing files.
- Credentials are write-only and securely stored.
- Agent configuration supports tenant defaults and project overrides.
- Every effective value identifies its source.
- Every published profile is versioned.
- Users can preview, test, compare, reset, restore, and audit changes.
- Every run stores its exact non-secret configuration snapshot.
- A non-author user completes the full first-user journey without assistance.
- No configuration option weakens an authority boundary.

## Final Review Line

IronDev should let a team build the engineering system that suits them without requiring them to build the safety system themselves.

## Killjoy Line

Configurability without defaults is homework. Configurability without reset is a trap. Configurability without a safety floor is an incident waiting to happen.
