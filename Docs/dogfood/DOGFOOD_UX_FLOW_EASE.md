# DOGFOOD-UX Flow Ease Protocol

**Status:** Required for every DOGFOOD-UX attempt

This protocol measures whether a capable operator can move from an idea to reviewed working code through the visible IronDev product without becoming a product archaeologist.

Architecture correctness and green CI are prerequisites. They are not evidence that the product journey is usable.

## Review line

Codex uses IronDev as a user during an attempt. It does not repair IronDev as its author.

## Killjoy line

If the operator bypasses the visible product to finish the task, the bypass is the finding.

## Attempt boundary

Each new project retry receives a new campaign attempt ID and begins at sign-in. An attempt ends as exactly one of:

- `Completed`
- `CompletedWithWorkaround`
- `Blocked`

A product fix, environment reset, different IronDev commit, or return to an earlier failed project creates a new attempt. Evidence from failed and replacement attempts must not be merged.

Every attempt stores:

```text
artifacts/dogfood-ux/<campaign-id>/<project>/<attempt-id>/
  manifest.json
  attempt.json
  operator-log.md
  screenshots/
  findings.json
  ...the remaining campaign evidence
```

Initialize the evidence package so the manifest, score record, findings record, and operator log are created together:

```powershell
.\tools\dogfood\New-DogfoodUxAttempt.ps1 `
  -CampaignId dogfood-ux-1-<date>-<commit> `
  -AttemptId dogfood-ux-1-<date>-<commit>-tinycalc `
  -Project TinyCalc `
  -IronDevCommit <commit>
```

The initializer writes only beneath `artifacts/dogfood-ux`, refuses unsafe path segments, and never overwrites an existing attempt. `manifest.json` references both `attempt.json` and `findings.json`. `attempt.json` conforms to [the attempt schema](../../tools/dogfood/dogfood-ux/attempt.schema.json), while `findings.json` conforms to [the findings schema](../../tools/dogfood/dogfood-ux/findings.schema.json), and the package passes:

```powershell
.\tools\dogfood\Test-DogfoodUxAttempt.ps1 `
  -Path .\artifacts\dogfood-ux\<campaign-id>\<project>\<attempt-id>\attempt.json
```

For real `AttemptEvidence`, the validator resolves the sibling `findings.json` automatically. Validation fixtures must pass an explicit `-FindingsPath` so fixture evidence cannot be confused with campaign evidence.

The validator calculates expected values independently and refuses a stored score, count, timing summary, severity cap, or progression decision that does not match the retained transitions, deviations, findings, and timestamp boundary.

## Evidence to record

Measure for every attempt:

| Measure | Meaning |
| --- | --- |
| Task completion | Completed, completed with workaround, or blocked |
| Time to next meaningful action | Seconds until the operator knows what to do next |
| Wall-clock elapsed time | Timestamp interval from sign-in through final outcome, including pauses |
| Active journey time | Product, governance, and recovery time while the attempt is active |
| Paused time | Explicitly excluded wall-clock time when the attempt is not active |
| Product work | Shaping, reviewing, testing, and deciding |
| Required governance ceremony | Deliberate review, disposition, approval, and apply boundaries |
| Product archaeology/recovery | Searching, retrying, interpreting errors, and recovering |
| Actions | Clicks, forms, confirmations, and route changes |
| Backtracks | Returns to an earlier screen to recover or rediscover context |
| Dead ends | Screens with no usable next action |
| Refusals with remedies | Refusals that explain the exact safe recovery action |
| Hidden knowledge | IDs, commands, endpoints, paths, or facts not visible in the product |
| Workarounds | Direct API, SQL, filesystem, or undocumented command use |
| Operator confidence | Whether the current state and next decision are understood |

Governance ceremony is not automatically friction. Looking for a hidden endpoint is.

## Required transition record

Every major transition records:

- timestamp in UTC;
- stage and current screen;
- operator intent;
- action taken;
- expected outcome;
- actual outcome;
- seconds to the next meaningful action;
- whether the next action was visible;
- whether it was known within five seconds;
- whether backtracking was required;
- whether contextual help, external documentation, or hidden knowledge was required;
- whether the transition was a useful refusal, unhelpful refusal, dead end, or none;
- ease score from 1 to 7.

The retained `operator-log.md` uses the same fields as `attempt.json`; prose may explain evidence, but it cannot replace the structured record.

## Stage ratings

After each reached major stage, record:

| Rating | Scale | Healthy target |
| --- | ---: | ---: |
| Ease | 1-7 | at least 5 average; no major step below 4 |
| Flow clarity | 1-7 | at least 5 |
| Help usefulness | 1-7 or not used | at least 5 when used |
| Bureaucracy felt | 1-7 | at most 3; lower is better |
| Confidence | 1-7 | at least 5 |

Ease scale:

```text
1  Extremely difficult
2  Very difficult
3  Difficult
4  Neither easy nor difficult
5  Easy
6  Very easy
7  Extremely easy
```

Required stages when reached:

```text
SignIn
SelectTenantOrProject
CreateProject
CompleteSetup
ShapeWorkItem
StartRun
UnderstandRunOutput
ReviewCriticFindings
DispositionFindings
Approve
Continue
Apply
InspectGovernance
InspectAudit
RecoverFromFailure
```

Blocked attempts rate every stage they reached, including the blocked stage. They do not invent ratings for stages never reached.

## Findings evidence

Final `AttemptEvidence` requires a sibling `findings.json`. It is a structured array; every entry records:

- a unique finding ID;
- project and screen/step;
- severity from `P0` through `P3`;
- observed and expected behavior;
- retained evidence references and reason codes;
- visible remedy and actual workaround, when present;
- authority impact, repeatability, and proposed owning slice.

The validator derives P0/P1/P2/P3 counts and the highest severity from this file, then compares them with `attempt.json`. Every deviation must reference a finding ID that exists in `findings.json`. A stored score cannot suppress a retained P0/P1 finding by editing only `attempt.json`.

## Flow efficiency

Record the wall-clock boundary, active journey, pauses, and three active-time buckets in seconds:

```text
wallClockElapsedSeconds
activeJourneySeconds
pausedSeconds
productWorkSeconds
governanceCeremonySeconds
archaeologyRecoverySeconds
```

`wallClockElapsedSeconds` is the `completedAtUtc - startedAtUtc` interval rounded to two decimal places. The validator requires:

```text
wallClockElapsedSeconds = activeJourneySeconds + pausedSeconds
activeJourneySeconds =
  productWorkSeconds
  + governanceCeremonySeconds
  + archaeologyRecoverySeconds
