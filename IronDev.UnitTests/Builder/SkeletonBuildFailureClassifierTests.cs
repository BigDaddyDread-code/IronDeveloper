using IronDev.Core.Builder;
using IronDev.Core.Workspaces;

namespace IronDev.UnitTests.Builder;

/// <summary>
/// REPAIR-1 — the failure classifier is pure evidence shaping. It names what
/// failed and carries a bounded excerpt; it grants nothing and decides nothing.
/// </summary>
[TestClass]
public sealed class SkeletonBuildFailureClassifierTests
{
    [TestMethod]
    public void ClassifiesFailedBuildCommand()
    {
        var classification = SkeletonBuildFailureClassifier.Classify(
        [
            Command("dotnet build", exitCode: 1, stdout: "Book.cs(5,1): error CS1002: ; expected")
        ]);

        Assert.AreEqual(SkeletonBuildFailureKind.BuildFailed, classification.Kind);
        Assert.AreEqual("dotnet build", classification.FailedCommand);
        StringAssert.Contains(classification.Excerpt, "error CS1002");
    }

    [TestMethod]
    public void ClassifiesFailedTestCommand()
    {
        var classification = SkeletonBuildFailureClassifier.Classify(
        [
            Command("dotnet build", exitCode: 0, stdout: "Build succeeded."),
            Command("dotnet test", exitCode: 1, stdout: "Failed PriceFor_BulkOrder_AppliesDiscount")
        ]);

        Assert.AreEqual(SkeletonBuildFailureKind.TestsFailed, classification.Kind);
        Assert.AreEqual("dotnet test", classification.FailedCommand);
    }

    [TestMethod]
    public void ClassifiesTimedOutCommand()
    {
        var classification = SkeletonBuildFailureClassifier.Classify(
        [
            Command("dotnet build", exitCode: 0, stdout: "ok"),
            Command("dotnet test", exitCode: -1, stdout: "", timedOut: true)
        ]);

        Assert.AreEqual(SkeletonBuildFailureKind.CommandTimedOut, classification.Kind);
    }

    [TestMethod]
    public void UnknownWhenNoCommandFailed()
    {
        var classification = SkeletonBuildFailureClassifier.Classify(
        [
            Command("dotnet build", exitCode: 0, stdout: "ok")
        ]);

        Assert.AreEqual(SkeletonBuildFailureKind.Unknown, classification.Kind);
    }

    [TestMethod]
    public void ExcerptPrefersErrorLinesAndStaysBounded()
    {
        var noise = string.Join('\n', Enumerable.Repeat("Compiling something unremarkable...", 500));
        var classification = SkeletonBuildFailureClassifier.Classify(
        [
            Command("dotnet build", exitCode: 1, stdout: noise + "\nBook.cs(9,1): error CS0103: The name 'x' does not exist")
        ]);

        StringAssert.Contains(classification.Excerpt, "error CS0103");
        Assert.IsFalse(classification.Excerpt.Contains("unremarkable"), "Error lines are preferred over build noise.");
        Assert.IsTrue(classification.Excerpt.Length <= 4000, "The excerpt is bounded evidence, not a log dump.");
    }

    private static DisposableWorkspaceCommandResult Command(string displayName, int exitCode, string stdout, bool timedOut = false) => new()
    {
        DisplayName = displayName,
        FileName = "dotnet",
        Arguments = displayName.Split(' ').Skip(1).ToArray(),
        ExitCode = exitCode,
        StandardOutput = stdout,
        TimedOut = timedOut
    };
}
