# Canonical Governed Refusal Envelope

**Status:** Canonical cleanup contract

**Programme slice:** CLN-13

Governed HTTP actions use one refusal shape:

```text
Allowed
ReasonCode
Message
BlockedReasons
MissingEvidence
NextSafeActions
ForbiddenActions
CorrelationId
```

`Allowed` is always false for a refusal. Every array is present, including when empty. `ReasonCode` is stable machine-readable truth; `Message` and the arrays are safe UI guidance. `CorrelationId` binds the refusal to logs and durable attribution.

Existing continuation and release-gate envelopes expose the canonical shape in an additive `Refusal` member so established clients retain their current status, data, errors, warnings, and boundary fields. The route/body scope guard returns the canonical shape directly because it blocks dispatch before a domain envelope exists.

Ordinary authentication, not-found, and input-validation responses are not governed-action refusals and are outside this contract.
