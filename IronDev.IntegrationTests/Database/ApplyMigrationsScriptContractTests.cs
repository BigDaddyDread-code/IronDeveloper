using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

/// <summary>
/// DEMO-REHEARSAL-001 residual R2, review-narrowed: the migration script's
/// generated connection is unencrypted ONLY for explicit local developer
/// targets (LocalDB, localhost, 127.0.0.1, .). Every non-local generated
/// connection stays encrypted by default. These tests run the real script
/// through its -ResolveConnectionStringOnly seam, so they pin behavior,
/// not source text.
/// </summary>
[TestClass]
[TestCategory("Contract")]
[TestCategory("Boundary")]
public sealed class ApplyMigrationsScriptContractTests
{
    [TestMethod]
    public void ApplyMigrations_LocalDb_DefaultsUnencrypted()
    {
        foreach (var server in new[] { @"(localdb)\MSSQLLocalDB", "localhost", "localhost,1433", "127.0.0.1", ".", @".\SQLEXPRESS" })
        {
            var connectionString = ResolveConnectionString("-Server", server, "-Database", "IronDev_Contract_Probe");

            StringAssert.Contains(connectionString, "Encrypt=False", $"Local developer target '{server}' must default to an unencrypted connection (legacy SqlClient cannot encrypt to LocalDB).");
            StringAssert.Contains(connectionString, "TrustServerCertificate=False", $"Local developer target '{server}' must not trust-server-certificate.");
        }
    }

    [TestMethod]
    public void ApplyMigrations_RemoteServer_DefaultsEncrypted()
    {
        foreach (var server in new[] { "sql.example.internal", "remote-sql,1433", @"remote-sql\NAMED", "localhost2.example.com", "127.0.0.10" })
        {
            var connectionString = ResolveConnectionString("-Server", server, "-Database", "IronDev_Contract_Probe");

            StringAssert.Contains(connectionString, "Encrypt=True", $"Non-local server '{server}' must stay encrypted by default. The R2 LocalDB fix must never widen into a generic downgrade.");
            StringAssert.Contains(connectionString, "TrustServerCertificate=False", $"Non-local server '{server}' must not silently trust the server certificate.");
        }
    }

    [TestMethod]
    public void ApplyMigrations_TrustServerCertificate_EnablesEncryptedTrustServerCertificate()
    {
        var connectionString = ResolveConnectionString("-Server", "sql.example.internal", "-Database", "IronDev_Contract_Probe", "-TrustServerCertificate");

        StringAssert.Contains(connectionString, "Encrypt=True", "-TrustServerCertificate means encrypted + trusted certificate, never unencrypted.");
        StringAssert.Contains(connectionString, "TrustServerCertificate=True", "-TrustServerCertificate must trust the server certificate on the encrypted connection.");
    }

    [TestMethod]
    public void ApplyMigrations_EncryptionContract_ExecutesInCi()
    {
        // Selection is not execution: this class must be executed by exact name
        // in a CI lane, not merely selected by category.
        var ciScript = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "ci", "run-governance-boundary-ci.ps1"));

        StringAssert.Contains(ciScript, "FullyQualifiedName~ApplyMigrationsScriptContractTests");
    }

    private static string ResolveConnectionString(params string[] arguments)
    {
        var script = Path.Combine(RepoRoot(), "Database", "apply-migrations.ps1");
        var shell = ResolvePowerShell();
        var startInfo = new ProcessStartInfo(shell)
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("-ResolveConnectionStringOnly");

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Assert.IsTrue(process.WaitForExit(TimeSpan.FromSeconds(60)), "apply-migrations.ps1 -ResolveConnectionStringOnly timed out.");
        Task.WaitAll(stdoutTask, stderrTask);
        Assert.AreEqual(0, process.ExitCode, $"apply-migrations.ps1 -ResolveConnectionStringOnly failed: {stdoutTask.Result}{stderrTask.Result}");
        return stdoutTask.Result.Trim();
    }

    private static string ResolvePowerShell()
    {
        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(candidate, "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                if (process is not null && process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // Try the next shell.
            }
        }

        Assert.Fail("PowerShell executable not found.");
        return "powershell";
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IronDev.slnx.");
    }
}
