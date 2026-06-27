using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF04ViewerRoleReadOnlyEnforcementTests
{
    [TestMethod]
    public void ReadOnlyIntentWithSeparateEvidenceMayProceedOnlyToSeparateVisibilityDecision()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request());

        Assert.AreEqual(ViewerReadOnlyDecisionKind.MayProceedToSeparateVisibilityDecision, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.None, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateRoleAssignment);
        Assert.IsTrue(decision.RequiresSeparateVisibilityDecision);
        Assert.IsTrue(decision.RequiresSeparateActionAuthority);
        Assert.IsTrue(decision.RequiresSeparateApproval);
        Assert.IsTrue(decision.RequiresSeparatePolicySatisfaction);
        Assert.IsTrue(decision.RequiresSeparateMutationAuthority);
        Assert.IsTrue(decision.RequiresSeparateWorkflowAuthority);
        Assert.IsFalse(decision.RequiresHumanReview);
        AssertRequiredWarnings(decision);
        AssertRequiredForbiddenImplications(decision);
    }

    [DataTestMethod]
    [DataRow(ViewerReadOnlyIntentKind.ReadStatus)]
    [DataRow(ViewerReadOnlyIntentKind.ReadReceipt)]
    [DataRow(ViewerReadOnlyIntentKind.ReadAudit)]
    [DataRow(ViewerReadOnlyIntentKind.ReadSummary)]
    [DataRow(ViewerReadOnlyIntentKind.ReadMetadata)]
    [DataRow(ViewerReadOnlyIntentKind.ReadReference)]
    [DataRow(ViewerReadOnlyIntentKind.ReadFrontendView)]
    public void BoundedReadIntentsMayReachSeparateVisibilityDecision(ViewerReadOnlyIntentKind intent)
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { IntentKind = intent });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.MayProceedToSeparateVisibilityDecision, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.None, decision.BlockKind);
        AssertRequiredWarnings(decision);
        AssertRequiredForbiddenImplications(decision);
    }

    [TestMethod]
    public void RedactedReadIntentRequiresSeparatePolicyAndRedactionEvidence()
    {
        var (catalog, matrix, entry) = SensitiveFixture();
        var request = Request(catalog, matrix, entry) with
        {
            IntentKind = ViewerReadOnlyIntentKind.ReadRedactedDetails,
            PolicyDecisionEvidenceRef = null,
            RedactionEvidenceRef = null
        };

        var decision = Service().Evaluate(catalog, matrix, request);

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.MissingEvidence, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparatePolicyDecision);
        Assert.IsTrue(decision.RequiresSeparateRedactionEnforcement);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    [TestMethod]
    public void RedactedReadIntentMayProceedWhenSeparatePolicyAndRedactionEvidenceArePresent()
    {
        var (catalog, matrix, entry) = SensitiveFixture();
        var request = Request(catalog, matrix, entry) with
        {
            IntentKind = ViewerReadOnlyIntentKind.ReadRedactedDetails
        };

        var decision = Service().Evaluate(catalog, matrix, request);

        Assert.AreEqual(ViewerReadOnlyDecisionKind.MayProceedToSeparateVisibilityDecision, decision.Decision);
        Assert.IsTrue(decision.RequiresSeparatePolicyDecision);
        Assert.IsTrue(decision.RequiresSeparateRedactionEnforcement);
    }

    [TestMethod]
    public void MissingRoleAssignmentEvidenceBlocksReadOnlyIntent()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { RoleAssignmentEvidenceRef = null });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.MissingEvidence, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateRoleAssignment);
    }

    [TestMethod]
    public void MissingVisibilityDecisionEvidenceBlocksReadOnlyIntent()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { VisibilityDecisionEvidenceRef = null });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.MissingEvidence, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateVisibilityDecision);
    }

    [TestMethod]
    public void HiddenRawPayloadEntryBlocksReadOnlyIntent()
    {
        var catalog = Catalog();
        var matrix = Matrix(catalog);
        var entry = matrix.Entries.Single(item =>
            item.RoleKind == GovernanceRoleKind.Observer &&
            item.MaterialKind == RoleVisibilityMaterialKind.RawPayload);

        var decision = Service().Evaluate(catalog, matrix, Request(catalog, matrix, entry));

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.RoleVisibilityMismatch, decision.BlockKind);
    }

    [TestMethod]
    public void UnknownRoleBlocksBeforeVisibilityDecision()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { RoleId = "role:f01:missing" });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByUnknownRole, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.UnknownRole, decision.BlockKind);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    [TestMethod]
    public void RoleKindMismatchBlocksBeforeVisibilityDecision()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { RoleKind = GovernanceRoleKind.Reviewer });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.RoleVisibilityMismatch, decision.BlockKind);
    }

    [TestMethod]
    public void CatalogMismatchBlocksBeforeVisibilityDecision()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { RoleCatalogVersion = "f04" });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.RoleVisibilityMismatch, decision.BlockKind);
    }

    [TestMethod]
    public void VisibilityLevelMismatchBlocksBeforeVisibilityDecision()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { VisibilityLevel = RoleVisibilityLevel.ReferenceOnly });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByRoleVisibilityMismatch, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.RoleVisibilityMismatch, decision.BlockKind);
    }

    [TestMethod]
    public void InvalidRoleCatalogBlocksBeforeVisibilityDecision()
    {
        var catalog = Catalog() with { Entries = [] };
        var decision = Service().Evaluate(catalog, Matrix(Catalog()), Request());

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByInvalidRoleCatalog, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.InvalidCatalog, decision.BlockKind);
    }

    [TestMethod]
    public void InvalidVisibilityMatrixBlocksBeforeVisibilityDecision()
    {
        var matrix = Matrix() with { Entries = [] };
        var decision = Service().Evaluate(Catalog(), matrix, Request());

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByInvalidVisibilityMatrix, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.InvalidMatrix, decision.BlockKind);
    }

    [DataTestMethod]
    [DataRow(ViewerReadOnlyIntentKind.ActionSourceApply, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionCommit, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionPush, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionPullRequest, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionReadyForReview, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionReviewRequest, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionMerge, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionRelease, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionDeploy, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionRollback, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionRetry, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionRecover, ViewerReadOnlyDecisionKind.BlockedByMutationIntent, ViewerReadOnlyBlockKind.MutationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionApprove, ViewerReadOnlyDecisionKind.BlockedByApprovalIntent, ViewerReadOnlyBlockKind.ApprovalIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionSatisfyPolicy, ViewerReadOnlyDecisionKind.BlockedByPolicyIntent, ViewerReadOnlyBlockKind.PolicyIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionContinueWorkflow, ViewerReadOnlyDecisionKind.BlockedByWorkflowContinuationIntent, ViewerReadOnlyBlockKind.WorkflowContinuationIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionPromoteMemory, ViewerReadOnlyDecisionKind.BlockedByMemoryPromotionIntent, ViewerReadOnlyBlockKind.MemoryPromotionIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionBypassRedaction, ViewerReadOnlyDecisionKind.BlockedByRedactionBypassIntent, ViewerReadOnlyBlockKind.RedactionBypassIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionDiscloseSecret, ViewerReadOnlyDecisionKind.BlockedBySensitiveDisclosureIntent, ViewerReadOnlyBlockKind.SensitiveDisclosureIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionDiscloseCredential, ViewerReadOnlyDecisionKind.BlockedBySensitiveDisclosureIntent, ViewerReadOnlyBlockKind.SensitiveDisclosureIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionDisclosePrivateReasoning, ViewerReadOnlyDecisionKind.BlockedBySensitiveDisclosureIntent, ViewerReadOnlyBlockKind.SensitiveDisclosureIntent)]
    [DataRow(ViewerReadOnlyIntentKind.ActionDiscloseRawPayload, ViewerReadOnlyDecisionKind.BlockedBySensitiveDisclosureIntent, ViewerReadOnlyBlockKind.SensitiveDisclosureIntent)]
    public void ViewerReadOnlyEvidenceBlocksActionIntent(
        ViewerReadOnlyIntentKind intent,
        ViewerReadOnlyDecisionKind expectedDecision,
        ViewerReadOnlyBlockKind expectedBlock)
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { IntentKind = intent });

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(expectedBlock, decision.BlockKind);
        Assert.IsTrue(decision.RequiresSeparateActionAuthority);
        AssertRequiredWarnings(decision);
        AssertRequiredForbiddenImplications(decision);
    }

    [TestMethod]
    public void UnknownIntentBlocksBeforeVisibilityDecision()
    {
        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { IntentKind = ViewerReadOnlyIntentKind.Unknown });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.InvalidRequest, decision.BlockKind);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    [TestMethod]
    public void UnsafePayloadBlocksAndDoesNotEchoRawProviderText()
    {
        var rawProviderText = string.Join(" ", "raw", "provider", "response");

        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { TenantId = rawProviderText });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(ViewerReadOnlyBlockKind.UnsafePayload, decision.BlockKind);
        Assert.AreEqual("[unsafe-rejected]", decision.TenantId);
        Assert.IsFalse(decision.RecordFingerprint.Contains(rawProviderText, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void UnsafeCredentialMarkerBlocksAndDoesNotEchoMaterial()
    {
        var credentialMarker = string.Concat("pass", "word", "=", "value");

        var decision = Service().Evaluate(Catalog(), Matrix(), Request() with { ProjectId = credentialMarker });

        Assert.AreEqual(ViewerReadOnlyDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual("[unsafe-rejected]", decision.ProjectId);
        Assert.IsFalse(decision.RecordFingerprint.Contains(credentialMarker, StringComparison.OrdinalIgnoreCase));
    }

    [DataTestMethod]
    [DataRow("status-read-ref:f04")]
    [DataRow("receipt-metadata:f04")]
    [DataRow("audit-summary:f04")]
    [DataRow("redacted-details:f04")]
    [DataRow("visibility-decision:f04")]
    public void SafeReadOnlyRefsAreNotRejectedAsUnsafe(string value)
    {
        Assert.IsFalse(ViewerReadOnlyEnforcementValidator.ContainsUnsafeViewerText(value));
    }

    [TestMethod]
    public void NullRequestValidationFailsClosed()
    {
        var result = ViewerReadOnlyEnforcementValidator.ValidateRequest(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "ViewerReadOnlyRequestRequired");
    }

    [TestMethod]
    public void ValidationRequiresUtcObservation()
    {
        var result = ViewerReadOnlyEnforcementValidator.ValidateRequest(
            Request() with { ObservedAtUtc = new DateTimeOffset(2026, 6, 27, 1, 0, 0, TimeSpan.FromHours(1)) });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "ObservedAtUtcMustBeUtc");
    }

    [TestMethod]
    public void DecisionModelDoesNotExposeGrantOrAuthorityFlags()
    {
        var forbiddenNames = new[]
        {
            "CanApply",
            "CanCommit",
            "CanPush",
            "CanCreatePullRequest",
            "CanMarkReadyForReview",
            "CanRequestReviewers",
            "CanApprove",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanContinueWorkflow",
            "GrantsAccess",
            "GrantsPermission",
            "GrantsAuthority",
            "IsAuthorized"
        };

        var propertyNames = typeof(ViewerReadOnlyEnforcementDecision)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        foreach (var forbiddenName in forbiddenNames)
        {
            CollectionAssert.DoesNotContain(propertyNames, forbiddenName);
        }
    }

    [TestMethod]
    public void StaticScanF04CoreAddsNoExecutorProviderOrMutationSurface()
    {
        var source = F04CoreSourceWithoutStrings();
        var forbidden = new[]
        {
            "RunProcessAsync",
            "ProcessStartInfo",
            "HttpClient",
            "File.Write",
            "File.Read",
            "Directory.",
            "git ",
            "gh ",
            "dotnet ",
            "ExecuteAsync",
            "ApplyAsync",
            "CommitAsync",
            "PushAsync",
            "MergeAsync",
            "ReleaseAsync",
            "DeployAsync",
            "ContinueWorkflowAsync",
            "PromoteMemoryAsync",
            "AuthorizeAsync",
            "AccessGranted",
            "PermissionGranted"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesViewerEnforcementCannotGrantAccess()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F04_VIEWER_ROLE_READ_ONLY_ENFORCEMENT.md"));

        StringAssert.Contains(receipt, "Viewer role enforcement can block action. It cannot grant access.");
        StringAssert.Contains(receipt, "Read-only is a brake, not a key.");
        StringAssert.Contains(receipt, "does not grant access");
        StringAssert.Contains(receipt, "does not approve work");
        StringAssert.Contains(receipt, "does not satisfy policy");
        StringAssert.Contains(receipt, "does not authorize execution");
        StringAssert.Contains(receipt, "does not bypass redaction");
    }

    private static ViewerReadOnlyEnforcementService Service() => new();

    private static GovernanceRoleCatalog Catalog() => new RoleCatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() => Matrix(Catalog());

    private static RoleVisibilityMatrix Matrix(GovernanceRoleCatalog catalog) =>
        new RoleVisibilityMatrixService().BuildDefaultMatrix(catalog);

    private static (GovernanceRoleCatalog Catalog, RoleVisibilityMatrix Matrix, RoleVisibilityMatrixEntry Entry) SensitiveFixture()
    {
        var catalog = Catalog();
        var matrix = Matrix(catalog);
        var entry = matrix.Entries.Single(item =>
            item.RoleKind == GovernanceRoleKind.PolicyOwnerCandidate &&
            item.MaterialKind == RoleVisibilityMaterialKind.SensitiveFindingSummary);
        return (catalog, matrix, entry);
    }

    private static ViewerReadOnlyEnforcementRequest Request() =>
        Request(Catalog(), Matrix(), Entry());

    private static ViewerReadOnlyEnforcementRequest Request(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrix matrix,
        RoleVisibilityMatrixEntry entry)
    {
        var role = catalog.Entries.Single(item => string.Equals(item.RoleId, entry.RoleId, StringComparison.Ordinal));
        return new ViewerReadOnlyEnforcementRequest
        {
            TenantId = "tenant-f04",
            ProjectId = "project-f04",
            OperationId = "operation-f04",
            CorrelationId = "correlation-f04",
            RoleId = role.RoleId,
            RoleKind = role.RoleKind,
            RoleScopeKind = role.ScopeKind,
            ViewerRoleKind = ViewerReadOnlyRoleKind.Observer,
            VisibilitySurface = entry.Surface,
            VisibilityMaterialKind = entry.MaterialKind,
            VisibilityLevel = entry.VisibilityLevel,
            SensitivityKind = entry.SensitivityKind,
            IntentKind = ViewerReadOnlyIntentKind.ReadStatus,
            RequestedSurfaceRef = "surface:f04",
            RequestedMaterialRef = "material:f04",
            RequestedEvidenceRef = "evidence:f04",
            RoleCatalogId = catalog.CatalogId,
            RoleCatalogVersion = catalog.CatalogVersion,
            RoleCatalogEntryRef = "role-entry:f04",
            VisibilityMatrixId = matrix.MatrixId,
            VisibilityMatrixVersion = matrix.CatalogVersion,
            VisibilityMatrixEntryRef = "matrix-entry:f04",
            RoleAssignmentEvidenceRef = "role-assignment:f04",
            VisibilityDecisionEvidenceRef = "visibility-decision:f04",
            PolicyDecisionEvidenceRef = "policy-decision:f04",
            RedactionEvidenceRef = "redaction:f04",
            ReasonCode = "readonly-boundary",
            Source = "f04-test",
            ObservedAtUtc = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static RoleVisibilityMatrixEntry Entry()
    {
        var matrix = Matrix();
        return matrix.Entries.Single(item =>
            item.RoleKind == GovernanceRoleKind.Observer &&
            item.Surface == RoleVisibilitySurface.OperationStatus &&
            item.MaterialKind == RoleVisibilityMaterialKind.OperationStatusSummary);
    }

    private static void AssertRequiredWarnings(ViewerReadOnlyEnforcementDecision decision)
    {
        foreach (var warning in ViewerReadOnlyEnforcementService.RequiredWarnings)
        {
            CollectionAssert.Contains(decision.Warnings.ToList(), warning);
        }
    }

    private static void AssertRequiredForbiddenImplications(ViewerReadOnlyEnforcementDecision decision)
    {
        foreach (var warning in ViewerReadOnlyEnforcementService.RequiredForbiddenAuthorityImplications)
        {
            CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), warning);
        }
    }

    private static string F04CoreSourceWithoutStrings()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "ViewerReadOnlyEnforcement*.cs");
        return string.Join(Environment.NewLine, files.Select(path => StripStringLiterals(File.ReadAllText(path))));
    }

    private static string StripStringLiterals(string source)
    {
        var chars = source.ToCharArray();
        var inString = false;
        var inVerbatim = false;
        for (var i = 0; i < chars.Length; i++)
        {
            if (!inString && chars[i] == '@' && i + 1 < chars.Length && chars[i + 1] == '"')
            {
                inString = true;
                inVerbatim = true;
                chars[i] = ' ';
                continue;
            }

            if (!inString && chars[i] == '"')
            {
                inString = true;
                inVerbatim = false;
                chars[i] = ' ';
                continue;
            }

            if (!inString)
            {
                continue;
            }

            if (inVerbatim && chars[i] == '"' && i + 1 < chars.Length && chars[i + 1] == '"')
            {
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i++;
                continue;
            }

            if ((!inVerbatim && chars[i] == '"' && (i == 0 || chars[i - 1] != '\\')) ||
                (inVerbatim && chars[i] == '"'))
            {
                inString = false;
                inVerbatim = false;
            }

            chars[i] = ' ';
        }

        return new string(chars);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Unable to locate repository root.");
    }
}
