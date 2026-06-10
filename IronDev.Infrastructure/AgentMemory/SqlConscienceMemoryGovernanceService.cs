using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlConscienceMemoryGovernanceService : IConscienceMemoryGovernanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlConscienceMemoryGovernanceService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<MemoryGovernanceCheckResult> CheckAsync(
        MemoryGovernanceCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<MemoryGovernanceIssue>();
        var scope = request.Scope;

        ValidateRequestShape(request, issues);

        if (issues.Any(issue => issue.Severity == MemoryGovernanceIssueSeverity.Critical))
            return BuildResult(request, issues);

        using var connection = _connectionFactory.CreateConnection();

        var influenceRows = await LoadInfluenceRowsAsync(connection, request, cancellationToken).ConfigureAwait(false);
        var memoryRows = await LoadMemoryRowsAsync(connection, request, influenceRows, cancellationToken).ConfigureAwait(false);
        var handoffRows = await LoadHandoffRowsAsync(connection, request, cancellationToken).ConfigureAwait(false);

        var memoryById = memoryRows.ToDictionary(row => row.MemoryItemId, StringComparer.Ordinal);
        var influenceById = influenceRows.ToDictionary(row => row.InfluenceId, StringComparer.Ordinal);
        var handoffById = handoffRows.ToDictionary(row => row.HandoffMemorySliceId, StringComparer.Ordinal);

        foreach (var artifact in request.ReferencedArtifacts.Where(HasArtifactReference))
        {
            if (!string.IsNullOrWhiteSpace(artifact.MemoryItemId))
            {
                ValidateMemoryReference(request, artifact, memoryById, influenceRows, issues);
            }

            if (!string.IsNullOrWhiteSpace(artifact.InfluenceId))
            {
                ValidateInfluenceReference(request, artifact, influenceById, memoryById, issues);
            }

            if (!string.IsNullOrWhiteSpace(artifact.HandoffMemorySliceId))
            {
                ValidateHandoffReference(request, artifact, handoffById, issues);
            }
        }

        if (request.ActionType == MemoryGovernanceActionType.SourceMutation &&
            !issues.Any(issue => issue.Severity is MemoryGovernanceIssueSeverity.High or MemoryGovernanceIssueSeverity.Critical))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.SourceMutationRequiresApprovalBeyondMemory,
                Severity = MemoryGovernanceIssueSeverity.Warning,
                Summary = "Memory governance cannot authorize source mutation; separate approval and policy gates are required."
            });
        }

        if (request.ActionType == MemoryGovernanceActionType.ExternalEffect &&
            !issues.Any(issue => issue.Severity is MemoryGovernanceIssueSeverity.High or MemoryGovernanceIssueSeverity.Critical))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.ExternalEffectRequiresApprovalBeyondMemory,
                Severity = MemoryGovernanceIssueSeverity.Warning,
                Summary = "Memory governance cannot authorize external effects; separate approval and policy gates are required."
            });
        }

        _ = scope;
        return BuildResult(request, issues);
    }

    private static void ValidateRequestShape(MemoryGovernanceCheckRequest request, List<MemoryGovernanceIssue> issues)
    {
        if (request.Scope is null ||
            string.IsNullOrWhiteSpace(request.Scope.TenantId) ||
            string.IsNullOrWhiteSpace(request.Scope.ProjectId) ||
            string.IsNullOrWhiteSpace(request.Scope.CampaignId) ||
            string.IsNullOrWhiteSpace(request.Scope.RunId) ||
            string.IsNullOrWhiteSpace(request.Scope.AgentId))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MissingScope,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                Summary = "Memory governance checks require complete tenant, project, campaign, run, and agent scope."
            });
        }

        if (string.IsNullOrWhiteSpace(request.DecisionId))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MissingDecisionId,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                Summary = "Memory governance checks require a decision ID."
            });
        }

        if (request.ReferencedArtifacts is null ||
            request.ReferencedArtifacts.Count == 0 ||
            request.ReferencedArtifacts.All(artifact => artifact is null || !HasArtifactReference(artifact)))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MissingReferencedArtifacts,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                Summary = "Memory governance checks require at least one memory, influence, or handoff reference."
            });
        }
    }

    private async Task<IReadOnlyList<MemoryRow>> LoadMemoryRowsAsync(
        System.Data.IDbConnection connection,
        MemoryGovernanceCheckRequest request,
        IReadOnlyList<InfluenceRow> influenceRows,
        CancellationToken cancellationToken)
    {
        var memoryItemIds = request.ReferencedArtifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.MemoryItemId))
            .Select(artifact => artifact.MemoryItemId!.Trim())
            .Concat(influenceRows.Select(row => row.MemoryItemId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (memoryItemIds.Length == 0)
            return Array.Empty<MemoryRow>();

        return (await connection.QueryAsync<MemoryRow>(new CommandDefinition(
            """
            SELECT
                MemoryItemId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryType,
                AuthorityLevel,
                Confidence,
                ExpiresAtUtc,
                CurrentEventType
            FROM agent.vwAgentLocalMemoryCurrentState
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND AgentId = @AgentId
              AND MemoryItemId IN @MemoryItemIds;
            """,
            new
            {
                request.Scope.TenantId,
                request.Scope.ProjectId,
                request.Scope.CampaignId,
                request.Scope.RunId,
                request.Scope.AgentId,
                MemoryItemIds = memoryItemIds
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();
    }

    private async Task<IReadOnlyList<InfluenceRow>> LoadInfluenceRowsAsync(
        System.Data.IDbConnection connection,
        MemoryGovernanceCheckRequest request,
        CancellationToken cancellationToken)
    {
        var influenceIds = request.ReferencedArtifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.InfluenceId))
            .Select(artifact => artifact.InfluenceId!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var memoryItemIds = request.ReferencedArtifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.MemoryItemId))
            .Select(artifact => artifact.MemoryItemId!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (influenceIds.Length == 0 && memoryItemIds.Length == 0)
            return Array.Empty<InfluenceRow>();

        return (await connection.QueryAsync<InfluenceRow>(new CommandDefinition(
            """
            SELECT
                InfluenceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryItemId,
                DecisionId,
                InfluenceType,
                Confidence,
                ThoughtLedgerEntryId
            FROM agent.AgentMemoryInfluenceRecord
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND AgentId = @AgentId
              AND
              (
                  (InfluenceId IN @InfluenceIds)
                  OR
                  (DecisionId = @DecisionId AND MemoryItemId IN @MemoryItemIds)
              );
            """,
            new
            {
                request.Scope.TenantId,
                request.Scope.ProjectId,
                request.Scope.CampaignId,
                request.Scope.RunId,
                request.Scope.AgentId,
                request.DecisionId,
                InfluenceIds = influenceIds.Length == 0 ? ["__none__"] : influenceIds,
                MemoryItemIds = memoryItemIds.Length == 0 ? ["__none__"] : memoryItemIds
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();
    }

    private async Task<IReadOnlyList<HandoffRow>> LoadHandoffRowsAsync(
        System.Data.IDbConnection connection,
        MemoryGovernanceCheckRequest request,
        CancellationToken cancellationToken)
    {
        var handoffIds = request.ReferencedArtifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.HandoffMemorySliceId))
            .Select(artifact => artifact.HandoffMemorySliceId!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (handoffIds.Length == 0)
            return Array.Empty<HandoffRow>();

        return (await connection.QueryAsync<HandoffRow>(new CommandDefinition(
            """
            SELECT
                HandoffMemorySliceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                SourceAgentId,
                TargetAgentId,
                MemoryItemIdsJson,
                MemorySnapshotsJson,
                AllowedUse,
                EvidenceRefsJson,
                Confidence,
                InfluenceIdsJson,
                DecisionId,
                ThoughtLedgerEntryId,
                CorrelationId,
                CreatedAtUtc,
                ExpiresAtUtc
            FROM agent.AgentMemoryHandoffSlice
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
              AND HandoffMemorySliceId IN @HandoffIds;
            """,
            new
            {
                request.Scope.TenantId,
                request.Scope.ProjectId,
                request.Scope.CampaignId,
                request.Scope.RunId,
                HandoffIds = handoffIds
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();
    }

    private static void ValidateMemoryReference(
        MemoryGovernanceCheckRequest request,
        MemoryGovernanceReferencedArtifact artifact,
        IReadOnlyDictionary<string, MemoryRow> memoryById,
        IReadOnlyList<InfluenceRow> influenceRows,
        List<MemoryGovernanceIssue> issues)
    {
        var memoryItemId = artifact.MemoryItemId!.Trim();

        if (!memoryById.TryGetValue(memoryItemId, out var memory))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MemoryNotFoundInScope,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                MemoryItemId = memoryItemId,
                Summary = "Referenced memory was not found in the acting agent memory scope."
            });
            return;
        }

        ValidateMemoryStatus(request, memory, issues);
        ValidateMemoryTypeActionRules(request, memory, issues);
        ValidateInfluenceRequirement(request, memoryItemId, influenceRows, issues);
    }

    private static void ValidateMemoryStatus(
        MemoryGovernanceCheckRequest request,
        MemoryRow memory,
        List<MemoryGovernanceIssue> issues)
    {
        var status = ToLifecycleStatus(memory.CurrentEventType, memory.ExpiresAtUtc, request.RequestedAt);

        if ((MemoryAuthorityLevel)memory.AuthorityLevel == MemoryAuthorityLevel.SystemRule)
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MemorySystemRuleUsedAsLocalMemory,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                MemoryItemId = memory.MemoryItemId,
                Summary = "SystemRule memory cannot be used through the local agent memory governance path."
            });
        }

        var code = status switch
        {
            MemoryLifecycleStatus.Expired => MemoryGovernanceIssueCode.MemoryExpired,
            MemoryLifecycleStatus.Invalidated => MemoryGovernanceIssueCode.MemoryInvalidated,
            MemoryLifecycleStatus.Superseded => MemoryGovernanceIssueCode.MemorySuperseded,
            MemoryLifecycleStatus.Rejected => MemoryGovernanceIssueCode.MemoryRejected,
            MemoryLifecycleStatus.Accepted => MemoryGovernanceIssueCode.MemoryAcceptedButNotLocalAuthority,
            _ => (MemoryGovernanceIssueCode?)null
        };

        if (code is not null)
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = code.Value,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                MemoryItemId = memory.MemoryItemId,
                Summary = $"Referenced memory is {status} and cannot justify this action."
            });
        }

        if (memory.Confidence < 0.5m)
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.LowConfidenceMemoryUse,
                Severity = MemoryGovernanceIssueSeverity.Warning,
                MemoryItemId = memory.MemoryItemId,
                Summary = "Referenced memory has confidence below 0.5."
            });
        }
    }

    private static void ValidateMemoryTypeActionRules(
        MemoryGovernanceCheckRequest request,
        MemoryRow memory,
        List<MemoryGovernanceIssue> issues)
    {
        var memoryType = (AgentMemoryType)memory.MemoryType;
        var status = ToLifecycleStatus(memory.CurrentEventType, memory.ExpiresAtUtc, request.RequestedAt);

        if (memoryType == AgentMemoryType.CandidatePattern)
        {
            if (request.ActionType is MemoryGovernanceActionType.SourceMutation or MemoryGovernanceActionType.ExternalEffect)
            {
                issues.Add(new MemoryGovernanceIssue
                {
                    Code = MemoryGovernanceIssueCode.CandidatePatternCannotJustifyExternalEffect,
                    Severity = MemoryGovernanceIssueSeverity.Critical,
                    MemoryItemId = memory.MemoryItemId,
                    Summary = "CandidatePattern memory cannot justify source mutation or external effects."
                });
            }
            else if (request.ActionType is MemoryGovernanceActionType.ToolCallJustification or MemoryGovernanceActionType.HandoffCreation)
            {
                issues.Add(new MemoryGovernanceIssue
                {
                    Code = MemoryGovernanceIssueCode.CandidatePatternCannotJustifyExternalEffect,
                    Severity = MemoryGovernanceIssueSeverity.Warning,
                    MemoryItemId = memory.MemoryItemId,
                    Summary = "CandidatePattern memory needs review before it is used for tool justification or handoff creation."
                });
            }
        }

        if (status == MemoryLifecycleStatus.ProposedForReview)
        {
            if (request.ActionType is MemoryGovernanceActionType.SourceMutation or MemoryGovernanceActionType.ExternalEffect)
            {
                issues.Add(new MemoryGovernanceIssue
                {
                    Code = MemoryGovernanceIssueCode.ProposedMemoryRequiresVerification,
                    Severity = MemoryGovernanceIssueSeverity.Critical,
                    MemoryItemId = memory.MemoryItemId,
                    Summary = "ProposedForReview memory cannot justify source mutation or external effects."
                });
            }
            else if (request.ActionType is MemoryGovernanceActionType.ContextUse or
                     MemoryGovernanceActionType.AvoidRepeat or
                     MemoryGovernanceActionType.CriticFinding or
                     MemoryGovernanceActionType.ToolCallJustification or
                     MemoryGovernanceActionType.HandoffCreation)
            {
                issues.Add(new MemoryGovernanceIssue
                {
                    Code = MemoryGovernanceIssueCode.ProposedMemoryRequiresVerification,
                    Severity = MemoryGovernanceIssueSeverity.Warning,
                    MemoryItemId = memory.MemoryItemId,
                    Summary = "ProposedForReview memory should be treated as unverified for this action."
                });
            }
        }
    }

    private static void ValidateInfluenceRequirement(
        MemoryGovernanceCheckRequest request,
        string memoryItemId,
        IReadOnlyList<InfluenceRow> influenceRows,
        List<MemoryGovernanceIssue> issues)
    {
        if (!request.InfluenceRecordRequired || !RequiresInfluenceRecord(request.ActionType))
            return;

        var matching = influenceRows.Any(row =>
            string.Equals(row.MemoryItemId, memoryItemId, StringComparison.Ordinal) &&
            string.Equals(row.DecisionId, request.DecisionId, StringComparison.Ordinal));

        if (matching)
            return;

        var severity = request.ActionType switch
        {
            MemoryGovernanceActionType.ToolCallJustification or
            MemoryGovernanceActionType.SourceMutation or
            MemoryGovernanceActionType.ExternalEffect => MemoryGovernanceIssueSeverity.Critical,
            _ => MemoryGovernanceIssueSeverity.Warning
        };

        issues.Add(new MemoryGovernanceIssue
        {
            Code = MemoryGovernanceIssueCode.MissingInfluenceRecord,
            Severity = severity,
            MemoryItemId = memoryItemId,
            Summary = "Referenced memory does not have a matching influence record for this decision."
        });
    }

    private static void ValidateInfluenceReference(
        MemoryGovernanceCheckRequest request,
        MemoryGovernanceReferencedArtifact artifact,
        IReadOnlyDictionary<string, InfluenceRow> influenceById,
        IReadOnlyDictionary<string, MemoryRow> memoryById,
        List<MemoryGovernanceIssue> issues)
    {
        var influenceId = artifact.InfluenceId!.Trim();

        if (!influenceById.TryGetValue(influenceId, out var influence))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.InfluenceNotFoundInScope,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                InfluenceId = influenceId,
                Summary = "Referenced influence record was not found in the acting agent memory scope."
            });
            return;
        }

        if (!string.Equals(influence.DecisionId, request.DecisionId, StringComparison.Ordinal))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.InfluenceDecisionMismatch,
                Severity = MemoryGovernanceIssueSeverity.High,
                MemoryItemId = influence.MemoryItemId,
                InfluenceId = influence.InfluenceId,
                Summary = "Referenced influence record does not match the requested decision."
            });
        }

        if (!string.IsNullOrWhiteSpace(artifact.MemoryItemId) &&
            !string.Equals(influence.MemoryItemId, artifact.MemoryItemId, StringComparison.Ordinal))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.InfluenceMemoryMismatch,
                Severity = MemoryGovernanceIssueSeverity.High,
                MemoryItemId = artifact.MemoryItemId,
                InfluenceId = influence.InfluenceId,
                Summary = "Referenced influence record does not match the referenced memory item."
            });
        }

        if (!memoryById.TryGetValue(influence.MemoryItemId, out var memory))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.MemoryNotFoundInScope,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                MemoryItemId = influence.MemoryItemId,
                InfluenceId = influence.InfluenceId,
                Summary = "Referenced influence memory was not found in the acting agent memory scope."
            });
            return;
        }

        ValidateMemoryStatus(request, memory, issues);
        ValidateMemoryTypeActionRules(request, memory, issues);
    }

    private static void ValidateHandoffReference(
        MemoryGovernanceCheckRequest request,
        MemoryGovernanceReferencedArtifact artifact,
        IReadOnlyDictionary<string, HandoffRow> handoffById,
        List<MemoryGovernanceIssue> issues)
    {
        var handoffId = artifact.HandoffMemorySliceId!.Trim();

        if (!handoffById.TryGetValue(handoffId, out var handoff))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.HandoffNotFound,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                HandoffMemorySliceId = handoffId,
                Summary = "Referenced handoff was not found in this run scope."
            });
            return;
        }

        if (!string.Equals(handoff.TargetAgentId, request.Scope.AgentId, StringComparison.Ordinal))
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.HandoffNotAddressedToAgent,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                HandoffMemorySliceId = handoffId,
                Summary = "Referenced handoff is not addressed to the acting agent."
            });
        }

        if (handoff.ExpiresAtUtc is not null && ToUtc(handoff.ExpiresAtUtc.Value) <= request.RequestedAt)
        {
            issues.Add(new MemoryGovernanceIssue
            {
                Code = MemoryGovernanceIssueCode.HandoffExpired,
                Severity = MemoryGovernanceIssueSeverity.Critical,
                HandoffMemorySliceId = handoffId,
                Summary = "Referenced handoff is expired."
            });
        }

        ValidateHandoffAllowedUse(request.ActionType, handoff, issues);
        ValidateHandoffSnapshots(handoff, issues);
    }

    private static void ValidateHandoffAllowedUse(
        MemoryGovernanceActionType actionType,
        HandoffRow handoff,
        List<MemoryGovernanceIssue> issues)
    {
        var allowedUse = (HandoffMemoryAllowedUse)handoff.AllowedUse;
        var evidenceRefs = DeserializeEvidenceRefs(handoff.EvidenceRefsJson);
        var influenceIds = DeserializeStringArray(handoff.InfluenceIdsJson);
        var hasVerificationEvidence = evidenceRefs.Count > 0 || influenceIds.Count > 0;

        MemoryGovernanceIssueSeverity? severity = allowedUse switch
        {
            HandoffMemoryAllowedUse.ContextOnly => actionType switch
            {
                MemoryGovernanceActionType.ContextUse => null,
                MemoryGovernanceActionType.AvoidRepeat => MemoryGovernanceIssueSeverity.Warning,
                MemoryGovernanceActionType.ToolCallJustification or
                MemoryGovernanceActionType.SourceMutation or
                MemoryGovernanceActionType.ExternalEffect => MemoryGovernanceIssueSeverity.Critical,
                _ => MemoryGovernanceIssueSeverity.Warning
            },
            HandoffMemoryAllowedUse.AvoidRepeat => actionType switch
            {
                MemoryGovernanceActionType.ContextUse or
                MemoryGovernanceActionType.AvoidRepeat => null,
                MemoryGovernanceActionType.ToolCallJustification => MemoryGovernanceIssueSeverity.Warning,
                MemoryGovernanceActionType.SourceMutation or
                MemoryGovernanceActionType.ExternalEffect => MemoryGovernanceIssueSeverity.Critical,
                _ => MemoryGovernanceIssueSeverity.Warning
            },
            HandoffMemoryAllowedUse.NeedsVerification => actionType switch
            {
                MemoryGovernanceActionType.ContextUse or
                MemoryGovernanceActionType.AvoidRepeat => MemoryGovernanceIssueSeverity.Warning,
                MemoryGovernanceActionType.ToolCallJustification => hasVerificationEvidence
                    ? MemoryGovernanceIssueSeverity.Warning
                    : MemoryGovernanceIssueSeverity.Critical,
                MemoryGovernanceActionType.SourceMutation or
                MemoryGovernanceActionType.ExternalEffect => MemoryGovernanceIssueSeverity.Critical,
                _ => MemoryGovernanceIssueSeverity.Warning
            },
            HandoffMemoryAllowedUse.ProposalSupport => actionType switch
            {
                MemoryGovernanceActionType.ProposalCreation or
                MemoryGovernanceActionType.Escalation or
                MemoryGovernanceActionType.ContextUse => null,
                MemoryGovernanceActionType.ToolCallJustification => MemoryGovernanceIssueSeverity.Warning,
                MemoryGovernanceActionType.SourceMutation or
                MemoryGovernanceActionType.ExternalEffect => MemoryGovernanceIssueSeverity.Critical,
                _ => MemoryGovernanceIssueSeverity.Warning
            },
            _ => MemoryGovernanceIssueSeverity.Critical
        };

        if (severity is null)
            return;

        issues.Add(new MemoryGovernanceIssue
        {
            Code = MemoryGovernanceIssueCode.HandoffAllowedUseViolation,
            Severity = severity.Value,
            HandoffMemorySliceId = handoff.HandoffMemorySliceId,
            Summary = $"Handoff allowed use '{allowedUse}' is not sufficient for action '{actionType}'."
        });
    }

    private static void ValidateHandoffSnapshots(HandoffRow handoff, List<MemoryGovernanceIssue> issues)
    {
        foreach (var snapshot in DeserializeSnapshots(handoff.MemorySnapshotsJson))
        {
            if (snapshot.StatusAtHandoff is MemoryLifecycleStatus.Expired or
                MemoryLifecycleStatus.Invalidated or
                MemoryLifecycleStatus.Superseded or
                MemoryLifecycleStatus.Rejected or
                MemoryLifecycleStatus.Accepted)
            {
                issues.Add(new MemoryGovernanceIssue
                {
                    Code = MemoryGovernanceIssueCode.HandoffSourceMemoryTerminalAtHandoff,
                    Severity = MemoryGovernanceIssueSeverity.Critical,
                    MemoryItemId = snapshot.MemoryItemId,
                    HandoffMemorySliceId = handoff.HandoffMemorySliceId,
                    Summary = "Handoff snapshot contains terminal source memory."
                });
            }
        }
    }

    private static MemoryGovernanceCheckResult BuildResult(
        MemoryGovernanceCheckRequest request,
        IReadOnlyList<MemoryGovernanceIssue> issues)
    {
        var decision = issues.Any(issue => issue.Severity is MemoryGovernanceIssueSeverity.Critical or MemoryGovernanceIssueSeverity.High)
            ? MemoryGovernanceDecision.Block
            : issues.Any(issue => issue.Severity == MemoryGovernanceIssueSeverity.Warning)
                ? MemoryGovernanceDecision.Warn
                : MemoryGovernanceDecision.Allow;

        return new MemoryGovernanceCheckResult
        {
            GovernanceCheckId = BuildGovernanceCheckId(request),
            Scope = request.Scope,
            DecisionId = request.DecisionId ?? string.Empty,
            ActionType = request.ActionType,
            Decision = decision,
            Issues = issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code)
                .ThenBy(issue => issue.MemoryItemId, StringComparer.Ordinal)
                .ThenBy(issue => issue.InfluenceId, StringComparer.Ordinal)
                .ThenBy(issue => issue.HandoffMemorySliceId, StringComparer.Ordinal)
                .ToArray(),
            CheckedAt = request.RequestedAt,
            CorrelationId = request.CorrelationId,
            ThoughtLedgerEntryId = request.ReferencedArtifacts?
                .Select(artifact => artifact.ThoughtLedgerEntryId)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        };
    }

    private static string BuildGovernanceCheckId(MemoryGovernanceCheckRequest request)
    {
        var artifactKey = request.ReferencedArtifacts is null
            ? string.Empty
            : string.Join("|", request.ReferencedArtifacts.Select(artifact =>
                $"{artifact.MemoryItemId}:{artifact.InfluenceId}:{artifact.HandoffMemorySliceId}:{artifact.DecisionId}:{artifact.ThoughtLedgerEntryId}"));

        var raw = $"{request.Scope?.TenantId}:{request.Scope?.ProjectId}:{request.Scope?.CampaignId}:{request.Scope?.RunId}:{request.Scope?.AgentId}:{request.DecisionId}:{request.ActionType}:{request.RequestedAt.UtcDateTime:O}:{artifactKey}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return "memory-governance-" + hash[..24];
    }

    private static bool RequiresInfluenceRecord(MemoryGovernanceActionType actionType) =>
        actionType is MemoryGovernanceActionType.ToolCallJustification or
            MemoryGovernanceActionType.HandoffCreation or
            MemoryGovernanceActionType.ProposalCreation or
            MemoryGovernanceActionType.Escalation or
            MemoryGovernanceActionType.CriticFinding or
            MemoryGovernanceActionType.SourceMutation or
            MemoryGovernanceActionType.ExternalEffect;

    private static bool HasArtifactReference(MemoryGovernanceReferencedArtifact artifact) =>
        !string.IsNullOrWhiteSpace(artifact.MemoryItemId) ||
        !string.IsNullOrWhiteSpace(artifact.InfluenceId) ||
        !string.IsNullOrWhiteSpace(artifact.HandoffMemorySliceId);

    private static MemoryLifecycleStatus ToLifecycleStatus(
        int? eventType,
        DateTime? expiresAtUtc,
        DateTimeOffset requestedAt)
    {
        if (expiresAtUtc is not null && ToUtc(expiresAtUtc.Value) <= requestedAt)
            return MemoryLifecycleStatus.Expired;

        return eventType switch
        {
            (int)AgentLocalMemoryEventType.Superseded => MemoryLifecycleStatus.Superseded,
            (int)AgentLocalMemoryEventType.Expired => MemoryLifecycleStatus.Expired,
            (int)AgentLocalMemoryEventType.Invalidated => MemoryLifecycleStatus.Invalidated,
            (int)AgentLocalMemoryEventType.ProposedForReview => MemoryLifecycleStatus.ProposedForReview,
            (int)AgentLocalMemoryEventType.Rejected => MemoryLifecycleStatus.Rejected,
            (int)AgentLocalMemoryEventType.Accepted => MemoryLifecycleStatus.Accepted,
            _ => MemoryLifecycleStatus.Active
        };
    }

    private static IReadOnlyList<string> DeserializeStringArray(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? Array.Empty<string>();

    private static IReadOnlyList<EvidenceRef> DeserializeEvidenceRefs(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<EvidenceRef>()
            : JsonSerializer.Deserialize<IReadOnlyList<EvidenceRef>>(json, JsonOptions) ?? Array.Empty<EvidenceRef>();

    private static IReadOnlyList<HandoffMemoryItemSnapshot> DeserializeSnapshots(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<HandoffMemoryItemSnapshot>()
            : JsonSerializer.Deserialize<IReadOnlyList<HandoffMemoryItemSnapshot>>(json, JsonOptions) ?? Array.Empty<HandoffMemoryItemSnapshot>();

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class MemoryRow
    {
        public string MemoryItemId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public int MemoryType { get; set; }
        public int AuthorityLevel { get; set; }
        public decimal Confidence { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public int? CurrentEventType { get; set; }
    }

    private sealed class InfluenceRow
    {
        public string InfluenceId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string MemoryItemId { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public int InfluenceType { get; set; }
        public decimal Confidence { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
    }

    private sealed class HandoffRow
    {
        public string HandoffMemorySliceId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string SourceAgentId { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public string MemoryItemIdsJson { get; set; } = "[]";
        public string MemorySnapshotsJson { get; set; } = "[]";
        public int AllowedUse { get; set; }
        public string EvidenceRefsJson { get; set; } = "[]";
        public decimal Confidence { get; set; }
        public string? InfluenceIdsJson { get; set; }
        public string? DecisionId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
