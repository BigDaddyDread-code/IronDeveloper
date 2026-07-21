namespace IronDev.Core.Workbench;

public static class RepositorySetupProfileCompatibility
{
    public static bool IsPinnedWinFormsFactCompatible(string key, string value)
    {
        var normalized = string.Join(' ', value
                .Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        return key switch
        {
            "DesiredLanguage" => normalized is "c#" or "csharp" or "c sharp",
            "DesiredFramework" => normalized is
                ".net 10" or ".net 10.0" or "dotnet 10" or "dotnet 10.0" or
                "net10" or "net10.0" or "net10.0-windows" or
                "winforms" or "windows forms" or ".net 10 winforms" or
                ".net 10 windows forms",
            "ApplicationType" => normalized is
                "winforms" or "windows forms" or ".net winforms" or
                ".net windows forms" or "windows desktop forms",
            "DesiredTestApproach" => normalized is "mstest" or "ms test",
            "TargetPlatform" => normalized is "windows" or "windows desktop",
            _ => false
        };
    }
}
