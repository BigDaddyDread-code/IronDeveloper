[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [string]$FindingsPath,

    [switch]$AllowInProgress,

    [switch]$PassThru
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Property {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) {
        throw "$Context is missing required property '$Name'."
    }
}

function Assert-Number {
    param(
        [Parameter(Mandatory = $true)][double]$Actual,
        [Parameter(Mandatory = $true)][double]$Expected,
        [Parameter(Mandatory = $true)][string]$Label,
        [double]$Tolerance = 0.005
    )

    if ([Math]::Abs($Actual - $Expected) -gt $Tolerance) {
        throw "$Label is $Actual; expected $Expected."
    }
}

function Assert-Value {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Actual -ne $Expected) {
        throw "$Label is '$Actual'; expected '$Expected'."
    }
}

function Assert-NonNegativeInteger {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -isnot [int] -and $Value -isnot [long]) {
        throw "$Label must be an integer."
    }

    if ([long]$Value -lt 0) {
        throw "$Label must not be negative."
    }
}

function Assert-Rating {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Assert-NonNegativeInteger -Value $Value -Label $Label
    if ([int]$Value -lt 1 -or [int]$Value -gt 7) {
        throw "$Label must be from 1 to 7."
    }
}

function Assert-NonEmptyString {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        throw "$Label must be a non-empty string."
    }
}

function Assert-StringArray {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Value,
        [Parameter(Mandatory = $true)][string]$Label,
        [switch]$RequireItem
    )

    if ($Value -is [string] -or $Value -isnot [array]) {
        throw "$Label must be an array of strings."
    }

    $items = @($Value)
    if ($RequireItem -and $items.Count -eq 0) {
        throw "$Label must retain at least one item."
    }
    foreach ($item in $items) {
        Assert-NonEmptyString -Value $item -Label "$Label item"
    }
    $duplicate = $items | Group-Object | Where-Object Count -gt 1 | Select-Object -First 1
    if ($null -ne $duplicate) {
        throw "$Label contains duplicate item '$($duplicate.Name)'."
    }
}

function Round-Score {
    param([Parameter(Mandatory = $true)][double]$Value)
    return [Math]::Round($Value, 2, [MidpointRounding]::AwayFromZero)
}

function Get-OccurrenceSum {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Deviations,
        [string]$Kind
    )

    $selected = if ([string]::IsNullOrWhiteSpace($Kind)) {
        @($Deviations)
    }
    else {
        @($Deviations | Where-Object { $_.kind -eq $Kind })
    }

    if (@($selected).Count -eq 0) {
        return 0
    }

    return [int](($selected | Measure-Object -Property occurrences -Sum).Sum)
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "DOGFOOD-UX attempt file not found: $Path"
}

$attempt = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
$requiredTopLevel = @(
    "schemaVersion",
    "recordKind",
    "campaignId",
    "attemptId",
    "project",
    "ironDevCommit",
    "startedAtUtc",
    "completedAtUtc",
    "outcome",
    "highestFindingSeverity",
    "findingCounts",
    "timing",
    "counts",
    "deviations",
    "transitions",
    "stageRatings",
    "score",
    "tinyCalcAcceptance"
)

foreach ($name in $requiredTopLevel) {
    Assert-Property -Object $attempt -Name $name -Context "DOGFOOD-UX attempt"
}

Assert-Value -Actual $attempt.schemaVersion -Expected "1.0" -Label "schemaVersion"
if ($attempt.recordKind -notin @("AttemptEvidence", "ValidationFixture")) {
    throw "recordKind must be AttemptEvidence or ValidationFixture."
}

if ([string]::IsNullOrWhiteSpace($FindingsPath)) {
    if ($attempt.recordKind -eq "ValidationFixture") {
        throw "ValidationFixture records must explicitly supply -FindingsPath."
    }

    $attemptDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
    $FindingsPath = Join-Path $attemptDirectory "findings.json"
}
if (-not (Test-Path -LiteralPath $FindingsPath -PathType Leaf)) {
    throw "DOGFOOD-UX findings file not found: $FindingsPath"
}

