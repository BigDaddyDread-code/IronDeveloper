using IronDev.Core.Security;

namespace IronDev.UnitTests;

[TestClass]
public sealed class SensitiveDataBoundaryTests
{
    [TestMethod]
    public void Redactor_RemovesCredentialsTokensPrivateKeysAndLocalPaths()
    {
        var input = "Password=top-secret Bearer abc.def.ghi sk-provider-secret " +
                    "C:\\Users\\bob\\source\\repo /home/runner/work/repo " +
                    "-----BEGIN PRIVATE KEY-----private-material-----END PRIVATE KEY-----";

        var result = SensitiveDataRedactor.Redact(input);

        Assert.IsFalse(result.Contains("top-secret", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("abc.def.ghi", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("sk-provider-secret", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("bob", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("runner", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("private-material", StringComparison.Ordinal));
        StringAssert.Contains(result, SensitiveDataRedactor.RedactedValue);
        StringAssert.Contains(result, SensitiveDataRedactor.LocalPathValue);
    }
}
