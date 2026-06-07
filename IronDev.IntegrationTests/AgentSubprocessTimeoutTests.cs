using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IronDev.Core.Agents;
using IronDev.Core.RunReports;
using IronDev.Core.Interfaces;
using IronDev.Client;
using IronDev.Infrastructure.Services.Agents;
using IronDev.Infrastructure.Services.RunReports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSubprocessTimeoutTests
{
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IronDev.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    [TestMethod]
    public async Task AgentProcessRunner_Timeout_ShouldKillProcessAndReturnTimedOut()
    {
        var runner = new AgentProcessRunner();
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools", "dogfood", $"timeout-test-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, "Start-Sleep -Seconds 5");
        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await runner.RunAsync(
                "powershell",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
                repoRoot);

            Assert.IsTrue(result.TimedOut);
            Assert.AreEqual(-1, result.ExitCode);
            StringAssert.Contains(result.Stderr, "powershell subprocess timed out after 1s and was killed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }

    [TestMethod]
    public async Task AgentProcessRunner_ShouldRejectPowerShellCommandArgument()
    {
        var runner = new AgentProcessRunner();

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "powershell",
                ["-NoProfile", "-Command", "Write-Output unsafe"],
                GetRepoRoot()));

        StringAssert.Contains(ex.Message, "-Command is not allowed");
    }

    [TestMethod]
    public async Task AgentProcessRunner_ShouldRejectPowerShellEncodedCommandArgument()
    {
        var runner = new AgentProcessRunner();

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "powershell",
                ["-NoProfile", "-EncodedCommand", "VwByAGkAdABlAC0ASABvAHMAdAAgAHUAbgBzAGEAZgBlAA=="],
                GetRepoRoot()));

        StringAssert.Contains(ex.Message, "-EncodedCommand is not allowed");
    }

    [TestMethod]
    public async Task SupervisorAgent_Timeout_ShouldKillProcessAndFailGracefully()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "SupervisorAgent",
            Purpose = "Test SupervisorAgent timeout behavior.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new SupervisorAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "SupervisorAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "TestProject",
                ["query"] = "TestQuery",
                ["plan_path"] = "TestPlan.md",
                ["live_llm"] = "false"
            }
        };

        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await agent.RunAsync(request);

            Assert.AreEqual(AgentRunStatus.Failed, result.Status);
            Assert.AreEqual(1, result.ExitCode); // SupervisorAgent maps failure to 1
            Assert.IsNotNull(result.OutputJson);
            StringAssert.Contains(result.Summary, "subprocess timeout");
            Assert.IsTrue(result.CommandsRun.Count > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
        }
    }

    [DataTestMethod]
    [DataRow("PausedForApproval", "blocked")]
    [DataRow("Failed", "failed")]
    public async Task SupervisorAgent_ShouldConsumeTesterRunContractForDecision(string runStatus, string expectedCommandStatus)
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "SupervisorAgent",
            Purpose = "Test SupervisorAgent should consume contract report.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var runId = "test-run";
        var testerRunId = $"{runId}-tester";
        var fakeReader = new FakeRunReportContractReader(
            new Dictionary<string, RunReportContractReadResult>
            {
                [testerRunId] = BuildRunReportContractReadResult(
                    testerRunId,
                    runStatus,
                    runStatus == "Failed" ? "Execution failed" : "Approval required",
                    "trace-run")
            });
        var fakeRunner = new FakeAgentProcessRunner(
            [
                new AgentProcessRunResult(0, """{"status":"Succeeded","contextPackage":{"Matches":[{"DocumentTitle":"Memory title"}],"SemanticTraceId":"trace-mem","WeightedContextBundle":{"summaryForAgent":"Memory context."}}}""", string.Empty, false, "retriever"),
                new AgentProcessRunResult(0, """{"review":{"decision":"Allow"}}""", string.Empty, false, "conscience"),
                new AgentProcessRunResult(0, """{"thoughtLedger":{}}""", string.Empty, false, "thoughtLedger"),
                new AgentProcessRunResult(0, """{"report":{"status":"Passed"}}""", string.Empty, false, "tester-run-plan")
            ]);
        var agent = new SupervisorAgent(definition, resolver, repoRoot, null, fakeReader, fakeRunner);

        var request = new AgentRequest
        {
            AgentName = "SupervisorAgent",
            GoalId = "test-goal",
            DogfoodRunId = runId,
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "TestProject",
                ["query"] = "TestQuery",
                ["plan_path"] = "TestPlan.md",
                ["live_llm"] = "false"
            }
        };

        var result = await agent.RunAsync(request);

        Assert.AreEqual(AgentRunStatus.Failed, result.Status);
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(1, fakeReader.Calls.Count);
        Assert.AreEqual(testerRunId, fakeReader.Calls[0]);
        Assert.IsNotNull(result.OutputJson);
        Assert.IsTrue(result.OutputJson.Contains($"\"runId\":\"{testerRunId}\"", StringComparison.Ordinal));
        Assert.IsTrue(result.OutputJson.Contains("\"traceId\":\"trace-run\"", StringComparison.Ordinal));
        Assert.IsTrue(result.OutputJson.Contains($"\"runStatus\":\"{runStatus}\"", StringComparison.Ordinal));
        Assert.IsTrue(result.OutputJson.Contains($"\"commandStatus\":\"{expectedCommandStatus}\"", StringComparison.Ordinal));
        Assert.IsTrue(result.OutputJson.Contains("\"governance\":", StringComparison.Ordinal));
        Assert.IsTrue(result.OutputJson.Contains("\"warnings\":", StringComparison.Ordinal));
        Assert.AreEqual(4, fakeRunner.Commands.Count);
    }

    [TestMethod]
    public async Task RunReportContractReader_ShouldCallRunsReportApiEndpoint()
    {
        var handler = new RunReportReaderHandler();
        var client = IronDevApiClientFactory.Create("http://localhost:5000", handler: handler);
        var reader = new RunReportContractReader(client);

        var result = await reader.ReadAsync("run-report-reader");

        Assert.AreEqual("runs report", result.Command);
        Assert.AreEqual("succeeded", result.Status);
        CollectionAssert.Contains(handler.RequestPaths, "/api/runs/run-report-reader/report");
    }

    [TestMethod]
    public async Task RetrieverAgent_Timeout_ShouldKillProcessAndFailGracefully()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "RetrieverAgent",
            Purpose = "Test RetrieverAgent timeout behavior.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new RetrieverAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "RetrieverAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "TestProject",
                ["query"] = "TestQuery",
                ["live_llm"] = "false"
            }
        };

        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await agent.RunAsync(request);

            Assert.AreEqual(AgentRunStatus.Failed, result.Status);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.IsNotNull(result.OutputJson);
            StringAssert.Contains(result.Summary, "RetrieverAgent subprocess timed out after 1s.");
            StringAssert.Contains(result.OutputJson, "dotnet subprocess timed out after 1s and was killed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
        }
    }

    [TestMethod]
    public async Task QualityAgent_Timeout_ShouldKillProcessAndFailGracefully()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "QualityAgent",
            Purpose = "Test QualityAgent timeout behavior.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new QualityAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "QualityAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "TestProject",
                ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                ["live_llm"] = "false"
            }
        };

        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await agent.RunAsync(request);

            Assert.AreEqual(AgentRunStatus.Failed, result.Status);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.IsNotNull(result.OutputJson);
            StringAssert.Contains(result.Summary, "QualityAgent subprocess timed out after 1s.");
            StringAssert.Contains(result.OutputJson, "QualityAgent subprocess timed out after 1s.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
        }
    }

    [TestMethod]
    public async Task TesterAgent_ShouldRejectAbsolutePlanPath()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "TesterAgent",
            Purpose = "Test TesterAgent plan path boundary.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new TesterAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "TesterAgent",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = Path.Combine(repoRoot, "tools", "dogfood", "test-agent-plans", "irondev-code-standards-alpha.json")
            }
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => agent.RunAsync(request));
        StringAssert.Contains(ex.Message, "must be relative");
    }

    [TestMethod]
    public async Task TesterAgent_ShouldRejectPlanTraversal()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "TesterAgent",
            Purpose = "Test TesterAgent plan path boundary.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new TesterAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "TesterAgent",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "../outside.json"
            }
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => agent.RunAsync(request));
        StringAssert.Contains(ex.Message, "must stay under");
    }

    [TestMethod]
    public async Task QualityAgent_ShouldRejectUnsafePlanPath()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "QualityAgent",
            Purpose = "Test QualityAgent plan path boundary.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new QualityAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "QualityAgent",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "TestPlan.md",
                ["live_llm"] = "false"
            }
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => agent.RunAsync(request));
        StringAssert.Contains(ex.Message, "approved JSON test-plan");
    }

    [TestMethod]
    public async Task TesterAgent_ShouldUseGovernedProcessExecutor()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "TesterAgent",
            Purpose = "Test TesterAgent should use governed process executor.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var executor = new FakeGovernedAgentProcessExecutor(request => new GovernedAgentProcessResult
        {
            ToolCallId = request.ToolCallId,
            Command = "powershell \"-NoProfile\" \"-ExecutionPolicy\" \"Bypass\" \"-File\" \"stub\"",
            ExitCode = 0,
            Stdout = """{"summary":"TesterExecutorPath","evidence":[{"path":"stub.evidence.json"}]}""",
            Stderr = string.Empty,
            TimedOut = false,
            EvidencePaths = ["test-results/tester-executor-stdout.log"],
            DurationMs = 0
        });
        var agent = new TesterAgent(definition, resolver, repoRoot, executor);

        var request = new AgentRequest
        {
            AgentName = "TesterAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "irondev-code-standards-alpha.json"
            }
        };

        var result = await agent.RunAsync(request);

        Assert.AreEqual(1, executor.InvocationCount);
        Assert.AreEqual("powershell", executor.Calls[0].FileName);
        StringAssert.Contains(result.Summary, "TesterExecutorPath");
        Assert.AreEqual(1, result.CommandsRun.Count);
        StringAssert.Contains(result.CommandsRun[0], "powershell");
        CollectionAssert.Contains(result.EvidencePaths.ToArray(), "test-results/tester-executor-stdout.log");
    }

    [TestMethod]
    public async Task QualityAgent_ShouldUseGovernedProcessExecutor()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "QualityAgent",
            Purpose = "Test QualityAgent should use governed process executor.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var executor = new FakeGovernedAgentProcessExecutor(request => new GovernedAgentProcessResult
        {
            ToolCallId = request.ToolCallId,
            Command = "powershell \"-NoProfile\" \"-ExecutionPolicy\" \"Bypass\" \"-File\" \"stub\"",
            ExitCode = 0,
            Stdout = """{"status":"passed","summary":"QualityExecutorPath","steps":[]}""",
            Stderr = string.Empty,
            TimedOut = false,
            EvidencePaths = ["test-results/quality-executor-stdout.log"],
            DurationMs = 0
        });
        var agent = new QualityAgent(definition, resolver, repoRoot, null, executor);

        var request = new AgentRequest
        {
            AgentName = "QualityAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                ["live_llm"] = "false"
            }
        };

        var result = await agent.RunAsync(request);

        Assert.AreEqual(1, executor.InvocationCount);
        Assert.AreEqual("powershell", executor.Calls[0].FileName);
        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        StringAssert.Contains(result.CommandsRun[0], "powershell");
        CollectionAssert.Contains(result.EvidencePaths.ToArray(), "test-results/quality-executor-stdout.log");
    }

    [TestMethod]
    public void AgentProcessBoundaryTests_ShouldNotUseRawProcessInAgents()
    {
        var testerSource = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "IronDev.Infrastructure", "Services", "Agents", "TesterAgent.cs"));
        var qualitySource = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "IronDev.Infrastructure", "Services", "Agents", "QualityAgent.cs"));

        Assert.IsFalse(testerSource.Contains("new Process", StringComparison.Ordinal), "TesterAgent should not instantiate raw Process.");
        Assert.IsFalse(qualitySource.Contains("new Process", StringComparison.Ordinal), "QualityAgent should not instantiate raw Process.");
        Assert.IsFalse(testerSource.Contains("ProcessStartInfo", StringComparison.Ordinal), "TesterAgent should not configure raw ProcessStartInfo.");
        Assert.IsFalse(qualitySource.Contains("ProcessStartInfo", StringComparison.Ordinal), "QualityAgent should not configure raw ProcessStartInfo.");
    }

    [TestMethod]
    public async Task GovernedAgentProcessExecutor_ShouldRejectPowershellCommandMode()
    {
        var executor = new GovernedAgentProcessExecutor();
        var request = new GovernedAgentProcessRequest
        {
            ToolCallId = Guid.NewGuid().ToString("N"),
            FileName = "powershell",
            WorkingDirectory = GetRepoRoot(),
            Arguments = ["-NoProfile", "-Command", "Get-Date"]
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executor.ExecuteAsync(request));
        StringAssert.Contains(ex.Message, "Governed PowerShell execution rejected: -Command is not allowed");
    }

    [TestMethod]
    public async Task GovernedAgentProcessExecutor_ShouldRequireNoProfile()
    {
        var executor = new GovernedAgentProcessExecutor();
        var request = new GovernedAgentProcessRequest
        {
            ToolCallId = Guid.NewGuid().ToString("N"),
            FileName = "powershell",
            WorkingDirectory = GetRepoRoot(),
            Arguments = ["-File", "tools/dogfood/Invoke-TestAgentPlan.ps1"]
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executor.ExecuteAsync(request));
        StringAssert.Contains(ex.Message, "Governed PowerShell execution rejected: -NoProfile is required");
    }

    [TestMethod]
    public async Task GovernedAgentProcessExecutor_ShouldRequireFileInvocation()
    {
        var executor = new GovernedAgentProcessExecutor();
        var request = new GovernedAgentProcessRequest
        {
            ToolCallId = Guid.NewGuid().ToString("N"),
            FileName = "powershell",
            WorkingDirectory = GetRepoRoot(),
            Arguments = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-CommandMode", "foo"]
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executor.ExecuteAsync(request));
        StringAssert.Contains(ex.Message, "Governed PowerShell execution rejected: -File is required");
    }

    private sealed class FakeGovernedAgentProcessExecutor : IGovernedAgentProcessExecutor
    {
        private readonly Func<GovernedAgentProcessRequest, GovernedAgentProcessResult> _resultFactory;
        private readonly List<GovernedAgentProcessRequest> _calls = [];

        public FakeGovernedAgentProcessExecutor(Func<GovernedAgentProcessRequest, GovernedAgentProcessResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public int InvocationCount => _calls.Count;
        public IReadOnlyList<GovernedAgentProcessRequest> Calls => _calls;

        public Task<GovernedAgentProcessResult> ExecuteAsync(
            GovernedAgentProcessRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _calls.Add(request);
            return Task.FromResult(_resultFactory(request));
        }
    }

    private sealed class FakeAgentProcessRunner : IAgentProcessRunner
    {
        private readonly Queue<AgentProcessRunResult> _scriptedResults;
        private readonly List<string> _commands = [];

        public FakeAgentProcessRunner(IEnumerable<AgentProcessRunResult> scriptedResults)
        {
            _scriptedResults = new Queue<AgentProcessRunResult>(scriptedResults);
        }

        public IReadOnlyList<string> Commands => _commands;

        public Task<AgentProcessRunResult> RunAsync(
            string fileName,
            string[] arguments,
            string workingDirectory,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var command = string.Join(' ', arguments.Prepend(fileName));
            _commands.Add(command);

            if (_scriptedResults.Count == 0)
                return Task.FromResult(
                    new AgentProcessRunResult(-1, string.Empty, "No scripted subprocess result was configured.", false, command));

            return Task.FromResult(_scriptedResults.Dequeue());
        }
    }

    private sealed class FakeRunReportContractReader : IRunReportContractReader
    {
        private readonly Dictionary<string, RunReportContractReadResult> _reports;
        private readonly List<string> _calls = [];

        public FakeRunReportContractReader(IReadOnlyDictionary<string, RunReportContractReadResult> reports)
        {
            _reports = new Dictionary<string, RunReportContractReadResult>(reports, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> Calls => _calls;

        public Task<RunReportContractReadResult> ReadAsync(string runId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _calls.Add(runId);

            if (_reports.TryGetValue(runId, out var report))
                return Task.FromResult(report);

            return Task.FromResult(RunReportContractMapper.MapFromApiFailure(runId, HttpStatusCode.NotFound, $"{{\"detail\":\"Run '{runId}' not found.\"}}"));
        }
    }

    private sealed class RunReportReaderHandler : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/runs/run-report-reader/report")
            {
                var payload = new RunReportDto
                {
                    Status = new RunStatusDto
                    {
                        RunId = "run-report-reader",
                        Project = "IronDev",
                        Title = "Run contract reader check",
                        Status = "Completed",
                        Recommendation = "Review",
                        TraceId = "trace-reader"
                    },
                    Report = new RunReportDetail
                    {
                        RunId = "run-report-reader",
                        Project = "IronDev",
                        Title = "Run contract reader check",
                        Status = "Completed",
                        TraceId = "trace-reader",
                        Summary = "Run contract reader check.",
                        Recommendation = "Review",
                        Evidence = []
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private static RunReportContractReadResult BuildRunReportContractReadResult(
        string runId,
        string status,
        string recommendation,
        string? traceId)
    {
        var report = new RunReportDto
        {
            Status = new RunStatusDto
            {
                RunId = runId,
                Status = status,
                Recommendation = recommendation,
                Project = "IronDev",
                Title = "Test run",
                TraceId = traceId
            },
            Report = new RunReportDetail
            {
                RunId = runId,
                Project = "IronDev",
                Title = "Test run",
                Status = status,
                Recommendation = recommendation,
                TraceId = traceId,
                Summary = $"{runId} test report.",
                Evidence = [new RunEvidenceItem { Type = "tool-call", Path = "test-results/tester-evidence.json", Summary = "Test evidence" }]
            }
        };

        return RunReportContractMapper.MapToReadResult(RunReportContractMapper.MapFromApiReport(report));
    }
}
