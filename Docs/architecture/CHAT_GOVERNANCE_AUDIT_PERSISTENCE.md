# Chat Governance Audit Persistence

Status: Slice 4 durability note

## Guarantee

New assistant chat turns saved through the governed `SaveMessageAsync` path and carrying a v1 governance envelope are saved as one logical database write:

- `ChatMessages`
- `ChatTurnGovernance`
- `ChatTurnClarifications`
- `ChatTurnTraces`

The message row and normalized audit rows are written inside the same database transaction. If any normalized audit row write fails, the message insert is rolled back with the audit rows.

## Runtime schema changes

`ChatTurnPersistenceService` must not create, alter, or probe audit table schema at runtime. Audit tables are expected to exist before the write path runs.

## Delete and reinsert behavior

When replacing normalized audit rows for a message, delete and insert operations run inside the same transaction as the message save.

## Audit source labels

Audit responses label their source explicitly:

- `NormalizedRows`: normalized audit rows were read.
- `TagsFallback`: fallback evidence was reconstructed from tags.

Fallback data is compatibility evidence, not equivalent to normalized durable audit rows.
