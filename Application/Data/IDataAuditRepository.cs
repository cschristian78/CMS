using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface IDataAuditRepository
{
    Task<DataAuditReport> GetReportAsync(CancellationToken cancellationToken = default);
}
