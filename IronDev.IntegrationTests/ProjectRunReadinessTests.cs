using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Core.RunReadiness;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("RunReadiness")]
[DoNotParallelize]
public sealed class ProjectRunReadinessTests
{
    private string _sandboxRoot = string.Empty;
    private string _projectPath = string.Empty;

    [TestInitialize]
    public void CreateSandbox()
    {
        _sandboxRoot = Path.Combine(Path.GetTempPath(), "irondev-apply-capability-" + Guid.NewGuid().ToString("N"));
        _projectPath = Path.Combine(_sandboxRoot, "project");
        Directory.CreateDirectory(_projectPath);
    }

    [TestCleanup]
    public void DeleteSandbox()
    {
        if (!Directory.Exists(_sandboxRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(_sandboxRoot))
        {
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(directory, recursive: false);
        }
        Directory.Delete(_sandboxRoot, recursive: true);
    }

    [TestMethod]
    public void ProjectApplyCapability_Ready_RequiresEveryLauncherAndSandboxCondition()
    {
        var result = ProjectApplyCapabilityEvaluator.Evaluate(ReadyApplyInput());

        Assert.IsTrue(result.IsReady, result.Reason);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.Ready, result.ReasonCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ReadinessEvidenceHash));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.SandboxRootFingerprint));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ProjectPathFingerprint));
    }

    [TestMethod]
    public void ProjectFeatureReadiness_RequiresSetupExecutionAndCompletionCapability()
    {
        Assert.AreEqual(ProjectRunReadinessStates.SetupIncomplete,
            ProjectRunReadinessService.StateFor(false, true, true));
        Assert.AreEqual(ProjectRunReadinessStates.RunConfigurationRequired,
            ProjectRunReadinessService.StateFor(true, false, true));
        Assert.AreEqual(ProjectRunReadinessStates.ProjectWorkSessionRequired,
            ProjectRunReadinessService.StateFor(true, true, false));
        Assert.AreEqual(ProjectRunReadinessStates.ReadyToRun,
            ProjectRunReadinessService.StateFor(true, true, true));
        Assert.IsTrue(ProjectRunReadinessService.IsReadyForPurpose(true, true, true));
        Assert.IsFalse(ProjectRunReadinessService.IsReadyForPurpose(true, true, false));
    }

    [TestMethod]
    public void ProjectApplyCapability_StableFailureCodes_AreReachable()
    {
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled,
            ReadyApplyInput() with { ApplyEnabled = false });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled,
            ReadyApplyInput() with { EnvironmentName = "Production" });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled,
            ReadyApplyInput() with { DatabaseName = "IronDeveloper" });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyLauncherCapabilityMissing,
            ReadyApplyInput() with { LauncherCapabilityDeclared = false });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplySessionIdentityMismatch,
            ReadyApplyInput() with { ApiSessionId = "different-session" });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplySandboxRootMissing,
            ReadyApplyInput() with { SandboxRoot = Path.Combine(_sandboxRoot, "missing") });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplySandboxRootUnsafe,
            ReadyApplyInput() with { SandboxRoot = Path.GetPathRoot(_sandboxRoot)!, ProjectPath = _projectPath });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyPathOutsideSandbox,
            ReadyApplyInput() with { ProjectPath = Path.GetTempPath() });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyPathIsRoot,
            ReadyApplyInput() with { ProjectPath = _sandboxRoot });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationAuthorityMissing,
            ReadyApplyInput() with { QualificationAuthorityConfigured = false });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMissing,
            ReadyApplyInput() with { Qualification = new ProjectApplyQualificationEvidence() });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationInvalid,
            ReadyApplyInput() with { Qualification = ReadyQualification() with { RecordSignatureValid = false } });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch,
            ReadyApplyInput() with { Qualification = ReadyQualification() with { RecordBindingMatches = false } });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMarkerMissing,
            ReadyApplyInput() with { Qualification = ReadyQualification() with { MarkerPresent = false } });
        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMarkerMismatch,
            ReadyApplyInput() with { Qualification = ReadyQualification() with { MarkerMatches = false } });
    }

    [TestMethod]
    public void ProjectApplyCapability_ReparsePointInProjectPath_IsRefused()
    {
        var target = Path.Combine(_sandboxRoot, "target");
        var link = Path.Combine(_sandboxRoot, "linked-project");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Fail($"This test host cannot create a directory symbolic link: {exception.Message}");
            }

            using var junction = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            junction!.WaitForExit();
            Assert.AreEqual(0, junction.ExitCode, "The test could not create a Windows junction.");
        }

        AssertCapabilityCode(ProjectApplyCapabilityReasonCodes.ProjectApplyPathReparsePoint,
            ReadyApplyInput() with { ProjectPath = link });
    }

    [TestMethod]
    public async Task QualifyDisposableProject_RetainsSignedServerAuthorityAndIdempotentGitCorrelation()
    {
        RunProcess("git", "init -q", _projectPath);
        using var session = QualificationTestSession.Start();
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Disposable", LocalPath = _projectPath });
        var service = CreateCapabilityService(projects, session.SessionId);

        var first = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        var recordPath = Directory.GetFiles(session.QualificationRoot, "*.json").Single();
        var firstRecordJson = File.ReadAllText(recordPath);
        var markerPath = Path.Combine(_projectPath, ".git", ProjectApplyCapabilityService.DisposableMarkerFileName);

        Assert.IsTrue(first.IsReady, first.Reason);
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.QualificationId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.QualificationFingerprint));
        Assert.IsTrue(File.Exists(markerPath));
        Assert.IsFalse(File.Exists(Path.Combine(_projectPath, ProjectApplyCapabilityService.DisposableMarkerFileName)));
        Assert.AreEqual(string.Empty, RunProcess("git", "status --porcelain", _projectPath));

        using (var record = JsonDocument.Parse(firstRecordJson))
        {
            var root = record.RootElement;
            Assert.AreEqual(1, root.GetProperty("tenantId").GetInt32());
            Assert.AreEqual(4, root.GetProperty("projectId").GetInt32());
            Assert.AreEqual(71, root.GetProperty("qualifyingActorUserId").GetInt32());
            Assert.AreEqual(session.SessionId, root.GetProperty("launcherSessionId").GetString());
            Assert.AreEqual("IronDeveloper_Test", root.GetProperty("databaseName").GetString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("canonicalProjectPathHash").GetString()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("sandboxRootHash").GetString()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("qualifiedAtUtc").GetString()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("signature").GetString()));
        }

        using (var marker = JsonDocument.Parse(File.ReadAllText(markerPath)))
        {
            Assert.AreEqual(first.QualificationId, marker.RootElement.GetProperty("qualificationId").GetString());
            Assert.AreEqual(first.QualificationFingerprint, marker.RootElement.GetProperty("recordFingerprint").GetString());
            Assert.IsFalse(marker.RootElement.TryGetProperty("signature", out _), "The correlation marker must contain no authority secret.");
        }

        var second = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 99);

        Assert.IsTrue(second.IsReady, second.Reason);
        Assert.AreEqual(first.QualificationId, second.QualificationId, "Qualification must be idempotent for the same binding.");
        Assert.AreEqual(first.QualificationFingerprint, second.QualificationFingerprint);
        Assert.AreEqual(firstRecordJson, File.ReadAllText(recordPath), "Idempotent qualification must retain its original actor and timestamp.");
    }

    [TestMethod]
    public async Task ForgedGitMarker_WithoutServerRecord_FailsClosed()
    {
        RunProcess("git", "init -q", _projectPath);
        using var session = QualificationTestSession.Start();
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Disposable", LocalPath = _projectPath });
        var markerPath = Path.Combine(_projectPath, ".git", ProjectApplyCapabilityService.DisposableMarkerFileName);
        File.WriteAllText(markerPath,
            "{\"contractVersion\":1,\"qualificationId\":\"forged\",\"recordFingerprint\":\"forged\"}");

        var result = await CreateCapabilityService(projects, session.SessionId).EvaluateAsync(4);

        Assert.IsFalse(result.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMissing, result.ReasonCode);
    }

    [TestMethod]
    public async Task DeletedServerRecord_InvalidatesPreviouslyQualifiedProject()
    {
        RunProcess("git", "init -q", _projectPath);
        using var session = QualificationTestSession.Start();
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Disposable", LocalPath = _projectPath });
        var service = CreateCapabilityService(projects, session.SessionId);
        var qualified = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        Assert.IsTrue(qualified.IsReady, qualified.Reason);

        File.Delete(Directory.GetFiles(session.QualificationRoot, "*.json").Single());
        var invalidated = await service.EvaluateAsync(4);

        Assert.IsFalse(invalidated.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMissing, invalidated.ReasonCode);
    }

    [TestMethod]
    public async Task QualificationFailureAfterProjectCreation_ReturnsFailClosedDecisionWithoutInvitingDuplicateCreation()
    {
        using var session = QualificationTestSession.Start();
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Already created", LocalPath = _projectPath })
        {
            ThrowOnGet = true
        };

        var result = await CreateCapabilityService(projects, session.SessionId)
            .QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);

        Assert.IsFalse(result.IsReady);
        Assert.AreEqual(4, result.ProjectId);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationInvalid, result.ReasonCode);
        Assert.AreEqual(0, projects.CreateCalls, "Qualification retry must not create the already-created project again.");
    }

    [TestMethod]
    public async Task TamperedServerRecordAndMismatchedMarker_AreNamedFailClosedStates()
    {
        RunProcess("git", "init -q", _projectPath);
        using var session = QualificationTestSession.Start();
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Disposable", LocalPath = _projectPath });
        var service = CreateCapabilityService(projects, session.SessionId);
        var qualified = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        Assert.IsTrue(qualified.IsReady, qualified.Reason);

        var recordPath = Directory.GetFiles(session.QualificationRoot, "*.json").Single();
        var record = JsonNode.Parse(File.ReadAllText(recordPath))!.AsObject();
        record["recordFingerprint"] = "altered-fingerprint";
        File.WriteAllText(recordPath, record.ToJsonString());

        var invalidRecord = await service.EvaluateAsync(4);
        Assert.IsFalse(invalidRecord.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationInvalid, invalidRecord.ReasonCode);

        var repaired = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        Assert.IsTrue(repaired.IsReady, repaired.Reason);
        var markerPath = Path.Combine(_projectPath, ".git", ProjectApplyCapabilityService.DisposableMarkerFileName);
        var marker = JsonNode.Parse(File.ReadAllText(markerPath))!.AsObject();
        marker["qualificationId"] = "different-qualification";
        File.WriteAllText(markerPath, marker.ToJsonString());

        var mismatchedMarker = await service.EvaluateAsync(4);
        Assert.IsFalse(mismatchedMarker.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMarkerMismatch, mismatchedMarker.ReasonCode);
    }

    [TestMethod]
    public async Task ChangedDatabaseAndSandboxRoot_FailClosedAgainstRetainedQualification()
    {
        RunProcess("git", "init -q", _projectPath);
        using var session = QualificationTestSession.Start();
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Disposable", LocalPath = _projectPath });
        var service = CreateCapabilityService(projects, session.SessionId);
        var qualified = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        Assert.IsTrue(qualified.IsReady, qualified.Reason);

        var changedDatabase = await CreateCapabilityService(
            projects, session.SessionId, databaseName: "IronDeveloper_Unexpected").EvaluateAsync(4);
        Assert.IsFalse(changedDatabase.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled, changedDatabase.ReasonCode);

        var broaderRoot = Path.GetDirectoryName(_sandboxRoot)!;
        var changedRoot = await CreateCapabilityService(
            projects, session.SessionId, sandboxRoot: broaderRoot).EvaluateAsync(4);
        Assert.IsFalse(changedRoot.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch, changedRoot.ReasonCode);
    }

    [TestMethod]
    public async Task ChangedProjectPath_InvalidatesOldRecord_UntilExplicitRequalification()
    {
        RunProcess("git", "init -q", _projectPath);
        var replacementPath = Path.Combine(_sandboxRoot, "replacement-project");
        Directory.CreateDirectory(replacementPath);
        RunProcess("git", "init -q", replacementPath);
        using var session = QualificationTestSession.Start();
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Disposable", LocalPath = _projectPath });
        var service = CreateCapabilityService(projects, session.SessionId);
        var original = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        Assert.IsTrue(original.IsReady, original.Reason);

        projects.Project.LocalPath = replacementPath;
        var invalidated = await service.EvaluateAsync(4);

        Assert.IsFalse(invalidated.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationBindingMismatch, invalidated.ReasonCode);

        var requalified = await service.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        Assert.IsTrue(requalified.IsReady, requalified.Reason);
        Assert.AreNotEqual(original.QualificationId, requalified.QualificationId);
    }

    [TestMethod]
    public async Task NewLauncherSession_InvalidatesAndCanExplicitlyRequalifyExistingProject()
    {
        RunProcess("git", "init -q", _projectPath);
        var projects = new StubProjectService(
            new Project { Id = 4, TenantId = 1, Name = "Disposable", LocalPath = _projectPath });
        string originalQualificationId;
        using (var firstSession = QualificationTestSession.Start())
        {
            var first = await CreateCapabilityService(projects, firstSession.SessionId)
                .QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
            Assert.IsTrue(first.IsReady, first.Reason);
            originalQualificationId = first.QualificationId;
        }

        using var secondSession = QualificationTestSession.Start();
        var secondService = CreateCapabilityService(projects, secondSession.SessionId);
        var invalidated = await secondService.EvaluateAsync(4);
        Assert.IsFalse(invalidated.IsReady);
        Assert.AreEqual(ProjectApplyCapabilityReasonCodes.ProjectApplyQualificationMissing, invalidated.ReasonCode);

        var requalified = await secondService.QualifyDisposableProjectAsync(4, qualifyingActorUserId: 71);
        Assert.IsTrue(requalified.IsReady, requalified.Reason);
        Assert.AreNotEqual(originalQualificationId, requalified.QualificationId);
    }

    [TestMethod]
    public void RequiredReasonCodes_AreStableAndIndividuallyReachable()
    {
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentProfileMissing, [], []);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionMissing, [Profile()], []);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionDisabled, [Profile()], [Connection(enabled: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionUnavailableForTenant, [Profile()], [Connection(tenantAvailable: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionUnavailableForProject, [Profile()], [Connection(projectAvailable: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentConnectionPurposeMismatch, [Profile()], [Connection(supportedPurposes: [ProjectRunPurposes.SmokeSimulation])]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentCredentialMissing, [Profile(provider: "openai")], [Connection(provider: "openai", credentialConfigured: false)]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentProviderUnsupported, [Profile(provider: "mystery")], [Connection(provider: "mystery")]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable, [Profile(provider: "fake")], [Connection(provider: "fake")]);
        AssertCode(ProjectRunReadinessReasonCodes.RunAgentModelMissing, [Profile(model: string.Empty)], [Connection()]);
    }

    [TestMethod]
    public void ConnectionPurpose_SeparatesWorkflowSmokeFromProjectFeatureWork()
    {
        var fake = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Builder,
            [Profile(provider: "fake")],
            [Connection(provider: "fake")]);
        var deterministicProjectWork = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Builder,
            [Profile(provider: "custom", model: "localtest-deterministic")],
            [Connection(provider: ProjectRunProviders.LocalTestDeterministic)]);
        var deterministicSmoke = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Builder,
            [Profile(provider: "custom", model: "localtest-deterministic")],
            [Connection(provider: ProjectRunProviders.LocalTestDeterministic)],
            ProjectRunPurposes.SmokeSimulation);
        var realProjectWork = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Builder,
            [Profile(provider: "custom")],
            [Connection(provider: "custom")]);

        Assert.IsFalse(fake.IsReady);
        Assert.IsTrue(fake.Blockers.Any(blocker => blocker.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable));
        Assert.IsFalse(deterministicProjectWork.IsReady, "A fixed smoke fixture must never make normal Work Item readiness green.");
        var mismatch = deterministicProjectWork.Blockers.Single(blocker =>
            blocker.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentConnectionPurposeMismatch);
        Assert.AreEqual(
            "LocalTest deterministic is a fixed smoke-test connection. It can exercise the governed workflow, but it cannot implement this Work Item. Configure an executable project-work connection to continue.",
            mismatch.Reason);
        Assert.IsTrue(deterministicSmoke.IsReady, string.Join("; ", deterministicSmoke.Blockers.Select(blocker => blocker.Reason)));
        Assert.IsTrue(realProjectWork.IsReady, string.Join("; ", realProjectWork.Blockers.Select(blocker => blocker.Reason)));
    }

    [TestMethod]
    public void Blocker_CarriesRoleProviderModelConnectionSourceAndRepairAction()
    {
        var result = ProjectRunReadinessService.EvaluateAgent(
            SkeletonAgentRole.Critic,
            [Profile(role: SkeletonAgentRole.Critic, provider: "fake")],
            [Connection(provider: "fake")]);
        var blocker = result.Blockers.Single(item =>
            item.ReasonCode == ProjectRunReadinessReasonCodes.RunAgentProviderNotExecutable);

        Assert.AreEqual(SkeletonAgentRole.Critic, blocker.Role);
        Assert.AreEqual("fake", blocker.EffectiveProvider);
        Assert.AreEqual("model-1", blocker.EffectiveModel);
        Assert.AreEqual("connection-1", blocker.ConnectionId);
        Assert.AreEqual("Project", blocker.SourceLayer);
        Assert.IsFalse(string.IsNullOrWhiteSpace(blocker.Reason));
        Assert.IsFalse(string.IsNullOrWhiteSpace(blocker.NextSafeAction));
    }

    private static void AssertCode(
        string expected,
        IReadOnlyList<EffectiveSkeletonAgentProfile> profiles,
        IReadOnlyList<AiConnectionMetadata> connections)
    {
        var result = ProjectRunReadinessService.EvaluateAgent(SkeletonAgentRole.Builder, profiles, connections);
        Assert.IsTrue(result.Blockers.Any(blocker => blocker.ReasonCode == expected),
            $"Expected {expected}; actual: {string.Join(", ", result.Blockers.Select(blocker => blocker.ReasonCode))}");
    }

    private ProjectApplyCapabilityInput ReadyApplyInput() => new()
    {
        ProjectId = 4,
        TenantId = 1,
        ProjectTenantId = 1,
        EnvironmentName = "LocalTest",
        DatabaseName = "IronDeveloper_Test",
        ExpectedDatabaseName = "IronDeveloper_Test",
        ApplyEnabled = true,
        LauncherCapabilityDeclared = true,
        LauncherSessionId = "session-1",
        ApiSessionId = "session-1",
        SessionMode = ProjectRunPurposes.ProjectFeatureWork,
        RepositoryCommit = "abc123",
        SandboxRoot = _sandboxRoot,
        ProjectPath = _projectPath,
        QualificationAuthorityConfigured = true,
        Qualification = ReadyQualification()
    };

    private static ProjectApplyQualificationEvidence ReadyQualification() => new()
    {
        RecordPresent = true,
        RecordSignatureValid = true,
        RecordBindingMatches = true,
        MarkerPresent = true,
        MarkerMatches = true,
        QualificationId = "qualification-1",
        QualificationFingerprint = "qualification-fingerprint-1"
    };

    private static void AssertCapabilityCode(string expected, ProjectApplyCapabilityInput input)
    {
        var result = ProjectApplyCapabilityEvaluator.Evaluate(input);
        Assert.IsFalse(result.IsReady);
        Assert.AreEqual(expected, result.ReasonCode, result.Reason);
    }

    private static string RunProcess(string fileName, string arguments, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, error);
        return output.Trim();
    }

    private ProjectApplyCapabilityService CreateCapabilityService(
        StubProjectService projects,
        string sessionId,
        string? databaseName = null,
        string? sandboxRoot = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:IronDeveloperDb"] = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName ?? "IronDeveloper_Test"};Integrated Security=True;",
            ["SkeletonApply:Enabled"] = "true",
            ["SkeletonApply:LauncherCapabilityDeclared"] = "true",
            ["SkeletonApply:LauncherSessionId"] = sessionId,
            ["SkeletonApply:SandboxRoot"] = sandboxRoot ?? _sandboxRoot
        }).Build();
        return new ProjectApplyCapabilityService(
            projects,
            new TestTenantContext(1),
            configuration,
            new TestHostEnvironment("LocalTest"),
            new ProjectApplyQualificationStore());
    }

    private sealed class StubProjectService(Project project) : IProjectService
    {
        public Project Project { get; } = project;
        public bool ThrowOnGet { get; init; }
        public int CreateCalls { get; private set; }
        public Task<int> CreateProjectAsync(Project toCreate, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(toCreate.Id);
        }
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>([Project]);
        public Task<Project?> GetByIdAsync(int projectId, CancellationToken cancellationToken = default) =>
            ThrowOnGet
                ? Task.FromException<Project?>(new IOException("qualification read failed"))
                : Task.FromResult<Project?>(projectId == Project.Id ? Project : null);
        public Task<Project?> UpdateProjectAsync(int projectId, Project toUpdate, CancellationToken cancellationToken = default) => Task.FromResult<Project?>(Project);
        public Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestTenantContext(int tenantId) : IronDev.Core.Auth.ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }

    private sealed class QualificationTestSession : IDisposable
    {
        private readonly Dictionary<string, string?> _previousEnvironment;
        private readonly string _sessionRoot;

        private QualificationTestSession(string sessionId)
        {
            SessionId = sessionId;
            _sessionRoot = Path.Combine(Path.GetTempPath(), "irondev-localtest-sessions", sessionId);
            QualificationRoot = Path.Combine(_sessionRoot, "project-apply-qualifications");
            Directory.CreateDirectory(_sessionRoot);
            _previousEnvironment = new Dictionary<string, string?>
            {
                ["IRONDEV_LOCALTEST_SESSION_ID"] = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_ID"),
                ["IRONDEV_LOCALTEST_SESSION_MODE"] = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_MODE"),
                ["IRONDEV_LOCALTEST_REPOSITORY_COMMIT"] = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_REPOSITORY_COMMIT"),
                ["IRONDEV_LOCALTEST_API_LOG_PATH"] = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_API_LOG_PATH"),
                ["IRONDEV_LOCALTEST_QUALIFICATION_KEY"] = Environment.GetEnvironmentVariable("IRONDEV_LOCALTEST_QUALIFICATION_KEY")
            };
            Environment.SetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_ID", sessionId);
            Environment.SetEnvironmentVariable("IRONDEV_LOCALTEST_SESSION_MODE", ProjectRunPurposes.ProjectFeatureWork);
            Environment.SetEnvironmentVariable("IRONDEV_LOCALTEST_REPOSITORY_COMMIT", "abc123");
            Environment.SetEnvironmentVariable("IRONDEV_LOCALTEST_API_LOG_PATH", Path.Combine(_sessionRoot, "api.application.log"));
            Environment.SetEnvironmentVariable("IRONDEV_LOCALTEST_QUALIFICATION_KEY",
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        }

        public string SessionId { get; }
        public string QualificationRoot { get; }

        public static QualificationTestSession Start() => new(Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            foreach (var item in _previousEnvironment)
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            if (Directory.Exists(_sessionRoot)) Directory.Delete(_sessionRoot, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "IronDev.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private static EffectiveSkeletonAgentProfile Profile(
        SkeletonAgentRole role = SkeletonAgentRole.Builder,
        string provider = "custom",
        string model = "model-1") => new()
    {
        Role = role,
        DisplayName = role.ToString(),
        AiConnectionId = "connection-1",
        Provider = provider,
        Model = model,
        TimeoutSeconds = 30,
        EffectiveSkill = string.Empty,
        EffectivePersonality = string.Empty,
        EffectiveHash = "hash",
        PublishedScopeLayer = "Project",
        FieldSources =
        [
            new SkeletonAgentProfileFieldSource
            {
                Field = nameof(SkeletonAgentProfile.AiConnectionId),
                SourceLayer = "Project",
                SourceLabel = "Project override",
                Inherited = false
            }
        ]
    };

    private static AiConnectionMetadata Connection(
        string provider = "custom",
        bool enabled = true,
        bool tenantAvailable = true,
        bool projectAvailable = true,
        bool credentialConfigured = true,
        IReadOnlyList<string>? supportedPurposes = null) => new()
    {
        Id = "connection-1",
        TenantId = 1,
        DisplayName = "Connection 1",
        ProviderKind = provider,
        ControlledEndpointId = "controlled-1",
        ControlledEndpoint = "deployment-configured",
        CredentialConfigured = credentialConfigured,
        CredentialStatus = credentialConfigured ? "Configured" : "Missing",
        SupportedPurposes = supportedPurposes ?? (provider == ProjectRunProviders.LocalTestDeterministic
            ? [ProjectRunPurposes.SmokeSimulation]
            : provider == "fake"
                ? []
                : [ProjectRunPurposes.ProjectFeatureWork]),
        PurposeDescription = "Test-controlled connection",
        Enabled = enabled,
        TenantAvailable = tenantAvailable,
        ProjectAvailable = projectAvailable,
        CreatedByUserId = 1,
        UpdatedByUserId = 1,
        Version = "test",
        Boundary = "non-secret"
    };
}