$findingsJson = Get-Content -LiteralPath $FindingsPath -Raw
if ([string]::IsNullOrWhiteSpace($findingsJson) -or $findingsJson.TrimStart()[0] -ne '[') {
    throw "DOGFOOD-UX findings evidence must be a JSON array."
}

$parsedFindings = $findingsJson | ConvertFrom-Json
$findings = @($parsedFindings)
$requiredFindingProperties = @(
    "findingId",
    "project",
    "screenStep",
    "severity",
    "observedBehavior",
    "expectedBehavior",
    "evidence",
    "reasonCodes",
    "visibleRemedy",
    "actualWorkaround",
    "authorityImpact",
    "repeatability",
    "proposedOwningSlice"
)
$findingIds = @{}
foreach ($finding in $findings) {
    foreach ($name in $requiredFindingProperties) {
        Assert-Property -Object $finding -Name $name -Context "finding"
    }

    $unexpectedProperties = @($finding.PSObject.Properties.Name | Where-Object { $_ -notin $requiredFindingProperties })
    if ($unexpectedProperties.Count -gt 0) {
        throw "Finding '$($finding.findingId)' contains unsupported property '$($unexpectedProperties[0])'."
    }

    foreach ($name in @(
        "findingId",
        "project",
        "screenStep",
        "observedBehavior",
        "expectedBehavior",
        "authorityImpact",
        "repeatability",
        "proposedOwningSlice"
    )) {
        Assert-NonEmptyString -Value $finding.$name -Label "finding.$name"
    }
    if ($finding.severity -notin @("P0", "P1", "P2", "P3")) {
        throw "Finding '$($finding.findingId)' has unknown severity '$($finding.severity)'."
    }
    Assert-StringArray -Value $finding.evidence -Label "finding.evidence" -RequireItem
    Assert-StringArray -Value $finding.reasonCodes -Label "finding.reasonCodes"
    foreach ($nullableStringName in @("visibleRemedy", "actualWorkaround")) {
        if ($null -ne $finding.$nullableStringName) {
            Assert-NonEmptyString -Value $finding.$nullableStringName -Label "finding.$nullableStringName"
        }
    }

    $findingId = [string]$finding.findingId
    if ($findingId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
        throw "finding.findingId must be one safe identifier containing only letters, digits, dot, underscore, or hyphen."
    }
    if ($findingIds.ContainsKey($findingId)) {
        throw "findings.json contains duplicate findingId '$findingId'."
    }
    $findingIds[$findingId] = $finding
}

foreach ($name in @("campaignId", "attemptId", "project", "ironDevCommit", "startedAtUtc")) {
    if ([string]::IsNullOrWhiteSpace([string]$attempt.$name)) {
        throw "$name must not be empty."
    }
}

$startedAt = [DateTimeOffset]::Parse([string]$attempt.startedAtUtc)

if ($attempt.outcome -eq "InProgress") {
    if (-not $AllowInProgress) {
        throw "InProgress attempts are not final evidence. Complete the attempt or pass -AllowInProgress for template validation."
    }

    if ($null -ne $attempt.completedAtUtc -or $null -ne $attempt.score -or $null -ne $attempt.tinyCalcAcceptance) {
        throw "InProgress attempts must leave completedAtUtc, score, and tinyCalcAcceptance null."
    }

    Write-Host "DOGFOOD-UX in-progress structure accepted: $($attempt.attemptId)"
    if ($PassThru) {
        [pscustomobject]@{
            AttemptId = $attempt.attemptId
            Outcome = $attempt.outcome
            Final = $false
        }
    }
    return
}

if ($attempt.outcome -notin @("Completed", "CompletedWithWorkaround", "Blocked")) {
    throw "Final outcome must be Completed, CompletedWithWorkaround, or Blocked."
}

if ($null -eq $attempt.completedAtUtc) {
    throw "Final attempt must record completedAtUtc."
}
$completedAt = [DateTimeOffset]::Parse([string]$attempt.completedAtUtc)
if ($completedAt -lt $startedAt) {
    throw "completedAtUtc cannot be earlier than startedAtUtc."
}

