using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class StaticAgentSkillRegistry : IAgentSkillRegistry
{
    private static readonly IReadOnlyList<AgentSkillDefinition> Skills =
    [
        Definition(
            AgentSkillIds.WorkspaceReadApplyContext,
            "Read workspace apply context",
            "Reads source-report or failure-package evidence and produces the shared advisory workspace apply context.",
            AgentSkillCategories.WorkspaceContext,
            ProjectApprovalRiskTiers.WorkspaceReporting,
            canReadEvidence: true,
            producesEvidenceTypes: ["agent-workspace-apply-context"],
            readsEvidenceTypes: ["source-report", "failure-package"],
            inputContract: "runId, workspacePath",
            outputContract: "AgentWorkspaceApplyContext"),

        Definition(
            AgentSkillIds.WorkspaceReadSourceReport,
            "Read source report",
            "Reads a completed workspace source-report artifact.",
            AgentSkillCategories.WorkspaceContext,
            ProjectApprovalRiskTiers.WorkspaceReporting,
            canReadEvidence: true,
            readsEvidenceTypes: ["source-report"],
            producesEvidenceTypes: ["workspace-apply-report-summary"],
            inputContract: "runId, workspacePath",
            outputContract: "WorkspaceApplyReportSummary"),

        Definition(
            AgentSkillIds.WorkspaceReadFailurePackage,
            "Read failure package",
            "Reads a workspace failure-package artifact.",
            AgentSkillCategories.WorkspaceContext,
            ProjectApprovalRiskTiers.WorkspaceReporting,
            canReadEvidence: true,
            readsEvidenceTypes: ["failure-package"],
            producesEvidenceTypes: ["workspace-apply-report-summary"],
            inputContract: "runId, workspacePath",
            outputContract: "WorkspaceApplyReportSummary"),

        Definition(
            AgentSkillIds.WorkspaceRecommendApplyAction,
            "Recommend workspace apply action",
            "Creates a deterministic advisory recommendation from workspace apply evidence.",
            AgentSkillCategories.AgentReview,
            ProjectApprovalRiskTiers.WorkspaceIntent,
            canReadEvidence: true,
            readsEvidenceTypes: ["workspace-apply-report-summary"],
            producesEvidenceTypes: ["workspace-apply-recommendation"],
            inputContract: "WorkspaceApplyReportSummary",
            outputContract: "WorkspaceApplyRecommendation"),

        Definition(
            AgentSkillIds.WorkspaceCreateActionRequest,
            "Create workspace apply action request",
            "Creates structured advisory intent from a workspace apply recommendation.",
            AgentSkillCategories.AgentReview,
            ProjectApprovalRiskTiers.WorkspaceIntent,
            canReadEvidence: true,
            readsEvidenceTypes: ["workspace-apply-report-summary", "workspace-apply-recommendation"],
            producesEvidenceTypes: ["workspace-apply-action-request"],
            inputContract: "WorkspaceApplyReportSummary, WorkspaceApplyRecommendation",
            outputContract: "WorkspaceApplyActionRequest"),

        Definition(
            AgentSkillIds.WorkspaceCreateActionReview,
            "Create workspace apply action review",
            "Creates a human-review package for a workspace apply action request.",
            AgentSkillCategories.AgentReview,
            ProjectApprovalRiskTiers.WorkspaceIntent,
            canReadEvidence: true,
            readsEvidenceTypes: ["workspace-apply-report-summary", "workspace-apply-recommendation", "workspace-apply-action-request"],
            producesEvidenceTypes: ["workspace-apply-action-review"],
            inputContract: "WorkspaceApplyReportSummary, WorkspaceApplyRecommendation, WorkspaceApplyActionRequest",
            outputContract: "WorkspaceApplyActionReview"),

        Definition(
            AgentSkillIds.WorkspaceEvaluatePolicyContext,
            "Evaluate workspace apply policy context",
            "Creates an advisory project policy context for a workspace apply action review.",
            AgentSkillCategories.AgentReview,
            ProjectApprovalRiskTiers.WorkspaceIntent,
            canReadEvidence: true,
            readsEvidenceTypes: ["workspace-apply-report-summary", "workspace-apply-recommendation", "workspace-apply-action-request", "workspace-apply-action-review"],
            producesEvidenceTypes: ["workspace-apply-policy-context"],
            inputContract: "ProjectApprovalPolicy, WorkspaceApplyActionReview",
            outputContract: "WorkspaceApplyPolicyContext"),

        Definition(
            AgentSkillIds.WorkspaceCheck,
            "Check disposable workspace readiness",
            "Checks whether a disposable workspace path is isolated and safe to prepare.",
            AgentSkillCategories.WorkspaceCommand,
            ProjectApprovalRiskTiers.WorkspacePreparation,
            canExecuteProcess: true,
            requiresHumanApproval: true,
            producesEvidenceTypes: ["workspace-readiness"],
            inputContract: "runId, sourceRepo, workspaceRoot",
            outputContract: "DisposableWorkspaceReadinessResult"),

        Definition(
            AgentSkillIds.WorkspacePrepare,
            "Prepare disposable workspace",
            "Creates and populates a disposable workspace after readiness succeeds.",
            AgentSkillCategories.WorkspaceCommand,
            ProjectApprovalRiskTiers.WorkspacePreparation,
            canExecuteProcess: true,
            canMutateWorkspace: true,
            requiresHumanApproval: true,
            readsEvidenceTypes: ["workspace-readiness"],
            producesEvidenceTypes: ["workspace-metadata"],
            inputContract: "runId, sourceRepo, workspaceRoot",
            outputContract: "DisposableWorkspacePrepareResult"),

        Definition(
            AgentSkillIds.WorkspaceValidate,
            "Validate disposable workspace",
            "Runs allowlisted validation inside a disposable workspace and records validation evidence.",
            AgentSkillCategories.WorkspaceCommand,
            ProjectApprovalRiskTiers.WorkspaceValidation,
            canExecuteProcess: true,
            canMutateWorkspace: true,
            requiresHumanApproval: true,
            readsEvidenceTypes: ["workspace-metadata"],
            producesEvidenceTypes: ["validation"],
            inputContract: "runId, workspacePath, profile",
            outputContract: "DisposableWorkspaceValidationResult"),

        Definition(
            AgentSkillIds.WorkspaceDiff,
            "Diff disposable workspace",
            "Compares source and disposable workspace file hashes without process execution.",
            AgentSkillCategories.WorkspaceCommand,
            ProjectApprovalRiskTiers.WorkspaceReporting,
            readsEvidenceTypes: ["workspace-metadata"],
            producesEvidenceTypes: ["diff"],
            inputContract: "runId, workspacePath",
            outputContract: "DisposableWorkspaceDiffResult"),

        Definition(
            AgentSkillIds.WorkspacePromotionPackage,
            "Create workspace promotion package",
            "Packages validation and diff evidence for human review.",
            AgentSkillCategories.WorkspaceApply,
            ProjectApprovalRiskTiers.WorkspaceReporting,
            canReadEvidence: true,
            readsEvidenceTypes: ["workspace-metadata", "validation", "diff"],
            producesEvidenceTypes: ["promotion-package"],
            inputContract: "runId, workspacePath",
            outputContract: "DisposableWorkspacePromotionPackageResult"),

        Definition(
            AgentSkillIds.WorkspaceFailurePackage,
            "Create workspace failure package",
            "Creates an advisory failure package from available workspace spine evidence.",
            AgentSkillCategories.WorkspaceApply,
            ProjectApprovalRiskTiers.WorkspaceReporting,
            canReadEvidence: true,
            readsEvidenceTypes: ["workspace-metadata", "validation", "diff", "apply-copy", "apply-verify", "post-apply-validation"],
            producesEvidenceTypes: ["failure-package"],
            inputContract: "runId, workspacePath, failedStage",
            outputContract: "DisposableWorkspaceFailurePackageResult"),

        Definition(
            AgentSkillIds.WorkspaceSourceReport,
            "Create workspace source report",
            "Creates the final advisory source report after apply verification and post-apply validation.",
            AgentSkillCategories.WorkspaceApply,
            ProjectApprovalRiskTiers.WorkspaceReporting,
            canReadEvidence: true,
            readsEvidenceTypes: ["workspace-metadata", "apply-copy", "apply-verify", "post-apply-validation"],
            producesEvidenceTypes: ["source-report"],
            inputContract: "runId, workspacePath",
            outputContract: "DisposableWorkspaceSourceReportResult"),

        Definition(
            AgentSkillIds.WorkspaceApplyCopy,
            "Apply copy-only workspace changes",
            "Copies approved add/modify files from the disposable workspace back to source using the full evidence chain.",
            AgentSkillCategories.WorkspaceApply,
            ProjectApprovalRiskTiers.SourceMutation,
            canReadEvidence: true,
            canMutateSource: true,
            requiresHumanApproval: true,
            readsEvidenceTypes: ["workspace-metadata", "diff", "promotion-package", "promotion-approval", "apply-preflight", "apply-dry-run"],
            producesEvidenceTypes: ["apply-copy"],
            inputContract: "runId, workspacePath",
            outputContract: "DisposableWorkspaceApplyCopyResult"),

        Definition(
            AgentSkillIds.MemorySearch,
            "Search memory",
            "Reads memory/search evidence without writing memory.",
            AgentSkillCategories.Memory,
            ProjectApprovalRiskTiers.ReadOnly,
            canReadEvidence: true,
            inputContract: "query, projectId",
            outputContract: "memory search results"),

        Definition(
            AgentSkillIds.TicketCreate,
            "Create ticket",
            "Creates a product ticket and therefore requires explicit human approval.",
            AgentSkillCategories.Ticketing,
            ProjectApprovalRiskTiers.TicketWrite,
            canCreateTicket: true,
            requiresHumanApproval: true,
            inputContract: "projectId, title, body",
            outputContract: "ticket id"),

        Definition(
            AgentSkillIds.GitCommit,
            "Create git commit",
            "Creates git history in the source repository.",
            AgentSkillCategories.Git,
            ProjectApprovalRiskTiers.GitOperation,
            canExecuteProcess: true,
            canMutateSource: true,
            requiresHumanApproval: true,
            inputContract: "repoPath, message",
            outputContract: "commit sha"),

        Definition(
            AgentSkillIds.GitHubPullRequestCreate,
            "Create GitHub pull request",
            "Creates a GitHub pull request through an external system.",
            AgentSkillCategories.Git,
            ProjectApprovalRiskTiers.GitOperation,
            canUseExternalSystem: true,
            requiresHumanApproval: true,
            inputContract: "repository, branch, title, body",
            outputContract: "pull request url")
    ];

    public IReadOnlyList<AgentSkillDefinition> List() => Skills;

    public AgentSkillDefinition? Find(string skillId) =>
        Skills.FirstOrDefault(skill => string.Equals(skill.SkillId, skillId, StringComparison.Ordinal));

    private static AgentSkillDefinition Definition(
        string skillId,
        string displayName,
        string description,
        string category,
        string riskTier,
        bool canReadEvidence = false,
        bool canExecuteProcess = false,
        bool canMutateWorkspace = false,
        bool canMutateSource = false,
        bool canWriteMemory = false,
        bool canCreateTicket = false,
        bool canUseExternalSystem = false,
        bool requiresHumanApproval = false,
        IReadOnlyList<string>? readsEvidenceTypes = null,
        IReadOnlyList<string>? producesEvidenceTypes = null,
        string? inputContract = null,
        string? outputContract = null) =>
        new()
        {
            SkillId = skillId,
            DisplayName = displayName,
            Description = description,
            Category = category,
            RiskTier = riskTier,
            CanReadEvidence = canReadEvidence,
            CanExecuteProcess = canExecuteProcess,
            CanMutateWorkspace = canMutateWorkspace,
            CanMutateSource = canMutateSource,
            CanWriteMemory = canWriteMemory,
            CanCreateTicket = canCreateTicket,
            CanUseExternalSystem = canUseExternalSystem,
            RequiresHumanApproval = requiresHumanApproval,
            ReadsEvidenceTypes = readsEvidenceTypes ?? [],
            ProducesEvidenceTypes = producesEvidenceTypes ?? [],
            InputContract = inputContract,
            OutputContract = outputContract
        };
}
