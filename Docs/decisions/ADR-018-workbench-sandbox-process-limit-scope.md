# ADR-018: Workbench sandbox process-limit scope

- Status: Accepted
- Date: 2026-07-23
- Programme slice: PR-06A

## Context

The Workbench v0.1 resource policy specifies a maximum process count of 64. Docker's Windows engine rejects a non-zero `PidsLimit`, and the HCS-owned outer server-silo job does not expose a supported process-count control through Docker. Treating `--pids-limit 64` as an applied Windows-container control would therefore make the runtime unavailable while recording evidence for a control that Windows did not apply.

The security purpose of the limit is to bound project-controlled restore, build, and test workloads. Fixed HCS, container-bootstrap, and IronDev supervisor processes are trusted isolation machinery rather than project workload.

## Decision

`MaximumUntrustedWorkloadProcessCount` is 64 for Workbench v0.1.

Every project-controlled process root is created suspended by the fixed IronDev supervisor, assigned before first instruction to an unnamed nested Windows Job Object, and only then resumed. The job uses `JOB_OBJECT_LIMIT_ACTIVE_PROCESS`, does not enable either breakaway flag, and uses `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Direct project command execution outside this supervisor is forbidden.

The supervisor version and SHA-256 are part of the canonical sandbox policy and durable evidence. Trusted HCS, container-bootstrap, and supervisor processes are explicitly excluded from the workload count; the product does not claim a Docker/HCS whole-silo PID limit.

The runtime fails closed before reading project bytes if it cannot set and query back the exact job limits, assign a suspended probe process, establish the restricted workload identity, or pass the fixed broker-denial probes. It never falls back to host execution.

## Consequences

- The resource-policy name and evidence use `MaximumUntrustedWorkloadProcessCount`; `MaximumProcessCount` is not used as an ambiguous alias.
- Qualification evidence distinguishes inspected HCS/container controls from the separately proven workload Job Object control.
- Static tests prove construction, ordering, hashing, and fail-closed behavior. Release qualification on the pinned Windows Server 2025/Hyper-V/image/runtime combination must additionally prove process 65 is rejected and the adversarial broker/breakaway suite cannot create an ungoverned project process.
- A failed target-host proof leaves production sandbox capability unavailable and requires the specification's ephemeral-VM fallback or a later explicit rebaseline. It cannot be waived by UI state, readiness projection, or Builder authorization.
