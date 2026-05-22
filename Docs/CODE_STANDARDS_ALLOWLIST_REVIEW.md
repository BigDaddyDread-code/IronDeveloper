# Code Standards Allowlist Review

## Purpose

This review records how to treat remaining code standards allowlist entries during Alpha.

## Current Policy

Allowlisted warnings are intentional debt, not ignored debt.

Each allowlist entry should be classified as:

- Keep for Alpha.
- Extract soon.
- Remove now.
- Replace with better rule.

## Current Assessment

ReplayRunner and dogfood command code still contains some large procedural surfaces. This is acceptable while proof slices are moving quickly, but it must remain visible.

The highest-value extraction targets remain:

- Remaining memory smoke handlers in `Program.cs`.
- Repeated dogfood plan wrapper patterns.
- Large deterministic agent command handlers.

## Rule

Warning count must not increase without a reason.

## Boundary

This review does not perform broad extraction. Cleanup should happen in separate focused PRs.

