using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Data.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectProfileService
{
    Task<ProjectProfile?> GetProjectProfileAsync(int projectId, CancellationToken ct = default);
    Task SaveProjectProfileAsync(ProjectProfile profile, CancellationToken ct = default);
    
    Task<List<ProjectCommand>> GetProjectCommandsAsync(int projectId, CancellationToken ct = default);
    Task SaveProjectCommandAsync(ProjectCommand command, CancellationToken ct = default);
    Task<ProjectCommand?> GetDefaultCommandAsync(int projectId, string commandType, CancellationToken ct = default);
    
    Task<List<ProjectProfileOption>> GetOptionsByCategoryAsync(string category, CancellationToken ct = default);
}
