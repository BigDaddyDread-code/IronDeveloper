$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ""
    Write-Host "== $Name =="
}

function Invoke-GovernanceBoundaryTestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    dotnet test $script:Project `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --filter $Filter
}

$script:Project = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"

Write-Section "Governance boundary CI"
Write-Host "GitHub Actions CI reports evidence only."
Write-Host "CI is not approval, policy satisfaction, merge readiness, release readiness, deployment readiness, or execution permission."

$bSeriesBoundaryFilter = @(
    "FullyQualifiedName~BlockB01AuthorityProfileKindUnificationTests",
    "FullyQualifiedName~BlockB03AuthorityProfileVocabularyDriftTests",
    "FullyQualifiedName~BlockB04AskBeforeMutationRunProfileTests",
    "FullyQualifiedName~BlockB05BoundedRunAuthorityProfileTests",
    "FullyQualifiedName~BlockB06AuthorityProfileStatusCanonicalModelTests",
    "FullyQualifiedName~BlockB07ProposalOnlyMutationStatusProofTests",
    "FullyQualifiedName~BlockB08AskBeforeMutationBoundaryProofTests",
    "FullyQualifiedName~BlockB09BoundedRunAuthorityDownstreamProofTests",
    "FullyQualifiedName~BlockB10CanonicalAuthorityGlossaryTests",
    "FullyQualifiedName~BlockB11StatusAuthorityGlossaryAdoptionTests",
    "FullyQualifiedName~BlockB12HostileProfileTextEligibilityProofTests"
) -join "|"

$compatibilityBoundaryFilter = @(
    "FullyQualifiedName~BlockBQRunAuthorityProfileContractTests",
    "FullyQualifiedName~BlockBRBoundedRunAuthorityGrantTests",
    "FullyQualifiedName~BlockBSOperationEligibilityEvaluatorTests",
    "FullyQualifiedName~BlockBTAuthorityProfileStatusMappingTests",
    "FullyQualifiedName~BlockBUSourceApplyConsumesBoundedAuthorityTests"
) -join "|"

Invoke-GovernanceBoundaryTestLane `
    -Name "B-series profile boundary tests" `
    -Filter $bSeriesBoundaryFilter

Invoke-GovernanceBoundaryTestLane `
    -Name "BQ-BU compatibility boundary tests" `
    -Filter $compatibilityBoundaryFilter

Write-Section "Governance boundary CI complete"
Write-Host "A green check is evidence, not permission."
