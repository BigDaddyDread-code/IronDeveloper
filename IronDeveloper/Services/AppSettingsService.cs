using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IronDev.Agent.Services;

public sealed class AppSettings
{
    public string SelectedModel { get; set; } = "gpt-4o";
    public string ApiEndpoint { get; set; } = "https://api.openai.com/v1";
    public bool StreamResponses { get; set; } = true;
    public bool AutoIndex { get; set; }
    public int MaxContextTokens { get; set; } = 8000;
    public bool UseContextAgent { get; set; }
    public bool IsLlmTracingEnabled { get; set; } = true;
    public bool RequireBuilderApplyApproval { get; set; } = true;
}

public interface IAppSettingsService
{
    AppSettings Current { get; }
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettings Current { get; private set; }

    public AppSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IronDev",
            "settings.json");

        var settingsDirectory = Path.GetDirectoryName(_settingsPath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Current = Load(settingsDirectory, _settingsPath);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken);
    }

    private static AppSettings Load(string settingsDirectory, string settingsPath)
    {
        try
        {
            Directory.CreateDirectory(settingsDirectory);
            if (!File.Exists(settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }
}
