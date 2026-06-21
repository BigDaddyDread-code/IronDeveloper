# BK - Controlled Source Apply Canonical Status

## Purpose

This slice maps controlled source apply outcomes into canonical GovernedOperationStatus.

It is a status/read-model mapping only.

## Boundary

Source apply status can explain:

- blocked source apply
- missing authority or evidence
- eligible source apply
- running source apply
- completed source apply
- failed source apply
- expired or stale source apply
- next safe action
- forbidden actions

Source apply status cannot approve.
Source apply status cannot satisfy policy.
Source apply status cannot execute.
Source apply status cannot mutate source.
Source apply status cannot commit.
Source apply status cannot push.
Source apply status cannot create PRs.
Source apply status cannot merge.
Source apply status cannot release.
Source apply status cannot deploy.
Source apply status cannot promote memory.
Source apply status cannot execute rollback.
Source apply status cannot continue workflow.

Eligible status is explanation, not execution authority.
Eligible status requires refs that explain eligibility.
Eligible status requires accepted source-apply request, policy-satisfaction, dry-run, patch artifact, rollback support, and worktree-state refs.
Eligible status requires a policy-satisfaction ref as explanatory evidence.
Policy satisfaction remains reference-only unless the relevant evaluator accepts it.
Completed source apply is not commit authority.
Completed source apply is not push authority.
Completed source apply is not PR authority.
Completed source apply is not workflow continuation authority.
Completed status requires a source-apply-receipt reference.
A source apply receipt is not rollback execution authority.

Patch proposal refs are evidence only.
Patch hash refs are evidence only.
Dry-run refs are evidence only.
Policy satisfaction refs remain references unless the relevant evaluator accepts them.
Receipt refs remain references and do not create new authority.
NextSafeActions are guidance, not permission.

## Mapping

```text
Blocked   -> Blocked
Eligible  -> Eligible
Running   -> Running
Completed -> Completed
Failed    -> Failed
Expired   -> Expired
```

The mapper writes OperationKind = SourceApply and validates every mapped status through GovernedOperationStatusValidator.

## Review Line

Source apply status can explain eligibility. It cannot execute source apply.

## Killjoy

Source apply status can show the loaded gate. It cannot pull the lever.
