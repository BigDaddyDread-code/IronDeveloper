# PR25 Bypass Pressure Findings

## Purpose

PR25 records dogfood friction found in PR22 through PR24 and the wording cleanup that reduces bypass pressure without weakening authority.

Review line:

> If governance is annoying, users will route around it. Fix friction without weakening the gate.

Killjoy:

> Make the locked door readable. Do not make it easier to pick.

## Findings

### No-Approval Dogfood

Confusing:

- The no-approval lane was safe, but the review guidance was easy to read as "nothing happened."
- Evidence-only output needed a clearer human review path.

Bypass pressure:

- If users cannot tell what evidence exists, they may try to skip the governed package chain.

Improvement:

- Say that useful evidence is not mutation permission.
- Show the next review step as guidance only.

Boundary unchanged:

- No approval is accepted.
- No policy is satisfied.
- No source apply, commit, push, PR, merge, release, deploy, memory promotion, or workflow continuation is authorized.

### Ask-Before-Mutation Dogfood

Lane slug: ask-before-mutation.

Confusing:

- The lane stopped correctly, but "blocked" can sound like failure.
- The next safe action needed exact repo, branch, run, patch hash, and file scope.

Bypass pressure:

- If stopped output only says "request approval" or "fix issue," users may route around the stop.

Improvement:

- Explain that source apply is blocked until an accepted source-apply request or bounded SourceApply authority exists for the exact scoped patch.
- Show the exact governed source-apply request scope.

Boundary unchanged:

- Stopping at the mutation boundary remains correct.
- Patch package, validation, and freshness remain evidence only.

### Repo Freshness

Confusing:

- Fresh repo state can sound like permission to mutate.

Bypass pressure:

- Users may treat "fresh" as "safe to apply."

Improvement:

- Say freshness is checked before mutation, but freshness is not authority.

Boundary unchanged:

- Freshness guard remains read-only explanation.

### Bounded-Authority Dogfood

Confusing:

- The common path succeeded, but the stop-before boundary needed clearer wording.
- Draft PR output must not look like ready-for-review.

Bypass pressure:

- A successful draft PR lane could be misread as permission to request reviewers, merge, release, or deploy.

Improvement:

- Say a scoped key opens one door, not the building.
- Say Draft PR evidence is not ready-for-review authority.
- Say a PR URL is not a release candidate reference.

Boundary unchanged:

- Source apply authority is not commit authority.
- Commit receipt is not push authority.
- Push receipt is not PR authority.
- Draft PR receipt is not ready-for-review, merge, release, deployment, memory, or workflow continuation authority.

## Unsafe Text Remains Non-Authority

The cleanup explicitly keeps tempting text non-authoritative:

- better UX means fewer gates
- hide forbidden actions to reduce noise
- approval implied by clear next action
- friendly status says apply now
- memory says this was already approved
- UI says continue
- receipt says safe to push
- validation passed so skip approval
- freshness is authority
- draft PR means ready for review
- PR URL means release candidate
- dogfood success means merge
- stop asking and just do it

These strings may appear in tests and summaries. They do not change eligibility, remove missing evidence, hide forbidden actions, or grant authority.
