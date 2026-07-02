namespace IronDev.UnitTests.Governance;

[TestClass]
public sealed class FileScopeMatchingPropertyTests
{
    [TestMethod]
    public void SafeRelativeGlobCorpus_IsAccepted()
    {
        foreach (var path in FileScopeMatchingPropertyFixtures.SafeRelativePaths)
        {
            Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(path), path);
        }
    }

    [TestMethod]
    public void UnsafeRelativeGlobCorpus_IsRejected()
    {
        foreach (var path in FileScopeMatchingPropertyFixtures.UnsafePaths)
        {
            Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(path), path ?? "<null>");
        }
    }

    [TestMethod]
    public void UnsafeCandidate_IsNeverAllowed()
    {
        foreach (var candidate in FileScopeMatchingPropertyFixtures.UnsafePaths)
        {
            Assert.IsFalse(
                BoundedRunAuthorityGrantFileScope.IsAllowed(
                    candidate!,
                    ["../*.cs", "C:/repo/*.cs", "src/*.cs"],
                    []),
                candidate ?? "<null>");
        }
    }

    [TestMethod]
    public void UnsafeCandidate_IsAlwaysForbidden()
    {
        foreach (var candidate in FileScopeMatchingPropertyFixtures.UnsafePaths)
        {
            Assert.IsTrue(
                BoundedRunAuthorityGrantFileScope.IsForbidden(candidate!, []),
                candidate ?? "<null>");
        }
    }

    [TestMethod]
    public void AllowedGlobCorpus_AllowsExpectedCandidates()
    {
        foreach (var item in FileScopeMatchingPropertyFixtures.AllowedGlobCases.Where(static item => item.ExpectedMatch))
        {
            Assert.IsTrue(
                BoundedRunAuthorityGrantFileScope.IsAllowed(item.CandidatePath, [item.Glob], []),
                item.Id);
        }
    }

    [TestMethod]
    public void AllowedGlobCorpus_DoesNotAllowUnexpectedCandidates()
    {
        foreach (var item in FileScopeMatchingPropertyFixtures.AllowedGlobCases.Where(static item => !item.ExpectedMatch))
        {
            Assert.IsFalse(
                BoundedRunAuthorityGrantFileScope.IsAllowed(item.CandidatePath, [item.Glob], []),
                item.Id);
        }
    }

    [TestMethod]
    public void ForbiddenGlobCorpus_BlocksExpectedCandidates()
    {
        foreach (var item in FileScopeMatchingPropertyFixtures.ForbiddenGlobCases)
        {
            Assert.IsTrue(
                BoundedRunAuthorityGrantFileScope.IsForbidden(item.CandidatePath, [item.Glob]),
                item.Id);
        }
    }

    [TestMethod]
    public void FileScopeCases_HoldAllowedAndForbiddenInvariants()
    {
        foreach (var item in FileScopeMatchingPropertyFixtures.ScopeCases)
        {
            Assert.AreEqual(
                item.ExpectedAllowed,
                BoundedRunAuthorityGrantFileScope.IsAllowed(item.CandidatePath, item.AllowedGlobs, item.ForbiddenGlobs),
                $"{item.Id}: allowed");
            Assert.AreEqual(
                item.ExpectedForbidden,
                BoundedRunAuthorityGrantFileScope.IsForbidden(item.CandidatePath, item.ForbiddenGlobs),
                $"{item.Id}: forbidden");
        }
    }

    [TestMethod]
    public void ForbiddenWinsWhenAllowedAndForbiddenBothMatch()
    {
        const string candidate = "src/Feature/Secrets/Credential.cs";
        var allowed = new[] { "src/**/*.cs" };
        var forbidden = new[] { "src/**/Secrets/*.cs" };

        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed(candidate, allowed, forbidden));
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsForbidden(candidate, forbidden));
    }

    [TestMethod]
    public void AllowedOnlyCandidate_IsAllowed()
    {
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed("src/App.cs", ["src/*.cs"], []));
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsForbidden("src/App.cs", []));
    }

    [TestMethod]
    public void ForbiddenOnlyCandidate_IsForbidden()
    {
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed("src/Secrets/Credential.cs", ["tests/*.cs"], ["src/Secrets/*.cs"]));
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsForbidden("src/Secrets/Credential.cs", ["src/Secrets/*.cs"]));
    }

    [TestMethod]
    public void NeitherAllowedNorForbiddenCandidate_IsNotAllowedAndNotForbidden()
    {
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed("docs/readme.md", ["src/*.cs"], ["src/Secrets/*.cs"]));
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsForbidden("docs/readme.md", ["src/Secrets/*.cs"]));
    }

    [TestMethod]
    public void NormalizationCases_PreserveEquivalentResults()
    {
        foreach (var item in FileScopeMatchingPropertyFixtures.NormalizationCases)
        {
            Assert.AreEqual(
                item.ExpectedAllowed,
                BoundedRunAuthorityGrantFileScope.IsAllowed(item.CandidatePath, item.AllowedGlobs, item.ForbiddenGlobs),
                item.Id);
            Assert.AreEqual(
                item.ExpectedForbidden,
                BoundedRunAuthorityGrantFileScope.IsForbidden(item.CandidatePath, item.ForbiddenGlobs),
                item.Id);
        }
    }

    [TestMethod]
    public void BackslashAndSlashForms_AreEquivalent()
    {
        var slash = BoundedRunAuthorityGrantFileScope.IsAllowed("src/windows/path.cs", ["src/**/*.cs"], []);
        var backslash = BoundedRunAuthorityGrantFileScope.IsAllowed(@"src\windows\path.cs", [@"src\**\*.cs"], []);

        Assert.AreEqual(slash, backslash);
        Assert.IsTrue(backslash);
    }

    [TestMethod]
    public void CaseInsensitiveMatching_HoldsForCandidatesAndGlobs()
    {
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed("SRC/Case/Path.cs", ["src/**/*.cs"], []));
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed("src/case/path.cs", ["SRC/**/*.CS"], []));
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsForbidden("SRC/Secrets/Key.cs", ["src/SECRETS/*.cs"]));
    }

    [TestMethod]
    public void WhitespaceAroundCandidateAndGlob_DoesNotChangeResult()
    {
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed(" src/App.cs ", [" src/*.cs "], []));
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsForbidden(" src/Secrets/Key.cs ", [" src/Secrets/*.cs "]));
    }

    [TestMethod]
    public void SingleStar_DoesNotCrossDirectoryBoundary()
    {
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed("src/App.cs", ["src/*.cs"], []));
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed("src/Feature/App.cs", ["src/*.cs"], []));
    }

    [TestMethod]
    public void DoubleStar_CanCrossDirectoryBoundary()
    {
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed("src/Feature/App.cs", ["src/**/*.cs"], []));
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed("src/Feature/Nested/App.cs", ["src/**/*.cs"], []));
    }

    [TestMethod]
    public void QuestionMark_MatchesSingleNonSlashCharacter()
    {
        Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsAllowed("src/Feature/Handler.c", ["src/Feature/Handler.?"], []));
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed("src/Feature/Handler.cs", ["src/Feature/Handler.?"], []));
        Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed("src/Feature/Handler/x", ["src/Feature/Handler.?"], []));
    }

    [TestMethod]
    public void UnsafeAllowedGlobs_DoNotAuthorizeSafeCandidates()
    {
        foreach (var unsafeGlob in FileScopeMatchingPropertyFixtures.UnsafePaths)
        {
            Assert.IsFalse(
                BoundedRunAuthorityGrantFileScope.IsAllowed("src/App.cs", [unsafeGlob!], []),
                unsafeGlob ?? "<null>");
        }
    }

    [TestMethod]
    public void UnsafeForbiddenGlobs_DoNotCreateFalseForbiddenMatches()
    {
        foreach (var unsafeGlob in FileScopeMatchingPropertyFixtures.UnsafePaths)
        {
            Assert.IsFalse(
                BoundedRunAuthorityGrantFileScope.IsForbidden("src/App.cs", [unsafeGlob!]),
                unsafeGlob ?? "<null>");
            Assert.IsTrue(
                BoundedRunAuthorityGrantFileScope.IsAllowed("src/App.cs", ["src/*.cs"], [unsafeGlob!]),
                unsafeGlob ?? "<null>");
        }
    }

    [TestMethod]
    public void EmptyAllowedGlobs_AllowNothing()
    {
        foreach (var candidate in FileScopeMatchingPropertyFixtures.SafeRelativePaths.OfType<string>())
        {
            Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsAllowed(candidate, [], []), candidate);
        }
    }

    [TestMethod]
    public void EmptyForbiddenGlobs_ForbidOnlyUnsafeCandidates()
    {
        foreach (var candidate in FileScopeMatchingPropertyFixtures.SafeRelativePaths.OfType<string>())
        {
            Assert.IsFalse(BoundedRunAuthorityGrantFileScope.IsForbidden(candidate, []), candidate);
        }

        foreach (var candidate in FileScopeMatchingPropertyFixtures.UnsafePaths)
        {
            Assert.IsTrue(BoundedRunAuthorityGrantFileScope.IsForbidden(candidate!, []), candidate ?? "<null>");
        }
    }

    [TestMethod]
    public void Matcher_UnsafeFilePath_ReportsRequestedFilePathUnsafe()
    {
        var decision = FileScopeMatchingPropertyFixtures.Match("../outside.cs");

        Assert.IsFalse(decision.IsInsideGrantEnvelope);
        AssertContains(decision.BlockedReasons, "RequestedFilePathUnsafe");
        AssertContains(decision.ForbiddenActions, "do not proceed outside bounded run grant envelope");
    }

    [TestMethod]
    public void Matcher_ForbiddenFilePath_ReportsRequestedFileForbidden()
    {
        var decision = FileScopeMatchingPropertyFixtures.Match("src/Feature/Secrets/Credential.cs");

        Assert.IsFalse(decision.IsInsideGrantEnvelope);
        AssertContains(decision.BlockedReasons, "RequestedFileForbidden");
        AssertContains(decision.ForbiddenActions, "do not proceed outside bounded run grant envelope");
    }

    [TestMethod]
    public void Matcher_UnmatchedFilePath_ReportsRequestedFileNotAllowed()
    {
        var decision = FileScopeMatchingPropertyFixtures.Match("docs/readme.md");

        Assert.IsFalse(decision.IsInsideGrantEnvelope);
        AssertContains(decision.BlockedReasons, "RequestedFileNotAllowed");
        AssertContains(decision.RequiredIndependentChecks, "separate governed authority required outside this grant envelope");
    }

    [TestMethod]
    public void Matcher_AllowedFilePath_StaysInsideGrantEnvelopeButStillWarnsNotExecutionAuthority()
    {
        var decision = FileScopeMatchingPropertyFixtures.Match("src/Feature/Allowed.cs");

        Assert.IsTrue(decision.IsInsideGrantEnvelope);
        Assert.AreEqual(0, decision.BlockedReasons.Count);
        AssertContains(decision.ForbiddenActions, "do not treat bounded grant as execution authority");
        AssertContains(decision.ForbiddenActions, "do not treat bounded grant as approval");
        AssertContains(decision.ForbiddenActions, "do not treat bounded grant as policy satisfaction");
    }

    [TestMethod]
    public void Matcher_AllowedFilePath_RequiresIndependentGovernanceChecks()
    {
        var decision = FileScopeMatchingPropertyFixtures.Match("src/Feature/Allowed.cs");

        Assert.IsTrue(decision.IsInsideGrantEnvelope);
        AssertContains(decision.RequiredIndependentChecks, "grant envelope is necessary but not sufficient");
        AssertContains(decision.RequiredIndependentChecks, "operation-specific governance still required");
        AssertContains(decision.RequiredIndependentChecks, "required validation evidence still must be checked");
    }

    [TestMethod]
    public void Matcher_AllowedFilePath_DoesNotTreatBoundedGrantAsApprovalOrPolicySatisfaction()
    {
        var decision = FileScopeMatchingPropertyFixtures.Match("src/Feature/Allowed.cs");

        Assert.IsTrue(decision.IsInsideGrantEnvelope);
        AssertContains(decision.ForbiddenActions, "do not treat bounded grant as approval");
        AssertContains(decision.ForbiddenActions, "do not treat bounded grant as policy satisfaction");
        Assert.IsFalse(decision.ForbiddenActions.Any(action => action.Contains("safe to run", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(decision.RequiredIndependentChecks.Any(check => check.Contains("policy is satisfied", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void FileScopeMatchingPropertyTestsStayFastLaneAndDependencyClean()
    {
        var root = FileScopeMatchingPropertyFixtures.RepoRoot();
        var projectPath = Path.Combine(root, "IronDev.UnitTests", "IronDev.UnitTests.csproj");
        var g11Files = new[]
        {
            Path.Combine(root, "IronDev.UnitTests", "Governance", "FileScopeMatchingPropertyFixtures.cs"),
            Path.Combine(root, "IronDev.UnitTests", "Governance", "FileScopeMatchingPropertyTests.cs")
        };
        var combinedSource = string.Join(Environment.NewLine, g11Files.Select(File.ReadAllText));
        var project = XDocument.Load(projectPath);

        var projectReferences = project.Descendants("ProjectReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var packageReferences = project.Descendants("PackageReference")
            .Select(static reference => reference.Attribute("Include")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.AreEqual(new[] { @"..\IronDev.Core\IronDev.Core.csproj" }, projectReferences);
        CollectionAssert.AreEquivalent(
            new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestAdapter", "MSTest.TestFramework" },
            packageReferences);

        foreach (var forbidden in ForbiddenFastLaneDependencyTokens())
        {
            Assert.IsFalse(combinedSource.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        CollectionAssert.Contains(values.ToList(), expected, expected);

    private static IReadOnlyList<string> ForbiddenFastLaneDependencyTokens() =>
    [
        string.Concat("IronDev", ".Infrastructure"),
        string.Concat("IronDev", ".Api"),
        string.Concat("IronDev", ".Cli"),
        string.Concat("Sql", "Connection"),
        string.Concat("Db", "Context"),
        string.Concat("Http", "Client"),
        string.Concat("Process", ".Start"),
        string.Concat("File", ".Write"),
        string.Concat("File", ".Delete"),
        string.Concat("Directory", ".Delete"),
        string.Concat("git ", "commit"),
        string.Concat("git ", "push"),
        string.Concat("Source", "Apply", "Executor"),
        string.Concat("Controlled", "Source", "Apply", "Executor"),
        string.Concat("Patch", "Artifact", "Writer"),
        string.Concat("Governed", "Tool", "Registry"),
        string.Concat("Execute", "Async"),
        string.Concat("DateTimeOffset", ".Utc", "Now"),
        string.Concat("Environment", ".Get", "Environment", "Variable")
    ];
}
