using IronDev.Core.Agents.Audit;
using AuditAgentRunStatus = IronDev.Core.Agents.Audit.AgentRunStatus;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public enum ManualImplementationPatchProposalStatus
{
    Succeeded = 1,
    Failed = 2,
    Blocked = 3,
    InvalidRequest = 4
}

public sealed record ManualImplementationPatchProposalRequest
{
    public required string ManualProposalId { get; init; }
    public required AgentToolRequest ToolRequest { get; init; }
    public required AgentToolExecutionGateDecision GateDecision { get; init; }
    public required string RequestedByUserId { get; init; }
    public required string ProposalGoal { get; init; }
    public IReadOnlyList<PatchProposalInputRef> Inputs { get; init; } = [];
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset RequestedAtUtc { get; init; }
}

public sealed record PatchProposalInputRef
{
    public required string InputRefId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsAuthoritativeForAction { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsSanitised { get; init; }
}

public sealed record ManualImplementationPatchProposalOutput
{
    public required string OutputId { get; init; }
    public required PatchProposalPackage Proposal { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool WritesFiles { get; init; }
    public bool DeletesFiles { get; init; }
    public bool RunsGit { get; init; }
    public bool CallsExternalSystem { get; init; }
    public bool SubmitsGitHubReview { get; init; }
    public bool CreatesPullRequest { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
}

public sealed record PatchProposalPackage
{
    public required string PatchProposalId { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Rationale { get; init; }
    public IReadOnlyList<ProposedFileChange> FileChanges { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsProposalOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public bool RequiresValidation { get; init; }
    public bool AppliesCleanlyClaimed { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool CreatesRuntimeAction { get; init; }
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
}

public sealed record ProposedFileChange
{
    public required string FileChangeId { get; init; }
    public required string Path { get; init; }
    public required string ChangeKind { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<ProposedPatchHunk> Hunks { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsProposalOnly { get; init; }
    public bool WritesFile { get; init; }
    public bool DeletesFile { get; init; }
    public bool AppliesPatch { get; init; }
}

public sealed record ProposedPatchHunk
{
    public required string HunkId { get; init; }
    public required string Summary { get; init; }
    public string? BeforeSnippet { get; init; }
    public string? AfterSnippet { get; init; }
    public string? UnifiedDiffPreview { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool ClaimsApplied { get; init; }
}

public sealed record ManualImplementationPatchProposalIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ManualImplementationPatchProposalResult
{
    public required bool Succeeded { get; init; }
    public required ManualImplementationPatchProposalStatus Status { get; init; }
    public required string ManualProposalId { get; init; }
    public string? ToolRequestId { get; init; }
    public string? GateDecisionId { get; init; }
    public ManualImplementationPatchProposalOutput? Output { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualImplementationPatchProposalIssue> Issues { get; init; } = [];
}

public sealed record PatchProposalGenerationRequest
{
    public required string ManualProposalId { get; init; }
    public required AgentToolRequest ToolRequest { get; init; }
    public required AgentToolExecutionGateDecision GateDecision { get; init; }
    public required string RequestedByUserId { get; init; }
    public required string ProposalGoal { get; init; }
    public IReadOnlyList<PatchProposalInputRef> Inputs { get; init; } = [];
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset RequestedAtUtc { get; init; }
}

public sealed record PatchProposalGenerationResult
{
    public required bool Succeeded { get; init; }
    public required string Summary { get; init; }
    public PatchProposalPackage? Proposal { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<ManualImplementationPatchProposalIssue> Issues { get; init; } = [];
}

public interface IPatchProposalGenerator
{
    PatchProposalGenerationResult Generate(PatchProposalGenerationRequest request);
}

public sealed class ScriptedPatchProposalGenerator : IPatchProposalGenerator
{
    private readonly Func<PatchProposalGenerationRequest, PatchProposalGenerationResult> _script;

    public int CallCount { get; private set; }
    public PatchProposalGenerationRequest? LastRequest { get; private set; }

    public ScriptedPatchProposalGenerator(Func<PatchProposalGenerationRequest, PatchProposalGenerationResult>? script = null)
    {
        _script = script ?? DefaultScript;
    }

    public PatchProposalGenerationResult Generate(PatchProposalGenerationRequest request)
    {
        CallCount++;
        LastRequest = request;
        return _script(request);
    }

    private static PatchProposalGenerationResult DefaultScript(PatchProposalGenerationRequest request) =>
        new()
        {
            Succeeded = true,
            Summary = "Scripted patch proposal generator produced proposal-only evidence.",
            Proposal = new PatchProposalPackage
            {
                PatchProposalId = $"patch-proposal-{request.ManualProposalId}",
                Title = "Proposal-only implementation change",
                Summary = "A controlled proposal describes a small implementation change.",
                Rationale = "The supplied evidence supports drafting a proposal for human review and validation.",
                EvidenceRefs = [$"proposal-goal:{request.ManualProposalId}"],
                IsProposalOnly = true,
                RequiresHumanReview = true,
                RequiresValidation = true,
                AppliesCleanlyClaimed = false,
                CreatesAuthority = false,
                CreatesRuntimeAction = false,
                MutatesSource = false,
                AppliesPatch = false,
                FileChanges =
                [
                    new ProposedFileChange
                    {
                        FileChangeId = $"file-change-{request.ManualProposalId}-1",
                        Path = "src/example.cs",
                        ChangeKind = "Modify",
                        Summary = "Describe the proposed implementation adjustment.",
                        EvidenceRefs = [$"proposal-goal:{request.ManualProposalId}"],
                        IsProposalOnly = true,
                        WritesFile = false,
                        DeletesFile = false,
                        AppliesPatch = false,
                        Hunks =
                        [
                            new ProposedPatchHunk
                            {
                                HunkId = $"hunk-{request.ManualProposalId}-1",
                                Summary = "Illustrative hunk preview for human review.",
                                BeforeSnippet = "before",
                                AfterSnippet = "after",
                                EvidenceRefs = [$"proposal-goal:{request.ManualProposalId}"],
                                ContainsRawPrivateReasoning = false,
                                ContainsSecret = false,
                                ClaimsApplied = false
                            }
                        ]
                    }
                ]
            },
            EvidenceRefs = [$"proposal-goal:{request.ManualProposalId}"]
        };
}

public interface IManualImplementationAgentPatchProposalService
{
    ManualImplementationPatchProposalResult Propose(ManualImplementationPatchProposalRequest request);
}

public sealed class ManualImplementationPatchProposalValidator
{
    public const string ProposalIdRequired = "IMPLEMENTATION_PROPOSAL_ID_REQUIRED";
    public const string RequestRequired = "IMPLEMENTATION_PROPOSAL_REQUEST_REQUIRED";
    public const string GateRequired = "IMPLEMENTATION_PROPOSAL_GATE_REQUIRED";
    public const string UserRequired = "IMPLEMENTATION_PROPOSAL_USER_REQUIRED";
    public const string GoalRequired = "IMPLEMENTATION_PROPOSAL_GOAL_REQUIRED";
    public const string InputRequired = "IMPLEMENTATION_PROPOSAL_INPUT_REQUIRED";
    public const string InputInvalid = "IMPLEMENTATION_PROPOSAL_INPUT_INVALID";
    public const string InputAuthorityBlocked = "IMPLEMENTATION_PROPOSAL_INPUT_AUTHORITY_BLOCKED";
    public const string InputRawReasoningBlocked = "IMPLEMENTATION_PROPOSAL_INPUT_RAW_REASONING_BLOCKED";
    public const string InputSecretBlocked = "IMPLEMENTATION_PROPOSAL_INPUT_SECRET_BLOCKED";
    public const string RequestGateMismatch = "IMPLEMENTATION_PROPOSAL_REQUEST_GATE_MISMATCH";
    public const string GateNotAllowed = "IMPLEMENTATION_PROPOSAL_GATE_NOT_ALLOWED";
    public const string GateActionFlagsUnsafe = "IMPLEMENTATION_PROPOSAL_GATE_ACTION_FLAGS_UNSAFE";
    public const string ToolKindInvalid = "IMPLEMENTATION_PROPOSAL_TOOL_KIND_INVALID";
    public const string RequestTypeInvalid = "IMPLEMENTATION_PROPOSAL_REQUEST_TYPE_INVALID";
    public const string AgentInvalid = "IMPLEMENTATION_PROPOSAL_AGENT_INVALID";
    public const string RequestClaimsApproval = "IMPLEMENTATION_PROPOSAL_REQUEST_CLAIMS_APPROVAL";
    public const string RequestClaimsPermission = "IMPLEMENTATION_PROPOSAL_REQUEST_CLAIMS_PERMISSION";
    public const string RequestContainsResult = "IMPLEMENTATION_PROPOSAL_REQUEST_CONTAINS_RESULT";
    public const string RequestExecutableWithoutGate = "IMPLEMENTATION_PROPOSAL_REQUEST_EXECUTABLE_WITHOUT_GATE";
    public const string ParameterUnsafe = "IMPLEMENTATION_PROPOSAL_PARAMETER_UNSAFE";
    public const string OutputUnsafe = "IMPLEMENTATION_PROPOSAL_OUTPUT_UNSAFE";
    public const string PackageInvalid = "IMPLEMENTATION_PROPOSAL_PACKAGE_INVALID";
    public const string FileChangeInvalid = "IMPLEMENTATION_PROPOSAL_FILE_CHANGE_INVALID";
    public const string HunkInvalid = "IMPLEMENTATION_PROPOSAL_HUNK_INVALID";
    public const string AuditInvalid = "IMPLEMENTATION_PROPOSAL_AUDIT_INVALID";
    public const string ThoughtLedgerInvalid = "IMPLEMENTATION_PROPOSAL_THOUGHT_LEDGER_INVALID";

    private static readonly IReadOnlyList<string> UnsafeTextMarkers =
    [
        "raw" + " prompt",
        "raw" + " completion",
        "chain" + "-of-" + "thought",
        "scratch" + "pad",
        "scratch" + " pad",
        "private" + " reasoning",
        "hidden" + " deliberation",
        "system" + " prompt",
        "developer" + " prompt",
        "approval granted",
        "approved for execution",
        "policy cleared",
        "grant authority",
        "authoritative for action",
        "promote memory",
        "accepted memory",
        "creates collective memory",
        "source mutation occurred",
        "patch applied",
        "file written"
    ];

    private static readonly IReadOnlyList<string> UnsafeParameterMarkers =
    [
        "secret",
        "password",
        "api" + "key",
        "token",
        "&&",
        "||",
        ";",
        "`",
        ">",
        "<",
        "|",
        "Power" + "Shell",
        "cmd" + ".exe",
        "ba" + "sh",
        "g" + "it " + "apply",
        "g" + "it " + "commit",
        "g" + "it " + "push",
        "patch" + " apply",
        "file" + " write"
    ];

    private static readonly IReadOnlySet<string> SupportedChangeKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Add",
        "Modify",
        "Delete",
        "Rename"
    };

    public IReadOnlyList<ManualImplementationPatchProposalIssue> Validate(ManualImplementationPatchProposalRequest request)
    {
        var issues = new List<ManualImplementationPatchProposalIssue>();

        if (request is null)
        {
            AddError(issues, RequestRequired, "Patch proposal request is required.", "Request");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.ManualProposalId))
            AddError(issues, ProposalIdRequired, "ManualProposalId is required.", nameof(request.ManualProposalId));

        if (request.ToolRequest is null)
            AddError(issues, RequestRequired, "ToolRequest is required.", nameof(request.ToolRequest));

        if (request.GateDecision is null)
            AddError(issues, GateRequired, "GateDecision is required.", nameof(request.GateDecision));

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, UserRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));

        if (string.IsNullOrWhiteSpace(request.ProposalGoal))
            AddError(issues, GoalRequired, "ProposalGoal is required.", nameof(request.ProposalGoal));

        ValidateInputs(request.Inputs, issues);

        if (request.ToolRequest is not null)
            ValidateToolRequest(request.ToolRequest, issues);

        if (request.ToolRequest is not null && request.GateDecision is not null)
            ValidateGateDecision(request.ToolRequest, request.GateDecision, issues);

        ValidateParameters(request.Parameters, issues);
        ValidateText([request.ManualProposalId, request.RequestedByUserId, request.ProposalGoal], ParameterUnsafe, "Request", issues);

        return issues;
    }

    public IReadOnlyList<ManualImplementationPatchProposalIssue> ValidateOutput(ManualImplementationPatchProposalOutput output)
    {
        var issues = new List<ManualImplementationPatchProposalIssue>();

        if (output is null)
        {
            AddError(issues, OutputUnsafe, "Patch proposal output is required.", "Output");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(output.OutputId) ||
            string.IsNullOrWhiteSpace(output.Summary) ||
            output.Proposal is null)
        {
            AddError(issues, OutputUnsafe, "OutputId, Summary, and Proposal are required.", "Output");
        }

        if (output.EvidenceRefs.Count == 0 || output.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, OutputUnsafe, "Patch proposal output requires evidence references.", nameof(output.EvidenceRefs));

        if (output.ContainsRawPrivateReasoning ||
            ContainsUnsafeText([output.OutputId, output.Summary, .. output.EvidenceRefs]))
        {
            AddError(issues, OutputUnsafe, "Patch proposal output cannot contain private reasoning or authority claims.", "Output");
        }

        if (output.MutatesSource ||
            output.AppliesPatch ||
            output.WritesFiles ||
            output.DeletesFiles ||
            output.RunsGit ||
            output.CallsExternalSystem ||
            output.SubmitsGitHubReview ||
            output.CreatesPullRequest ||
            output.PromotesMemory ||
            output.CreatesCollectiveMemory ||
            output.WritesWeaviate)
        {
            AddError(issues, OutputUnsafe, "Patch proposal output cannot claim mutation, application, file, version-control, external, GitHub, memory, or index side effects.", "Output");
        }

        if (output.Proposal is not null)
            issues.AddRange(ValidatePackage(output.Proposal));

        return issues;
    }

    public IReadOnlyList<ManualImplementationPatchProposalIssue> ValidatePackage(PatchProposalPackage package)
    {
        var issues = new List<ManualImplementationPatchProposalIssue>();

        if (package is null)
        {
            AddError(issues, PackageInvalid, "Patch proposal package is required.", "Package");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(package.PatchProposalId) ||
            string.IsNullOrWhiteSpace(package.Title) ||
            string.IsNullOrWhiteSpace(package.Summary) ||
            string.IsNullOrWhiteSpace(package.Rationale))
        {
            AddError(issues, PackageInvalid, "PatchProposalId, Title, Summary, and Rationale are required.", "Package");
        }

        if (package.EvidenceRefs.Count == 0 || package.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, PackageInvalid, "Patch proposal package requires evidence references.", nameof(package.EvidenceRefs));

        if (package.FileChanges.Count == 0)
            AddError(issues, PackageInvalid, "Patch proposal package requires at least one proposed file change.", nameof(package.FileChanges));

        if (!package.IsProposalOnly ||
            !package.RequiresHumanReview ||
            !package.RequiresValidation ||
            package.AppliesCleanlyClaimed ||
            package.CreatesAuthority ||
            package.CreatesRuntimeAction ||
            package.MutatesSource ||
            package.AppliesPatch)
        {
            AddError(issues, PackageInvalid, "Patch proposal package must be proposal-only, require review and validation, and create no authority, runtime action, mutation, or application claim.", "Package");
        }

        if (ContainsUnsafeText([package.PatchProposalId, package.Title, package.Summary, package.Rationale, .. package.EvidenceRefs]))
            AddError(issues, PackageInvalid, "Patch proposal package cannot contain private reasoning or authority claims.", "Package");

        foreach (var change in package.FileChanges)
            issues.AddRange(ValidateFileChange(change));

        return issues;
    }

    public IReadOnlyList<ManualImplementationPatchProposalIssue> ValidateFileChange(ProposedFileChange change)
    {
        var issues = new List<ManualImplementationPatchProposalIssue>();

        if (change is null)
        {
            AddError(issues, FileChangeInvalid, "Proposed file change is required.", "FileChange");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(change.FileChangeId) ||
            string.IsNullOrWhiteSpace(change.Path) ||
            string.IsNullOrWhiteSpace(change.ChangeKind) ||
            string.IsNullOrWhiteSpace(change.Summary))
        {
            AddError(issues, FileChangeInvalid, "FileChangeId, Path, ChangeKind, and Summary are required.", "FileChange");
        }

        if (!SupportedChangeKinds.Contains(change.ChangeKind))
            AddError(issues, FileChangeInvalid, "ChangeKind must be Add, Modify, Delete, or Rename.", nameof(change.ChangeKind));

        if (!IsSafeRelativePath(change.Path))
            AddError(issues, FileChangeInvalid, "Proposed file path must be a safe relative path.", nameof(change.Path));

        if (change.EvidenceRefs.Count == 0 || change.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, FileChangeInvalid, "Proposed file change requires evidence references.", nameof(change.EvidenceRefs));

        if (!change.IsProposalOnly || change.WritesFile || change.DeletesFile || change.AppliesPatch)
            AddError(issues, FileChangeInvalid, "Proposed file change must be proposal-only and cannot claim file writes, deletes, or application.", "FileChange");

        if (!string.Equals(change.ChangeKind, "Rename", StringComparison.OrdinalIgnoreCase) && change.Hunks.Count == 0)
            AddError(issues, FileChangeInvalid, "Add, Modify, and Delete proposed file changes require hunks.", nameof(change.Hunks));

        if (ContainsUnsafeText([change.FileChangeId, change.Path, change.ChangeKind, change.Summary, .. change.EvidenceRefs]))
            AddError(issues, FileChangeInvalid, "Proposed file change cannot contain private reasoning or authority claims.", "FileChange");

        foreach (var hunk in change.Hunks)
            issues.AddRange(ValidateHunk(hunk));

        return issues;
    }

    public IReadOnlyList<ManualImplementationPatchProposalIssue> ValidateHunk(ProposedPatchHunk hunk)
    {
        var issues = new List<ManualImplementationPatchProposalIssue>();

        if (hunk is null)
        {
            AddError(issues, HunkInvalid, "Proposed hunk is required.", "Hunk");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(hunk.HunkId) || string.IsNullOrWhiteSpace(hunk.Summary))
            AddError(issues, HunkInvalid, "HunkId and Summary are required.", "Hunk");

        if (string.IsNullOrWhiteSpace(hunk.BeforeSnippet) &&
            string.IsNullOrWhiteSpace(hunk.AfterSnippet) &&
            string.IsNullOrWhiteSpace(hunk.UnifiedDiffPreview))
        {
            AddError(issues, HunkInvalid, "Proposed hunk requires at least one snippet or preview.", "Hunk");
        }

        if (hunk.EvidenceRefs.Count == 0 || hunk.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, HunkInvalid, "Proposed hunk requires evidence references.", nameof(hunk.EvidenceRefs));

        if (hunk.ContainsRawPrivateReasoning || hunk.ContainsSecret || hunk.ClaimsApplied)
            AddError(issues, HunkInvalid, "Proposed hunk cannot contain private reasoning, secrets, or applied-state claims.", "Hunk");

        if (ContainsUnsafeText([hunk.HunkId, hunk.Summary, hunk.BeforeSnippet, hunk.AfterSnippet, hunk.UnifiedDiffPreview, .. hunk.EvidenceRefs]))
            AddError(issues, HunkInvalid, "Proposed hunk cannot contain private reasoning or authority claims.", "Hunk");

        return issues;
    }

    private static void ValidateToolRequest(
        AgentToolRequest toolRequest,
        List<ManualImplementationPatchProposalIssue> issues)
    {
        if (toolRequest.ToolKind != AgentToolKind.PatchProposal)
            AddError(issues, ToolKindInvalid, "Manual ImplementationAgent proposal only supports AgentToolKind.PatchProposal.", nameof(toolRequest.ToolKind));

        if (toolRequest.RequestType != AgentToolRequestType.PatchProposalRequest)
            AddError(issues, RequestTypeInvalid, "Manual ImplementationAgent proposal only supports PatchProposalRequest.", nameof(toolRequest.RequestType));

        if (toolRequest.RiskLevel != AgentToolRiskLevel.Medium)
            AddError(issues, RequestTypeInvalid, "Manual ImplementationAgent proposal requires Medium risk.", nameof(toolRequest.RiskLevel));

        if (!IsImplementationAgent(toolRequest.Actor))
            AddError(issues, AgentInvalid, "Manual ImplementationAgent proposal requires the built-in ImplementationAgent actor with CreateReport.", nameof(toolRequest.Actor));

        if (toolRequest.ClaimsApproval)
            AddError(issues, RequestClaimsApproval, "ToolRequest cannot claim approval.", nameof(toolRequest.ClaimsApproval));

        if (toolRequest.ClaimsExecutionPermission)
            AddError(issues, RequestClaimsPermission, "ToolRequest cannot claim execution permission.", nameof(toolRequest.ClaimsExecutionPermission));

        if (toolRequest.ContainsExecutionResult)
            AddError(issues, RequestContainsResult, "ToolRequest cannot contain execution results.", nameof(toolRequest.ContainsExecutionResult));

        if (toolRequest.IsExecutableWithoutGate)
            AddError(issues, RequestExecutableWithoutGate, "ToolRequest cannot be executable without the gate.", nameof(toolRequest.IsExecutableWithoutGate));
    }

    private static void ValidateGateDecision(
        AgentToolRequest toolRequest,
        AgentToolExecutionGateDecision gateDecision,
        List<ManualImplementationPatchProposalIssue> issues)
    {
        if (!string.Equals(toolRequest.ToolRequestId, gateDecision.ToolRequestId, StringComparison.Ordinal))
            AddError(issues, RequestGateMismatch, "ToolRequestId must match GateDecision.ToolRequestId.", nameof(gateDecision.ToolRequestId));

        if (gateDecision.Decision != AgentToolExecutionGateDecisionType.Allowed ||
            !gateDecision.GrantsExecution ||
            !gateDecision.RequiresExecutor)
        {
            AddError(issues, GateNotAllowed, "GateDecision must be Allowed, grant future execution, and require a future executor.", nameof(gateDecision.Decision));
        }

        if (gateDecision.ExecutesTool ||
            gateDecision.MutatesSource ||
            gateDecision.CallsExternalSystem ||
            gateDecision.SubmitsGitHubReview ||
            gateDecision.PersistsResult ||
            gateDecision.PromotesMemory ||
            gateDecision.CreatesCollectiveMemory ||
            gateDecision.WritesWeaviate)
        {
            AddError(issues, GateActionFlagsUnsafe, "GateDecision must not claim execution, mutation, external, persistence, memory, or index side effects.", nameof(AgentToolExecutionGateDecision));
        }
    }

    private static void ValidateInputs(
        IReadOnlyList<PatchProposalInputRef> inputs,
        List<ManualImplementationPatchProposalIssue> issues)
    {
        if (inputs.Count == 0)
        {
            AddError(issues, InputRequired, "At least one proposal input is required.", nameof(inputs));
            return;
        }

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.InputRefId) ||
                string.IsNullOrWhiteSpace(input.RefType) ||
                string.IsNullOrWhiteSpace(input.RefId))
            {
                AddError(issues, InputInvalid, "InputRefId, RefType, and RefId are required.", nameof(input.InputRefId));
            }

            if (input.IsAuthoritativeForAction)
                AddError(issues, InputAuthorityBlocked, "Proposal inputs cannot be authoritative for action.", nameof(input.IsAuthoritativeForAction));

            if (input.ContainsRawPrivateReasoning || ContainsUnsafeText([input.Summary, input.Source, .. input.EvidenceRefs]))
                AddError(issues, InputRawReasoningBlocked, "Proposal inputs cannot contain private reasoning or authority claims.", nameof(input.ContainsRawPrivateReasoning));

            if (input.ContainsSecret && (!input.IsSanitised || input.EvidenceRefs.Count == 0))
                AddError(issues, InputSecretBlocked, "Secret-bearing proposal inputs must be sanitised and backed by redaction evidence.", nameof(input.ContainsSecret));
        }
    }

    private static bool IsImplementationAgent(AgentToolRequestActor actor)
    {
        if (actor is null)
            return false;

        var definition = AgentDefinitionCatalog.ImplementationAgent;
        return string.Equals(actor.AgentId, definition.AgentId, StringComparison.Ordinal) &&
               string.Equals(actor.AgentName, definition.Name, StringComparison.Ordinal) &&
               actor.AgentKind == AgentKind.ImplementationAgent &&
               actor.ExecutionMode == AgentExecutionMode.SourceMutation &&
               actor.DeclaredCapabilities.Contains(AgentCapability.CreateReport) &&
               !actor.ForbiddenCapabilities.Contains(AgentCapability.CreateReport);
    }

    private static void ValidateParameters(
        IReadOnlyDictionary<string, string> parameters,
        List<ManualImplementationPatchProposalIssue> issues)
    {
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Key) ||
                ContainsUnsafeParameterText(parameter.Key) ||
                ContainsUnsafeParameterText(parameter.Value))
            {
                AddError(issues, ParameterUnsafe, "Parameters are data only and cannot contain secrets, command fragments, or application instructions.", nameof(parameters));
            }
        }
    }