```

```text
flowEfficiency =
  (productWorkSeconds + governanceCeremonySeconds)
  / activeJourneySeconds
```

The stored efficiency is rounded to four decimal places. A final attempt with zero active time is invalid. Reports still expose wall-clock elapsed and paused time, so pauses cannot disappear from the retained journey evidence.

## Flow Ease Score

The score is out of 100 and is calculated from evidence, not manually chosen.

| Component | Weight | Calculation |
| --- | ---: | --- |
| Task completion | 30 | `Completed` = 30; `CompletedWithWorkaround` = 15; `Blocked` = 0 |
| No undocumented workaround | 20 | no deviation = 20; deviations present but all explicitly documented = 10; any hidden or undocumented deviation = 0 |
| Clear next actions | 15 | proportion of transitions where the next action was visible and the operator knew it within five seconds |
| Useful refusal/recovery | 15 | proportion of refusal/dead-end opportunities classified as a useful refusal; 15 when no recovery opportunity occurred |
| Efficient journey | 10 | `flowEfficiency * 10` |
| Operator confidence | 10 | average confidence normalized from 1-7 onto 0-10 using `(average - 1) / 6 * 10` |

Component and total scores are rounded to two decimal places.

If the attempt has a P0 or P1 finding, the final score is `min(rawScore, 59)`. The retained score must state whether this severity cap was applied.

Score bands:

| Score | Band |
| ---: | --- |
| 90-100 | Smooth |
| 75-89.99 | UsableWithMinorFriction |
| 60-74.99 | Difficult |
| 40-59.99 | SeriouslyObstructed |
| 0-39.99 | BrokenCorridor |

P0/P1 attempts can never be scored above `SeriouslyObstructed`, even if individual screens looked polished.

## Deviations and hidden rescue

Every workaround is a structured deviation with:

- kind: `DirectApi`, `DirectSql`, `ManualFilesystem`, `UndocumentedCommand`, or `Other`;
- occurrence count;
- whether it was documented before use;
- reason;
- related finding ID.

Direct investigation after a visible route fails may be allowed by campaign review. It remains a deviation and cannot be represented as product-path recovery.

The following count as hidden rescue for TinyCalc progression:

- hidden IDs or author-only facts;
- direct API calls;
- direct SQL operations;
- manual filesystem operations;
- undocumented commands.

## TinyCalc progression gate

BookSeller must not start until a fresh TinyCalc attempt satisfies all of:

- outcome is `Completed`;
- zero deviations/workarounds;
- no P0 or P1 findings;
- average ease is at least 5;
- no reached major stage has ease below 4;
- average flow clarity is at least 5;
- average help usefulness is at least 5 when help was used;
- average bureaucracy felt is at most 3;
- average confidence is at least 5;
- flow efficiency is at least 0.75;
- zero hidden IDs, API calls, SQL operations, filesystem operations, or undocumented commands.

`attempt.json` records each predicate and `eligibleToProceed`. The validator recomputes the conjunction. Campaign authority still belongs to human review; a true value is evidence for the decision, not permission to proceed automatically.

## UX observations

Every screen should answer:

1. Where am I?
2. What needs my attention?
3. What is the obvious next action?

Campaign observations must check these rules:

- one dominant primary action on a blocked screen;
- required actions and recovery remain visible in the main flow;
- hover/focus/click help explains terms but never hides the remedy;
- contextual help identifies the exact blocked role or configuration;
- technical IDs, hashes, reason codes, HTTP verbs, JSON, database rows, and internal mechanics stay in technical details unless they directly help the operator;
- following a remedy preserves a return path to the originating Work Item or Board;
- early journey language is `Set up project`, `Shape the work`, `Build`, `Review`, `Approve`, and `Apply`;
- precise authority boundaries appear at the decision where they matter;
- Workshop suggestions remain editable and never silently create a Work Item or grant readiness.

Tooltips explain. The main screen still tells the operator what to do.

## Campaign stop and review

Stop immediately for P0/P1 safety, authority, tenant/project isolation, false-green, manual SQL, or filesystem-surgery findings. Capture evidence and end the attempt as `Blocked` unless a reviewer explicitly authorizes a documented investigative deviation.

After each project, present completion, friction, deviations, findings, evidence gaps, Flow Ease Score, wall-clock/active/paused time, flow efficiency, stage ratings, and the computed progression predicates. The human reviewer decides `Proceed`, `Repeat`, `Fix before proceeding`, or `Unsupported`.

Governance should be felt as confidence, not experienced as paperwork.
