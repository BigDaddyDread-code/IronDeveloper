using System.Threading.Tasks;

namespace IronDev.Services;

public interface IProjectContextExportService
{
    Task<string> ExportProjectContextPackAsync(int projectId);
}
