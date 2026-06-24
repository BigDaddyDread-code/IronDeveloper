using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD20AuthorityWarningFormatterHardeningTests
{
    private const string TenantId = "tenant-d20";
    private const string ProjectId = "project-d20";
    private const string OperationId = "op_000000000000d020";
    private const string CorrelationId = "corr_000000000000d020";
    private const string Source = "d20-test";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T11:00:00Z");

    [TestMethod]
    public void InvalidRequest_FailsClosedWithoutAuthority()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            OperationId = "",
            ReadEnvelope = null,
            NextSafeActionFormatterResult = null,
            WarningFacts = null
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(OperationStatusAuthorityWarningFormatterStatus.InvalidRequest, result.FormatterStatus);
        AssertContains(result.Issues, "OperationStatusAuthorityWarningOperationIdRequired");
        AssertContains(result.Issues, "OperationStatusAuthorityWarningInputRequired");
        AssertLine(result, OperationStatusAuthorityWarningKind.ManualAuthorityReviewRequired);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("tenant", "OperationStatusAuthorityWarningTenantIdRequired")]
    [DataRow("project", "OperationStatusAuthorityWarningProjectIdRequired")]
    [DataRow("operation", "OperationStatusAuthorityWarningOperationIdRequired")]
    [DataRow("operation-invalid", "OperationStatusAuthorityWarningOperationIdInvalid")]
    [DataRow("correlation-invalid", "OperationStatusAuthorityWarningCorrelationIdInvalid")]
    [DataRow("as-of", "OperationStatusAuthorityWarningAsOfUtcRequired")]
    [DataRow("source", "OperationStatusAuthorityWarningSourceRequired")]
    [DataRow("input", "OperationStatusAuthorityWarningInputRequired")]
    public void RequestRequiredFields_FailClosed(string field, string expectedIssue)
    {
        var request = field switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run_123" },
            "correlation-invalid" => Request() with { CorrelationId = "corr bad" },
            "as-of" => Request() with { AsOfUtc = default },
            "source" => Request() with { Source = "" },
            "input" => Request() with { ReadEnvelope = null, NextSafeActionFormatterResult = null, WarningFacts = null },
            _ => Request()
        };

        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateRequest(request);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, expectedIssue);
        AssertContains(validation.Warnings, "authority warning formatting is not authority");
    }

    [TestMethod]
    public void CrossScopeReadEnvelope_FailsClosed()
    {
        var envelope = OperationStatusReadEnvelopeFactory.NotFound(Context()) with
        {
            TenantId = "tenant-other",
            ProjectId = "project-other",
            OperationId = "op_000000000000d000",
            CorrelationId = "corr_000000000000d000"
        };

        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateRequest(Request() with
        {
            ReadEnvelope = envelope
        });

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningEnvelopeTenantMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningEnvelopeProjectMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningEnvelopeOperationMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningEnvelopeCorrelationMismatch");
    }

    [TestMethod]
    public void CrossScopeD19Result_FailsClosed()
    {
        var d19 = D19Result() with
        {
            TenantId = "tenant-other",
            ProjectId = "project-other",
            OperationId = "op_000000000000d000",
            CorrelationId = "corr_000000000000d000"
        };

        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateRequest(Request() with
        {
            NextSafeActionFormatterResult = d19
        });

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningNextSafeActionTenantMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningNextSafeActionProjectMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningNextSafeActionOperationMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningNextSafeActionCorrelationMismatch");
    }

    [TestMethod]
    public void CrossScopeWarningFacts_FailsClosed()
    {
        var facts = Facts() with
        {
            TenantId = "tenant-other",
            ProjectId = "project-other",
            OperationId = "op_000000000000d000",
            CorrelationId = "corr_000000000000d000"
        };

        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateRequest(Request() with
        {
            WarningFacts = facts
        });

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningFactsTenantMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningFactsProjectMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningFactsOperationMismatch");
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningFactsCorrelationMismatch");
    }

    [DataTestMethod]
    [DataRow("status", OperationStatusAuthorityWarningKind.StatusIsNotAuthority)]
    [DataRow("evidence", OperationStatusAuthorityWarningKind.EvidenceIsNotApproval)]
    [DataRow("receipt", OperationStatusAuthorityWarningKind.ReceiptIsNotExecutionProof)]
    [DataRow("validation", OperationStatusAuthorityWarningKind.ValidationIsNotApproval)]
    [DataRow("freshness-validation", OperationStatusAuthorityWarningKind.FreshnessIsNotPermission)]
    [DataRow("freshness-patch", OperationStatusAuthorityWarningKind.FreshnessIsNotPermission)]
    [DataRow("worktree", OperationStatusAuthorityWarningKind.WorktreeStateIsNotMutationAuthority)]
    [DataRow("interrupted", OperationStatusAuthorityWarningKind.InterruptedRunIsNotRetryAuthority)]
    [DataRow("rollback", OperationStatusAuthorityWarningKind.RollbackPlanIsNotRollbackExecution)]
    [DataRow("recovery", OperationStatusAuthorityWarningKind.RecoveryPlanIsNotRecoveryAuthority)]
    [DataRow("page", OperationStatusAuthorityWarningKind.PaginationIsNotActionQueue)]
    [DataRow("envelope", OperationStatusAuthorityWarningKind.EnvelopeIsNotAuthority)]
    [DataRow("d19", OperationStatusAuthorityWarningKind.NextSafeActionTextIsDisplayOnly)]
    [DataRow("redacted", OperationStatusAuthorityWarningKind.RedactionIsNotDenial)]
    [DataRow("ui", OperationStatusAuthorityWarningKind.UiStateIsNotAuthority)]
    public void SuppliedFacts_MapToBoundaryWarnings(
        string factKind,
        OperationStatusAuthorityWarningKind warningKind)
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = null,
            NextSafeActionFormatterResult = null,
            WarningFacts = FactsFor(factKind)
        });

        AssertValid(result);
        Assert.AreEqual(OperationStatusAuthorityWarningFormatterStatus.Formatted, result.FormatterStatus);
        AssertLine(result, warningKind);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void SuppliedEnvelope_ProducesEnvelopeAndStatusWarnings()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.Success(Context(), Summary()),
            NextSafeActionFormatterResult = null,
            WarningFacts = null
        });

        AssertValid(result);
        AssertLine(result, OperationStatusAuthorityWarningKind.EnvelopeIsNotAuthority);
        AssertLine(result, OperationStatusAuthorityWarningKind.StatusIsNotAuthority);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void SuppliedD19Result_ProducesDisplayOnlyWarning()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = null,
            NextSafeActionFormatterResult = D19Result(),
            WarningFacts = null
        });

        AssertValid(result);
        AssertLine(result, OperationStatusAuthorityWarningKind.NextSafeActionTextIsDisplayOnly);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void RedactedEnvelope_ProducesRedactionNotDenialWarning()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.Redacted(
                Context(),
                Summary() with { IsRedacted = true, RedactionReason = "summary withheld" }),
            NextSafeActionFormatterResult = null,
            WarningFacts = null
        });

        AssertValid(result);
        AssertLine(result, OperationStatusAuthorityWarningKind.RedactionIsNotDenial);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void NoWarningFacts_ReturnsNoWarningsWithoutInventingAuthority()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = null,
            NextSafeActionFormatterResult = null,
            WarningFacts = Facts()
        });

        AssertValid(result);
        Assert.AreEqual(OperationStatusAuthorityWarningFormatterStatus.NoWarnings, result.FormatterStatus);
        AssertLine(result, OperationStatusAuthorityWarningKind.NoWarning);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void DeterministicPriority_OrdersAndCapsWarningLines()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.Redacted(
                Context(OperationStatusReadKind.StatusPage),
                Summary() with { IsRedacted = true, RedactionReason = "summary withheld" }) with
                {
                    PageSummary = PageSummary()
                },
            NextSafeActionFormatterResult = D19Result(),
            WarningFacts = Facts() with
            {
                ProjectedStatus = OperationProjectedStatusKind.PushObserved,
                EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved,
                ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved,
                ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed,
                PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed,
                WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
                InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
                RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.Assessed,
                HasPageSummary = true,
                HasRedactedSummary = true,
                EnvelopeKind = OperationStatusReadEnvelopeKind.Redacted,
                NextSafeActionFormatterStatus = OperationStatusNextSafeActionFormatterStatus.Formatted,
                HasNextSafeActionDisplayLines = true
            }
        });

        AssertValid(result);
        Assert.AreEqual(OperationStatusAuthorityWarningFormatterValidator.MaxLineCount, result.Lines.Count);
        CollectionAssert.AreEqual(
            new[]
            {
                OperationStatusAuthorityWarningKind.NextSafeActionTextIsDisplayOnly,
                OperationStatusAuthorityWarningKind.StatusIsNotAuthority,
                OperationStatusAuthorityWarningKind.EvidenceIsNotApproval,
                OperationStatusAuthorityWarningKind.ReceiptIsNotExecutionProof,
                OperationStatusAuthorityWarningKind.ValidationIsNotApproval,
                OperationStatusAuthorityWarningKind.FreshnessIsNotPermission,
                OperationStatusAuthorityWarningKind.WorktreeStateIsNotMutationAuthority,
                OperationStatusAuthorityWarningKind.InterruptedRunIsNotRetryAuthority,
                OperationStatusAuthorityWarningKind.RollbackPlanIsNotRollbackExecution,
                OperationStatusAuthorityWarningKind.RecoveryPlanIsNotRecoveryAuthority
            },
            result.Lines.Select(static line => line.WarningKind).ToArray());
    }

    [TestMethod]
    public void AmbiguousAndUnassessableInputs_SetFormatterStatusesWithoutSelectingWorkflowStep()
    {
        var ambiguous = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.Ambiguous(Context()),
            NextSafeActionFormatterResult = null,
            WarningFacts = null
        });
        var unassessable = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.Unassessable(Context()),
            NextSafeActionFormatterResult = null,
            WarningFacts = null
        });

        AssertValid(ambiguous);
        AssertValid(unassessable);
        Assert.AreEqual(OperationStatusAuthorityWarningFormatterStatus.AmbiguousInput, ambiguous.FormatterStatus);
        Assert.AreEqual(OperationStatusAuthorityWarningFormatterStatus.Unassessable, unassessable.FormatterStatus);
        AssertNoAuthority(ambiguous);
        AssertNoAuthority(unassessable);
    }

    [TestMethod]
    public void EveryLineContainsWarningOnlyNoGrantNoDenyNoWorkflowActionBoundary()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            NextSafeActionFormatterResult = D19Result(),
            WarningFacts = FactsFor("worktree")
        });

        AssertValid(result);
        Assert.IsTrue(result.Lines.Count > 1);
        Assert.IsTrue(result.Lines.All(static line =>
            line.Boundary == OperationStatusAuthorityWarningFormatterValidator.RequiredBoundary));
        Assert.IsTrue(result.Lines.All(static line => line.Boundary.Contains("Warning only.", StringComparison.Ordinal)));
        Assert.IsTrue(result.Lines.All(static line => line.Boundary.Contains("grants no authority", StringComparison.Ordinal)));
        Assert.IsTrue(result.Lines.All(static line => line.Boundary.Contains("denies no authority", StringComparison.Ordinal)));
        Assert.IsTrue(result.Lines.All(static line => line.Boundary.Contains("performs no workflow action", StringComparison.Ordinal)));
    }

    [DataTestMethod]
    [DataRow("approved")]
    [DataRow("authorized")]
    [DataRow("allowed")]
    [DataRow("denied")]
    [DataRow("ready to apply")]
    [DataRow("ready to commit")]
    [DataRow("ready to push")]
    [DataRow("retry now")]
    [DataRow("rollback now")]
    [DataRow("recover now")]
    [DataRow("continue workflow")]
    [DataRow("execute")]
    [DataRow("run this")]
    public void RenderedAuthorityOrDenialPhrases_AreRejected(string phrase)
    {
        var result = new OperationStatusAuthorityWarningFormatterResult
        {
            IsValid = true,
            FormatterStatus = OperationStatusAuthorityWarningFormatterStatus.Formatted,
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            AsOfUtc = AsOfUtc,
            Lines =
            [
                new OperationStatusAuthorityWarningLine
                {
                    WarningKind = OperationStatusAuthorityWarningKind.StatusIsNotAuthority,
                    Severity = OperationStatusAuthorityWarningSeverity.BoundaryWarning,
                    Title = $"Unsafe {phrase}",
                    Detail = "Projected status is display state only.",
                    Boundary = OperationStatusAuthorityWarningFormatterValidator.RequiredBoundary,
                    Source = Source
                }
            ],
            Issues = [],
            Warnings = OperationStatusAuthorityWarningFormatterValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = OperationStatusAuthorityWarningFormatterValidator.RequiredForbiddenAuthorityImplications
        };

        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateResult(result);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningTitleUnsafe");
    }

    [TestMethod]
    public void FormatterOutput_DoesNotContainForbiddenActionOrDenialLanguageExceptBoundary()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            NextSafeActionFormatterResult = D19Result(),
            WarningFacts = Facts() with
            {
                ProjectedStatus = OperationProjectedStatusKind.CompletedObserved,
                EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved,
                ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved,
                ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed,
                WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
                InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
                RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.Assessed
            }
        });

        AssertValid(result);

        var forbiddenRenderedPhrases = new[]
        {
            "approved",
            "authorized",
            "allowed",
            "denied",
            "forbidden by policy",
            "permission granted",
            "permission denied",
            "safe to execute",
            "ready to apply",
            "ready to commit",
            "ready to push",
            "retry now",
            "rollback now",
            "recover now",
            "continue workflow",
            "execute",
            "run this",
            "click",
            "press",
            "you can",
            "system can",
            "agent can",
            "must proceed",
            "go ahead"
        };

        foreach (var line in result.Lines)
        {
            var rendered = string.Join(" ", line.Title, line.Detail);
            foreach (var phrase in forbiddenRenderedPhrases)
            {
                Assert.DoesNotContain(phrase, rendered, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [TestMethod]
    public void FormatterValidation_RejectsTamperedBoundary()
    {
        var result = OperationStatusAuthorityWarningFormatter.Format(Request() with
        {
            WarningFacts = FactsFor("status")
        }) with
        {
            Lines =
            [
                OperationStatusAuthorityWarningFormatter.Format(Request() with
                {
                    WarningFacts = FactsFor("status")
                }).Lines.Single() with
                {
                    Boundary = "Looks useful."
                }
            ]
        };

        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateResult(result);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusAuthorityWarningBoundaryRequired");
    }

    [TestMethod]
    public void ResultModel_DoesNotExposeAuthorityCommandDenialOrPayloadFields()
    {
        var propertyNames = typeof(OperationStatusAuthorityWarningFormatterResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Concat(typeof(OperationStatusAuthorityWarningLine)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(static property => property.Name))
            .ToArray();

        var forbidden = new[]
        {
            "Can",
            "ApprovalStatus",
            "PolicySatisfied",
            "AuthorityGranted",
            "ActionAllowed",
            "Command",
            "Endpoint",
            "Mutation",
            "Raw",
            "Payload",
            "Denied",
            "Authorized"
        };

        Assert.IsFalse(propertyNames.Any(name =>
            forbidden.Any(marker => name.Contains(marker, StringComparison.Ordinal))));
    }

    [TestMethod]
    public void FormatterCore_DoesNotInvokeStoresResolversPaginatorEnvelopeFactoryD19FormatterOrClock()
    {
        var source = FormatterCoreSource();
        var forbidden = new[]
        {
            "DbContext",
            "Repository",
            "Store",
            "AuthorizationService",
            "Authenticate",
            "MissingEvidenceResolver.",
            "ForbiddenActionResolver.",
            "ReceiptReferenceResolver.",
            "EvidenceResolver.",
            "ValidationStalenessResolver.",
            "PatchBaseFreshnessResolver.",
            "WorktreeBaseHeadFreshnessReadModelAssembler.",
            "InterruptedRunReadModelAssembler.",
            "RollbackRecoveryReadModelAssembler.",
            "OperationStatusPaginator.",
            "OperationStatusReadEnvelopeFactory.",
            "OperationStatusNextSafeActionFormatter.",
            "DateTimeOffset.UtcNow",
            "DateTime.UtcNow"
        };

        foreach (var marker in forbidden)
        {
            Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void FormatterCore_DoesNotExposeExecutionMutationOrProviderSurfaces()
    {
        var source = FormatterCoreSource();
        var formatterSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Core",
            "Governance",
            "OperationStatusAuthorityWarningFormatter.cs"));
        var forbidden = new[]
        {
            "Process.Start",
            "RunProcessAsync",
            "HttpClient",
            "ControllerBase",
            "MapGet",
            "MapPost",
            "SqlConnection",
            "MigrationBuilder",
            "File.ReadAllText",
            "Directory.",
            "SourceApplyExecutor",
            "ControlledSourceApply",
            "SourceApplyGateway",
            "commit executor",
            "push executor",
            "merge executor",
            "release executor",
            "deploy executor",
            "retry executor",
            "resume executor",
            "recovery executor",
            "rollback executor",
            "WorkflowContinuationExecutor",
            "MemoryPromotionExecutor",
            "ApprovalAcceptanceExecutor",
            "PolicySatisfactionResolver"
        };

        foreach (var marker in forbidden)
        {
            Assert.DoesNotContain(marker, source, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var marker in new[] { "git ", "gh ", "dotnet ", "npm ", "powershell", "cmd.exe", "bash" })
        {
            Assert.DoesNotContain(marker, formatterSource, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void ReceiptRecordsDisplayOnlyAndWarningTextIsNotAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "D20_AUTHORITY_WARNING_FORMATTER_HARDENING.md"));

        Assert.Contains("authority-warning formatter-is-display-only", receipt);
        Assert.Contains("warning-text-is-not-authority", receipt);
        Assert.Contains("does not approve operations, deny operations, satisfy policy, choose workflow steps, grant authority", receipt);
    }

    [TestMethod]
    public void PriorD01ThroughD19ContractsRemainAvailable()
    {
        Assert.IsTrue(OperationIdentityValidator.ValidateOperationId(OperationId).IsValid);

        var d19 = D19Result();
        var d19Validation = OperationStatusNextSafeActionFormatterValidator.ValidateResult(d19);
        Assert.IsTrue(d19Validation.IsValid, string.Join(", ", d19Validation.Issues));

        var envelope = OperationStatusReadEnvelopeFactory.NotFound(Context());
        var envelopeValidation = OperationStatusReadEnvelopeValidator.Validate(envelope);
        Assert.IsTrue(envelopeValidation.IsValid, string.Join(", ", envelopeValidation.Issues));
    }

    private static OperationStatusAuthorityWarningFormatterRequest Request() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            AsOfUtc = AsOfUtc,
            ReadEnvelope = null,
            NextSafeActionFormatterResult = null,
            WarningFacts = FactsFor("status"),
            Source = Source
        };

    private static OperationStatusAuthorityWarningFacts Facts() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Source = Source,
            RecordedAtUtc = RecordedAtUtc
        };

    private static OperationStatusAuthorityWarningFacts FactsFor(string factKind) =>
        factKind switch
        {
            "status" => Facts() with { ProjectedStatus = OperationProjectedStatusKind.BlockedObserved },
            "evidence" => Facts() with { EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved },
            "receipt" => Facts() with { ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved },
            "validation" => Facts() with { ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed },
            "freshness-validation" => Facts() with { ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed },
            "freshness-patch" => Facts() with { PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed },
            "worktree" => Facts() with { WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed },
            "interrupted" => Facts() with { InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted },
            "rollback" => Facts() with { RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.Assessed },
            "recovery" => Facts() with { RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.Assessed },
            "page" => Facts() with { HasPageSummary = true },
            "envelope" => Facts() with { EnvelopeKind = OperationStatusReadEnvelopeKind.Success },
            "d19" => Facts() with { NextSafeActionFormatterStatus = OperationStatusNextSafeActionFormatterStatus.Formatted, HasNextSafeActionDisplayLines = true },
            "redacted" => Facts() with { HasRedactedSummary = true },
            "ui" => Facts() with { HasPageSummary = true },
            _ => Facts()
        };

    private static OperationStatusReadContext Context(OperationStatusReadKind readKind = OperationStatusReadKind.SingleStatus) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReadKind = readKind,
            AsOfUtc = AsOfUtc,
            Source = Source
        };

    private static OperationStatusSafeSummary Summary() =>
        new()
        {
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ProjectedStatus = OperationProjectedStatusKind.BlockedObserved,
            CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T10:00:00Z"),
            UpdatedAtUtc = DateTimeOffset.Parse("2026-06-24T10:30:00Z"),
            LastEventAtUtc = DateTimeOffset.Parse("2026-06-24T10:30:00Z"),
            TimelineEventCount = 3,
            DiagnosticStatusSummary = new OperationStatusDiagnosticStatusSummary(),
            IsRedacted = false
        };

    private static OperationStatusPageEnvelopeSummary PageSummary() =>
        new()
        {
            PageSize = 20,
            ItemCount = 1,
            MatchedCount = 1,
            ScannedCount = 1,
            HasMore = false,
            HasNextCursor = false,
            CursorState = "PageReturned"
        };

    private static OperationStatusNextSafeActionFormatterResult D19Result() =>
        new()
        {
            IsValid = true,
            FormatterStatus = OperationStatusNextSafeActionFormatterStatus.Formatted,
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            AsOfUtc = AsOfUtc,
            Lines =
            [
                new OperationStatusNextSafeActionLine
                {
                    DisplayKind = OperationStatusNextSafeActionDisplayKind.ReviewMissingEvidence,
                    Severity = OperationStatusNextSafeActionSeverity.Warning,
                    Title = "Review missing evidence",
                    Detail = "Supplied facts report missing or ambiguous evidence.",
                    Rationale = "Evidence gaps remain diagnostic until resolved elsewhere.",
                    AuthorityBoundary = OperationStatusNextSafeActionFormatterValidator.RequiredAuthorityBoundary,
                    Source = Source
                }
            ],
            Issues = [],
            Warnings = OperationStatusNextSafeActionFormatterValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = OperationStatusNextSafeActionFormatterValidator.RequiredForbiddenAuthorityImplications
        };

    private static OperationStatusAuthorityWarningLine AssertLine(
        OperationStatusAuthorityWarningFormatterResult result,
        OperationStatusAuthorityWarningKind warningKind)
    {
        var line = result.Lines.SingleOrDefault(line => line.WarningKind == warningKind);
        Assert.IsNotNull(line, $"Expected line {warningKind}. Actual: {string.Join(", ", result.Lines.Select(static item => item.WarningKind))}");
        return line;
    }

    private static void AssertValid(OperationStatusAuthorityWarningFormatterResult result)
    {
        var validation = OperationStatusAuthorityWarningFormatterValidator.ValidateResult(result);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
    }

    private static void AssertNoAuthority(OperationStatusAuthorityWarningFormatterResult result)
    {
        AssertContains(result.Warnings, "authority warning formatting is not authority");
        AssertContains(result.Warnings, "warning text does not grant or deny permission");
        AssertContains(result.Warnings, "warning text does not execute workflow");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is display only");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not approval");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not policy satisfaction");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not source apply");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not rollback execution");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not recovery execution");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not commit");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not push");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not release");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not deployment");
        AssertContains(result.ForbiddenAuthorityImplications, "warning text is not workflow continuation");
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Any(value => string.Equals(value, expected, StringComparison.Ordinal)), $"Missing {expected}. Actual: {string.Join(", ", values)}");

    private static string FormatterCoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusAuthorityWarningFormatterModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusAuthorityWarningFormatterValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusAuthorityWarningFormatter.cs")));
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

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
