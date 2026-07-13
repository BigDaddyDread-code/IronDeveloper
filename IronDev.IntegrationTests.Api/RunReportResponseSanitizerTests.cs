using IronDev.Api.Security;
using IronDev.Core.RunReports;
using IronDev.Core.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class RunReportResponseSanitizerTests
{
    [TestMethod]
    public void RunReportResponse_ExposesOnlyBoundedReferencesAndRedactedEventPayloads()
    {
        var report = new RunReportDetail
        {
            RunId = "run-1",
            Summary = "token=private-token",
            WorkspacePath = "C:\\Users\\bob\\workspaces\\run-1",
            ReportPath = "C:\\Users\\bob\\runs\\run-1\\report.json",
            Evidence =
            [
                new RunEvidenceItem
                {
                    Type = "log",
                    Path = "C:\\Users\\bob\\runs\\run-1\\logs\\build.log",
                    Summary = "Bearer private-bearer"
                },
                new RunEvidenceItem
                {
                    Type = "external",
                    Path = "C:\\private\\outside.log",
                    Summary = "Outside reference"
                },
                new RunEvidenceItem
                {
                    Type = "traversal",
                    Path = "logs/../../private.txt",
                    Summary = "Malformed relative reference"
                }
            ]
        };

        var result = RunReportResponseSanitizer.Sanitize(report);
        var runEvent = RunReportResponseSanitizer.Sanitize(new RunEventDto
        {
            RunId = "run-1",
            EventType = "ModelCalled",
            Payload = new Dictionary<string, string>
            {
                ["workspacePath"] = "C:\\Users\\bob\\workspaces\\run-1",
                ["rawPrompt"] = "private project prompt",
                ["status"] = "token=private-token"
            }
        });

        Assert.AreEqual("run-1", result.WorkspacePath);
        Assert.AreEqual("report.json", result.ReportPath);
        Assert.AreEqual("logs/build.log", result.Evidence[0].Path);
        Assert.AreEqual("outside.log", result.Evidence[1].Path);
        Assert.AreEqual("private.txt", result.Evidence[2].Path);
        Assert.IsFalse(result.Summary.Contains("private-token", StringComparison.Ordinal));
        Assert.IsFalse(result.Evidence[0].Summary.Contains("private-bearer", StringComparison.Ordinal));
        Assert.AreEqual("run-1", runEvent.Payload["workspacePath"]);
        Assert.AreEqual(SensitiveDataRedactor.RedactedValue, runEvent.Payload["rawPrompt"]);
        Assert.IsFalse(runEvent.Payload["status"].Contains("private-token", StringComparison.Ordinal));
    }
}
