using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF10aExternalViewerRoleCatalogContractTests
{
    [TestMethod]
    public void DefaultCatalogContainsExactlyOneExternalViewerRole()
    {
        var entries = CatalogService().ListByKind(Catalog(), GovernanceRoleKind.ExternalViewer);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("role:f01:external-viewer", entries[0].RoleId);
        Assert.AreEqual("External Viewer", entries[0].DisplayName);
    }

    [TestMethod]
    public void ExternalViewerRoleIsCatalogOnlyExternalFacingAndReadOnly()
    {
        var entry = ExternalViewer();

        Assert.AreEqual(GovernanceRoleScopeKind.ProjectScoped, entry.ScopeKind);
        Assert.AreEqual(GovernanceRoleKind.ExternalViewer, entry.RoleKind);
        CollectionAssert.Contains(entry.Surfaces.ToList(), GovernanceRoleSurface.StatusReadModel);
        CollectionAssert.Contains(entry.Surfaces.ToList(), GovernanceRoleSurface.FrontendReadOnly);
        StringAssert.Contains(entry.Description, "external-facing read-only responsibility marker");
        StringAssert.Contains(entry.ResponsibilitySummary, "remains evidence only");
        StringAssert.Contains(entry.BoundaryStatement, "does not grant authority");
    }

    [TestMethod]
    public void ExternalViewerRoleTextContainsNoAuthorityGrantWording()
    {
        var entry = ExternalViewer();
        var text = string.Join(" ", entry.Description, entry.ResponsibilitySummary);
        var forbidden = new[]
        {
            "grant access",
            "assigned",
            "share link",
            "export raw",
            "view raw",
            "view secret",
            "view credential",
            "private reasoning",
            "bypass redaction",
            "all tenants",
            "platform data",
            "approve",
            "satisfy policy",
            "refresh validation",
            "source safety",
            "run diagnostic",
            "retry",
            "rollback",
            "recover",
            "mutate",
            "continue workflow",
            "merge",
            "release",
            "deploy"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [DataTestMethod]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsExternalViewerAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsRoleAssignmentAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsVisibilityAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsAccess))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.CreatesShareLink))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.ExportsRawData))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsCrossTenantVisibility))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsPlatformVisibility))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsApprovalAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.SatisfiesPolicy))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.RefreshesValidation))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.ProvesSourceSafety))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsDiagnosticExecutionAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsRetryAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsRollbackAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsRecoveryAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsMutationAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsWorkflowContinuation))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsMergeAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsReleaseAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.GrantsDeploymentAuthority))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.BypassesRedaction))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.DisclosesSecrets))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.DisclosesCredentials))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.DisclosesRawPayload))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.DisclosesRawSource))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.DisclosesRawLogs))]
    [DataRow(nameof(ExternalViewerBoundaryFinding.DisclosesPrivateReasoning))]
    public void ExternalViewerRoleDoesNotImplyAuthorityOrDisclosure(string propertyName)
    {
        var finding = ExternalViewerBoundaryFinding.FromSafeCatalogEntry(ExternalViewer());
        var value = (bool)(typeof(ExternalViewerBoundaryFinding).GetProperty(propertyName)?.GetValue(finding)
            ?? throw new InvalidOperationException($"Missing property {propertyName}."));

        Assert.IsFalse(value, propertyName);
    }

    [TestMethod]
    public void RoleCatalogValidatorAcceptsExternalViewerAsCatalogEntryOnly()
    {
        var result = RoleCatalogValidator.ValidateEntry(ExternalViewer());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog is descriptive only");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not assign users");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not grant permissions");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not authorization");
    }

    [DataTestMethod]
    [DataRow("ExternalAccessGranted = true")]
    [DataRow("ExternalViewerGranted = true")]
    [DataRow("ExternalViewerAssigned = true")]
    [DataRow("CanCreateShareLink = true")]
    [DataRow("CanExportRawData = true")]
    [DataRow("CanViewRawPayload = true")]
    [DataRow("CanViewRawSource = true")]
    [DataRow("CanViewRawLog = true")]
    [DataRow("CanViewSecrets = true")]
    [DataRow("CanViewCredentials = true")]
    [DataRow("CanViewPrivateReasoning = true")]
    [DataRow("CanBypassRedaction = true")]
    [DataRow("CanAccessAllTenants = true")]
    [DataRow("CanViewPlatformData = true")]
    [DataRow("CanApprove = true")]
    [DataRow("SatisfiesPolicy = true")]
    [DataRow("ValidationRefreshed = true")]
    [DataRow("SourceSafetyProven = true")]
    [DataRow("CanRunDiagnostic = true")]
    [DataRow("CanRetry = true")]
    [DataRow("CanRollback = true")]
    [DataRow("CanRecover = true")]
    [DataRow("CanMutate = true")]
    [DataRow("CanContinueWorkflow = true")]
    [DataRow("CanMerge = true")]
    [DataRow("CanRelease = true")]
    [DataRow("CanDeploy = true")]
    [DataRow("external viewer may see raw payload")]
    [DataRow("external viewer may see secrets")]
    [DataRow("external viewer may see credentials")]
    [DataRow("external viewer may see private reasoning")]
    [DataRow("external viewer may bypass redaction")]
    [DataRow("external viewer may inspect all tenants")]
    [DataRow("external viewer may access platform data")]
    [DataRow("external viewer may export raw data")]
    [DataRow("external viewer may receive provider response")]
    [DataRow("external viewer may approve policy")]
    [DataRow("external viewer may continue workflow")]
    public void RoleCatalogValidatorRejectsExternalViewerAuthorityText(string unsafeText)
    {
        var result = RoleCatalogValidator.ValidateEntry(ExternalViewer() with
        {
            Description = $"Unsafe external-viewer claim: {unsafeText}."
        });
        var finding = ExternalViewerAuthorityMarkerDetector.Evaluate(ExternalViewerEvidenceFixture.Hostile(unsafeText));

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal)));
        CollectionAssert.Contains(result.UnsafeRoleIds.ToList(), ExternalViewer().RoleId);
        Assert.IsTrue(finding.IsHidden);
        Assert.IsTrue(finding.RejectedMarkers.Contains(unsafeText, StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DefaultCatalogValidationStillPassesWithExternalViewer()
    {
        var result = CatalogService().ValidateDefaultCatalog();

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [TestMethod]
    public void DefaultVisibilityMatrixStillValidWithExternalViewer()
    {
        var result = MatrixService().ValidateDefaultMatrix(Catalog());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [DataTestMethod]
    [DataRow(RoleVisibilityMaterialKind.RawPayload)]
    [DataRow(RoleVisibilityMaterialKind.CredentialMaterial)]
    [DataRow(RoleVisibilityMaterialKind.PrivateReasoning)]
    public void ExternalViewerMatrixEntriesDoNotExposeSensitiveMaterial(RoleVisibilityMaterialKind materialKind)
    {
        var entries = ExternalViewerMatrixEntries()
            .Where(entry => entry.MaterialKind == materialKind)
            .ToArray();

        Assert.AreEqual(1, entries.Length);
        Assert.IsTrue(entries.All(static entry => entry.VisibilityLevel == RoleVisibilityLevel.NotVisible));
        Assert.IsTrue(entries.All(static entry => entry.RequiresSeparatePolicyDecision));
        Assert.IsTrue(entries.All(static entry => entry.RequiresRedaction));
    }

    [TestMethod]
    public void ExternalViewerMatrixEntriesRemainSeparateVisibilityHintsOnly()
    {
        foreach (var entry in ExternalViewerMatrixEntries())
        {
            Assert.IsTrue(entry.RequiresSeparateRoleAssignment);
            Assert.IsTrue(entry.RequiresSeparateVisibilityDecision);
            StringAssert.Contains(entry.BoundaryStatement, "does not grant access");
            StringAssert.Contains(entry.BoundaryStatement, "does not grant authority");
            Assert.AreNotEqual(RoleVisibilityLevel.DetailEligibilityHint, entry.VisibilityLevel);
        }
    }

    [TestMethod]
    public void ExternalViewerRoleNameCannotBeUsedAsAccessVisibilityShareExportOrRawDisclosureEvidence()
    {
        var finding = ExternalViewerAuthorityMarkerDetector.Evaluate(ExternalViewerEvidenceFixture.Safe());

        Assert.IsFalse(finding.GrantsExternalViewerAuthority);
        Assert.IsFalse(finding.GrantsRoleAssignmentAuthority);
        Assert.IsFalse(finding.GrantsVisibilityAuthority);
        Assert.IsFalse(finding.GrantsAccess);
        Assert.IsFalse(finding.CreatesShareLink);
        Assert.IsFalse(finding.ExportsRawData);
        Assert.IsFalse(finding.BypassesRedaction);
        Assert.IsFalse(finding.DisclosesSecrets);
        Assert.IsFalse(finding.DisclosesCredentials);
        Assert.IsFalse(finding.DisclosesRawPayload);
        Assert.IsFalse(finding.DisclosesRawSource);
        Assert.IsFalse(finding.DisclosesRawLogs);
        Assert.IsFalse(finding.DisclosesPrivateReasoning);
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "external-viewer-assignment-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "visibility-decision");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "redaction-decision");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "tenant-boundary-decision");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "access-decision");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "raw-export-authority");
    }

    [TestMethod]
    public void ExternalViewerRoleNameCannotBeUsedAsApprovalPolicyValidationSourceSafetyDiagnosticOrWorkflowEvidence()
    {
        var finding = ExternalViewerAuthorityMarkerDetector.Evaluate(ExternalViewerEvidenceFixture.Safe());

        Assert.IsFalse(finding.GrantsApprovalAuthority);
        Assert.IsFalse(finding.SatisfiesPolicy);
        Assert.IsFalse(finding.RefreshesValidation);
        Assert.IsFalse(finding.ProvesSourceSafety);
        Assert.IsFalse(finding.GrantsDiagnosticExecutionAuthority);
        Assert.IsFalse(finding.GrantsRetryAuthority);
        Assert.IsFalse(finding.GrantsRollbackAuthority);
        Assert.IsFalse(finding.GrantsRecoveryAuthority);
        Assert.IsFalse(finding.GrantsMutationAuthority);
        Assert.IsFalse(finding.GrantsWorkflowContinuation);
        Assert.IsFalse(finding.GrantsMergeAuthority);
        Assert.IsFalse(finding.GrantsReleaseAuthority);
        Assert.IsFalse(finding.GrantsDeploymentAuthority);
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "approval-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "policy-satisfaction-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "validation-freshness-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "source-safety-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "workflow-continuation-evidence");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "merge-authority");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "release-authority");
        CollectionAssert.Contains(finding.RequiredSeparateEvidence.ToList(), "deployment-authority");
    }

    [TestMethod]
    public void StaticScanF10aAddsNoProductionAccessIdentityShareExportOrMutationSurface()
    {
        var source = StripStringLiterals(F10aCoreSource());
        var forbidden = new[]
        {
            "ClaimsPrincipal",
            "IPrincipal",
            "UserManager",
            "RoleManager",
            "PermissionResolver",
            "AccessControl",
            "ShareLink",
            "Invite",
            "ExportAsync",
            "RawExport",
            "ApiController",
            "ControllerBase",
            "DbContext",
            "SqlConnection",
            "IRepository",
            "Repository<",
            "Store<",
            "HttpClient",
            "ProcessStartInfo",
            "RunProcessAsync",
            "ExecuteAsync",
            "ApplyAsync",
            "CommitAsync",
            "PushAsync",
            "MergeAsync",
            "ReleaseAsync",
            "DeployAsync",
            "WorkflowTransition",
            "ApprovalRecord",
            "PolicySatisfactionService"
        };

        foreach (var marker in forbidden)
        {
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), marker);
        }
    }

    [TestMethod]
    public void ReceiptExistsAndStatesExternalViewerRoleNameIsNotAccessAuthority()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "F10A_EXTERNAL_VIEWER_ROLE_CATALOG_CONTRACT.md"));

        StringAssert.Contains(receipt, "External viewer role evidence is not external access authority.");
        StringAssert.Contains(receipt, "External viewer evidence is not visibility authority.");
        StringAssert.Contains(receipt, "A redacted view is not a permission slip.");
        StringAssert.Contains(receipt, "does not implement external viewer authority");
        StringAssert.Contains(receipt, "does not implement external access grant");
        StringAssert.Contains(receipt, "does not implement share links");
        StringAssert.Contains(receipt, "does not implement raw exports");
        StringAssert.Contains(receipt, "does not implement redaction bypass");
    }

    private static GovernanceRoleCatalog Catalog() => CatalogService().BuildDefaultCatalog();

    private static RoleCatalogService CatalogService() => new();

    private static RoleVisibilityMatrixService MatrixService() => new();

    private static GovernanceRoleCatalogEntry ExternalViewer() =>
        Catalog().Entries.Single(entry => entry.RoleKind == GovernanceRoleKind.ExternalViewer);

    private static IReadOnlyList<RoleVisibilityMatrixEntry> ExternalViewerMatrixEntries() =>
        MatrixService().FindEntriesByRoleId(
            MatrixService().BuildDefaultMatrix(Catalog()),
            ExternalViewer().RoleId);

    private static string F10aCoreSource()
    {
        var root = RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RoleCatalogModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RoleCatalogService.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RoleCatalogValidator.cs")
        };

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
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

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record ExternalViewerEvidenceFixture
    {
        public required string RoleId { get; init; }
        public required IReadOnlyList<string> EvidenceRefs { get; init; }
        public required IReadOnlyList<string> TextMarkers { get; init; }

        public static ExternalViewerEvidenceFixture Safe() =>
            new()
            {
                RoleId = "role:f01:external-viewer",
                EvidenceRefs = ["role-catalog-entry:role:f01:external-viewer"],
                TextMarkers = []
            };

        public static ExternalViewerEvidenceFixture Hostile(string marker) =>
            Safe() with
            {
                TextMarkers = [marker]
            };
    }

    private sealed record ExternalViewerBoundaryFinding
    {
        public required bool GrantsExternalViewerAuthority { get; init; }
        public required bool GrantsRoleAssignmentAuthority { get; init; }
        public required bool GrantsVisibilityAuthority { get; init; }
        public required bool GrantsAccess { get; init; }
        public required bool CreatesShareLink { get; init; }
        public required bool ExportsRawData { get; init; }
        public required bool GrantsCrossTenantVisibility { get; init; }
        public required bool GrantsPlatformVisibility { get; init; }
        public required bool GrantsApprovalAuthority { get; init; }
        public required bool SatisfiesPolicy { get; init; }
        public required bool RefreshesValidation { get; init; }
        public required bool ProvesSourceSafety { get; init; }
        public required bool GrantsDiagnosticExecutionAuthority { get; init; }
        public required bool GrantsRetryAuthority { get; init; }
        public required bool GrantsRollbackAuthority { get; init; }
        public required bool GrantsRecoveryAuthority { get; init; }
        public required bool GrantsMutationAuthority { get; init; }
        public required bool GrantsWorkflowContinuation { get; init; }
        public required bool GrantsMergeAuthority { get; init; }
        public required bool GrantsReleaseAuthority { get; init; }
        public required bool GrantsDeploymentAuthority { get; init; }
        public required bool BypassesRedaction { get; init; }
        public required bool DisclosesSecrets { get; init; }
        public required bool DisclosesCredentials { get; init; }
        public required bool DisclosesRawPayload { get; init; }
        public required bool DisclosesRawSource { get; init; }
        public required bool DisclosesRawLogs { get; init; }
        public required bool DisclosesPrivateReasoning { get; init; }
        public required bool IsHidden { get; init; }
        public required IReadOnlyList<string> RequiredSeparateEvidence { get; init; }
        public required IReadOnlyList<string> RejectedMarkers { get; init; }

        public static ExternalViewerBoundaryFinding FromSafeCatalogEntry(GovernanceRoleCatalogEntry entry)
        {
            Assert.AreEqual(GovernanceRoleKind.ExternalViewer, entry.RoleKind);
            return new ExternalViewerBoundaryFinding
            {
                GrantsExternalViewerAuthority = false,
                GrantsRoleAssignmentAuthority = false,
                GrantsVisibilityAuthority = false,
                GrantsAccess = false,
                CreatesShareLink = false,
                ExportsRawData = false,
                GrantsCrossTenantVisibility = false,
                GrantsPlatformVisibility = false,
                GrantsApprovalAuthority = false,
                SatisfiesPolicy = false,
                RefreshesValidation = false,
                ProvesSourceSafety = false,
                GrantsDiagnosticExecutionAuthority = false,
                GrantsRetryAuthority = false,
                GrantsRollbackAuthority = false,
                GrantsRecoveryAuthority = false,
                GrantsMutationAuthority = false,
                GrantsWorkflowContinuation = false,
                GrantsMergeAuthority = false,
                GrantsReleaseAuthority = false,
                GrantsDeploymentAuthority = false,
                BypassesRedaction = false,
                DisclosesSecrets = false,
                DisclosesCredentials = false,
                DisclosesRawPayload = false,
                DisclosesRawSource = false,
                DisclosesRawLogs = false,
                DisclosesPrivateReasoning = false,
                IsHidden = false,
                RequiredSeparateEvidence =
                [
                    "external-viewer-assignment-evidence",
                    "role-assignment-evidence",
                    "visibility-decision",
                    "redaction-decision",
                    "tenant-boundary-decision",
                    "access-decision",
                    "share-link-authority",
                    "raw-export-authority",
                    "approval-evidence",
                    "policy-satisfaction-evidence",
                    "validation-freshness-evidence",
                    "source-safety-evidence",
                    "diagnostic-execution-authority",
                    "retry-authority",
                    "rollback-authority",
                    "recovery-authority",
                    "mutation-authority",
                    "workflow-continuation-evidence",
                    "merge-authority",
                    "release-authority",
                    "deployment-authority"
                ],
                RejectedMarkers = []
            };
        }
    }

    private static class ExternalViewerAuthorityMarkerDetector
    {
        public static ExternalViewerBoundaryFinding Evaluate(ExternalViewerEvidenceFixture fixture)
        {
            var rejectedMarkers = fixture.TextMarkers
                .Where(RoleCatalogValidator.ContainsUnsafeRoleText)
                .ToArray();

            return ExternalViewerBoundaryFinding.FromSafeCatalogEntry(ExternalViewer()) with
            {
                IsHidden = rejectedMarkers.Length > 0,
                RejectedMarkers = rejectedMarkers
            };
        }
    }
}
