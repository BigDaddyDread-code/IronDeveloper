using System.Threading;
using System.Threading.Tasks;
using IronDev.Data.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectProfileDetectionService
{
    Task<ProjectProfileDetectionResult> DetectAsync(
        string projectRoot,
        int projectId = 0,
        CancellationToken ct = default);
}
