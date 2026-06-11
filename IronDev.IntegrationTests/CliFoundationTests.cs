using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IronDev.Cli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CliFoundationTests
{
    [TestMethod]
    public async Task IronDevCli_Help_Succeeds()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "--help" },
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(output.ToString().Contains("irondev api ping", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task IronDevCli_Version_Succeeds()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "--version" },
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(output.ToString()));
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task IronDevCli_UnknownCommand_ReturnsUsageError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCli.RunAsync(
            new[] { "missing-command" },
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreNotEqual(0, exitCode);
        Assert.IsTrue(error.ToString().Contains("unknown command", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task IronDevCli_ApiPing_MissingApiBaseUrl_ReturnsConfigError()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliFoundation.HandleApiPingAsync(
            new[] { "api", "ping" },
            output,
            error,
            EmptyEnvironment(),
            new RecordingHandler(HttpStatusCode.OK, "{\"status\":\"ok\"}"),
            CancellationToken.None);

        Assert.AreEqual(IronDevCliFoundation.ConfigError, exitCode);
        Assert.IsTrue(error.ToString().Contains("IRONDEV_CLI_API_BASE_URL_REQUIRED", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public async Task IronDevCli_ConfigShow_OutputMode_RespectsEnvironment()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await IronDevCliFoundation.HandleConfigShowAsync(
            new[] { "config", "show" },
            output,
            error,
            new Dictionary<string, string?>
            {
                ["IRONDEV_OUTPUT"] = "json",
                ["IRONDEV_API_BASE_URL"] = "https://api.example.test",
                ["IRONDEV_API_TOKEN"] = "secret-token"
            },
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("config show", document.RootElement.GetProperty("command").GetString());
        Assert.IsTrue(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.IsTrue(document.RootElement.GetProperty("data").GetProperty("tokenConfigured").GetBoolean());
        Assert.IsFalse(output.ToString().Contains("secret-token", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task IronDevCli_ApiPing_FlagOutputOverridesEnvironment()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"status\":\"ok\"}");

        var exitCode = await IronDevCliFoundation.HandleApiPingAsync(
            new[] { "api", "ping", "--output", "json", "--api-base-url", "https://api.example.test" },
            output,
            error,
            new Dictionary<string, string?>
            {
                ["IRONDEV_OUTPUT"] = "text"
            },
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("api ping", document.RootElement.GetProperty("command").GetString());
        Assert.IsTrue(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task IronDevCli_ApiPing_UsesBaseUrlAndAttachesTokenWithoutPrintingToken()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"status\":\"ok\"}");

        var exitCode = await IronDevCliFoundation.HandleApiPingAsync(
            new[] { "api", "ping", "--api-base-url", "https://api.example.test/root", "--token", "secret-token" },
            output,
            error,
            EmptyEnvironment(),
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("https://api.example.test/root/health", handler.RequestUri?.ToString());
        Assert.AreEqual("Bearer", handler.Authorization?.Scheme);
        Assert.AreEqual("secret-token", handler.Authorization?.Parameter);
        Assert.IsFalse(output.ToString().Contains("secret-token", StringComparison.Ordinal));
        Assert.IsFalse(error.ToString().Contains("secret-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task IronDevCli_ApiPing_NonSuccessApiResponse_ReturnsFailure()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable, "{\"status\":\"down\"}");

        var exitCode = await IronDevCliFoundation.HandleApiPingAsync(
            new[] { "api", "ping", "--api-base-url", "https://api.example.test", "--output", "json" },
            output,
            error,
            EmptyEnvironment(),
            handler,
            CancellationToken.None);

        Assert.AreEqual(IronDevCliFoundation.ApiFailure, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.IsFalse(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("failed", document.RootElement.GetProperty("status").GetString());
        Assert.AreEqual("IRONDEV_API_NON_SUCCESS", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public async Task IronDevCli_ApiPing_PreservesWarnings()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"status\":\"ok\",\"warnings\":[\"using degraded cache\"]}");

        var exitCode = await IronDevCliFoundation.HandleApiPingAsync(
            new[] { "api", "ping", "--api-base-url", "https://api.example.test", "--output", "json" },
            output,
            error,
            EmptyEnvironment(),
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("using degraded cache", document.RootElement.GetProperty("warnings")[0].GetString());
        Assert.AreEqual(string.Empty, error.ToString());
    }

    [TestMethod]
    public void IronDevCli_FoundationFiles_DoNotReferenceBackendRuntimeServices()
    {
        var root = LocateRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "CliFoundation.cs"));
        var forbidden = new[]
        {
            "ISupervisorAgentRunService",
            "IDisposableWorkspace",
            "AgentSkillExecution",
            "ManualDogfood",
            "ApplyCopy",
            "MemoryPromotion",
            "SqlConnection",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "IHostedService",
            "BackgroundService"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Forbidden token found: {token}");
    }

    private static IReadOnlyDictionary<string, string?> EmptyEnvironment()
        => new Dictionary<string, string?>();

    private static string LocateRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IronDev.slnx")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent is null)
                break;

            current = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