$transitions = @($attempt.transitions)
$ratings = @($attempt.stageRatings)
$deviations = @($attempt.deviations)
$validStages = @(
    "SignIn",
    "SelectTenantOrProject",
    "CreateProject",
    "CompleteSetup",
    "ShapeWorkItem",
    "StartRun",
    "UnderstandRunOutput",
    "ReviewCriticFindings",
    "DispositionFindings",
    "Approve",
    "Continue",
    "Apply",
    "InspectGovernance",
    "InspectAudit",
    "RecoverFromFailure"
)
if ($transitions.Count -eq 0) {
    throw "Final attempt must retain at least one transition."
}
if ($ratings.Count -eq 0) {
    throw "Final attempt must retain at least one stage rating."
}
if ($null -eq $attempt.score -or $null -eq $attempt.tinyCalcAcceptance) {
    throw "Final attempt must retain score and tinyCalcAcceptance."
}

foreach ($countName in @(
    "actions",
    "backtracks",
    "deadEnds",
    "refusals",
    "usefulRefusals",
    "hiddenKnowledgeEvents",
    "workarounds",
    "directApiCalls",
    "directSqlOperations",
    "manualFilesystemOperations",
    "undocumentedCommands"
)) {
    Assert-Property -Object $attempt.counts -Name $countName -Context "counts"
    Assert-NonNegativeInteger -Value $attempt.counts.$countName -Label "counts.$countName"
}

foreach ($severityName in @("p0", "p1", "p2", "p3")) {
    Assert-Property -Object $attempt.findingCounts -Name $severityName -Context "findingCounts"
    Assert-NonNegativeInteger -Value $attempt.findingCounts.$severityName -Label "findingCounts.$severityName"
}

if ([int]$attempt.counts.actions -lt $transitions.Count) {
    throw "counts.actions cannot be lower than the number of retained major transitions."
}

$transitionTimes = @()
foreach ($transition in $transitions) {
    foreach ($name in @(
        "timestampUtc",
        "stage",
        "currentScreen",
        "operatorIntent",
        "actionTaken",
        "expectedOutcome",
        "actualOutcome",
        "timeToNextMeaningfulActionSeconds",
        "nextActionVisible",
        "nextActionKnownWithinFiveSeconds",
        "backtrackRequired",
        "helpKind",
        "recoveryClassification",
        "easeScore"
    )) {
        Assert-Property -Object $transition -Name $name -Context "transition"
    }

    $transitionTimestamp = [DateTimeOffset]::Parse([string]$transition.timestampUtc)
    if ($transitionTimestamp -lt $startedAt -or $transitionTimestamp -gt $completedAt) {
        throw "Transition timestamp must fall within the attempt start/completion boundary."
    }
    if ($transition.stage -notin $validStages) {
        throw "Unknown transition stage '$($transition.stage)'."
    }
    if ($transition.helpKind -notin @("None", "ContextualHelp", "Documentation", "HiddenKnowledge")) {
        throw "Unknown transition helpKind '$($transition.helpKind)'."
    }
    if ($transition.recoveryClassification -notin @("None", "UsefulRefusal", "UnhelpfulRefusal", "DeadEnd")) {
        throw "Unknown transition recoveryClassification '$($transition.recoveryClassification)'."
    }
    foreach ($booleanName in @("nextActionVisible", "nextActionKnownWithinFiveSeconds", "backtrackRequired")) {
        if ($transition.$booleanName -isnot [bool]) {
            throw "transition.$booleanName must be true or false."
        }
    }
    if ($transition.recoveryClassification -eq "DeadEnd" -and $transition.nextActionVisible) {
        throw "A DeadEnd transition cannot claim that a usable next action was visible."
    }
    if ($transition.recoveryClassification -eq "UsefulRefusal" -and -not $transition.nextActionVisible) {
        throw "A UsefulRefusal transition must expose a visible recovery action."
    }
    if ([double]$transition.timeToNextMeaningfulActionSeconds -lt 0) {
        throw "Transition timeToNextMeaningfulActionSeconds must not be negative."
    }
    Assert-Rating -Value $transition.easeScore -Label "transition.easeScore"
    $transitionTimes += [double]$transition.timeToNextMeaningfulActionSeconds
}

$ratingStages = @($ratings | ForEach-Object { [string]$_.stage })
$duplicates = @($ratingStages | Group-Object | Where-Object Count -gt 1)
if ($duplicates.Count -gt 0) {
    throw "stageRatings must contain one rating per reached stage; duplicate: $($duplicates[0].Name)."
}

