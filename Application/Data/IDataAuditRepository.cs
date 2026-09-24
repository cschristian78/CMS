using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface IDataAuditRepository
{
    Task<DataAuditReport> GetReportAsync(CancellationToken cancellationToken = default);

    Task<DataAuditExportReport> GetExportAsync(CancellationToken cancellationToken = default);

    /// <summary>Static catalog of audit checks (no SQL executed).</summary>
    IReadOnlyList<DataAuditCheckDescriptor> ListCheckCatalog();

    /// <summary>Run a single audit check by id (same queries as the Admin Data Audit page).</summary>
    Task<DataAuditCheck?> GetCheckAsync(string checkId, CancellationToken cancellationToken = default);
}
