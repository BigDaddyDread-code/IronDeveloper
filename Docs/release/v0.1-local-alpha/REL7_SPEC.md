# REL-7 - Release Docs and Packaging

## Purpose

Create the repeatable v0.1 Local Alpha release packet: docs, commands, package manifest, and evidence bundle that let another developer understand what is included, what is excluded, and how to run the alpha path.

REL-7 packages release truth. It does not create release authority.

## Review Line

A release package is a bounded handoff artifact. It is not proof that the product is ready beyond the evidence it names.

## Killjoy Line

If the package needs tribal knowledge to run, it is not a release package. It is a zip file with hope inside.

## Suggested PR

Title:

```text
release(alpha): add v0.1 local alpha docs and package manifest
```

Branch:

```text
release/rel7-alpha-docs-packaging
```

## Required Release Artifacts

REL-7 must add or update:

- v0.1 Local Alpha runbook
- Fresh-machine setup guide
- Known limitations
- Troubleshooting guide
- Release evidence index
- Package manifest
- Package creation/check script
- Release receipt

Suggested paths:

```text
Docs/release/v0.1-local-alpha/README.md
Docs/release/v0.1-local-alpha/RUNBOOK.md
Docs/release/v0.1-local-alpha/SETUP.md
Docs/release/v0.1-local-alpha/TROUBLESHOOTING.md
Docs/release/v0.1-local-alpha/KNOWN_LIMITATIONS.md
Docs/release/v0.1-local-alpha/EVIDENCE_INDEX.md
Docs/release/v0.1-local-alpha/PACKAGE_MANIFEST.md
Scripts/release/package-alpha-local.ps1
Docs/receipts/REL7_RELEASE_DOCS_AND_PACKAGING.md
```

## Required Runbook Flow

The runbook must describe the happy path in exact commands:

```text
Clone or update repo.
Run release doctor.
Prepare local override.
Prepare SQL if needed.
Prepare Weaviate if needed or confirm not required.
Start LocalTest API/UI.
Run deterministic alpha smoke.
Open the UI journey if REL-5.5 is available.
Collect release evidence.
Read known limitations before declaring alpha suitability.
```

The runbook must distinguish:

- Deterministic service/API smoke
- Product UI journey
- Live-model draft smoke
- Local setup readiness
- Packaging evidence

## Package Manifest Rules

The package manifest must list:

- Included docs
- Included scripts
- Included source paths required to run the alpha
- Included test commands
- Included sample fixture paths
- Evidence files included by reference
- Explicit exclusions
- Required external tools
- Optional external tools
- Known unproven paths

The manifest must not include:

- Secrets
- Local override files
- Raw user-local paths
- `node_modules`
- Build output
- Test result output
- Local SQL databases
- Local Weaviate data
- Dogfood run folders unless explicitly redacted and intended
- Generated release zips committed to git

## Package Script Rules

If a package script is added, it must:

- Support `-CheckOnly`.
- Support `-OutputDirectory`.
- Refuse output directories under the repository by default.
- Write a manifest/check summary.
- Include only whitelisted docs/scripts/source paths.
- Exclude local override/config secret files.
- Exclude build output and dependency folders.
- Redact machine/user paths in summaries.
- Produce a hash for the generated package.
- State that package creation is evidence only.

The script must not:

- Build the product unless explicitly requested.
- Run smoke tests unless explicitly requested.
- Start API, UI, SQL, Weaviate, Docker, or live model providers.
- Push, publish, release, deploy, tag, or upload artifacts.

## Required Docs Honesty

Docs must state:

- v0.1 Local Alpha is local-only.
- CI green is evidence, not release authority.
- Smoke success is evidence, not approval or deployment readiness.
- REL-4 live-model smoke is opt-in draft evidence only.
- REL-5 chat-confirmed ticket path stops at the approval gate unless later slices prove continuation/apply through UI.
- REL-5.5 UI journey, if not merged, is not part of the package truth.
- Fresh-machine readiness depends on REL-6 evidence.
- Known limitations are release-blocking unless explicitly descoped.

## Required Evidence Index

The evidence index must name current proof sources:

- Governance boundary CI
- Fast unit CI
- SQL integration CI
- Full SQL integration CI
- Frontend contract CI
- SkeletonRun CI
- D-series deterministic smoke
- REL-2 service-level `Applied` path
- REL-3 SQL/API persisted `Applied` path
- REL-4 live-model ticket-draft path
- REL-5 chat-confirmed ticket to gate path
- REL-6 fresh-machine doctor proof, if available

Every evidence entry must include:

- Scope
- Command or workflow
- What it proves
- What it does not prove
- Where to find its receipt or report

## Tests Required

Docs/package contract tests:

```text
ReleaseDocs_RunbookContainsExactCommands
ReleaseDocs_KnownLimitationsAreExplicit
ReleaseDocs_EvidenceIndexSeparatesProofFromAuthority
ReleaseDocs_PackageManifestExcludesSecretsAndLocalOverrides
ReleaseDocs_PackageManifestExcludesBuildOutputsAndDependencyFolders
ReleasePackage_CheckOnlyDoesNotWritePackage
ReleasePackage_RefusesRepoOutputDirectoryByDefault
ReleasePackage_WritesManifestAndHashWhenExplicitlyRequested
ReleasePackage_DoesNotStartServicesOrRunMutation
```

Secret/path safety tests:

```text
ReleaseDocs_DoNotContainRawConnectionStrings
ReleaseDocs_DoNotContainFullUserLocalPaths
ReleasePackageSummary_RedactsUserAndMachinePaths
```

## Receipt

Path:

```text
Docs/receipts/REL7_RELEASE_DOCS_AND_PACKAGING.md
```

The receipt must include:

- Commit SHA
- Docs added/updated
- Package manifest path
- Package script path, if added
- Package check command and result
- Output directory used, redacted if user-local
- Package hash, if a package was created
- Explicit exclusions verified
- Known limitations carried forward
- Boundary statement

## Acceptance Criteria

- A developer can read `Docs/release/v0.1-local-alpha/README.md` and find the complete local alpha path.
- The runbook contains exact commands and does not require tribal knowledge.
- The package manifest lists included and excluded material.
- Package check mode proves the package would exclude secrets, local overrides, dependency folders, build outputs, and local databases.
- Evidence is indexed by what it proves and what it does not prove.
- No generated package archive is committed to git.

## Review Traps

Block if:

- Docs claim release readiness without naming evidence.
- Docs blur CI success into release authority.
- Package manifest includes local override files, secrets, build output, `node_modules`, or local data stores.
- Package script writes under the repo by default.
- Package script starts services or mutates local setup in check-only mode.
- Known limitations are softened into marketing language.
- REL-5.5 or REL-6 proof is implied before it exists.

## Out Of Scope

- Installer.
- Signed releases.
- Public distribution.
- Deployment.
- Auto-update.
- Production packaging.
- Live model release proof beyond existing opt-in draft smoke.

## Next PR

REL-8 local alpha dogfood transcript and final go/no-go verdict.