    private static void ValidateText(
        IEnumerable<string?> values,
        string code,
        string field,
        List<ManualImplementationPatchProposalIssue> issues)
    {
        if (ContainsUnsafeText(values))
            AddError(issues, code, "Text cannot contain private reasoning, approval, authority, application, or promotion claims.", field);
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return !Path.IsPathRooted(path) &&
               !path.Contains(':', StringComparison.Ordinal) &&
               !path.Contains('*', StringComparison.Ordinal) &&
               !path.Contains('?', StringComparison.Ordinal) &&
               segments.Length > 0 &&
               segments.All(segment => segment != "." &&
                                       segment != ".." &&
                                       !string.IsNullOrWhiteSpace(segment) &&
                                       !string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsUnsafeText(IEnumerable<string?> values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            UnsafeTextMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static bool ContainsUnsafeParameterText(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        UnsafeParameterMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void AddError(
        List<ManualImplementationPatchProposalIssue> issues,
        string code,
        string message,
        string field) =>
        issues.Add(new ManualImplementationPatchProposalIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed class ManualImplementationAgentPatchProposalService : IManualImplementationAgentPatchProposalService
{
    private readonly IPatchProposalGenerator _generator;
    private readonly ManualImplementationPatchProposalValidator _validator;
    private readonly AgentRunAuditEnvelopeValidator _auditValidator;
    private readonly ThoughtLedgerSafetyValidator _thoughtLedgerValidator;

    public ManualImplementationAgentPatchProposalService()
        : this(
            new ScriptedPatchProposalGenerator(),
            new ManualImplementationPatchProposalValidator(),
            new AgentRunAuditEnvelopeValidator(),
            new ThoughtLedgerSafetyValidator())
    {
    }

    public ManualImplementationAgentPatchProposalService(
        IPatchProposalGenerator generator,
        ManualImplementationPatchProposalValidator? validator = null,
        AgentRunAuditEnvelopeValidator? auditValidator = null,
        ThoughtLedgerSafetyValidator? thoughtLedgerValidator = null)
    {
        _generator = generator;
        _validator = validator ?? new ManualImplementationPatchProposalValidator();
        _auditValidator = auditValidator ?? new AgentRunAuditEnvelopeValidator();
        _thoughtLedgerValidator = thoughtLedgerValidator ?? new ThoughtLedgerSafetyValidator();
    }

    public ManualImplementationPatchProposalResult Propose(ManualImplementationPatchProposalRequest request)
    {
        var issues = _validator.Validate(request);
        if (issues.Count > 0)
            return Rejected(request, DetermineRejectedStatus(issues), issues);

        var generationResult = _generator.Generate(new PatchProposalGenerationRequest
        {
            ManualProposalId = request.ManualProposalId,
            ToolRequest = request.ToolRequest,
            GateDecision = request.GateDecision,
            RequestedByUserId = request.RequestedByUserId,
            ProposalGoal = request.ProposalGoal,
            Inputs = request.Inputs,
            Parameters = request.Parameters,
            RequestedAtUtc = request.RequestedAtUtc
        });

        if (!generationResult.Succeeded || generationResult.Proposal is null)
        {
            var failureIssues = generationResult.Issues.Count > 0
                ? generationResult.Issues
                : [Issue(ManualImplementationPatchProposalValidator.OutputUnsafe, "Generator did not produce a proposal package.", "Generator")];
            return Rejected(request, ManualImplementationPatchProposalStatus.Failed, failureIssues);
        }

        var output = BuildOutput(request.ManualProposalId, generationResult);
        issues = _validator.ValidateOutput(output)
            .Concat(generationResult.Issues)
            .ToArray();

        if (issues.Any(issue => string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
            return Rejected(request, ManualImplementationPatchProposalStatus.InvalidRequest, issues);

        var auditEnvelope = BuildAuditEnvelope(request, output);
        issues = ToManualIssues(_auditValidator.Validate(auditEnvelope), ManualImplementationPatchProposalValidator.AuditInvalid)
            .Concat(ToManualIssues(_thoughtLedgerValidator.Validate(auditEnvelope.ThoughtLedger), ManualImplementationPatchProposalValidator.ThoughtLedgerInvalid))
            .ToArray();

        if (issues.Count > 0)
            return Rejected(request, ManualImplementationPatchProposalStatus.InvalidRequest, issues);

        return new ManualImplementationPatchProposalResult
        {
            Succeeded = true,
            Status = ManualImplementationPatchProposalStatus.Succeeded,
            ManualProposalId = request.ManualProposalId,
            ToolRequestId = request.ToolRequest.ToolRequestId,
            GateDecisionId = request.GateDecision.GateDecisionId,
            Output = output,
            AuditEnvelope = auditEnvelope,
            Issues = []
        };
    }

    private static ManualImplementationPatchProposalOutput BuildOutput(
        string manualProposalId,
        PatchProposalGenerationResult generationResult)
    {
        var evidenceRefs = new HashSet<string>(StringComparer.Ordinal)
        {
            $"manual-proposal:{manualProposalId}"
        };

        foreach (var evidence in generationResult.EvidenceRefs)
            evidenceRefs.Add(evidence);

        foreach (var evidence in generationResult.Proposal!.EvidenceRefs)
            evidenceRefs.Add(evidence);

        foreach (var change in generationResult.Proposal.FileChanges)
        {
            foreach (var evidence in change.EvidenceRefs)
                evidenceRefs.Add(evidence);

            foreach (var hunk in change.Hunks)
            {
                foreach (var evidence in hunk.EvidenceRefs)
                    evidenceRefs.Add(evidence);
            }
        }

        return new ManualImplementationPatchProposalOutput
        {
            OutputId = $"manual-implementation-patch-proposal-output-{manualProposalId}",
            Proposal = generationResult.Proposal,
            Summary = generationResult.Summary,
            EvidenceRefs = evidenceRefs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ContainsRawPrivateReasoning = false,
            MutatesSource = false,
            AppliesPatch = false,
            WritesFiles = false,
            DeletesFiles = false,
            RunsGit = false,
            CallsExternalSystem = false,
            SubmitsGitHubReview = false,
            CreatesPullRequest = false,
            PromotesMemory = false,
            CreatesCollectiveMemory = false,
            WritesWeaviate = false
        };
    }

    private static AgentRunAuditEnvelope BuildAuditEnvelope(
        ManualImplementationPatchProposalRequest request,
        ManualImplementationPatchProposalOutput output)
    {
        var runId = request.ManualProposalId;
        var evidenceRefs = BuildEvidenceRefs(request, output);
        var completedAt = request.RequestedAtUtc.AddMilliseconds(1);

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = runId,
                TenantId = request.ToolRequest.Scope.TenantId,
                ProjectId = request.ToolRequest.Scope.ProjectId,
                CampaignId = request.ToolRequest.Scope.CampaignId ?? "campaign-unspecified",
                RunId = request.ToolRequest.Scope.RunId ?? runId,
                AgentId = AgentDefinitionCatalog.ImplementationAgent.AgentId,
                AgentName = AgentDefinitionCatalog.ImplementationAgent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = AgentRunTriggerType.ManualGovernedRequest,
                Status = AuditAgentRunStatus.Completed,
                RequestSummary = "Manual ImplementationAgent proposal for a gated PatchProposal request.",
                Purpose = "Draft proposal-only implementation evidence after AgentToolExecutionGate allowed a future executor.",
                CreatedAtUtc = request.RequestedAtUtc,
                StartedAtUtc = request.RequestedAtUtc,
                CompletedAtUtc = completedAt
            },
            AgentDefinitionSnapshot = AgentDefinitionCatalog.ImplementationAgent,
            Inputs = BuildInputs(runId, request),
            Outputs =
            [
                new AgentRunOutputRef
                {
                    OutputRefId = $"output-{output.OutputId}",
                    AgentRunId = runId,
                    RefType = "ManualImplementationPatchProposalOutput",
                    RefId = output.OutputId,
                    Summary = "Patch proposal output was recorded as proposal-only evidence.",
                    IsReviewOnly = false,
                    IsProposalOnly = true,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = false,
                    EvidenceRefs = evidenceRefs
                }
            ],
            Steps = BuildSteps(runId, evidenceRefs, request.RequestedAtUtc, completedAt),
            CapabilityUses = BuildCapabilityUses(runId),
            BoundaryDecisions = BuildBoundaryDecisions(runId, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(runId, evidenceRefs, completedAt)
        };
    }

    private static IReadOnlyList<AgentRunInputRef> BuildInputs(
        string runId,
        ManualImplementationPatchProposalRequest request)
    {
        var inputs = new List<AgentRunInputRef>
        {
            new()
            {
                InputRefId = $"input-tool-request-{request.ToolRequest.ToolRequestId}",
                AgentRunId = runId,
                RefType = "AgentToolRequest",
                RefId = request.ToolRequest.ToolRequestId,
                Source = "manual-user-request",
                Summary = "Typed PatchProposal tool request supplied for manual ImplementationAgent proposal.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            },
            new()
            {
                InputRefId = $"input-gate-decision-{request.GateDecision.GateDecisionId}",
                AgentRunId = runId,
                RefType = "AgentToolExecutionGateDecision",
                RefId = request.GateDecision.GateDecisionId,
                Source = "agent-tool-execution-gate",
                Summary = "Gate decision allowed future executor use without applying the proposal.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            },
            new()
            {
                InputRefId = $"input-proposal-goal-{request.ManualProposalId}",
                AgentRunId = runId,
                RefType = "ProposalGoal",
                RefId = request.ManualProposalId,
                Source = "manual-user-request",
                Summary = "Manual proposal goal supplied as bounded data.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            }
        };

        inputs.AddRange(request.Inputs.Select(input => new AgentRunInputRef
        {
            InputRefId = $"input-{input.InputRefId}",
            AgentRunId = runId,
            RefType = input.RefType,
            RefId = input.RefId,
            Source = input.Source,
            Summary = input.Summary,
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        }));

        return inputs;
    }

    private static IReadOnlyList<AgentRunStep> BuildSteps(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt) =>
        [
            Step(runId, 1, AgentRunStepType.Created, "Manual ImplementationAgent patch proposal request was created.", evidenceRefs, startedAt),
            Step(runId, 2, AgentRunStepType.InputBound, "Tool request, gate decision, proposal goal, and bounded inputs were bound.", evidenceRefs, startedAt),
            Step(runId, 3, AgentRunStepType.CapabilityEvaluated, "CreateReport was checked and dangerous capabilities stayed blocked.", evidenceRefs, startedAt),
            Step(runId, 4, AgentRunStepType.BoundaryDecision, "Gate decision allowed future executor use and had not performed side effects.", evidenceRefs, startedAt),
            Step(runId, 5, AgentRunStepType.OutputRecorded, "Patch proposal output was recorded as proposal-only evidence.", evidenceRefs, completedAt),
            Step(runId, 6, AgentRunStepType.Completed, "Manual ImplementationAgent patch proposal completed with safe audit evidence.", evidenceRefs, completedAt)
        ];

    private static AgentRunStep Step(
        string runId,
        int sequence,
        AgentRunStepType stepType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset occurredAtUtc) =>
        new()
        {
            StepId = $"step-{runId}-{sequence:000}",
            AgentRunId = runId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = occurredAtUtc,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            ContainsRawPrivateReasoning = false
        };

    private static IReadOnlyList<AgentCapabilityUseRecord> BuildCapabilityUses(string runId) =>
        [
            CapabilityUse(runId, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.RunTool, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanApproval, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanPromotionDecision, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.BlockExecution, AgentCapabilityUseOutcome.Blocked)
        ];

    private static AgentCapabilityUseRecord CapabilityUse(
        string runId,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome)
    {
        var definition = AgentDefinitionCatalog.ImplementationAgent;
        var declared = definition.Capabilities?.Contains(capability) == true;
        var forbidden = definition.ForbiddenCapabilities?.Contains(capability) == true;

        return new AgentCapabilityUseRecord
        {
            CapabilityUseId = $"capability-{runId}-{capability}",
            AgentRunId = runId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome} for controlled manual ImplementationAgent patch proposal.",
            PolicyDecisionId = $"policy-{runId}",
            BoundaryDecisionId = $"boundary-{runId}-{capability}",
            EvidenceRef = $"evidence-{runId}",
            WasDeclaredOnAgent = declared,
            WasForbiddenOnAgent = forbidden
        };
    }

    private static IReadOnlyList<AgentBoundaryDecision> BuildBoundaryDecisions(
        string runId,
        IReadOnlyList<string> evidenceRefs) =>
        [
            Boundary(runId, "tool-request-validated", AgentBoundaryDecisionType.Evidence, "allow", "Tool request was validated as ImplementationAgent PatchProposal evidence.", evidenceRefs),
            Boundary(runId, "gate-decision-allowed", AgentBoundaryDecisionType.GovernanceDecision, "allow", "Gate decision allowed future executor use and did not perform side effects.", evidenceRefs),
            Boundary(runId, "future-proposal-generator-invoked-manually", AgentBoundaryDecisionType.Capability, "allow", "Controlled patch proposal generator was invoked manually for proposal-only output.", evidenceRefs),
            Boundary(runId, "patch-proposal-output-recorded", AgentBoundaryDecisionType.OutputValidation, "allow", "Patch proposal output was recorded as proposal-only evidence.", evidenceRefs),
            Boundary(runId, "source-mutation-blocked", AgentBoundaryDecisionType.Capability, "block", "Source mutation remained blocked.", evidenceRefs),
            Boundary(runId, "patch-apply-blocked", AgentBoundaryDecisionType.Capability, "block", "Patch application remained blocked.", evidenceRefs),
            Boundary(runId, "file-write-blocked", AgentBoundaryDecisionType.Capability, "block", "File writes remained blocked.", evidenceRefs),
            Boundary(runId, "git-command-blocked", AgentBoundaryDecisionType.Capability, "block", "Version-control commands remained blocked.", evidenceRefs),
            Boundary(runId, "external-effect-blocked", AgentBoundaryDecisionType.Capability, "block", "External effects remained blocked.", evidenceRefs),
            Boundary(runId, "github-submission-blocked", AgentBoundaryDecisionType.Capability, "block", "GitHub submission remained blocked.", evidenceRefs),
            Boundary(runId, "memory-promotion-blocked", AgentBoundaryDecisionType.Capability, "block", "Memory promotion remained blocked.", evidenceRefs),
            Boundary(runId, "weaviate-write-blocked", AgentBoundaryDecisionType.Capability, "block", "Index writing remained blocked.", evidenceRefs),
            Boundary(runId, "thought-ledger-safety", AgentBoundaryDecisionType.ThoughtLedgerSafety, "allow", "ThoughtLedger entries contain safe rationale only.", evidenceRefs)
        ];

    private static AgentBoundaryDecision Boundary(
        string runId,
        string suffix,
        AgentBoundaryDecisionType type,
        string decision,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{runId}-{suffix}",
            AgentRunId = runId,
            BoundaryType = type,
            Decision = decision,
            Reason = reason,
            SourceRefId = $"manual-implementation-{suffix}",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc) =>
        [
            Thought(runId, "request-validated", ThoughtLedgerEntryType.DecisionRationale, "ImplementationAgent manual patch proposal request was validated.", evidenceRefs, recordedAtUtc),
            Thought(runId, "gate-allowed", ThoughtLedgerEntryType.BoundaryDecision, "AgentToolExecutionGate decision allowed future executor use.", evidenceRefs, recordedAtUtc),
            Thought(runId, "generator-invoked", ThoughtLedgerEntryType.EvidenceUsed, "Controlled patch proposal generator was invoked manually.", evidenceRefs, recordedAtUtc),
            Thought(runId, "output-recorded", ThoughtLedgerEntryType.OutputRationale, "Patch proposal output was recorded as proposal-only evidence.", evidenceRefs, recordedAtUtc),
            Thought(runId, "dangerous-blocked", ThoughtLedgerEntryType.BoundaryDecision, "Dangerous capabilities remained blocked.", evidenceRefs, recordedAtUtc),
            Thought(runId, "no-dangerous-effects", ThoughtLedgerEntryType.FollowUp, "No source mutation, file write, patch application, version-control command, external effect, GitHub submission, memory promotion, or index write occurred.", evidenceRefs, recordedAtUtc)
        ];

    private static AuditThoughtLedgerEntry Thought(
        string runId,
        string suffix,
        ThoughtLedgerEntryType type,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{runId}-{suffix}",
            AgentRunId = runId,
            EntryType = type,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false,
            RecordedAtUtc = recordedAtUtc
        };

    private static IReadOnlyList<string> BuildEvidenceRefs(
        ManualImplementationPatchProposalRequest request,
        ManualImplementationPatchProposalOutput output)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal)
        {
            request.ToolRequest.ToolRequestId,
            request.GateDecision.GateDecisionId,
            request.ManualProposalId,
            output.OutputId,
            output.Proposal.PatchProposalId
        };

        foreach (var input in request.Inputs)
        {
            refs.Add(input.RefId);
            foreach (var evidenceRef in input.EvidenceRefs)
                refs.Add(evidenceRef);
        }

        foreach (var evidence in request.ToolRequest.Evidence)
            refs.Add(evidence.EvidenceId);

        foreach (var evidenceRef in output.EvidenceRefs)
            refs.Add(evidenceRef);

        return refs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static ManualImplementationPatchProposalStatus DetermineRejectedStatus(
        IReadOnlyList<ManualImplementationPatchProposalIssue> issues) =>
        issues.Any(issue => issue.Code is
            ManualImplementationPatchProposalValidator.GateNotAllowed or
            ManualImplementationPatchProposalValidator.GateActionFlagsUnsafe or
            ManualImplementationPatchProposalValidator.RequestGateMismatch)
            ? ManualImplementationPatchProposalStatus.Blocked
            : ManualImplementationPatchProposalStatus.InvalidRequest;

    private static ManualImplementationPatchProposalResult Rejected(
        ManualImplementationPatchProposalRequest? request,
        ManualImplementationPatchProposalStatus status,
        IReadOnlyList<ManualImplementationPatchProposalIssue> issues) =>
        new()
        {
            Succeeded = false,
            Status = status,
            ManualProposalId = string.IsNullOrWhiteSpace(request?.ManualProposalId) ? "missing-manual-proposal-id" : request.ManualProposalId,
            ToolRequestId = request?.ToolRequest?.ToolRequestId,
            GateDecisionId = request?.GateDecision?.GateDecisionId,
            Issues = issues
        };

    private static ManualImplementationPatchProposalIssue Issue(
        string code,
        string message,
        string field) =>
        new()
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        };

    private static IReadOnlyList<ManualImplementationPatchProposalIssue> ToManualIssues(
        IReadOnlyList<AgentDefinitionValidationIssue> issues,
        string code) =>
        issues.Select(issue => new ManualImplementationPatchProposalIssue
        {
            Code = code,
            Severity = issue.Severity,
            Message = $"{issue.Code}: {issue.Message}",
            Field = code
        }).ToArray();
}
