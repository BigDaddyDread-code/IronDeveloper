using System.Collections.Generic;

namespace IronDev.Data.Models;

public sealed class ProjectProfileDetectionResult
{
    public ProjectProfile Profile { get; set; } = new();
    public ProjectCommand BuildCommand { get; set; } = new() { CommandType = "Build" };
    public ProjectCommand TestCommand { get; set; } = new() { CommandType = "Test" };
    public List<string> DetectedFacts { get; } = new();
    public List<string> Warnings { get; } = new();
}
