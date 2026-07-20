using IronDev.Core.Workbench;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.UnitTests;

[TestClass]
public sealed class RepositorySetupPlanningTests
{
    [TestMethod]
    [DataRow("DesiredLanguage", "C#")]
    [DataRow("DesiredLanguage", "csharp")]
    [DataRow("DesiredFramework", ".NET 10")]
    [DataRow("DesiredFramework", "net10.0-windows")]
    [DataRow("ApplicationType", "Windows Forms")]
    [DataRow("DesiredTestApproach", "MSTest")]
    [DataRow("TargetPlatform", "Windows desktop")]
    public void Compatibility_AllowsOnlyExplicitPinnedVocabulary(string key, string value) =>
        Assert.IsTrue(RepositorySetupProfileCompatibility.IsPinnedWinFormsFactCompatible(key, value));

    [TestMethod]
    [DataRow("DesiredLanguage", "C")]
    [DataRow("DesiredLanguage", "C++")]
    [DataRow("DesiredFramework", ".NET 8")]
    [DataRow("DesiredFramework", ".NET MAUI")]
    [DataRow("ApplicationType", "WPF")]
    [DataRow("DesiredTestApproach", "xUnit")]
    [DataRow("TargetPlatform", "Linux")]
    public void Compatibility_RejectsUnsupportedTechnologyWithoutSubstitution(string key, string value) =>
        Assert.IsFalse(RepositorySetupProfileCompatibility.IsPinnedWinFormsFactCompatible(key, value));

