# BT — Authority Profile Status Mapping

## Review Line

A blocked authority state must explain the missing permission and the next safe action.

## Boundary

This PR adds authority profile status mapping only.

It does not add a runner.
It does not execute commands.
It does not issue grants.
It does not store grants.
It does not mutate source.
It does not apply patches.
It does not create approvals.
It does not satisfy policy.
It does not run validation.
It does not create validation evidence.
It does not promote memory.
It does not continue workflow.
It does not add frontend/API/CLI.
It does not add source apply execution.
It does not create global authority.
It does not create cross-repo authority.
It does not accept memory-supplied authority.

Canonical status explains missing permission and next safe action.
Status is not authority.
Eligible status is necessary but not sufficient.

## Mapping

ProposalOnly plus durable mutation maps to Blocked.
AskBeforeMutation plus patch readiness maps to Blocked until explicit human apply approval evidence is present.
BoundedRunAuthority maps to Eligible only when the submitted operation eligibility decision is eligible and the grant is not expired.
Push requires separate bounded authority and is not inherited from source apply eligibility.
Expired bounded grants map to Expired and override otherwise eligible input.

## Killjoy

A blocked authority state must explain the missing permission and the next safe action.