foreach ($rating in $ratings) {
    foreach ($name in @("stage", "ease", "flowClarity", "helpUsefulness", "bureaucracyFelt", "confidence")) {
        Assert-Property -Object $rating -Name $name -Context "stageRating"
    }
    if ($rating.stage -notin $validStages) {
        throw "Unknown stageRating stage '$($rating.stage)'."
    }
    Assert-Rating -Value $rating.ease -Label "$($rating.stage).ease"
    Assert-Rating -Value $rating.flowClarity -Label "$($rating.stage).flowClarity"
    Assert-Rating -Value $rating.bureaucracyFelt -Label "$($rating.stage).bureaucracyFelt"
    Assert-Rating -Value $rating.confidence -Label "$($rating.stage).confidence"
    if ($null -ne $rating.helpUsefulness) {
        Assert-Rating -Value $rating.helpUsefulness -Label "$($rating.stage).helpUsefulness"
    }
}

foreach ($stage in @($transitions | Select-Object -ExpandProperty stage -Unique)) {
    if ($stage -notin $ratingStages) {
        throw "Reached stage '$stage' is missing its stage rating."
    }

    $helpWasUsed = @($transitions | Where-Object {
        $_.stage -eq $stage -and $_.helpKind -in @("ContextualHelp", "Documentation")
    }).Count -gt 0
    $stageRating = $ratings | Where-Object stage -eq $stage | Select-Object -First 1
    if ($helpWasUsed -and $null -eq $stageRating.helpUsefulness) {
        throw "Stage '$stage' used help but did not rate helpUsefulness."
    }
}

$expectedBacktracks = @($transitions | Where-Object backtrackRequired).Count
$expectedDeadEnds = @($transitions | Where-Object recoveryClassification -eq "DeadEnd").Count
$expectedRefusals = @($transitions | Where-Object recoveryClassification -in @("UsefulRefusal", "UnhelpfulRefusal")).Count
$expectedUsefulRefusals = @($transitions | Where-Object recoveryClassification -eq "UsefulRefusal").Count
$expectedHiddenKnowledge = @($transitions | Where-Object helpKind -eq "HiddenKnowledge").Count
Assert-Value -Actual $attempt.counts.backtracks -Expected $expectedBacktracks -Label "counts.backtracks"
Assert-Value -Actual $attempt.counts.deadEnds -Expected $expectedDeadEnds -Label "counts.deadEnds"
Assert-Value -Actual $attempt.counts.refusals -Expected $expectedRefusals -Label "counts.refusals"
Assert-Value -Actual $attempt.counts.usefulRefusals -Expected $expectedUsefulRefusals -Label "counts.usefulRefusals"
Assert-Value -Actual $attempt.counts.hiddenKnowledgeEvents -Expected $expectedHiddenKnowledge -Label "counts.hiddenKnowledgeEvents"

foreach ($deviation in $deviations) {
    foreach ($name in @("kind", "occurrences", "documentedBeforeUse", "reason", "findingId")) {
        Assert-Property -Object $deviation -Name $name -Context "deviation"
    }
    if ($deviation.kind -notin @("DirectApi", "DirectSql", "ManualFilesystem", "UndocumentedCommand", "Other")) {
        throw "Unknown deviation kind '$($deviation.kind)'."
    }
    if ($deviation.documentedBeforeUse -isnot [bool]) {
        throw "deviation.documentedBeforeUse must be true or false."
    }
    Assert-NonNegativeInteger -Value $deviation.occurrences -Label "deviation.occurrences"
    if ([int]$deviation.occurrences -lt 1) {
        throw "deviation.occurrences must be at least 1."
    }
    if ([string]::IsNullOrWhiteSpace([string]$deviation.reason) -or [string]::IsNullOrWhiteSpace([string]$deviation.findingId)) {
        throw "Every deviation must name its reason and findingId."
    }
    if (-not $findingIds.ContainsKey([string]$deviation.findingId)) {
        throw "Deviation findingId '$($deviation.findingId)' does not exist in findings.json."
    }
}