    [TestMethod]
    public void TemplateBundleHash_IsCanonicalAndSensitiveToAuthorityBytes()
    {
        var original = ValidBundle();
        var hash = RepositorySetupTemplateBundleCodec.ComputeHash(original);
        Assert.AreEqual(64, hash.Length);
        Assert.IsTrue(hash.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f'));

        var reorderedEnumeration = original with { Files = original.Files.Reverse().ToArray() };
        Assert.AreEqual(hash, RepositorySetupTemplateBundleCodec.ComputeHash(reorderedEnumeration));

        var changedContent = original with
        {
            Files = original.Files.Select(value => value.Order == 2
                ? value with { Utf8Content = "changed\n" }
                : value).ToArray()
        };
        var changedPath = original with
        {
            Files = original.Files.Select(value => value.Order == 2
                ? value with { RelativePath = "src/other.txt" }
                : value).ToArray()
        };
        var changedOrder = original with
        {
            Files = original.Files.Select(value => value.Order == 2
                ? value with { Order = 3 }
                : value).ToArray()
        };
        var changedProfile = original with { ProfileDefinitionId = "another-profile" };
        Assert.AreNotEqual(hash, RepositorySetupTemplateBundleCodec.ComputeHash(changedContent));
        Assert.AreNotEqual(hash, RepositorySetupTemplateBundleCodec.ComputeHash(changedPath));
        Assert.AreNotEqual(hash, RepositorySetupTemplateBundleCodec.ComputeHash(changedOrder));
        Assert.AreNotEqual(hash, RepositorySetupTemplateBundleCodec.ComputeHash(changedProfile));
        Assert.ThrowsException<RepositorySetupIntegrityException>(() =>
            RepositorySetupTemplateBundleCodec.ComputeHash(original with { SchemaVersion = 2 }));
    }

    [TestMethod]
    [DataRow("../escape.txt")]
    [DataRow("/rooted.txt")]
    [DataRow("C:/device.txt")]
    [DataRow("folder\\file.txt")]
    [DataRow(".git/config")]
    [DataRow("CON/file.txt")]
    [DataRow("folder./file.txt")]
    [DataRow("folder /file.txt")]
    [DataRow("folder/file?.txt")]
    [DataRow("folder/file*.txt")]
    public void TemplateBundle_RejectsUnsafeWindowsPath(string path)
    {
        var bundle = ValidBundle() with
        {
            Files = [new RepositorySetupTemplateFileDefinition(1, path, "safe\n")]
        };
        Assert.ThrowsException<RepositorySetupIntegrityException>(() =>
            RepositorySetupTemplateBundleCodec.ComputeHash(bundle));
    }

    [TestMethod]
    public void TemplateBundle_RejectsDuplicateCaseInsensitivePathOrderAndInvalidContent()
    {
        AssertInvalid([
            new(1, "src/File.txt", "safe\n"),
            new(2, "src/file.txt", "safe\n")
        ]);
        AssertInvalid([
            new(1, "src/a.txt", "safe\n"),
            new(1, "src/b.txt", "safe\n")
        ]);
        AssertInvalid([new(1, "src/a.txt", "bad\r\n")]);
        AssertInvalid([new(1, "src/a.txt", "missing final LF")]);
        AssertInvalid([new(1, "src/a.txt", "{{UNKNOWN_TOKEN}}\n")]);
        AssertInvalid([new(1, "src/a.txt", "\uFEFFbom\n")]);
        AssertInvalid([new(1, "src/a.txt", "\uD800\n")]);
    }

    [TestMethod]
    public void TemplateRenderer_UsesOnlyConfirmedServerIdentifiersAndInvariantLowerToken()
    {
        var tokenized = new RepositorySetupTemplateBundle(
            1,
            "profile",
            [new RepositorySetupTemplateFileDefinition(
                1,
                "src/{{APP_PROJECT_NAME}}/lock.txt",
                "{{SOLUTION_NAME}}|{{APP_PROJECT_NAME}}|{{APP_PROJECT_NAME_LOWER}}|{{TEST_PROJECT_NAME}}\n")]);
        var plan = PlanFor(tokenized);
        var rendered = RepositorySetupTemplateBundleRenderer.Render(tokenized, plan);
        var file = rendered.Files.Single();
        Assert.AreEqual("src/Sample.App/lock.txt", file.RelativePath);
        Assert.AreEqual("Sample|Sample.App|sample.app|Sample.Tests\n", file.Utf8Content);
        Assert.AreEqual(0, RepositorySetupTemplateBundleRenderer.Utf8NoBomStrict.GetPreamble().Length);
    }

    private static RepositorySetupTemplateBundle ValidBundle() => new(
        1,
        "profile",
        [
            new RepositorySetupTemplateFileDefinition(1, "global.json", "{}\n"),
            new RepositorySetupTemplateFileDefinition(2, "src/{{APP_PROJECT_NAME}}/file.txt", "content\n")
        ]);

    private static void AssertInvalid(IReadOnlyList<RepositorySetupTemplateFileDefinition> files)
    {
        var bundle = new RepositorySetupTemplateBundle(1, "profile", files);
        Assert.ThrowsException<RepositorySetupIntegrityException>(() =>
            RepositorySetupTemplateBundleCodec.ComputeHash(bundle));
    }

    private static RepositorySetupPlanPreview PlanFor(RepositorySetupTemplateBundle bundle)
    {
        var profile = new RepositorySetupProfileSummary(
            "profile", "Profile", RepositoryProfileCompatibilityStates.NoPreference,
            "No preference", RepositoryPlanningReadinessStates.PreviewPlanningOnly,
            RepositoryProfileCertificationStates.NotCertificationReady,
            new string('a', 64), RepositorySetupTemplateBundleCodec.ComputeHash(bundle));
        return new RepositorySetupPlanPreview(
            1, "ProjectUnderstanding", 1, "Sample", 10, 1, 1, new string('b', 64),
            1, new string('a', 64), RepositorySetupPreviewStates.ReadyForConfirmation,
            RepositorySetupReasonCodes.Ready, "Ready", profile, "C:\\IronDevTest\\sample-1",
            "Sample", "Sample.App", "Sample.Tests", "Sample.slnx",
            "src/Sample.App/Sample.App.csproj", "tests/Sample.Tests/Sample.Tests.csproj",
            profile.TemplateBundleSha256, new string('c', 64), "net10.0-windows", "C#",
            "WinForms", "MSTest", "10.0.302", "10.0.10", "restore", "build", "test",
            "toolchain", "image", "main", true, true, "sandbox", "resource", string.Empty);
    }
}
