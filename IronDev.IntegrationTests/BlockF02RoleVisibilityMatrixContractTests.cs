using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF02RoleVisibilityMatrixContractTests
{
    [TestMethod]
    public void DefaultRoleVisibilityMatrixIsValid()
    {
        var result = MatrixService().ValidateDefaultMatrix(Catalog());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixHasStableMatrixIdAndVersion()
    {
        var matrix = Matrix();

        Assert.AreEqual(RoleVisibilityMatrixService.DefaultMatrixId, matrix.MatrixId);
        Assert.AreEqual(RoleCatalogService.DefaultCatalogVersion, matrix.CatalogVersion);
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixReferencesF01Catalog()
    {
        var catalog = Catalog();
        var matrix = MatrixService().BuildDefaultMatrix(catalog);

        Assert.AreEqual(RoleCatalogService.DefaultCatalogId, matrix.CatalogId);
        Assert.AreEqual(catalog.CatalogVersion, matrix.CatalogVersion);
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixCoversAllDefaultRoles()
    {
        var matrix = Matrix();

        foreach (var role in Catalog().Entries)
        {
            Assert.IsTrue(matrix.Entries.Any(entry => entry.RoleId == role.RoleId), role.RoleId);
        }
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixHasUniqueRoleSurfaceMaterialKeys()
    {
        var matrix = Matrix();
        var unique = matrix.Entries
            .Select(static entry => $"{entry.RoleId}|{entry.Surface}|{entry.MaterialKind}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.AreEqual(matrix.Entries.Count, unique);
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixEntriesRequireSeparateRoleAssignment()
    {
        Assert.IsTrue(Matrix().Entries.All(static entry => entry.RequiresSeparateRoleAssignment));
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixEntriesRequireSeparateVisibilityDecision()
    {
        Assert.IsTrue(Matrix().Entries.All(static entry => entry.RequiresSeparateVisibilityDecision));
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixEntriesHaveAuthorityDenyingBoundaryStatements()
    {
        foreach (var entry in Matrix().Entries)
        {
            StringAssert.Contains(entry.BoundaryStatement, "does not grant access");
            StringAssert.Contains(entry.BoundaryStatement, "does not grant authority");
        }
    }

    [TestMethod]
    public void DefaultRoleVisibilityMatrixIncludesNoUnknownSurfacesMaterialsSensitivityOrLevels()
    {
        foreach (var entry in Matrix().Entries)
        {
            Assert.AreNotEqual(RoleVisibilitySurface.Unknown, entry.Surface);
            Assert.AreNotEqual(RoleVisibilityMaterialKind.Unknown, entry.MaterialKind);
            Assert.AreNotEqual(RoleVisibilitySensitivityKind.Unknown, entry.SensitivityKind);
            Assert.AreNotEqual(RoleVisibilityLevel.Unknown, entry.VisibilityLevel);
        }
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.Requester, RoleVisibilityMaterialKind.PlanSummary, RoleVisibilityLevel.SummaryOnly)]
    [DataRow(GovernanceRoleKind.Requester, RoleVisibilityMaterialKind.ProposalSummary, RoleVisibilityLevel.SummaryOnly)]
    [DataRow(GovernanceRoleKind.Requester, RoleVisibilityMaterialKind.OperationStatusSummary, RoleVisibilityLevel.SummaryOnly)]
    [DataRow(GovernanceRoleKind.Requester, RoleVisibilityMaterialKind.ReceiptMetadata, RoleVisibilityLevel.MetadataOnly)]
    public void RequesterHasSummaryOrMetadataVisibilityHintsOnly(
        GovernanceRoleKind kind,
        RoleVisibilityMaterialKind material,
        RoleVisibilityLevel level) =>
        AssertRoleHas(kind, material, level);

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.PlanSummary)]
    [DataRow(RoleVisibilityMaterialKind.ProposalSummary)]
    public void PlannerHasPlanningAndProposalVisibilityHints(RoleVisibilityMaterialKind material) =>
        AssertRoleHas(GovernanceRoleKind.Planner, material);

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.ProposalSummary)]
    [DataRow(RoleVisibilityMaterialKind.ValidationSummary)]
    [DataRow(RoleVisibilityMaterialKind.ValidationEvidenceRefs)]
    [DataRow(RoleVisibilityMaterialKind.PullRequestMetadata)]
    [DataRow(RoleVisibilityMaterialKind.PullRequestDiffSummary)]
    public void ReviewerHasReviewVisibilityHintsWithoutApprovalAuthority(RoleVisibilityMaterialKind material)
    {
        AssertRoleHas(GovernanceRoleKind.Reviewer, material);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not approval");
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.ApprovalPackageSummary)]
    [DataRow(RoleVisibilityMaterialKind.PolicyReviewSummary)]
    [DataRow(RoleVisibilityMaterialKind.ValidationSummary)]
    public void ApproverCandidateHasApprovalPackageHintsWithoutApprovalAuthority(RoleVisibilityMaterialKind material)
    {
        AssertRoleHas(GovernanceRoleKind.ApproverCandidate, material);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not approval");
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.PolicyReviewSummary)]
    [DataRow(RoleVisibilityMaterialKind.ApprovalPackageSummary)]
    [DataRow(RoleVisibilityMaterialKind.SensitiveFindingSummary)]
    [DataRow(RoleVisibilityMaterialKind.SecretScanSummary)]
    public void PolicyOwnerCandidateHasPolicyReviewHintsWithoutPolicySatisfaction(RoleVisibilityMaterialKind material)
    {
        AssertRoleHas(GovernanceRoleKind.PolicyOwnerCandidate, material);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not policy satisfaction");
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.ValidationSummary)]
    [DataRow(RoleVisibilityMaterialKind.SensitiveFindingSummary)]
    [DataRow(RoleVisibilityMaterialKind.SecretScanSummary)]
    [DataRow(RoleVisibilityMaterialKind.PatchMetadata)]
    [DataRow(RoleVisibilityMaterialKind.PullRequestDiffSummary)]
    public void SecurityReviewerHasSensitiveSummaryHintsWithRedaction(RoleVisibilityMaterialKind material)
    {
        var entries = EntriesFor(GovernanceRoleKind.SecurityReviewer).Where(entry => entry.MaterialKind == material).ToArray();

        Assert.IsTrue(entries.Length > 0, material.ToString());
        foreach (var entry in entries.Where(static entry =>
            entry.MaterialKind is RoleVisibilityMaterialKind.SensitiveFindingSummary or RoleVisibilityMaterialKind.SecretScanSummary))
        {
            Assert.IsTrue(entry.RequiresRedaction);
            Assert.IsTrue(entry.RequiresSeparatePolicyDecision);
        }
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.MergeReadinessSummary, "role visibility matrix is not merge authority")]
    [DataRow(RoleVisibilityMaterialKind.ReleaseReadinessSummary, "role visibility matrix is not release authority")]
    [DataRow(RoleVisibilityMaterialKind.DeploymentReadinessSummary, "role visibility matrix is not deployment authority")]
    public void ReleaseReviewerHasReleaseReadinessHintsWithoutReleaseAuthority(
        RoleVisibilityMaterialKind material,
        string forbiddenImplication)
    {
        AssertRoleHas(GovernanceRoleKind.ReleaseReviewer, material);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), forbiddenImplication);
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.DeploymentReadinessSummary)]
    [DataRow(RoleVisibilityMaterialKind.OperationStatusSummary)]
    [DataRow(RoleVisibilityMaterialKind.RecoverySummary)]
    [DataRow(RoleVisibilityMaterialKind.RollbackSummary)]
    public void OperationsReviewerHasDeploymentReadinessHintsWithoutDeploymentAuthority(RoleVisibilityMaterialKind material)
    {
        AssertRoleHas(GovernanceRoleKind.OperationsReviewer, material);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not deployment authority");
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.SourceApplySummary)]
    [DataRow(RoleVisibilityMaterialKind.CommitPackageSummary)]
    [DataRow(RoleVisibilityMaterialKind.PushReceiptSummary)]
    [DataRow(RoleVisibilityMaterialKind.OperationStatusSummary)]
    public void ExecutorOperatorCandidateHasExecutorSurfaceHintsWithoutExecutionAuthority(RoleVisibilityMaterialKind material)
    {
        AssertRoleHas(GovernanceRoleKind.ExecutorOperatorCandidate, material);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not execution authority");
    }

    [TestMethod]
    public void RollbackReviewerHasRollbackHintsWithoutRollbackAuthority()
    {
        AssertRoleHas(GovernanceRoleKind.RollbackReviewer, RoleVisibilityMaterialKind.RollbackSummary);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not mutation authority");
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.RecoverySummary)]
    [DataRow(RoleVisibilityMaterialKind.RetrySummary)]
    public void RecoveryReviewerHasRecoveryHintsWithoutRecoveryAuthority(RoleVisibilityMaterialKind material)
    {
        AssertRoleHas(GovernanceRoleKind.RecoveryReviewer, material);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not mutation authority");
    }

    [TestMethod]
    public void AuditorHasAuditHintsWithoutAuthority()
    {
        AssertRoleHas(GovernanceRoleKind.Auditor, RoleVisibilityMaterialKind.AuditTrailSummary);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not authorization");
    }

    [TestMethod]
    public void ObserverHasReadModelHintsWithoutMutationAuthority()
    {
        AssertRoleHas(GovernanceRoleKind.Observer, RoleVisibilityMaterialKind.OperationStatusSummary);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not mutation authority");
    }

    [TestMethod]
    public void AutomationAgentHasStatusHintsWithoutAutonomy()
    {
        AssertRoleHas(GovernanceRoleKind.AutomationAgent, RoleVisibilityMaterialKind.WorkflowContinuationSummary);
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not workflow continuation authority");
    }

    [TestMethod]
    public void SystemReadOnlyHasReadOnlyHintsWithoutBackendAuthority()
    {
        var entries = EntriesFor(GovernanceRoleKind.SystemReadOnly);

        Assert.IsTrue(entries.Any(static entry => entry.Surface == RoleVisibilitySurface.FrontendReadOnly));
        CollectionAssert.Contains(Validation().ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not mutation authority");
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.RawPayload)]
    [DataRow(RoleVisibilityMaterialKind.CredentialMaterial)]
    [DataRow(RoleVisibilityMaterialKind.PrivateReasoning)]
    public void AlwaysHiddenMaterialCannotBeVisibleForAnyRole(RoleVisibilityMaterialKind material)
    {
        var entries = Matrix().Entries.Where(entry => entry.MaterialKind == material).ToArray();

        Assert.AreEqual(Catalog().Entries.Count, entries.Length);
        Assert.IsTrue(entries.All(static entry => entry.VisibilityLevel == RoleVisibilityLevel.NotVisible));
        Assert.IsTrue(entries.All(static entry => entry.RequiresSeparatePolicyDecision));
    }

    [DataTestMethod]
    [DataRow(RoleVisibilitySensitivityKind.SecretLike)]
    [DataRow(RoleVisibilitySensitivityKind.CredentialLike)]
    [DataRow(RoleVisibilitySensitivityKind.RawPayload)]
    [DataRow(RoleVisibilitySensitivityKind.PrivateReasoning)]
    public void SecretRawCredentialOrPrivateSensitivityCannotBeMarkedDetailEligible(RoleVisibilitySensitivityKind sensitivity)
    {
        var result = RoleVisibilityMatrixValidator.ValidateEntry(Catalog(), Entry() with
        {
            SensitivityKind = sensitivity,
            VisibilityLevel = RoleVisibilityLevel.DetailEligibilityHint,
            RequiresRedaction = true,
            RequiresSeparatePolicyDecision = true
        });

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(static issue => issue.Contains("OverexposedMaterial", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void NullVisibilityMatrixEntryFailsClosed()
    {
        var result = RoleVisibilityMatrixValidator.ValidateEntry(Catalog(), null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleVisibilityMatrixEntryRequired");
        AssertAuthorityWarnings(result);
    }

    [DataTestMethod]
    [DataRow("missing-role-id", "RoleVisibilityMatrixEntryRoleIdRequired")]
    [DataRow("malformed-role-id", "RoleVisibilityMatrixEntryRoleIdInvalid")]
    [DataRow("unknown-role-id", "RoleVisibilityMatrixEntryUnknownRoleId:role:f01:not-in-catalog")]
    [DataRow("role-kind-mismatch", "RoleVisibilityMatrixEntryRoleKindMismatch")]
    [DataRow("role-scope-mismatch", "RoleVisibilityMatrixEntryRoleScopeKindMismatch")]
    [DataRow("missing-catalog-version", "RoleVisibilityMatrixEntryCatalogVersionRequired")]
    [DataRow("catalog-version-mismatch", "RoleVisibilityMatrixEntryCatalogVersionMismatch")]
    [DataRow("unknown-surface", "RoleVisibilityMatrixEntrySurfaceUnknown")]
    [DataRow("unknown-material", "RoleVisibilityMatrixEntryMaterialKindUnknown")]
    [DataRow("unknown-sensitivity", "RoleVisibilityMatrixEntrySensitivityKindUnknown")]
    [DataRow("unknown-level", "RoleVisibilityMatrixEntryVisibilityLevelUnknown")]
    [DataRow("missing-reason", "RoleVisibilityMatrixEntryReasonRequired")]
    [DataRow("missing-boundary", "RoleVisibilityMatrixEntryBoundaryStatementRequired")]
    [DataRow("boundary-without-denial", "RoleVisibilityMatrixEntryBoundaryStatementMustDenyAccessAndAuthority")]
    [DataRow("unsafe-text", "RoleVisibilityMatrixEntryReasonUnsafe")]
    [DataRow("visible-raw", "RoleVisibilityMatrixEntryOverexposedMaterial:role:f01:reviewer:Proposal:RawPayload")]
    [DataRow("visible-credential", "RoleVisibilityMatrixEntryOverexposedMaterial:role:f01:reviewer:Proposal:CredentialMaterial")]
    [DataRow("visible-private", "RoleVisibilityMatrixEntryOverexposedMaterial:role:f01:reviewer:Proposal:PrivateReasoning")]
    [DataRow("sensitive-without-redaction", "RoleVisibilityMatrixEntryRedactionRequired")]
    [DataRow("redacted-without-redaction", "RoleVisibilityMatrixEntryRedactionRequired")]
    [DataRow("missing-role-assignment", "RoleVisibilityMatrixEntrySeparateRoleAssignmentRequired")]
    [DataRow("missing-visibility-decision", "RoleVisibilityMatrixEntrySeparateVisibilityDecisionRequired")]
    [DataRow("sensitive-without-policy", "RoleVisibilityMatrixEntrySeparatePolicyDecisionRequired")]
    public void InvalidVisibilityMatrixEntryFailsClosed(string caseName, string expectedIssue)
    {
        var result = RoleVisibilityMatrixValidator.ValidateEntry(Catalog(), InvalidEntry(caseName));

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), expectedIssue);
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void NullVisibilityMatrixFailsClosed()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(Catalog(), null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleVisibilityMatrixRequired");
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void NullRoleCatalogFailsClosed()
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(null, Matrix());

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleVisibilityCatalogRequired");
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void InvalidRoleCatalogFailsClosed()
    {
        var invalidCatalog = Catalog() with { Entries = [] };
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(invalidCatalog, Matrix());

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleVisibilityCatalogInvalid");
    }

    [DataTestMethod]
    [DataRow("missing-matrix-id", "RoleVisibilityMatrixIdRequired")]
    [DataRow("malformed-matrix-id", "RoleVisibilityMatrixIdInvalid")]
    [DataRow("missing-catalog-id", "RoleVisibilityMatrixCatalogIdRequired")]
    [DataRow("catalog-id-mismatch", "RoleVisibilityMatrixCatalogIdMismatch")]
    [DataRow("catalog-version-mismatch", "RoleVisibilityMatrixCatalogVersionMismatch")]
    [DataRow("empty-matrix", "RoleVisibilityMatrixEntriesRequired")]
    [DataRow("duplicate-key", "RoleVisibilityMatrixDuplicateKey:role:f01:reviewer|Proposal|ProposalSummary")]
    [DataRow("invalid-entry", "Entry::RoleVisibilityMatrixEntryRoleIdRequired")]
    [DataRow("matrix-boundary-without-denial", "RoleVisibilityMatrixBoundaryStatementMustDenyAccessAndAuthority")]
    public void InvalidVisibilityMatrixFailsClosed(string caseName, string expectedIssue)
    {
        var result = RoleVisibilityMatrixValidator.ValidateMatrix(Catalog(), InvalidMatrix(caseName));

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), expectedIssue);
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void MatrixWarningsDenyAuthority()
    {
        AssertAuthorityWarnings(Validation());
    }

    [TestMethod]
    public void FindEntriesByRoleIdReturnsOnlyMatchingEntries()
    {
        var entries = MatrixService().FindEntriesByRoleId(Matrix(), "role:f01:reviewer");

        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(static entry => entry.RoleId == "role:f01:reviewer"));
    }

    [TestMethod]
    public void FindEntriesByRoleIdReturnsEmptyForMissingRole()
    {
        var entries = MatrixService().FindEntriesByRoleId(Matrix(), "role:f01:not-in-catalog");

        Assert.AreEqual(0, entries.Count);
    }

    [TestMethod]
    public void ListBySurfaceReturnsOnlyMatchingEntries()
    {
        var entries = MatrixService().ListBySurface(Matrix(), RoleVisibilitySurface.ReleaseReadiness);

        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(static entry => entry.Surface == RoleVisibilitySurface.ReleaseReadiness));
    }

    [TestMethod]
    public void ListByMaterialReturnsOnlyMatchingEntries()
    {
        var entries = MatrixService().ListByMaterial(Matrix(), RoleVisibilityMaterialKind.ReceiptMetadata);

        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(static entry => entry.MaterialKind == RoleVisibilityMaterialKind.ReceiptMetadata));
    }

    [TestMethod]
    public void ListByVisibilityLevelReturnsOnlyMatchingEntries()
    {
        var entries = MatrixService().ListByVisibilityLevel(Matrix(), RoleVisibilityLevel.MetadataOnly);

        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(static entry => entry.VisibilityLevel == RoleVisibilityLevel.MetadataOnly));
    }

    [TestMethod]
    public void LookupDoesNotGrantAccessPermissionsApprovalPolicyExecutionOrMutation()
    {
        var entries = MatrixService().FindEntriesByRoleId(Matrix(), "role:f01:approver-candidate");
        var validation = Validation();

        Assert.IsTrue(entries.Count > 0);
        CollectionAssert.Contains(validation.Warnings.ToList(), "role visibility matrix does not grant access");
        CollectionAssert.Contains(validation.Warnings.ToList(), "role visibility matrix does not grant permissions");
        CollectionAssert.Contains(validation.Warnings.ToList(), "role visibility matrix does not approve work");
        CollectionAssert.Contains(validation.Warnings.ToList(), "role visibility matrix does not satisfy policy");
        CollectionAssert.Contains(validation.Warnings.ToList(), "role visibility matrix does not authorize execution");
        CollectionAssert.Contains(validation.Warnings.ToList(), "role visibility matrix does not authorize mutation");
        AssertNoAuthorityNames(typeof(RoleVisibilityMatrixEntry).GetProperties().Select(static property => property.Name));
    }

    [DataTestMethod]
    [DataRow("visibility hint")]
    [DataRow("approval package summary")]
    [DataRow("policy review summary")]
    [DataRow("release readiness summary")]
    [DataRow("workflow continuation summary")]
    [DataRow("source apply summary")]
    [DataRow("merge readiness summary")]
    [DataRow("deployment readiness summary")]
    [DataRow("redacted details")]
    [DataRow("metadata only")]
    [DataRow("summary only")]
    public void ValidGovernanceVisibilityTextIsNotRejected(string text)
    {
        var result = RoleVisibilityMatrixValidator.ValidateEntry(Catalog(), Entry() with
        {
            Reason = $"Catalog only {text} for bounded read model hints."
        });

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [DataTestMethod]
    [DataRow("can view")]
    [DataRow("can read")]
    [DataRow("can access")]
    [DataRow("access granted")]
    [DataRow("permission granted")]
    [DataRow("authority granted")]
    [DataRow("safe to disclose")]
    [DataRow("redaction bypassed")]
    [DataRow("raw provider response")]
    [DataRow("chain-of-thought")]
    public void UnsafeVisibilityTextFails(string unsafeText)
    {
        var result = RoleVisibilityMatrixValidator.ValidateEntry(Catalog(), Entry() with
        {
            Reason = $"Bad text says {unsafeText}."
        });

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void F02AddsNoApiCliUiWorkerOrOpenApiSurface()
    {
        var changedNames = F02AllowedFileNames();

        Assert.IsTrue(changedNames.All(static path =>
            path.StartsWith("IronDev.Core/Governance/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.IntegrationTests/", StringComparison.Ordinal) ||
            path.StartsWith("Docs/receipts/", StringComparison.Ordinal)));
        Assert.IsFalse(changedNames.Any(static path =>
            path.StartsWith("IronDev.Api/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Cli/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Infrastructure/", StringComparison.Ordinal) ||
            path.StartsWith("OpenApi/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void F02AddsNoPersistenceSqlIdentityPrincipalPermissionOrExecutorSurface()
    {
        var source = StripStrings(F02CoreSource());

        AssertDoesNotContain(source, "SqlConnection");
        AssertDoesNotContain(source, "DbContext");
        AssertDoesNotContain(source, "IRepository");
        AssertDoesNotContain(source, "Repository<");
        AssertDoesNotContain(source, "Repository(");
        AssertDoesNotContain(source, "Store<");
        AssertDoesNotContain(source, "Store(");
        AssertDoesNotContain(source, "ClaimsPrincipal");
        AssertDoesNotContain(source, "IPrincipal");
        AssertDoesNotContain(source, "UserManager");
        AssertDoesNotContain(source, "RoleManager");
        AssertDoesNotContain(source, "Authorize");
        AssertDoesNotContain(source, "Permission");
        AssertDoesNotContain(source, "ExecuteAsync");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "GitHub");
        AssertDoesNotContain(source, "Octokit");
    }

    [TestMethod]
    public void F02DoesNotIntroduceAuthorityShapedVisibilityFields()
    {
        var fieldNames = typeof(RoleVisibilityMatrixEntry)
            .GetProperties()
            .Select(static property => property.Name)
            .Concat(typeof(RoleVisibilityMatrix).GetProperties().Select(static property => property.Name))
            .ToArray();

        AssertNoAuthorityNames(fieldNames);
    }

    [TestMethod]
    public void F02DoesNotIntroduceAccessGrantDecisionNames()
    {
        var enumNames = Enum.GetNames<RoleVisibilityLevel>()
            .Concat(Enum.GetNames<RoleVisibilitySurface>())
            .Concat(Enum.GetNames<RoleVisibilityMaterialKind>())
            .Concat(Enum.GetNames<RoleVisibilitySensitivityKind>())
            .ToArray();

        AssertNoAuthorityNames(enumNames);
    }

    [DataTestMethod]
    [DataRow("Visibility hints are not access grants.")]
    [DataRow("Seeing the map is not permission to enter the room.")]
    [DataRow("catalog-only")]
    [DataRow("identity assignment")]
    [DataRow("user assignment")]
    [DataRow("group assignment")]
    [DataRow("role assignment")]
    [DataRow("access grant")]
    [DataRow("permission grant")]
    [DataRow("read authorization")]
    [DataRow("redaction bypass")]
    [DataRow("secret disclosure authority")]
    [DataRow("credential disclosure authority")]
    [DataRow("private reasoning disclosure authority")]
    [DataRow("raw payload disclosure authority")]
    [DataRow("approval")]
    [DataRow("policy satisfaction")]
    [DataRow("execution authority")]
    [DataRow("mutation authority")]
    [DataRow("merge authority")]
    [DataRow("release authority")]
    [DataRow("deployment authority")]
    [DataRow("workflow continuation")]
    public void ReceiptContainsBoundaryLines(string phrase)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "F02_ROLE_VISIBILITY_MATRIX_CONTRACT.md"));

        StringAssert.Contains(receipt, phrase);
    }

    private static GovernanceRoleCatalog Catalog() => CatalogService().BuildDefaultCatalog();

    private static RoleVisibilityMatrix Matrix() => MatrixService().BuildDefaultMatrix(Catalog());

    private static RoleVisibilityMatrixValidationResult Validation() =>
        RoleVisibilityMatrixValidator.ValidateMatrix(Catalog(), Matrix());

    private static RoleCatalogService CatalogService() => new();

    private static RoleVisibilityMatrixService MatrixService() => new();

    private static IReadOnlyList<RoleVisibilityMatrixEntry> EntriesFor(GovernanceRoleKind kind)
    {
        var roleId = Catalog().Entries.Single(entry => entry.RoleKind == kind).RoleId;
        return MatrixService().FindEntriesByRoleId(Matrix(), roleId);
    }

    private static void AssertRoleHas(
        GovernanceRoleKind kind,
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilityLevel? expectedLevel = null)
    {
        var entries = EntriesFor(kind).Where(entry => entry.MaterialKind == materialKind).ToArray();

        Assert.IsTrue(entries.Length > 0, $"{kind}:{materialKind}");
        if (expectedLevel.HasValue)
        {
            Assert.IsTrue(entries.Any(entry => entry.VisibilityLevel == expectedLevel.Value), $"{kind}:{materialKind}:{expectedLevel}");
        }
    }

    private static RoleVisibilityMatrixEntry Entry() =>
        new()
        {
            RoleId = "role:f01:reviewer",
            CatalogVersion = "f01",
            RoleKind = GovernanceRoleKind.Reviewer,
            RoleScopeKind = GovernanceRoleScopeKind.ProjectScoped,
            Surface = RoleVisibilitySurface.Proposal,
            MaterialKind = RoleVisibilityMaterialKind.ProposalSummary,
            SensitivityKind = RoleVisibilitySensitivityKind.Normal,
            VisibilityLevel = RoleVisibilityLevel.SummaryOnly,
            RequiresRedaction = false,
            RequiresSeparateRoleAssignment = true,
            RequiresSeparateVisibilityDecision = true,
            RequiresSeparatePolicyDecision = false,
            Reason = "Catalog only visibility hint for bounded summary.",
            BoundaryStatement = "This visibility hint does not grant access and does not grant authority."
        };

    private static RoleVisibilityMatrixEntry InvalidEntry(string caseName) =>
        caseName switch
        {
            "missing-role-id" => Entry() with { RoleId = "" },
            "malformed-role-id" => Entry() with { RoleId = "reviewer" },
            "unknown-role-id" => Entry() with { RoleId = "role:f01:not-in-catalog" },
            "role-kind-mismatch" => Entry() with { RoleKind = GovernanceRoleKind.Planner },
            "role-scope-mismatch" => Entry() with { RoleScopeKind = GovernanceRoleScopeKind.GlobalCatalog },
            "missing-catalog-version" => Entry() with { CatalogVersion = "" },
            "catalog-version-mismatch" => Entry() with { CatalogVersion = "f99" },
            "unknown-surface" => Entry() with { Surface = RoleVisibilitySurface.Unknown },
            "unknown-material" => Entry() with { MaterialKind = RoleVisibilityMaterialKind.Unknown },
            "unknown-sensitivity" => Entry() with { SensitivityKind = RoleVisibilitySensitivityKind.Unknown },
            "unknown-level" => Entry() with { VisibilityLevel = RoleVisibilityLevel.Unknown },
            "missing-reason" => Entry() with { Reason = "" },
            "missing-boundary" => Entry() with { BoundaryStatement = "" },
            "boundary-without-denial" => Entry() with { BoundaryStatement = "This visibility hint is descriptive." },
            "unsafe-text" => Entry() with { Reason = "This can view the material." },
            "visible-raw" => Entry() with { MaterialKind = RoleVisibilityMaterialKind.RawPayload, SensitivityKind = RoleVisibilitySensitivityKind.RawPayload, VisibilityLevel = RoleVisibilityLevel.SummaryOnly, RequiresRedaction = true, RequiresSeparatePolicyDecision = true },
            "visible-credential" => Entry() with { MaterialKind = RoleVisibilityMaterialKind.CredentialMaterial, SensitivityKind = RoleVisibilitySensitivityKind.CredentialLike, VisibilityLevel = RoleVisibilityLevel.SummaryOnly, RequiresRedaction = true, RequiresSeparatePolicyDecision = true },
            "visible-private" => Entry() with { MaterialKind = RoleVisibilityMaterialKind.PrivateReasoning, SensitivityKind = RoleVisibilitySensitivityKind.PrivateReasoning, VisibilityLevel = RoleVisibilityLevel.SummaryOnly, RequiresRedaction = true, RequiresSeparatePolicyDecision = true },
            "sensitive-without-redaction" => Entry() with { SensitivityKind = RoleVisibilitySensitivityKind.SecuritySensitive, RequiresRedaction = false, RequiresSeparatePolicyDecision = true },
            "redacted-without-redaction" => Entry() with { VisibilityLevel = RoleVisibilityLevel.RedactedDetails, RequiresRedaction = false, RequiresSeparatePolicyDecision = true },
            "missing-role-assignment" => Entry() with { RequiresSeparateRoleAssignment = false },
            "missing-visibility-decision" => Entry() with { RequiresSeparateVisibilityDecision = false },
            "sensitive-without-policy" => Entry() with { SensitivityKind = RoleVisibilitySensitivityKind.SecuritySensitive, RequiresRedaction = true, RequiresSeparatePolicyDecision = false },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static RoleVisibilityMatrix InvalidMatrix(string caseName) =>
        caseName switch
        {
            "missing-matrix-id" => Matrix() with { MatrixId = "" },
            "malformed-matrix-id" => Matrix() with { MatrixId = "f02" },
            "missing-catalog-id" => Matrix() with { CatalogId = "" },
            "catalog-id-mismatch" => Matrix() with { CatalogId = "role-catalog:other" },
            "catalog-version-mismatch" => Matrix() with { CatalogVersion = "f99" },
            "empty-matrix" => Matrix() with { Entries = [] },
            "duplicate-key" => Matrix() with { Entries = [Entry(), Entry()] },
            "invalid-entry" => Matrix() with { Entries = [Entry() with { RoleId = "" }] },
            "matrix-boundary-without-denial" => Matrix() with { BoundaryStatement = "The matrix is descriptive." },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertAuthorityWarnings(RoleVisibilityMatrixValidationResult result)
    {
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix is descriptive only");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not assign users");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not grant access");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not grant permissions");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not bypass redaction");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not approve work");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not satisfy policy");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not authorize execution");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not authorize mutation");
        CollectionAssert.Contains(result.Warnings.ToList(), "role visibility matrix does not continue workflow");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not identity");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not authorization");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not access control");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not approval");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not policy satisfaction");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not execution authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not mutation authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not workflow continuation authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not redaction bypass");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role visibility matrix is not secret disclosure authority");
    }

    private static void AssertNoAuthorityNames(IEnumerable<string> names)
    {
        var forbidden = new[]
        {
            "CanView",
            "CanRead",
            "CanAccess",
            "CanApprove",
            "CanExecute",
            "CanMutate",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanContinue",
            "HasPermission",
            "PermissionGranted",
            "AccessGranted",
            "ReadGranted",
            "AuthorityGranted",
            "ApprovalGranted",
            "PolicySatisfied",
            "ValidationSatisfied",
            "RoleAuthorized",
            "UserInRole"
        };

        foreach (var name in names)
        {
            CollectionAssert.DoesNotContain(forbidden, name);
        }
    }

    private static string F02CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "RoleVisibilityMatrix*.cs");
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static IReadOnlyList<string> F02AllowedFileNames() =>
    [
        "IronDev.Core/Governance/RoleVisibilityMatrixModels.cs",
        "IronDev.Core/Governance/RoleVisibilityMatrixValidator.cs",
        "IronDev.Core/Governance/RoleVisibilityMatrixService.cs",
        "IronDev.IntegrationTests/BlockF02RoleVisibilityMatrixContractTests.cs",
        "Docs/receipts/F02_ROLE_VISIBILITY_MATRIX_CONTRACT.md"
    ];

    private static string StripStrings(string source) =>
        System.Text.RegularExpressions.Regex.Replace(source, "\"(?:\\\\.|[^\"])*\"", "\"\"");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void AssertDoesNotContain(string source, string forbidden) =>
        Assert.IsFalse(
            source.Contains(forbidden, StringComparison.Ordinal),
            $"Unexpected forbidden marker found in F02 source: {forbidden}");
}