$expectedWorkarounds = Get-OccurrenceSum -Deviations $deviations
$expectedDirectApi = Get-OccurrenceSum -Deviations $deviations -Kind "DirectApi"
$expectedDirectSql = Get-OccurrenceSum -Deviations $deviations -Kind "DirectSql"
$expectedFilesystem = Get-OccurrenceSum -Deviations $deviations -Kind "ManualFilesystem"
$expectedUndocumented = Get-OccurrenceSum -Deviations $deviations -Kind "UndocumentedCommand"
Assert-Value -Actual $attempt.counts.workarounds -Expected $expectedWorkarounds -Label "counts.workarounds"
Assert-Value -Actual $attempt.counts.directApiCalls -Expected $expectedDirectApi -Label "counts.directApiCalls"
Assert-Value -Actual $attempt.counts.directSqlOperations -Expected $expectedDirectSql -Label "counts.directSqlOperations"
Assert-Value -Actual $attempt.counts.manualFilesystemOperations -Expected $expectedFilesystem -Label "counts.manualFilesystemOperations"
Assert-Value -Actual $attempt.counts.undocumentedCommands -Expected $expectedUndocumented -Label "counts.undocumentedCommands"

if ($attempt.outcome -eq "Completed" -and $expectedWorkarounds -gt 0) {
    throw "An attempt with deviations must use outcome CompletedWithWorkaround, not Completed."
}
if ($attempt.outcome -eq "CompletedWithWorkaround" -and $expectedWorkarounds -eq 0) {
    throw "CompletedWithWorkaround requires at least one structured deviation."
}

$p0 = @($findings | Where-Object severity -eq "P0").Count
$p1 = @($findings | Where-Object severity -eq "P1").Count
$p2 = @($findings | Where-Object severity -eq "P2").Count
$p3 = @($findings | Where-Object severity -eq "P3").Count
Assert-Value -Actual $attempt.findingCounts.p0 -Expected $p0 -Label "findingCounts.p0"
Assert-Value -Actual $attempt.findingCounts.p1 -Expected $p1 -Label "findingCounts.p1"
Assert-Value -Actual $attempt.findingCounts.p2 -Expected $p2 -Label "findingCounts.p2"
Assert-Value -Actual $attempt.findingCounts.p3 -Expected $p3 -Label "findingCounts.p3"
$expectedSeverity = if ($p0 -gt 0) { "P0" } elseif ($p1 -gt 0) { "P1" } elseif ($p2 -gt 0) { "P2" } elseif ($p3 -gt 0) { "P3" } else { "None" }
Assert-Value -Actual $attempt.highestFindingSeverity -Expected $expectedSeverity -Label "highestFindingSeverity"

$wallClock = [double]$attempt.timing.wallClockElapsedSeconds
$active = [double]$attempt.timing.activeJourneySeconds
$paused = [double]$attempt.timing.pausedSeconds
$product = [double]$attempt.timing.productWorkSeconds
$governance = [double]$attempt.timing.governanceCeremonySeconds
$recovery = [double]$attempt.timing.archaeologyRecoverySeconds
foreach ($timingName in @(
    "wallClockElapsedSeconds",
    "activeJourneySeconds",
    "pausedSeconds",
    "productWorkSeconds",
    "governanceCeremonySeconds",
    "archaeologyRecoverySeconds",
    "averageTimeToNextMeaningfulActionSeconds",
    "maximumTimeToNextMeaningfulActionSeconds",
    "flowEfficiency"
)) {
    Assert-Property -Object $attempt.timing -Name $timingName -Context "timing"
    if ([double]$attempt.timing.$timingName -lt 0) {
        throw "timing.$timingName must not be negative."
    }
}
if ($active -le 0) {
    throw "Final activeJourneySeconds must be greater than zero."
}
$elapsedFromTimestamps = [Math]::Round(($completedAt - $startedAt).TotalSeconds, 2, [MidpointRounding]::AwayFromZero)
Assert-Number -Actual $wallClock -Expected $elapsedFromTimestamps -Label "timing.wallClockElapsedSeconds"
Assert-Number -Actual $wallClock -Expected ($active + $paused) -Label "Wall-clock timing total"
Assert-Number -Actual $active -Expected ($product + $governance + $recovery) -Label "Active timing bucket total"

