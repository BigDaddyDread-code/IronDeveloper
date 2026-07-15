# Non-Author Operator Walk

**Status:** Canonical qualification gate

**Required tester:** a person who did not build the system or the qualification slice.

## Start Rule

Give the tester only the repository README, `Docs/cleanup/CLEAN_CLONE_QUALIFICATION.md` once CLN-42 is merged, and `Docs/product/V2_NON_AUTHOR_QUALIFICATION.md`. The author may observe but must not provide hidden IDs, SQL, filesystem edits, undocumented commands, fixture knowledge, or recovery steps.

## Required Record

| Category | Observation | Exact step | Expected remedy | Actual remedy | Blocking? |
| --- | --- | --- | --- | --- | --- |
| Dead end |  |  |  |  |  |
| Missing remedy |  |  |  |  |  |
| Undocumented assumption |  |  |  |  |  |
| Manual database intervention |  |  |  |  |  |
| Manual filesystem intervention |  |  |  |  |  |
| Author-only knowledge |  |  |  |  |  |

Record tester identity, commit, date/time, machine profile, screenshots/video, relevant run IDs, correlation IDs, receipts, support bundle reference, and final `PASS` or `FAIL`.

## Pass Rule

The tester must complete the clean-clone LocalTest journey using visible product actions and documented commands. Any author-only recovery, manual SQL, manual filesystem repair, hidden identifier injection, unexplained dead end, or missing actionable remedy is `FAIL` and blocks cleanup completion. A later fix requires a new non-author walk; the author cannot self-certify the remediation.
