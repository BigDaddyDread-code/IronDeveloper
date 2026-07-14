# CLN-26 Memory Retrieval Security Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Delivered

- Added an explicit retrieval security context carrying tenant, project, actor, consumer, allowed authority classes, and as-of time through API and internal chat calls.
- Enforced actor/project membership in both the chat pipeline and prompt builder before memory retrieval.
- Removed the invalid universal five-year cutoff in favour of lifecycle, as-of, authority-class, and consumer checks.
- Added current `memory.vw_CurrentProjectCanonMemory` assembly as governed `Binding` context.
- Excluded legacy context rows self-labelled `Binding` or `StrongGuidance`; only explicitly allowed observation/context classes remain eligible.
- Escaped stored memory and wrapped it as untrusted quoted data.
- Added explicit prompt-injection isolation instructions.
- Added SQL-backed assembly and refusal tests covering governed canon inclusion, legacy authority exclusion, other-tenant exclusion, and unauthorized internal retrieval.

## Boundary

This slice secures current SQL-backed prompt assembly. Existing semantic evidence remains a derived quoted-evidence lane; automatic semantic/vector candidate-to-authority injection remains disabled. This slice does not grant new consumer capabilities, promote candidates, or enable automatic memory authority elsewhere.