$flowEfficiency = [Math]::Round(($product + $governance) / $active, 4, [MidpointRounding]::AwayFromZero)
$averageNextAction = [Math]::Round(($transitionTimes | Measure-Object -Average).Average, 2, [MidpointRounding]::AwayFromZero)
$maximumNextAction = [Math]::Round(($transitionTimes | Measure-Object -Maximum).Maximum, 2, [MidpointRounding]::AwayFromZero)
Assert-Number -Actual $attempt.timing.flowEfficiency -Expected $flowEfficiency -Label "timing.flowEfficiency" -Tolerance 0.00005
Assert-Number -Actual $attempt.timing.averageTimeToNextMeaningfulActionSeconds -Expected $averageNextAction -Label "timing.averageTimeToNextMeaningfulActionSeconds"
Assert-Number -Actual $attempt.timing.maximumTimeToNextMeaningfulActionSeconds -Expected $maximumNextAction -Label "timing.maximumTimeToNextMeaningfulActionSeconds"

$taskCompletion = switch ($attempt.outcome) {
    "Completed" { 30.0 }
    "CompletedWithWorkaround" { 15.0 }
    "Blocked" { 0.0 }
}

$hasHiddenOrUndocumentedDeviation = $expectedHiddenKnowledge -gt 0 -or @($deviations | Where-Object {
    -not $_.documentedBeforeUse -or $_.kind -eq "UndocumentedCommand"
}).Count -gt 0
$noUndocumentedWorkaround = if ($expectedWorkarounds -eq 0 -and -not $hasHiddenOrUndocumentedDeviation) {
    20.0
}
elseif (-not $hasHiddenOrUndocumentedDeviation) {
    10.0
}
else {
    0.0
}

$clearCount = @($transitions | Where-Object { $_.nextActionVisible -and $_.nextActionKnownWithinFiveSeconds }).Count
$clearNextActions = Round-Score (15.0 * $clearCount / $transitions.Count)
$recoveryOpportunities = $expectedRefusals + $expectedDeadEnds
$usefulRecovery = if ($recoveryOpportunities -eq 0) {
    15.0
}
else {
    Round-Score (15.0 * $expectedUsefulRefusals / $recoveryOpportunities)
}
$efficientJourney = Round-Score (10.0 * $flowEfficiency)
$averageConfidence = [double](($ratings | Measure-Object -Property confidence -Average).Average)
$operatorConfidence = Round-Score ((($averageConfidence - 1.0) / 6.0) * 10.0)
$rawScore = Round-Score ($taskCompletion + $noUndocumentedWorkaround + $clearNextActions + $usefulRecovery + $efficientJourney + $operatorConfidence)
$severityCapApplied = $p0 -gt 0 -or $p1 -gt 0
$finalScore = if ($severityCapApplied) { Round-Score ([Math]::Min($rawScore, 59.0)) } else { $rawScore }
$band = if ($finalScore -ge 90) {
    "Smooth"
}
elseif ($finalScore -ge 75) {
    "UsableWithMinorFriction"
}
elseif ($finalScore -ge 60) {
    "Difficult"
}
elseif ($finalScore -ge 40) {
    "SeriouslyObstructed"
}
else {
    "BrokenCorridor"
}

$expectedScore = [ordered]@{
    taskCompletion = $taskCompletion
    noUndocumentedWorkaround = $noUndocumentedWorkaround
    clearNextActions = $clearNextActions
    usefulRecovery = $usefulRecovery
    efficientJourney = $efficientJourney
    operatorConfidence = $operatorConfidence
    rawScore = $rawScore
    severityCapApplied = $severityCapApplied
    finalScore = $finalScore
    band = $band
}

foreach ($name in $expectedScore.Keys) {
    Assert-Property -Object $attempt.score -Name $name -Context "score"
}
foreach ($name in @("taskCompletion", "noUndocumentedWorkaround", "clearNextActions", "usefulRecovery", "efficientJourney", "operatorConfidence", "rawScore", "finalScore")) {
    Assert-Number -Actual $attempt.score.$name -Expected $expectedScore[$name] -Label "score.$name"
}
Assert-Value -Actual $attempt.score.severityCapApplied -Expected $severityCapApplied -Label "score.severityCapApplied"
Assert-Value -Actual $attempt.score.band -Expected $band -Label "score.band"

