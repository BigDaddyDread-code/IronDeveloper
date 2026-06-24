using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockD19NextSafeActionFormatterHardeningTests
{
    private const string TenantId = "tenant-d19";
    private const string ProjectId = "project-d19";
    private const string OperationId = "op_000000000000d019";
    private const string CorrelationId = "corr_000000000000d019";
    private const string Source = "d19-test";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-24T11:00:00Z");

    [TestMethod]
    public void InvalidRequest_FailsClosedWithoutAuthority()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            OperationId = "",
            ReadEnvelope = null,
            DiagnosticFacts = null
        });

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(OperationStatusNextSafeActionFormatterStatus.InvalidRequest, result.FormatterStatus);
        AssertContains(result.Issues, "OperationStatusNextSafeActionOperationIdRequired");
        AssertContains(result.Issues, "OperationStatusNextSafeActionInputRequired");
        AssertLine(result, OperationStatusNextSafeActionDisplayKind.ReviewInvalidRequest);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("tenant", "OperationStatusNextSafeActionTenantIdRequired")]
    [DataRow("project", "OperationStatusNextSafeActionProjectIdRequired")]
    [DataRow("as-of", "OperationStatusNextSafeActionAsOfUtcRequired")]
    [DataRow("source", "OperationStatusNextSafeActionSourceRequired")]
    [DataRow("input", "OperationStatusNextSafeActionInputRequired")]
    public void RequestRequiredFields_FailClosed(string field, string expectedIssue)
    {
        var request = field switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "as-of" => Request() with { AsOfUtc = default },
            "source" => Request() with { Source = "" },
            "input" => Request() with { ReadEnvelope = null, DiagnosticFacts = null },
            _ => Request()
        };

        var validation = OperationStatusNextSafeActionFormatterValidator.ValidateRequest(request);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, expectedIssue);
        AssertContains(validation.Warnings, "formatted guidance is not authority");
    }

    [TestMethod]
    public void EnvelopeScopeMismatch_FailsClosed()
    {
        var envelope = OperationStatusReadEnvelopeFactory.NotFound(Context()) with
        {
            TenantId = "tenant-other",
            ProjectId = "project-other",
            OperationId = "op_000000000000d000",
            CorrelationId = "corr_000000000000d000"
        };

        var validation = OperationStatusNextSafeActionFormatterValidator.ValidateRequest(Request() with
        {
            ReadEnvelope = envelope
        });

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusNextSafeActionEnvelopeTenantMismatch");
        AssertContains(validation.Issues, "OperationStatusNextSafeActionEnvelopeProjectMismatch");
        AssertContains(validation.Issues, "OperationStatusNextSafeActionEnvelopeOperationMismatch");
        AssertContains(validation.Issues, "OperationStatusNextSafeActionEnvelopeCorrelationMismatch");
    }

    [TestMethod]
    public void DiagnosticFactsScopeMismatch_FailsClosed()
    {
        var facts = Facts() with
        {
            TenantId = "tenant-other",
            ProjectId = "project-other",
            OperationId = "op_000000000000d000",
            CorrelationId = "corr_000000000000d000"
        };

        var validation = OperationStatusNextSafeActionFormatterValidator.ValidateRequest(Request() with
        {
            DiagnosticFacts = facts
        });

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusNextSafeActionFactsTenantMismatch");
        AssertContains(validation.Issues, "OperationStatusNextSafeActionFactsProjectMismatch");
        AssertContains(validation.Issues, "OperationStatusNextSafeActionFactsOperationMismatch");
        AssertContains(validation.Issues, "OperationStatusNextSafeActionFactsCorrelationMismatch");
    }

    [TestMethod]
    public void NotFoundEnvelope_FormatsGenericReviewLine()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.NotFound(Context()),
            DiagnosticFacts = null
        });

        AssertValid(result);
        Assert.AreEqual(OperationStatusNextSafeActionFormatterStatus.Formatted, result.FormatterStatus);
        var line = AssertLine(result, OperationStatusNextSafeActionDisplayKind.ReviewNotFound);
        Assert.AreEqual(OperationStatusNextSafeActionSeverity.Notice, line.Severity);
        Assert.DoesNotContain("another tenant", line.Detail, StringComparison.OrdinalIgnoreCase);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(OperationStatusReadEnvelopeKind.InvalidRequest, OperationStatusNextSafeActionDisplayKind.ReviewInvalidRequest, OperationStatusNextSafeActionFormatterStatus.Formatted)]
    [DataRow(OperationStatusReadEnvelopeKind.Ambiguous, OperationStatusNextSafeActionDisplayKind.ReviewAmbiguousStatus, OperationStatusNextSafeActionFormatterStatus.AmbiguousInput)]
    [DataRow(OperationStatusReadEnvelopeKind.Unassessable, OperationStatusNextSafeActionDisplayKind.ReviewUnassessableStatus, OperationStatusNextSafeActionFormatterStatus.Unassessable)]
    [DataRow(OperationStatusReadEnvelopeKind.Redacted, OperationStatusNextSafeActionDisplayKind.ReviewRedactedStatus, OperationStatusNextSafeActionFormatterStatus.Formatted)]
    [DataRow(OperationStatusReadEnvelopeKind.Error, OperationStatusNextSafeActionDisplayKind.ManualReviewRequired, OperationStatusNextSafeActionFormatterStatus.Formatted)]
    public void EnvelopeKinds_MapToBoundedDisplayKinds(
        OperationStatusReadEnvelopeKind envelopeKind,
        OperationStatusNextSafeActionDisplayKind displayKind,
        OperationStatusNextSafeActionFormatterStatus formatterStatus)
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = Envelope(envelopeKind),
            DiagnosticFacts = null
        });

        AssertValid(result);
        Assert.AreEqual(formatterStatus, result.FormatterStatus);
        AssertLine(result, displayKind);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("missing-evidence", OperationStatusNextSafeActionDisplayKind.ReviewMissingEvidence)]
    [DataRow("forbidden-action", OperationStatusNextSafeActionDisplayKind.ReviewForbiddenActionFacts)]
    [DataRow("receipt", OperationStatusNextSafeActionDisplayKind.ReviewReceiptReferences)]
    [DataRow("evidence", OperationStatusNextSafeActionDisplayKind.ReviewEvidenceReferences)]
    [DataRow("validation", OperationStatusNextSafeActionDisplayKind.ReviewValidationStaleness)]
    [DataRow("patch-base", OperationStatusNextSafeActionDisplayKind.ReviewPatchBaseFreshness)]
    [DataRow("worktree", OperationStatusNextSafeActionDisplayKind.ReviewWorktreeBaseHeadFreshness)]
    [DataRow("interrupted", OperationStatusNextSafeActionDisplayKind.ReviewInterruptedRun)]
    [DataRow("rollback", OperationStatusNextSafeActionDisplayKind.ReviewRollbackRecoveryMaterial)]
    public void DiagnosticStatuses_MapToBoundedDisplayKinds(
        string diagnosticKind,
        OperationStatusNextSafeActionDisplayKind displayKind)
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = null,
            DiagnosticFacts = FactsFor(diagnosticKind)
        });

        AssertValid(result);
        Assert.AreEqual(OperationStatusNextSafeActionFormatterStatus.Formatted, result.FormatterStatus);
        AssertLine(result, displayKind);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void DeterministicPriority_OrdersInvalidForbiddenMissingInterruptedFreshnessAndReferences()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = Envelope(OperationStatusReadEnvelopeKind.InvalidRequest),
            DiagnosticFacts = Facts() with
            {
                ForbiddenActionStatus = ForbiddenActionResolutionStatus.Forbidden,
                MissingEvidenceStatus = MissingEvidenceResolutionStatus.MissingEvidence,
                InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
                RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.MissingMaterial,
                WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness,
                PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.MixedFreshness,
                ValidationStalenessStatus = ValidationStalenessResolutionStatus.MixedStaleness,
                ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.PartiallyResolved,
                EvidenceResolutionStatus = EvidenceResolutionStatus.PartiallyResolved
            }
        });

        AssertValid(result);
        Assert.AreEqual(OperationStatusNextSafeActionFormatterValidator.MaxLineCount, result.Lines.Count);
        CollectionAssert.AreEqual(
            new[]
            {
                OperationStatusNextSafeActionDisplayKind.ReviewInvalidRequest,
                OperationStatusNextSafeActionDisplayKind.ReviewForbiddenActionFacts,
                OperationStatusNextSafeActionDisplayKind.ReviewMissingEvidence,
                OperationStatusNextSafeActionDisplayKind.ReviewInterruptedRun,
                OperationStatusNextSafeActionDisplayKind.ReviewRollbackRecoveryMaterial,
                OperationStatusNextSafeActionDisplayKind.ReviewWorktreeBaseHeadFreshness,
                OperationStatusNextSafeActionDisplayKind.ReviewPatchBaseFreshness,
                OperationStatusNextSafeActionDisplayKind.ReviewValidationStaleness
            },
            result.Lines.Select(static line => line.DisplayKind).ToArray());
    }

    [TestMethod]
    public void NoDiagnosticFacts_ReturnsNoGuidanceWithoutInventingAction()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.Success(Context(), Summary()),
            DiagnosticFacts = Facts()
        });

        AssertValid(result);
        Assert.AreEqual(OperationStatusNextSafeActionFormatterStatus.NoGuidance, result.FormatterStatus);
        AssertLine(result, OperationStatusNextSafeActionDisplayKind.NoGuidance);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void PageEnvelope_FormatsStatusPageContextOnly()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.PageSuccess(
                Context(OperationStatusReadKind.StatusPage),
                new OperationStatusPageEnvelopeSummary
                {
                    PageSize = 20,
                    ItemCount = 0,
                    MatchedCount = 0,
                    ScannedCount = 0,
                    HasMore = false,
                    HasNextCursor = false,
                    CursorState = "NoRows"
                }),
            DiagnosticFacts = null
        });

        AssertValid(result);
        AssertLine(result, OperationStatusNextSafeActionDisplayKind.ReviewStatusPage);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("approved")]
    [DataRow("authorized")]
    [DataRow("ready to push")]
    [DataRow("go ahead")]
    [DataRow("run this")]
    [DataRow("click")]
    public void RenderedAuthorityPhrases_AreRejected(string phrase)
    {
        var result = new OperationStatusNextSafeActionFormatterResult
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
                    Title = $"Unsafe {phrase}",
                    Detail = "Supplied facts report missing evidence.",
                    Rationale = "The formatter only names the supplied diagnostic area.",
                    AuthorityBoundary = OperationStatusNextSafeActionFormatterValidator.RequiredAuthorityBoundary,
                    Source = Source
                }
            ],
            Issues = [],
            Warnings = OperationStatusNextSafeActionFormatterValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = OperationStatusNextSafeActionFormatterValidator.RequiredForbiddenAuthorityImplications
        };

        var validation = OperationStatusNextSafeActionFormatterValidator.ValidateResult(result);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusNextSafeActionTitleUnsafe");
    }

    [TestMethod]
    public void EveryFormattedLine_CarriesExactDisplayOnlyBoundary()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = Envelope(OperationStatusReadEnvelopeKind.Ambiguous),
            DiagnosticFacts = Facts() with
            {
                MissingEvidenceStatus = MissingEvidenceResolutionStatus.MissingEvidence,
                ForbiddenActionStatus = ForbiddenActionResolutionStatus.Forbidden
            }
        });

        AssertValid(result);
        Assert.IsTrue(result.Lines.Count > 1);
        Assert.IsTrue(result.Lines.All(static line =>
            line.AuthorityBoundary == OperationStatusNextSafeActionFormatterValidator.RequiredAuthorityBoundary));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ResultModel_DoesNotExposeAuthorityShapedFields()
    {
        var propertyNames = typeof(OperationStatusNextSafeActionFormatterResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Concat(typeof(OperationStatusNextSafeActionLine)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(static property => property.Name))
            .ToArray();

        Assert.IsFalse(propertyNames.Any(static name =>
            name.StartsWith("Can", StringComparison.Ordinal) ||
            name.Contains("ApprovalStatus", StringComparison.Ordinal) ||
            name.Contains("PolicySatisfied", StringComparison.Ordinal) ||
            name.Contains("AuthorityGranted", StringComparison.Ordinal) ||
            name.Contains("ActionAllowed", StringComparison.Ordinal) ||
            name.Contains("Command", StringComparison.Ordinal) ||
            name.Contains("Endpoint", StringComparison.Ordinal) ||
            name.Contains("Mutation", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void FormatterSource_DoesNotInvokeResolversPaginatorFactoryStoresOrClock()
    {
        var source = FormatterSource();
        var forbidden = new[]
        {
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
            "IRepository",
            "DbContext",
            "HttpClient",
            "DateTimeOffset.UtcNow",
            "DateTime.UtcNow",
            "Process.Start",
            "RunProcessAsync"
        };

        foreach (var marker in forbidden)
        {
            Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void FormatterOutput_DoesNotContainForbiddenActionLanguageExceptBoundary()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = Envelope(OperationStatusReadEnvelopeKind.Error),
            DiagnosticFacts = Facts() with
            {
                MissingEvidenceStatus = MissingEvidenceResolutionStatus.MissingEvidence,
                ForbiddenActionStatus = ForbiddenActionResolutionStatus.Forbidden,
                InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted,
                RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.MissingMaterial
            }
        });

        AssertValid(result);

        var forbiddenRenderedPhrases = new[]
        {
            "approved",
            "authorized",
            "allowed",
            "permission granted",
            "safe to execute",
            "ready to apply",
            "ready to commit",
            "ready to push",
            "ready to merge",
            "ready to release",
            "ready to deploy",
            "retry now",
            "resume now",
            "recover now",
            "rollback now",
            "continue workflow",
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
            var rendered = string.Join(" ", line.Title, line.Detail, line.Rationale);
            foreach (var phrase in forbiddenRenderedPhrases)
            {
                Assert.DoesNotContain(phrase, rendered, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [TestMethod]
    public void FormatterValidation_RejectsTamperedBoundary()
    {
        var result = OperationStatusNextSafeActionFormatter.Format(Request() with
        {
            ReadEnvelope = OperationStatusReadEnvelopeFactory.NotFound(Context()),
            DiagnosticFacts = null
        }) with
        {
            Lines =
            [
                OperationStatusNextSafeActionFormatter.Format(Request() with
                {
                    ReadEnvelope = OperationStatusReadEnvelopeFactory.NotFound(Context()),
                    DiagnosticFacts = null
                }).Lines.Single() with
                {
                    AuthorityBoundary = "Looks useful."
                }
            ]
        };

        var validation = OperationStatusNextSafeActionFormatterValidator.ValidateResult(result);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusNextSafeActionAuthorityBoundaryRequired");
    }

    private static OperationStatusNextSafeActionFormatterRequest Request() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            AsOfUtc = AsOfUtc,
            ReadEnvelope = OperationStatusReadEnvelopeFactory.NotFound(Context()),
            DiagnosticFacts = Facts(),
            Source = Source
        };

    private static OperationStatusNextSafeActionDiagnosticFacts Facts() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ProjectedStatus = OperationProjectedStatusKind.BlockedObserved,
            Source = Source,
            RecordedAtUtc = RecordedAtUtc
        };

    private static OperationStatusNextSafeActionDiagnosticFacts FactsFor(string diagnosticKind) =>
        diagnosticKind switch
        {
            "missing-evidence" => Facts() with { MissingEvidenceStatus = MissingEvidenceResolutionStatus.MissingEvidence },
            "forbidden-action" => Facts() with { ForbiddenActionStatus = ForbiddenActionResolutionStatus.Forbidden },
            "receipt" => Facts() with { ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.PartiallyResolved },
            "evidence" => Facts() with { EvidenceResolutionStatus = EvidenceResolutionStatus.PartiallyResolved },
            "validation" => Facts() with { ValidationStalenessStatus = ValidationStalenessResolutionStatus.MixedStaleness },
            "patch-base" => Facts() with { PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.MixedFreshness },
            "worktree" => Facts() with { WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness },
            "interrupted" => Facts() with { InterruptedRunStatus = InterruptedRunReadModelStatus.Interrupted },
            "rollback" => Facts() with { RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.MissingMaterial },
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

    private static OperationStatusReadEnvelope Envelope(OperationStatusReadEnvelopeKind envelopeKind) =>
        envelopeKind switch
        {
            OperationStatusReadEnvelopeKind.InvalidRequest => OperationStatusReadEnvelopeFactory.InvalidRequest(Context()),
            OperationStatusReadEnvelopeKind.NotFound => OperationStatusReadEnvelopeFactory.NotFound(Context()),
            OperationStatusReadEnvelopeKind.Ambiguous => OperationStatusReadEnvelopeFactory.Ambiguous(Context()),
            OperationStatusReadEnvelopeKind.Unassessable => OperationStatusReadEnvelopeFactory.Unassessable(Context()),
            OperationStatusReadEnvelopeKind.Redacted => OperationStatusReadEnvelopeFactory.Redacted(Context(), Summary() with { IsRedacted = true, RedactionReason = "summary withheld" }),
            OperationStatusReadEnvelopeKind.Error => OperationStatusReadEnvelopeFactory.Error(Context()),
            _ => OperationStatusReadEnvelopeFactory.Success(Context(), Summary())
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

    private static OperationStatusNextSafeActionLine AssertLine(
        OperationStatusNextSafeActionFormatterResult result,
        OperationStatusNextSafeActionDisplayKind displayKind)
    {
        var line = result.Lines.SingleOrDefault(line => line.DisplayKind == displayKind);
        Assert.IsNotNull(line, $"Expected line {displayKind}. Actual: {string.Join(", ", result.Lines.Select(static item => item.DisplayKind))}");
        return line;
    }

    private static void AssertValid(OperationStatusNextSafeActionFormatterResult result)
    {
        var validation = OperationStatusNextSafeActionFormatterValidator.ValidateResult(result);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
    }

    private static void AssertNoAuthority(OperationStatusNextSafeActionFormatterResult result)
    {
        AssertContains(result.Warnings, "formatted guidance is not authority");
        AssertContains(result.Warnings, "formatted guidance is not next safe action authority");
        AssertContains(result.Warnings, "formatted guidance does not execute workflow");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is display only");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is not source apply");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is not rollback");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is not commit");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is not push");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is not release");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is not deployment");
        AssertContains(result.ForbiddenAuthorityImplications, "formatted guidance is not workflow continuation");
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Any(value => string.Equals(value, expected, StringComparison.Ordinal)), $"Missing {expected}. Actual: {string.Join(", ", values)}");

    private static string FormatterSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusNextSafeActionFormatterModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusNextSafeActionFormatterValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "OperationStatusNextSafeActionFormatter.cs")));
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
