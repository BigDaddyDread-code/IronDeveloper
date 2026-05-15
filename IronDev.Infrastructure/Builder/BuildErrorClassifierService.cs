using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Builder;

public class BuildErrorClassifierService : IBuildErrorClassifierService
{
    public Task<BuildArchitectureReconciliation?> ClassifyBuildFailureAsync(
        DotNetBuildResult buildResult,
        ProjectProfile profile,
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (buildResult.Succeeded) return Task.FromResult<BuildArchitectureReconciliation?>(null);

        var output = buildResult.StandardOutput + "\n" + buildResult.StandardError;
        if (string.IsNullOrWhiteSpace(output)) return Task.FromResult<BuildArchitectureReconciliation?>(null);

        // 1. Test Framework Mismatch (xUnit)
        if (output.Contains("CS0246") && (output.Contains("Xunit") || output.Contains("FactAttribute") || output.Contains("Fact")))
        {
            var r = new BuildArchitectureReconciliation
            {
                FailureCategory = "TestFrameworkMismatch",
                Summary = "Generated tests use xUnit, but the test project does not reference xUnit.",
                Evidence = "Build output contains CS0246 for Xunit or FactAttribute.",
                CurrentProfileValue = $"TestFramework: {profile.TestFramework ?? "Unknown"}",
                DetectedProjectFacts = "xUnit package reference missing",
                RecommendedAction = "AskUser",
                RequiresUserApproval = true
            };
            r.Questions.Add("Should the project use xUnit and add package references?");
            r.Questions.Add("Or should the tests be regenerated using the existing project test framework?");
            
            r.AvailableActions.Add(new ReconciliationAction { Title = "Add xUnit package to project", ActionType = "AddPackage_xUnit" });
            r.AvailableActions.Add(new ReconciliationAction { Title = "Regenerate tests", ActionType = "RegenerateTests" });
            r.AvailableActions.Add(new ReconciliationAction { Title = "Update Project Profile only", ActionType = "UpdateProfileOnly" });
            r.AvailableActions.Add(new ReconciliationAction { Title = "Cancel", ActionType = "Cancel" });

            return Task.FromResult<BuildArchitectureReconciliation?>(r);
        }

        // Add more categories as needed...

        return Task.FromResult<BuildArchitectureReconciliation?>(null);
    }
}