$averageEase = [double](($ratings | Measure-Object -Property ease -Average).Average)
$minimumEase = [int](($ratings | Measure-Object -Property ease -Minimum).Minimum)
$averageFlowClarity = [double](($ratings | Measure-Object -Property flowClarity -Average).Average)
$helpRatings = @($ratings | Where-Object { $null -ne $_.helpUsefulness })
$averageBureaucracy = [double](($ratings | Measure-Object -Property bureaucracyFelt -Average).Average)
$completedWithoutWorkaround = $attempt.outcome -eq "Completed" -and $expectedWorkarounds -eq 0
$noP0OrP1 = -not $severityCapApplied
$averageEaseAtLeastFive = $averageEase -ge 5.0
$noMajorStepBelowFour = $minimumEase -ge 4
$averageFlowClarityAtLeastFive = $averageFlowClarity -ge 5.0
$helpUsefulnessAtLeastFiveWhenUsed = $helpRatings.Count -eq 0 -or [double](($helpRatings | Measure-Object -Property helpUsefulness -Average).Average) -ge 5.0
$averageBureaucracyAtMostThree = $averageBureaucracy -le 3.0
$averageConfidenceAtLeastFive = $averageConfidence -ge 5.0
$flowEfficiencyAtLeast075 = $flowEfficiency -ge 0.75
$zeroHiddenActions = $expectedHiddenKnowledge -eq 0 -and $expectedDirectApi -eq 0 -and $expectedDirectSql -eq 0 -and $expectedFilesystem -eq 0 -and $expectedUndocumented -eq 0
$eligibleToProceed = $completedWithoutWorkaround `
    -and $noP0OrP1 `
    -and $averageEaseAtLeastFive `
    -and $noMajorStepBelowFour `
    -and $averageFlowClarityAtLeastFive `
    -and $helpUsefulnessAtLeastFiveWhenUsed `
    -and $averageBureaucracyAtMostThree `
    -and $averageConfidenceAtLeastFive `
    -and $flowEfficiencyAtLeast075 `
    -and $zeroHiddenActions

$expectedAcceptance = [ordered]@{
    completedWithoutWorkaround = $completedWithoutWorkaround
    noP0OrP1 = $noP0OrP1
    averageEaseAtLeastFive = $averageEaseAtLeastFive
    noMajorStepBelowFour = $noMajorStepBelowFour
    averageFlowClarityAtLeastFive = $averageFlowClarityAtLeastFive
    helpUsefulnessAtLeastFiveWhenUsed = $helpUsefulnessAtLeastFiveWhenUsed
    averageBureaucracyAtMostThree = $averageBureaucracyAtMostThree
    averageConfidenceAtLeastFive = $averageConfidenceAtLeastFive
    flowEfficiencyAtLeast075 = $flowEfficiencyAtLeast075
    zeroHiddenActions = $zeroHiddenActions
    eligibleToProceed = $eligibleToProceed
}

foreach ($name in $expectedAcceptance.Keys) {
    Assert-Property -Object $attempt.tinyCalcAcceptance -Name $name -Context "tinyCalcAcceptance"
    Assert-Value -Actual $attempt.tinyCalcAcceptance.$name -Expected $expectedAcceptance[$name] -Label "tinyCalcAcceptance.$name"
}

Write-Host "DOGFOOD-UX attempt valid: $($attempt.attemptId)"
Write-Host "Flow Ease Score: $finalScore/100 ($band); wall clock: $wallClock seconds; active: $active seconds; paused: $paused seconds; flow efficiency: $flowEfficiency; TinyCalc eligible: $eligibleToProceed"

if ($PassThru) {
    [pscustomobject]@{
        AttemptId = $attempt.attemptId
        Outcome = $attempt.outcome
        RawScore = $rawScore
        FinalScore = $finalScore
        Band = $band
        WallClockElapsedSeconds = $wallClock
        ActiveJourneySeconds = $active
        PausedSeconds = $paused
        FlowEfficiency = $flowEfficiency
        TinyCalcEligible = $eligibleToProceed
        Final = $true
    }
}
