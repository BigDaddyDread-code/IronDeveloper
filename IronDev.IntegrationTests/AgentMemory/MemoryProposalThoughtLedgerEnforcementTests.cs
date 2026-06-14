using IronDev.Core.AgentMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
public sealed class MemoryProposalThoughtLedgerEnforcementTests
{
    private static readonly MemoryProposalThoughtLedgerTraceabilityValidator Validator = new();
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_StagedProposalRequiresTraceability()
    {
        Assert.IsTrue(Validator.ValidateStagedProposal(ValidProposal()).IsValid);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_EvidencePackageRequiresTraceability()
    {
        Assert.IsTrue(Validator.ValidateEvidencePackage(ValidEvidencePackage()).IsValid);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_PromotionRequestPackageRequiresTraceability()
    {
        Assert.IsTrue(Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage()).IsValid);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_AllowsThoughtLedgerEntryReference()
    {
        var proposal = ValidProposal(evidenceReference: EvidenceReference(thoughtLedgerEntryId: Guid.NewGuid()));

        Assert.IsTrue(Validator.ValidateStagedProposal(proposal).IsValid);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_AllowsGovernanceEventReference()
    {
        var proposal = ValidProposal(evidenceReference: EvidenceReference(governanceEventId: Guid.NewGuid()));

        Assert.IsTrue(Validator.ValidateStagedProposal(proposal).IsValid);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_AllowsWorkflowEvidenceReference()
    {
        var proposal = ValidProposal(evidenceReference: EvidenceReference(workflowRunEvidenceReferenceId: Guid.NewGuid()));

        Assert.IsTrue(Validator.ValidateStagedProposal(proposal).IsValid);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_AllowsGroundingReferenceAsTraceabilityOnly()
    {
        var proposal = ValidProposal(evidenceReferences: Array.Empty<MemoryProposalEvidenceReference>(), groundingReferences: new[] { GroundingReference() });

        Assert.IsTrue(Validator.ValidateStagedProposal(proposal).IsValid);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsUntraceableProposal()
    {
        var result = Validator.ValidateStagedProposal(ValidProposal(evidenceReferences: Array.Empty<MemoryProposalEvidenceReference>()));

        AssertHasIssue(result, "traceability_required");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsUntraceableEvidencePackage()
    {
        var result = Validator.ValidateEvidencePackage(ValidEvidencePackage(evidenceReferences: Array.Empty<MemoryProposalPackageEvidenceReference>()));

        AssertHasIssue(result, "traceability_required");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsUntraceablePromotionRequestPackage()
    {
        var result = Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage(evidenceReferences: Array.Empty<MemoryPromotionRequestEvidenceReference>()));

        AssertHasIssue(result, "traceability_required");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_ThoughtLedgerReferenceIsEvidenceOnly()
    {
        var proposal = ValidProposal(evidenceReference: EvidenceReference(thoughtLedgerEntryId: Guid.NewGuid()));

        Assert.IsTrue(Validator.ValidateStagedProposal(proposal).IsValid);
        Assert.IsFalse(proposal.IsApproval);
        Assert.IsFalse(proposal.GrantsApproval);
        Assert.IsFalse(proposal.IsAcceptedMemory);
        Assert.IsFalse(proposal.PromotesMemory);
        Assert.IsFalse(proposal.IsRetrievalAuthority);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_ThoughtLedgerReferenceDoesNotApproveMemory()
    {
        AssertHasIssue(Validator.ValidateStagedProposal(ValidProposal(grantsApproval: true)), "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_ThoughtLedgerReferenceDoesNotAcceptMemory()
    {
        AssertHasIssue(Validator.ValidateStagedProposal(ValidProposal(isAcceptedMemory: true)), "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_ThoughtLedgerReferenceDoesNotPromoteMemory()
    {
        AssertHasIssue(Validator.ValidateStagedProposal(ValidProposal(promotesMemory: true)), "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_ThoughtLedgerReferenceDoesNotActivateRetrieval()
    {
        AssertHasIssue(Validator.ValidateStagedProposal(ValidProposal(isRetrievalAuthority: true)), "proposal_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_GovernanceReferenceIsEvidenceOnly()
    {
        var package = ValidEvidencePackage(evidenceReference: PackageEvidenceReference(governanceEventId: Guid.NewGuid()));

        Assert.IsTrue(Validator.ValidateEvidencePackage(package).IsValid);
        Assert.IsFalse(package.GrantsApproval);
        Assert.IsFalse(package.AcceptsMemory);
        Assert.IsFalse(package.PromotesMemory);
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_WorkflowReferenceIsContextOnly()
    {
        var package = ValidEvidencePackage(evidenceReferences: Array.Empty<MemoryProposalPackageEvidenceReference>(), workflowReferences: new[] { PackageWorkflowReference() });

        Assert.IsTrue(Validator.ValidateEvidencePackage(package).IsValid);
        Assert.IsFalse(package.StartsWithAuthorityShape());
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsHiddenReasoning()
    {
        AssertRejectsUnsafeProposalText("hiddenReasoning leaked");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsChainOfThought()
    {
        AssertRejectsUnsafeProposalText("chainOfThought leaked");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsScratchpad()
    {
        AssertRejectsUnsafeProposalText("scratchpad leaked");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsRawPrompt()
    {
        AssertRejectsUnsafeProposalText("rawPrompt leaked");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsRawCompletion()
    {
        AssertRejectsUnsafeProposalText("rawCompletion leaked");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsRawToolOutput()
    {
        AssertRejectsUnsafeProposalText("rawToolOutput leaked");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsEntirePatch()
    {
        AssertRejectsUnsafeProposalText("entirePatch leaked");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsAcceptedMemoryLanguage()
    {
        AssertRejectsUnsafePromotionText("accepted memory");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsPromotedMemoryLanguage()
    {
        AssertRejectsUnsafePromotionText("promoted memory");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsPromotionApprovedLanguage()
    {
        AssertRejectsUnsafePromotionText("promotion approved");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsPolicySatisfiedLanguage()
    {
        AssertRejectsUnsafePromotionText("policy satisfied");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsRetrievalActiveLanguage()
    {
        AssertRejectsUnsafePromotionText("retrieval active");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsEmbeddingLanguage()
    {
        AssertRejectsUnsafePromotionText("embedding created");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsVectorWriteLanguage()
    {
        AssertRejectsUnsafePromotionText("vector write");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsPortableApprovedLanguage()
    {
        AssertRejectsUnsafePromotionText("portable engineering memory accepted");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_RejectsAuthorityTransferLanguage()
    {
        AssertRejectsUnsafePromotionText("authority transferred");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_PromotionRequestTraceDoesNotSatisfyApproval()
    {
        AssertHasIssue(Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage(grantsApproval: true)), "promotion_request_package_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_PromotionRequestTraceDoesNotSatisfyPolicy()
    {
        AssertHasIssue(Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage(satisfiesPolicy: true)), "promotion_request_package_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_PromotionRequestTraceDoesNotAllowPromotion()
    {
        AssertHasIssue(Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage(promotesMemory: true)), "promotion_request_package_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_PromotionRequestTraceDoesNotCreateAcceptedMemory()
    {
        AssertHasIssue(Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage(createsAcceptedMemory: true)), "promotion_request_package_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_PromotionRequestTraceDoesNotCreatePortableMemory()
    {
        AssertHasIssue(Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage(createsPortableMemory: true)), "promotion_request_package_authority_forbidden");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotAddSqlMigration()
    {
        AssertNoPr114ReferencesUnder("Database");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotAddApiEndpoint()
    {
        AssertNoPr114ReferencesUnder("IronDev.Api");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotAddCliCommand()
    {
        AssertNoPr114ReferencesUnder(Path.Combine("tools", "IronDev.Cli"));
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotAddAcceptedMemoryStore()
    {
        AssertChangedFilesDoNotContain("AcceptedMemoryStore");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotAddPromotionPath()
    {
        AssertChangedFilesDoNotContain("MemoryPromotionPath", "IMemoryPromotionStore", "MemoryPromotionStore");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotAddEmbeddingWriter()
    {
        AssertChangedFilesDoNotContain("EmbeddingWriter", "IEmbeddingWriter");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotAddVectorStoreWriter()
    {
        AssertChangedFilesDoNotContain("VectorStoreWriter", "IVectorStoreWriter");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotReferenceWeaviateWrite()
    {
        AssertChangedFilesDoNotContain("WeaviateClient", "IWeaviate", "WeaviateWriter");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotReferenceRetrievalActivation()
    {
        AssertChangedFilesDoNotContain("RetrievalActivationService", "IRetrievalActivation", "ActivateRetrievalAsync");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotReferenceModelClient()
    {
        AssertNoPr114ReferencesUnder("IronDev.Infrastructure");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotReferenceSourceApply()
    {
        AssertChangedFilesDoNotContain("SourceApplyService", "ApplyToSource", "ApplySourceAsync");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotReferenceWorkflowRunner()
    {
        AssertChangedFilesDoNotContain("WorkflowRunner", "IWorkflowRunner", "RunWorkflowAsync");
    }

    [TestMethod]
    public void MemoryProposalThoughtLedgerEnforcement_DoesNotReferenceAgentDispatcher()
    {
        AssertChangedFilesDoNotContain("AgentDispatcher", "IAgentDispatcher", "DispatchAgentAsync");
    }

    private static void AssertRejectsUnsafeProposalText(string text)
    {
        var result = Validator.ValidateStagedProposal(ValidProposal(safeProposedMemory: text));

        AssertHasIssue(result, "proposal_text_unsafe");
    }

    private static void AssertRejectsUnsafePromotionText(string text)
    {
        var result = Validator.ValidatePromotionRequestPackage(ValidPromotionRequestPackage(safeProposedMemory: text));

        AssertHasIssue(result, "promotion_request_package_text_unsafe");
    }

    private static void AssertHasIssue(MemoryProposalValidationResult result, string code)
    {
        Assert.IsFalse(result.IsValid, "Expected validation to fail.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == code), "Expected issue code " + code + " but got: " + string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    private static MemoryProposal ValidProposal(
        string safeProposedMemory = "Prefer deterministic memory proposal review boundaries.",
        MemoryProposalEvidenceReference? evidenceReference = null,
        IReadOnlyList<MemoryProposalEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryProposalGroundingReference>? groundingReferences = null,
        bool grantsApproval = false,
        bool isAcceptedMemory = false,
        bool promotesMemory = false,
        bool isRetrievalAuthority = false)
    {
        return new MemoryProposal
        {
            MemoryProposalId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ProposalKey = "proposal.traceability",
            ProposalType = MemoryProposalType.ProjectFactCandidate,
            TargetMemoryScope = MemoryProposalTargetScope.ProjectLocalCandidate,
            ProposalStatus = MemoryProposalStatus.ReadyForReview,
            SourceType = "human-review",
            SafeProposedMemory = safeProposedMemory,
            SafeRationaleSummary = "Proposal needs review because the evidence repeats across safe review context.",
            SafeRiskSummary = "Traceability remains evidence only.",
            ConfidentialityLabel = MemoryProposalConfidentialityLabel.ProjectConfidential,
            SanitizationStatus = MemoryProposalSanitizationStatus.RequiresReview,
            CreatedByActorType = "human",
            CreatedByActorId = "reviewer",
            MetadataVersion = 1,
            MetadataJson = "{}",
            GrantsApproval = grantsApproval,
            IsAcceptedMemory = isAcceptedMemory,
            PromotesMemory = promotesMemory,
            IsRetrievalAuthority = isRetrievalAuthority,
            EvidenceReferences = evidenceReferences ?? new[] { evidenceReference ?? EvidenceReference(thoughtLedgerEntryId: Guid.NewGuid()) },
            GroundingReferences = groundingReferences ?? Array.Empty<MemoryProposalGroundingReference>(),
            WorkflowReferences = Array.Empty<MemoryProposalWorkflowReference>(),
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryProposalEvidenceReference EvidenceReference(Guid? thoughtLedgerEntryId = null, Guid? governanceEventId = null, Guid? workflowRunEvidenceReferenceId = null)
    {
        return new MemoryProposalEvidenceReference
        {
            MemoryProposalEvidenceReferenceId = Guid.NewGuid(),
            MemoryProposalId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            EvidenceType = thoughtLedgerEntryId.HasValue ? MemoryProposalEvidenceType.ThoughtLedgerReference : governanceEventId.HasValue ? MemoryProposalEvidenceType.GovernanceEvent : MemoryProposalEvidenceType.WorkflowRun,
            EvidenceId = Guid.NewGuid().ToString("N"),
            EvidenceLabel = "Safe trace evidence",
            SafeSummary = "Safe trace reference for review only.",
            AllowedUse = MemoryProposalEvidenceAllowedUse.Traceability,
            ThoughtLedgerEntryId = thoughtLedgerEntryId,
            GovernanceEventId = governanceEventId,
            WorkflowRunEvidenceReferenceId = workflowRunEvidenceReferenceId,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryProposalGroundingReference GroundingReference()
    {
        return new MemoryProposalGroundingReference
        {
            MemoryProposalGroundingReferenceId = Guid.NewGuid(),
            MemoryProposalId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            GroundingReferenceId = Guid.NewGuid(),
            ClaimType = MemoryProposalGroundingClaimType.MemoryProposalTrace,
            ClaimId = "claim.trace",
            SafeSummary = "Grounding reference traces the proposal for review only.",
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryProposalEvidencePackage ValidEvidencePackage(
        MemoryProposalPackageEvidenceReference? evidenceReference = null,
        IReadOnlyList<MemoryProposalPackageEvidenceReference>? evidenceReferences = null,
        IReadOnlyList<MemoryProposalPackageWorkflowReference>? workflowReferences = null)
    {
        return new MemoryProposalEvidencePackage
        {
            MemoryProposalEvidencePackageId = Guid.NewGuid(),
            MemoryProposalId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            PackageKey = "proposal.traceability.evidence",
            Status = MemoryProposalEvidencePackageStatus.ReadyForReview,
            Purpose = MemoryProposalEvidencePackagePurpose.HumanReview,
            ProposalType = "ProjectFactCandidate",
            TargetMemoryScope = "ProjectLocalCandidate",
            ProposalStatus = "ReadyForReview",
            SafeProposedMemory = "Prefer deterministic memory proposal review boundaries.",
            ConfidentialityLabel = "ProjectConfidential",
            SanitizationStatus = "RequiresReview",
            EvidenceReferences = evidenceReferences ?? new[] { evidenceReference ?? PackageEvidenceReference(thoughtLedgerEntryId: Guid.NewGuid()) },
            GroundingReferences = Array.Empty<MemoryProposalPackageGroundingReference>(),
            WorkflowReferences = workflowReferences ?? Array.Empty<MemoryProposalPackageWorkflowReference>(),
            ReviewNotes = Array.Empty<MemoryProposalPackageReviewNote>(),
            CreatedByActorType = "system",
            CreatedByActorId = "evidence-package-test",
            MetadataVersion = 1,
            MetadataJson = "{}",
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryProposalPackageEvidenceReference PackageEvidenceReference(Guid? thoughtLedgerEntryId = null, Guid? governanceEventId = null)
    {
        return new MemoryProposalPackageEvidenceReference
        {
            EvidenceType = thoughtLedgerEntryId.HasValue ? "ThoughtLedgerReference" : governanceEventId.HasValue ? "GovernanceEvent" : "WorkflowRun",
            EvidenceId = Guid.NewGuid().ToString("N"),
            EvidenceLabel = "Safe evidence package trace",
            SafeSummary = "Evidence reference is for review only.",
            AllowedUse = "Traceability",
            ThoughtLedgerEntryId = thoughtLedgerEntryId,
            GovernanceEventId = governanceEventId
        };
    }

    private static MemoryProposalPackageWorkflowReference PackageWorkflowReference()
    {
        return new MemoryProposalPackageWorkflowReference
        {
            WorkflowRunId = Guid.NewGuid(),
            ReferenceType = "Traceability",
            SafeSummary = "Workflow reference is context only."
        };
    }

    private static MemoryPromotionRequestPackage ValidPromotionRequestPackage(
        string safeProposedMemory = "Prefer deterministic memory proposal review boundaries.",
        IReadOnlyList<MemoryPromotionRequestEvidenceReference>? evidenceReferences = null,
        bool grantsApproval = false,
        bool satisfiesPolicy = false,
        bool promotesMemory = false,
        bool createsAcceptedMemory = false,
        bool createsPortableMemory = false)
    {
        return new MemoryPromotionRequestPackage
        {
            MemoryPromotionRequestPackageId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            MemoryProposalId = Guid.NewGuid(),
            PromotionRequestKey = "proposal.traceability.promotion-request",
            Status = MemoryPromotionRequestPackageStatus.ReadyForReview,
            Purpose = MemoryPromotionRequestPurpose.HumanPromotionReview,
            ProposalType = "ProjectFactCandidate",
            CurrentProposalStatus = "ReadyForReview",
            RequestedTargetMemoryScope = MemoryPromotionRequestedTargetMemoryScope.ProjectLocalCandidateForPromotion,
            SafeProposedMemory = safeProposedMemory,
            SafePromotionRationale = "Package requests review only.",
            ConfidentialityLabel = "ProjectConfidential",
            SanitizationStatus = "RequiresReview",
            EvidenceReferences = evidenceReferences ?? new[] { PromotionEvidenceReference() },
            GroundingReferences = Array.Empty<MemoryPromotionRequestGroundingReference>(),
            SignalReferences = Array.Empty<MemoryPromotionRequestSignalReference>(),
            ApprovalRequirementReferences = Array.Empty<MemoryPromotionApprovalRequirementReference>(),
            ReviewNotes = Array.Empty<MemoryPromotionRequestReviewNote>(),
            CreatedByActorType = "system",
            CreatedByActorId = "promotion-request-test",
            MetadataVersion = 1,
            MetadataJson = "{}",
            GrantsApproval = grantsApproval,
            SatisfiesPolicy = satisfiesPolicy,
            PromotesMemory = promotesMemory,
            CreatesAcceptedMemory = createsAcceptedMemory,
            CreatesPortableMemory = createsPortableMemory,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryPromotionRequestEvidenceReference PromotionEvidenceReference()
    {
        return new MemoryPromotionRequestEvidenceReference
        {
            EvidenceType = "MemoryProposalEvidencePackage",
            EvidenceId = Guid.NewGuid().ToString("N"),
            EvidenceLabel = "Memory proposal evidence package",
            SafeSummary = "Evidence package is a review trace only.",
            AllowedUse = "PromotionReview",
            MemoryProposalId = Guid.NewGuid(),
            MemoryProposalEvidencePackageId = Guid.NewGuid()
        };
    }

    private static void AssertChangedFilesDoNotContain(params string[] tokens)
    {
        foreach (var relativePath in ChangedFiles())
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{relativePath} contains forbidden token {token}.");
            }
        }
    }

    private static void AssertNoPr114ReferencesUnder(string relativeDirectory)
    {
        var directory = Path.Combine(RepositoryRoot, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var references = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsTextFile)
            .Where(path => File.ReadAllText(path).Contains("MemoryProposalThoughtLedgerTraceabilityValidator", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(path).Contains("PR114", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, references.Length, "PR114 should not be wired into forbidden surfaces: " + string.Join(", ", references));
    }

    private static IReadOnlyList<string> ChangedFiles() => new[]
    {
        Path.Combine("IronDev.Core", "AgentMemory", "MemoryProposalThoughtLedgerTraceabilityValidator.cs"),
        Path.Combine("Docs", "BLOCK_K_GOVERNED_MEMORY_PROPOSAL_SUBSTRATE.md"),
        Path.Combine("Docs", "receipts", "PR114_MEMORY_PROPOSAL_THOUGHTLEDGER_ENFORCEMENT.md")
    };

    private static bool IsTextFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".sql" or ".md" or ".json" or ".ps1";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}

internal static class MemoryProposalEvidencePackageTestExtensions
{
    public static bool StartsWithAuthorityShape(this MemoryProposalEvidencePackage package)
    {
        return package.GrantsApproval
            || package.GrantsExecution
            || package.AcceptsMemory
            || package.PromotesMemory
            || package.ActivatesRetrieval
            || package.SatisfiesPolicy
            || package.TransfersAuthority;
    }
}

