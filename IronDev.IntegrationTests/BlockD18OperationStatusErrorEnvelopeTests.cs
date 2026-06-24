using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed partial class BlockD18OperationStatusErrorEnvelopeTests
{
    private const string TenantId = "tenant-d18";
    private const string ProjectId = "project-d18";
    private const string OperationId = "op_000000000000d018";
    private const string CorrelationId = "corr_000000000000d018";
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.Parse("2026-06-24T10:00:00Z");

    [TestMethod]
    public void SuccessEnvelope_CarriesSafeSummaryOnlyAndDoesNotGrantAuthority()
    {
        var envelope = OperationStatusReadEnvelopeFactory.Success(Context(), Summary());
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.IsTrue(envelope.IsValid);
        Assert.AreEqual(OperationStatusReadEnvelopeKind.Success, envelope.EnvelopeKind);
        Assert.AreEqual(OperationStatusReadErrorCode.None, envelope.ErrorCode);
        Assert.IsNotNull(envelope.SafeSummary);
        Assert.IsNull(envelope.PageSummary);
        AssertContains(envelope.Warnings, "operation status read envelope is not authority");
        AssertContains(envelope.ForbiddenAuthorityImplications, "success envelope is not action allowed");
        AssertContains(envelope.ForbiddenAuthorityImplications, "operation status read envelope is not source apply");
    }

    [TestMethod]
    public void PageSuccess_AllowsMissingOperationAndCorrelationIds()
    {
        var context = Context(OperationStatusReadKind.StatusPage) with
        {
            OperationId = null,
            CorrelationId = null
        };

        var envelope = OperationStatusReadEnvelopeFactory.PageSuccess(context, PageSummary());
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.IsNull(envelope.OperationId);
        Assert.IsNull(envelope.CorrelationId);
        Assert.IsNotNull(envelope.PageSummary);
    }

    [TestMethod]
    public void NotFoundEnvelope_UsesGenericMessageWithoutTenantExistenceLeak()
    {
        var envelope = OperationStatusReadEnvelopeFactory.NotFound(Context());
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.IsTrue(envelope.IsValid);
        Assert.AreEqual(OperationStatusReadEnvelopeKind.NotFound, envelope.EnvelopeKind);
        Assert.AreEqual("Operation status was not found for the supplied scope.", envelope.Issues.Single().Message);
        Assert.DoesNotContain("another tenant", envelope.Issues.Single().Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("another project", envelope.Issues.Single().Message, StringComparison.OrdinalIgnoreCase);
        AssertContains(envelope.Warnings, "not found is not denial");
    }

    [DataTestMethod]
    [DataRow("Operation exists in another tenant.")]
    [DataRow("Operation exists but you do not have access.")]
    [DataRow("Wrong tenant.")]
    [DataRow("Found outside scope.")]
    [DataRow("Project mismatch revealed.")]
    [DataRow("Foreign tenant detected: tenant-d18-other.")]
    [DataRow("Foreign project detected: project-d18-other.")]
    public void NotFoundEnvelope_RejectsCrossTenantOrCrossProjectLeakMessages(string unsafeMessage)
    {
        var envelope = OperationStatusReadEnvelopeFactory.NotFound(Context()) with
        {
            Issues =
            [
                OperationStatusReadEnvelopeFactory.Issue(
                    OperationStatusReadErrorCode.OperationStatusNotFound,
                    unsafeMessage,
                    OperationStatusReadIssueSeverity.Info)
            ]
        };

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusReadIssueMessageUnsafe");
        AssertContains(validation.Issues, "OperationStatusReadNotFoundMessageMustBeGeneric");
    }

    [TestMethod]
    public void InvalidRequestEnvelope_CarriesSafeBoundedIssuesAndIsNotForbidden()
    {
        var envelope = OperationStatusReadEnvelopeFactory.InvalidRequest(
            Context(),
            [
                OperationStatusReadEnvelopeFactory.Issue(
                    OperationStatusReadErrorCode.OperationStatusRequestInvalid,
                    "Operation status read request was invalid.",
                    OperationStatusReadIssueSeverity.Error,
                    field: nameof(OperationStatusReadContext.OperationId),
                    isUserCorrectable: true)
            ]);

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.IsFalse(envelope.IsValid);
        Assert.AreEqual(OperationStatusReadEnvelopeKind.InvalidRequest, envelope.EnvelopeKind);
        AssertContains(envelope.ForbiddenAuthorityImplications, "invalid request envelope is not forbidden");
    }

    [TestMethod]
    public void AmbiguousEnvelope_DoesNotSelectWinner()
    {
        var envelope = OperationStatusReadEnvelopeFactory.Ambiguous(Context());
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.IsFalse(envelope.IsValid);
        Assert.AreEqual(OperationStatusReadEnvelopeKind.Ambiguous, envelope.EnvelopeKind);
        Assert.IsNull(envelope.SafeSummary);
        Assert.IsNull(envelope.PageSummary);
    }

    [TestMethod]
    public void AmbiguousCursorEnvelope_IsSafeAndDoesNotSelectWinner()
    {
        var envelope = OperationStatusReadEnvelopeFactory.Ambiguous(
            Context(OperationStatusReadKind.CursorPage) with
            {
                OperationId = null,
                CorrelationId = null
            },
            OperationStatusReadErrorCode.OperationStatusCursorAmbiguous);
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.AreEqual(OperationStatusReadErrorCode.OperationStatusCursorAmbiguous, envelope.ErrorCode);
        Assert.IsNull(envelope.SafeSummary);
    }

    [TestMethod]
    public void UnassessableEnvelope_DoesNotChooseNextSafeAction()
    {
        var envelope = OperationStatusReadEnvelopeFactory.Unassessable(Context());
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.IsFalse(envelope.IsValid);
        AssertNoAuthoritySurface(envelope);
        Assert.IsFalse(envelope.Issues.Any(issue => issue.Message.Contains("next safe action", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void RedactedEnvelope_CarriesRedactionMetadataOnly()
    {
        var envelope = OperationStatusReadEnvelopeFactory.Redacted(
            Context(),
            Summary() with
            {
                IsRedacted = true,
                RedactionReason = "summary withheld"
            });
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        Assert.IsTrue(envelope.IsValid);
        Assert.AreEqual(OperationStatusReadEnvelopeKind.Redacted, envelope.EnvelopeKind);
        Assert.AreEqual("summary withheld", envelope.SafeSummary!.RedactionReason);
        AssertContains(envelope.ForbiddenAuthorityImplications, "redacted envelope is not denied");
    }

    [DataTestMethod]
    [DataRow("System.NullReferenceException: nope")]
    [DataRow("stack trace at Service.cs:42")]
    [DataRow("SELECT * FROM dbo.Secrets")]
    [DataRow("C:\\Users\\bob\\source\\repos\\secret.txt")]
    [DataRow("{\"raw\":\"request\"}")]
    [DataRow("raw response body follows")]
    public void ErrorEnvelope_RejectsInternalOrRawDetails(string unsafeMessage)
    {
        var envelope = OperationStatusReadEnvelopeFactory.Error(
            Context(),
            [
                OperationStatusReadEnvelopeFactory.Issue(
                    OperationStatusReadErrorCode.OperationStatusReadModelError,
                    unsafeMessage,
                    OperationStatusReadIssueSeverity.Error)
            ]);

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusReadIssueMessageUnsafe");
        AssertContains(envelope.Warnings, "error is not permission");
    }

    [DataTestMethod]
    [DataRow("tenant", "OperationStatusReadTenantIdRequired")]
    [DataRow("project", "OperationStatusReadProjectIdRequired")]
    [DataRow("as-of", "OperationStatusReadAsOfUtcRequired")]
    [DataRow("read-kind", "OperationStatusReadKindRequired")]
    [DataRow("envelope-kind", "OperationStatusReadEnvelopeKindRequired")]
    [DataRow("error-code", "OperationStatusReadErrorCodeRequired")]
    [DataRow("source", "OperationStatusReadSourceRequired")]
    public void MissingOrUnknownEnvelopeFields_FailClosed(string fieldKind, string expectedIssue)
    {
        var envelope = fieldKind switch
        {
            "tenant" => GoodEnvelope() with { TenantId = "" },
            "project" => GoodEnvelope() with { ProjectId = "" },
            "as-of" => GoodEnvelope() with { AsOfUtc = default },
            "read-kind" => GoodEnvelope() with { ReadKind = OperationStatusReadKind.Unknown },
            "envelope-kind" => GoodEnvelope() with { EnvelopeKind = OperationStatusReadEnvelopeKind.Unknown },
            "error-code" => GoodEnvelope() with { ErrorCode = OperationStatusReadErrorCode.Unknown },
            "source" => GoodEnvelope() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldKind), fieldKind, null)
        };

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, expectedIssue);
    }

    [TestMethod]
    public void InvalidOperationId_FailsClosed()
    {
        var envelope = GoodEnvelope() with { OperationId = "run_123" };
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.StartsWith("OperationStatusReadOperationId:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void InvalidCorrelationId_FailsClosed()
    {
        var envelope = GoodEnvelope() with { CorrelationId = "op_000000000000d018" };
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusReadCorrelationIdInvalid");
    }

    [TestMethod]
    public void SingleStatusMissingOperationId_FailsClosed()
    {
        var envelope = GoodEnvelope() with { OperationId = null };
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusReadOperationIdRequired");
    }

    [TestMethod]
    public void SingleStatusMissingCorrelationId_FailsClosed()
    {
        var envelope = GoodEnvelope() with { CorrelationId = null };
        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusReadCorrelationIdRequired");
    }

    [TestMethod]
    public void UnsafeScopeOrSource_FailsClosed()
    {
        var tenant = OperationStatusReadEnvelopeValidator.Validate(GoodEnvelope() with { TenantId = "tenant approve" });
        var project = OperationStatusReadEnvelopeValidator.Validate(GoodEnvelope() with { ProjectId = "project policy" });
        var source = OperationStatusReadEnvelopeValidator.Validate(GoodEnvelope() with { Source = "source approve" });

        AssertContains(tenant.Issues, "OperationStatusReadTenantIdInvalid");
        AssertContains(project.Issues, "OperationStatusReadProjectIdInvalid");
        AssertContains(source.Issues, "OperationStatusReadSourceInvalid");
    }

    [DataTestMethod]
    [DataRow("approved")]
    [DataRow("policy satisfied")]
    [DataRow("authorized")]
    [DataRow("can commit")]
    [DataRow("next safe action")]
    public void IssueMessagesRejectAuthorityText(string unsafeMessage)
    {
        var envelope = OperationStatusReadEnvelopeFactory.InvalidRequest(
            Context(),
            [
                OperationStatusReadEnvelopeFactory.Issue(
                    OperationStatusReadErrorCode.OperationStatusRequestInvalid,
                    unsafeMessage,
                    OperationStatusReadIssueSeverity.Error)
            ]);

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusReadIssueMessageUnsafe");
    }

    [DataTestMethod]
    [DataRow("approved")]
    [DataRow("policy satisfied")]
    [DataRow("can push")]
    [DataRow("next safe action")]
    public void WarningMessagesRejectAuthorityText(string unsafeWarning)
    {
        var envelope = GoodEnvelope() with
        {
            Warnings =
            [
                ..OperationStatusReadEnvelopeValidator.RequiredWarnings,
                unsafeWarning
            ]
        };

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        AssertContains(validation.Issues, "OperationStatusReadWarningUnsafe");
    }

    [TestMethod]
    public void RedactedSummaryRequiresSafeRedactionReason()
    {
        var missing = OperationStatusReadEnvelopeValidator.Validate(
            OperationStatusReadEnvelopeFactory.Redacted(Context(), Summary() with { IsRedacted = true, RedactionReason = "" }));
        var unsafeReason = OperationStatusReadEnvelopeValidator.Validate(
            OperationStatusReadEnvelopeFactory.Redacted(Context(), Summary() with { IsRedacted = true, RedactionReason = "raw payload withheld" }));

        AssertContains(missing.Issues, "OperationStatusSafeSummaryRedactionReasonRequired");
        AssertContains(unsafeReason.Issues, "OperationStatusSafeSummaryRedactionReasonInvalid");
    }

    [DataTestMethod]
    [DataRow("page-size")]
    [DataRow("item-count")]
    [DataRow("matched-count")]
    [DataRow("scanned-count")]
    [DataRow("has-more-no-cursor")]
    [DataRow("cursor-state")]
    public void PageSummaryRejectsImpossibleOrUnsafeState(string caseName)
    {
        var summary = caseName switch
        {
            "page-size" => PageSummary() with { PageSize = -1 },
            "item-count" => PageSummary() with { ItemCount = -1 },
            "matched-count" => PageSummary() with { MatchedCount = -1 },
            "scanned-count" => PageSummary() with { ScannedCount = -1 },
            "has-more-no-cursor" => PageSummary() with { HasMore = true, HasNextCursor = false },
            "cursor-state" => PageSummary() with { HasNextCursor = true, CursorState = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        var envelope = OperationStatusReadEnvelopeFactory.PageSuccess(Context(OperationStatusReadKind.StatusPage) with
        {
            OperationId = null,
            CorrelationId = null
        }, summary);

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
    }

    [DataTestMethod]
    [DataRow("raw payload withheld")]
    [DataRow("approved for release")]
    [DataRow("SELECT * FROM dbo.OperationStatus")]
    public void SafeSummaryTextRejectsRawOrAuthorityMarkers(string unsafeReason)
    {
        var envelope = OperationStatusReadEnvelopeFactory.Redacted(
            Context(),
            Summary() with { IsRedacted = true, RedactionReason = unsafeReason });

        var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Contains("RedactionReason", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ForbiddenAuthorityImplications_AreIncludedInEveryFactoryEnvelope()
    {
        var envelopes = new[]
        {
            OperationStatusReadEnvelopeFactory.Success(Context(), Summary()),
            OperationStatusReadEnvelopeFactory.NotFound(Context()),
            OperationStatusReadEnvelopeFactory.InvalidRequest(Context()),
            OperationStatusReadEnvelopeFactory.Ambiguous(Context()),
            OperationStatusReadEnvelopeFactory.Unassessable(Context()),
            OperationStatusReadEnvelopeFactory.Redacted(Context(), Summary() with { IsRedacted = true, RedactionReason = "summary withheld" }),
            OperationStatusReadEnvelopeFactory.Error(Context())
        };

        foreach (var envelope in envelopes)
        {
            var validation = OperationStatusReadEnvelopeValidator.Validate(envelope);
            Assert.IsTrue(validation.Issues.All(issue => !issue.StartsWith("OperationStatusReadForbiddenAuthorityImplicationMissing:", StringComparison.Ordinal)), string.Join(", ", validation.Issues));
            AssertContains(envelope.ForbiddenAuthorityImplications, "operation status read envelope is not workflow continuation");
            AssertContains(envelope.ForbiddenAuthorityImplications, "operation status read envelope is not approval");
        }
    }

    [TestMethod]
    public void EnvelopeContractsExposeNoAuthorityOrRawPayloadProperties()
    {
        var forbiddenNames = new[]
        {
            "CanApply",
            "CanCommit",
            "CanPush",
            "CanCreatePullRequest",
            "CanRollback",
            "CanRecover",
            "CanRetry",
            "CanResume",
            "CanContinue",
            "ApprovalStatus",
            "PolicySatisfied",
            "NextSafeAction",
            "AuthorityGranted",
            "ActionAllowed",
            "RawPatch",
            "RawDiff",
            "RawEvidencePayload",
            "RawReceiptPayload",
            "RawTimelinePayload",
            "SourceContent"
        };

        var types = new[]
        {
            typeof(OperationStatusReadEnvelope),
            typeof(OperationStatusSafeSummary),
            typeof(OperationStatusReadIssue),
            typeof(OperationStatusPageEnvelopeSummary)
        };

        foreach (var type in types)
        {
            var names = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(static property => property.Name).ToArray();
            Assert.IsFalse(names.Any(static name => name.StartsWith("Can", StringComparison.Ordinal)), $"{type.Name} exposes a Can* property.");
            foreach (var forbidden in forbiddenNames)
            {
                Assert.IsFalse(names.Contains(forbidden, StringComparer.Ordinal), $"{type.Name} exposes {forbidden}.");
            }
        }
    }

    [TestMethod]
    public void D01D03IdentityAndCorrelationValidationStillPass()
    {
        var identity = OperationIdentityValidator.ValidateOperationId(OperationId);
        var correlation = OperationCorrelationValidator.ValidateLink(new OperationCorrelationLink
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            SurfaceKind = OperationCorrelationSurfaceKind.OperationStatus,
            SurfaceId = "status-d18",
            ObservedAtUtc = AsOfUtc,
            Source = "d18-test"
        });

        Assert.IsTrue(identity.IsValid, string.Join(", ", identity.Issues));
        Assert.IsTrue(correlation.IsValid, string.Join(", ", correlation.Issues));
    }

    [TestMethod]
    public void D16PaginatorCanStillRepresentNoRowsWithoutD18InvokingIt()
    {
        var page = OperationStatusPaginator.Page(new OperationStatusPageRequest
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            AsOfUtc = AsOfUtc,
            PageSize = 10,
            SortField = OperationStatusPageSortField.CreatedAtUtc,
            SortDirection = OperationStatusPageSortDirection.Ascending,
            Rows = []
        });

        Assert.IsTrue(page.IsValid, string.Join(", ", page.Issues));
        Assert.AreEqual(OperationStatusPageResolutionStatus.NoRows, page.ResolutionStatus);
    }

    [TestMethod]
    public void StaticScan_D18CoreAddsNoApiSqlUiStoreOrMutationSurface()
    {
        var source = SanitizedCoreSource();

        foreach (var marker in new[]
                 {
                     "Controller",
                     "MapGet",
                     "MapPost",
                     "OpenApi",
                     "SqlConnection",
                     "DbContext",
                     "MigrationBuilder",
                     "IQueryable",
                     "HttpClient",
                     "File.",
                     "Directory.",
                     "ProcessStartInfo",
                     "Process.",
                     "RunProcessAsync",
                     "git ",
                     "gh ",
                     "CommitAsync",
                     "PushAsync",
                     "MergeAsync",
                     "Deploy",
                     "Release",
                     "PromoteMemory",
                     "ContinueWorkflow"
                 })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void StaticScan_D18CoreDoesNotInvokeUpstreamResolversOrPaginator()
    {
        var source = SanitizedCoreSource();

        foreach (var marker in new[]
                 {
                     "OperationLookup",
                     "OperationTimeline",
                     "AppendOnlyEventToStatusProjection",
                     "MissingEvidenceResolver.",
                     "ForbiddenActionResolver.",
                     "ReceiptReferenceResolver.",
                     "EvidenceResolver.",
                     "ValidationResultStalenessResolver.",
                     "PatchBaseFreshnessResolver.",
                     "WorktreeBaseHeadFreshnessReadModel.",
                     "InterruptedRunReadModelBuilder.",
                     "RollbackRecoveryReadModelBuilder.",
                     "OperationStatusPaginator.",
                     "BlockedState",
                     "NextSafeAction",
                     "AuthorityWarning"
                 })
        {
            AssertDoesNotContain(source, marker);
        }
    }

    [TestMethod]
    public void StaticScan_D18FactoryDoesNotUseSystemClockOrAuthorization()
    {
        var source = StripStrings(StripComments(File.ReadAllText(RepoPath("IronDev.Core", "Governance", "OperationStatusReadEnvelopeFactory.cs"))));

        AssertDoesNotContain(source, "UtcNow");
        AssertDoesNotContain(source, "Now");
        AssertDoesNotContain(source, "Authorize");
        AssertDoesNotContain(source, "TenantStore");
    }

    [TestMethod]
    public void ReceiptRecordsNotFoundDoesNotLeakTenantExistenceAndEnvelopeIsNotAuthority()
    {
        var receipt = File.ReadAllText(RepoPath("Docs", "receipts", "D18_OPERATION_STATUS_ERROR_ENVELOPE.md"));

        Assert.Contains("not-found-does-not-leak-tenant-existence", receipt);
        Assert.Contains("envelope-is-not-authority", receipt);
        Assert.Contains("The operation status not-found/error envelope standard represents read-model failure safely using supplied context and safe metadata only.", receipt);
    }

    private static OperationStatusReadEnvelope GoodEnvelope() =>
        OperationStatusReadEnvelopeFactory.Success(Context(), Summary());

    private static OperationStatusReadContext Context(OperationStatusReadKind readKind = OperationStatusReadKind.SingleStatus) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReadKind = readKind,
            AsOfUtc = AsOfUtc,
            Source = "d18-test"
        };

    private static OperationStatusSafeSummary Summary() =>
        new()
        {
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ProjectedStatus = OperationProjectedStatusKind.Minted,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = CreatedAtUtc.AddMinutes(1),
            LastEventAtUtc = CreatedAtUtc.AddMinutes(2),
            TimelineEventCount = 1,
            DiagnosticStatusSummary = new OperationStatusDiagnosticStatusSummary
            {
                MissingEvidenceStatus = MissingEvidenceResolutionStatus.Complete,
                ForbiddenActionStatus = ForbiddenActionResolutionStatus.NoForbiddenFactsObserved,
                ReceiptResolutionStatus = ReceiptReferenceResolutionStatus.Resolved,
                EvidenceResolutionStatus = EvidenceResolutionStatus.Resolved,
                ValidationStalenessStatus = ValidationStalenessResolutionStatus.Assessed,
                PatchBaseFreshnessStatus = PatchBaseFreshnessResolutionStatus.Assessed,
                WorktreeBaseHeadFreshnessStatus = WorktreeBaseHeadFreshnessResolutionStatus.Assessed,
                InterruptedRunStatus = InterruptedRunReadModelStatus.NoInterruptionObserved,
                RollbackRecoveryStatus = RollbackRecoveryReadModelStatus.NoMaterial
            },
            IsRedacted = false,
            RedactionReason = null
        };

    private static OperationStatusPageEnvelopeSummary PageSummary() =>
        new()
        {
            PageSize = 25,
            ItemCount = 1,
            MatchedCount = 1,
            ScannedCount = 1,
            HasMore = false,
            HasNextCursor = false,
            CursorState = "none"
        };

    private static void AssertNoAuthoritySurface(OperationStatusReadEnvelope envelope)
    {
        var text = string.Join("\n",
            envelope.Issues.Select(static issue => issue.Message)
                .Concat(envelope.Warnings)
                .Concat(envelope.ForbiddenAuthorityImplications));

        Assert.DoesNotContain("approved", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("policy satisfied", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("can apply", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("can commit", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizedCoreSource()
    {
        var files = new[]
        {
            RepoPath("IronDev.Core", "Governance", "OperationStatusReadEnvelopeModels.cs"),
            RepoPath("IronDev.Core", "Governance", "OperationStatusReadEnvelopeValidator.cs"),
            RepoPath("IronDev.Core", "Governance", "OperationStatusReadEnvelopeFactory.cs")
        };

        return string.Join("\n", files.Select(static file => StripStrings(StripComments(File.ReadAllText(file)))));
    }

    private static string RepoPath(params string[] parts) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", Path.Combine(parts));

    private static string StripComments(string source)
    {
        var withoutLineComments = LineCommentRegex().Replace(source, "");
        return BlockCommentRegex().Replace(withoutLineComments, "");
    }

    private static string StripStrings(string source) =>
        StringLiteralRegex().Replace(source, "\"\"");

    private static void AssertContains(
        IEnumerable<string> values,
        string expected)
    {
        if (!values.Any(value => string.Equals(value, expected, StringComparison.Ordinal)))
        {
            Assert.Fail($"Expected '{expected}' in: {string.Join(", ", values)}");
        }
    }

    private static void AssertDoesNotContain(string source, string marker)
    {
        if (source.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"Unexpected marker '{marker}' found in D18 core source.");
        }
    }

    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"")]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex("//.*?$", RegexOptions.Multiline)]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex("/\\*.*?\\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();
}
