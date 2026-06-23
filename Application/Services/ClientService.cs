using System.Globalization;
using System.Text;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;

namespace ClientManagementSystem.Services;

/// <summary>Form preparation and persistence against the CMS_PhaseI_All schema. The legacy
/// link tables and <c>usp_Ref_CreateClientList</c> path are retired — <c>tClient</c> carries
/// direct FKs to <c>BusinessClassID</c>, <c>ClientCategoryID</c>, and <c>ClientTypeID</c>,
/// and the directory grid is fed by <c>dbo.vClient_WithBU</c> (via <c>usp_Client_List</c>).</summary>
public sealed class ClientService : IClientService
{
    private const string ClientEntityType = "Client";

    private readonly IClientRepository _clients;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IAdminRepository _admin;
    private readonly IDashboardRepository _dashboard;
    private readonly ILogger<ClientService>? _logger;

    public ClientService(
        IClientRepository clients,
        IReferenceDataRepository referenceData,
        IAdminRepository admin,
        IDashboardRepository dashboard,
        ILogger<ClientService>? logger = null)
    {
        _clients = clients;
        _referenceData = referenceData;
        _admin = admin;
        _dashboard = dashboard;
        _logger = logger;
    }

    /// <summary>Best-effort invocation of <c>dbo.usp_Create_vProject_Current</c> so the
    /// home page client/project list reflects the most recent client write. We never
    /// want a refresh failure to surface as a save failure, so log and swallow.</summary>
    private async Task RefreshHomeListAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dashboard.RefreshClientProjectListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "dbo.usp_Create_vProject_Current refresh failed after a Client write.");
        }
    }

    public async Task<ClientFormViewModel> PrepareCreateFormAsync(ClientFormViewModel? model = null, CancellationToken cancellationToken = default)
    {
        model ??= new ClientFormViewModel();
        await PopulateListsAsync(model, cancellationToken);
        model.ExistingClients = await _clients.ListAsync(cancellationToken: cancellationToken);
        return model;
    }

    public async Task<ClientFormViewModel?> PrepareEditFormAsync(int clientId, CancellationToken cancellationToken = default)
    {
        var client = await _clients.GetAsync(clientId, cancellationToken);
        if (client is null)
        {
            return null;
        }

        var model = new ClientFormViewModel
        {
            ClientID = client.ClientID,
            ClientName = client.ClientName ?? string.Empty,
            ClientShortName = client.ClientShortName,
            BusinessClassID = client.BusinessClassID,
            BusinessUnitID = client.BusinessUnitID ?? 0,
            ClientCategoryID = client.ClientCategoryID,
            ClientTypeID = client.ClientTypeID,
            StatusID = client.StatusID,
            IsDeleted = client.IsDeleted
        };

        await PopulateListsAsync(model, cancellationToken);
        model.ExistingClients = await _clients.ListAsync(cancellationToken: cancellationToken);
        return model;
    }

    public async Task<IReadOnlyList<(string Value, string Text)>> GetBusinessClassOptionsForBusinessUnitAsync(
        int businessUnitId,
        CancellationToken cancellationToken = default)
    {
        if (businessUnitId <= 0)
        {
            return [];
        }

        var rows = await _referenceData.GetBusinessClassesForBusinessUnitAsync(businessUnitId, cancellationToken);
        return rows
            .Select(c =>
            {
                var label = BusinessClass.FormatDisplayDescription(c.BusinessClassName, c.BusinessClassDescription);
                return (c.BusinessClassID.ToString(CultureInfo.InvariantCulture), label ?? $"Business class {c.BusinessClassID}");
            })
            .ToList();
    }

    public async Task<int> SaveAsync(ClientFormViewModel model, CancellationToken cancellationToken = default)
    {
        var client = new Client
        {
            ClientID = model.ClientID ?? 0,
            ClientName = model.ClientName.Trim(),
            ClientShortName = model.ClientShortName?.Trim(),
            BusinessClassID = model.BusinessClassID,
            ClientCategoryID = model.ClientCategoryID,
            ClientTypeID = model.ClientTypeID,
            StatusID = model.StatusID
        };

        var existing = client.ClientID > 0
            ? await _clients.GetAsync(client.ClientID, cancellationToken)
            : null;

        var clientId = client.ClientID > 0
            ? await _clients.UpdateAsync(client, cancellationToken)
            : await _clients.AddAsync(client, cancellationToken);

        // Explicit ActivityLog instrumentation per cross-cutting decision #3:
        // log status transitions on update, and creation/restore on add.
        try
        {
            if (existing is null)
            {
                await _admin.LogActivityAsync(ClientEntityType, clientId, "Add", null, null, client.ClientName, cancellationToken);
            }
            else if (existing.StatusID != client.StatusID)
            {
                await _admin.LogActivityAsync(
                    ClientEntityType,
                    clientId,
                    "StatusChange",
                    nameof(Client.StatusID),
                    existing.StatusID.ToString(CultureInfo.InvariantCulture),
                    client.StatusID.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);
            }
        }
        catch (Exception logEx)
        {
            _logger?.LogWarning(logEx, "Activity log write failed for ClientID {ClientID}.", clientId);
        }

        await RefreshHomeListAsync(cancellationToken);
        return clientId;
    }

    public async Task SoftDeleteAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await _clients.SoftDeleteAsync(clientId, cancellationToken);
        try
        {
            await _admin.LogActivityAsync(ClientEntityType, clientId, "SoftDelete", null, null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Activity log write failed for SoftDelete of ClientID {ClientID}.", clientId);
        }

        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task RestoreAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await _clients.RestoreAsync(clientId, cancellationToken);
        try
        {
            await _admin.LogActivityAsync(ClientEntityType, clientId, "Restore", null, null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Activity log write failed for Restore of ClientID {ClientID}.", clientId);
        }

        await RefreshHomeListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ClientListRow>> ListAsync(string? searchTerm = null, int? statusId = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
        => _clients.ListAsync(searchTerm, statusId, includeDeleted, projectEligibleOnly: false, cancellationToken);

    public async Task<ClientListRow?> GetClientListRowAsync(int clientId, CancellationToken cancellationToken = default)
    {
        if (clientId <= 0)
        {
            return null;
        }

        var client = await _clients.GetAsync(clientId, cancellationToken);
        if (client is null)
        {
            return null;
        }

        return new ClientListRow
        {
            ClientID = client.ClientID,
            ClientName = client.ClientName,
            ClientShortName = client.ClientShortName,
            BusinessUnitID = client.BusinessUnitID,
            BusinessUnitName = client.BusinessUnitName,
            BusinessClassID = client.BusinessClassID,
            BusinessClassName = BusinessClass.FormatDisplayDescription(client.BusinessClassName, client.BusinessClassDescription),
            BusinessClassDescription = client.BusinessClassDescription,
            ClientCategoryID = client.ClientCategoryID > 0 ? client.ClientCategoryID : null,
            CCName = client.CCName,
            ClientTypeID = client.ClientTypeID > 0 ? client.ClientTypeID : null,
            CTName = client.CTName,
            CTDescription = client.CTDescription,
            StatusID = client.StatusID > 0 ? client.StatusID : null,
            StatusCode = client.StatusCode,
            StatusDescription = client.StatusDescription,
            CreatedDate = client.CreatedDate,
            ModifiedDate = client.ModifiedDate
        };
    }

    public async Task<ClientBulkParseResult> ParseImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("The CSV file is empty or missing a header row.");
        }

        var headerCells = CsvRowParser.SplitLine(headerLine);
        var columnIndex = BuildClientImportHeaderMap(headerCells);
        if (!columnIndex.TryGetValue("ClientName", out var clientNameCol))
        {
            throw new InvalidOperationException("The CSV must include a ClientName column.");
        }

        var rows = new List<ClientImportRowDto>();
        var skippedEmpty = 0;
        var lineNumber = 1;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = CsvRowParser.SplitLine(line);
            var clientName = GetCell(cells, clientNameCol)?.Trim();
            if (string.IsNullOrWhiteSpace(clientName))
            {
                skippedEmpty++;
                continue;
            }

            rows.Add(new ClientImportRowDto
            {
                LineNumber = lineNumber,
                ClientName = Truncate(clientName, 200) ?? string.Empty,
                ClientShortName = Truncate(OptionalCell(cells, columnIndex, "ClientShortName")?.Trim(), 50) ?? string.Empty,
                BusinessClassID = ParseOptionalIntFromCsv(cells, columnIndex, "BusinessClassID", 0),
                ClientCategoryID = ParseOptionalIntFromCsv(cells, columnIndex, "ClientCategoryID", 0),
                ClientTypeID = ParseOptionalIntFromCsv(cells, columnIndex, "ClientTypeID", 0),
                StatusID = ParseOptionalIntFromCsv(cells, columnIndex, "StatusID", 1)
            });
        }

        return new ClientBulkParseResult { Rows = rows, SkippedEmptyRows = skippedEmpty };
    }

    public async Task<ClientBulkVerifyResult> VerifyImportRowsAsync(
        IReadOnlyList<ClientImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (rows is null || rows.Count == 0)
        {
            errors.Add("There are no rows to verify. Choose a CSV file with at least one data row.");
            return new ClientBulkVerifyResult { Valid = false, Errors = errors };
        }

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.ClientName))
            {
                errors.Add($"Line {r.LineNumber}: ClientName is required.");
            }
            else if (r.ClientName.Trim().Length > 200)
            {
                errors.Add($"Line {r.LineNumber}: ClientName exceeds 200 characters.");
            }

            if ((r.ClientShortName ?? string.Empty).Length > 50)
            {
                errors.Add($"Line {r.LineNumber}: ClientShortName exceeds 50 characters.");
            }
        }

        var duplicateMessages = rows
            .GroupBy(r => (r.ClientName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
            .Select(g =>
                $"Duplicate ClientName \"{g.Key}\" on lines: {string.Join(", ", g.Select(x => x.LineNumber).Order())}.")
            .ToList();
        errors.AddRange(duplicateMessages);

        var statuses = (await _referenceData.GetStatusesForEntityAsync(ClientEntityType, cancellationToken))
            .Select(s => s.StatusID)
            .ToHashSet();
        var businessClasses = (await _referenceData.GetBusinessClassesAsync(cancellationToken))
            .Select(s => s.BusinessClassID)
            .ToHashSet();
        var clientCategories = (await _referenceData.GetClientCategoriesAsync(cancellationToken: cancellationToken))
            .Select(s => s.ClientCategoryID)
            .ToHashSet();
        var clientTypes = (await _referenceData.GetClientTypesAsync(cancellationToken: cancellationToken))
            .Select(s => s.ClientTypeID)
            .ToHashSet();

        foreach (var r in rows)
        {
            if (!statuses.Contains(r.StatusID))
            {
                errors.Add($"Line {r.LineNumber}: StatusID {r.StatusID} is not a valid Client status.");
            }

            if (!businessClasses.Contains(r.BusinessClassID))
            {
                errors.Add($"Line {r.LineNumber}: BusinessClassID {r.BusinessClassID} is not defined.");
            }

            if (!clientCategories.Contains(r.ClientCategoryID))
            {
                errors.Add($"Line {r.LineNumber}: ClientCategoryID {r.ClientCategoryID} is not defined.");
            }

            if (!clientTypes.Contains(r.ClientTypeID))
            {
                errors.Add($"Line {r.LineNumber}: ClientTypeID {r.ClientTypeID} is not defined.");
            }
        }

        return new ClientBulkVerifyResult { Valid = errors.Count == 0, Errors = errors };
    }

    public async Task<ClientBulkImportResult> CommitImportRowsAsync(
        IReadOnlyList<ClientImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var verify = await VerifyImportRowsAsync(rows, cancellationToken);
        if (!verify.Valid)
        {
            throw new InvalidOperationException(string.Join(' ', verify.Errors));
        }

        var clients = rows.Select(DtoToClient).ToList();
        await _clients.BulkInsertAsync(clients, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);

        return new ClientBulkImportResult
        {
            ImportedCount = clients.Count,
            SkippedEmptyRows = 0,
            RowErrors = []
        };
    }

    private static Client DtoToClient(ClientImportRowDto d) => new()
    {
        ClientID = 0,
        ClientName = d.ClientName.Trim(),
        ClientShortName = Truncate(string.IsNullOrWhiteSpace(d.ClientShortName) ? null : d.ClientShortName.Trim(), 50),
        BusinessClassID = d.BusinessClassID,
        ClientCategoryID = d.ClientCategoryID,
        ClientTypeID = d.ClientTypeID,
        StatusID = d.StatusID
    };

    private static string? GetCell(IReadOnlyList<string> cells, int columnIndex)
        => columnIndex < 0 || columnIndex >= cells.Count ? null : cells[columnIndex];

    private static string? OptionalCell(
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> columnIndex,
        string columnName)
        => columnIndex.TryGetValue(columnName, out var idx) ? GetCell(cells, idx) : null;

    private static int ParseOptionalIntFromCsv(
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> columnIndex,
        string columnName,
        int defaultWhenMissingOrEmpty)
    {
        if (!columnIndex.TryGetValue(columnName, out var idx))
        {
            return defaultWhenMissingOrEmpty;
        }

        var raw = GetCell(cells, idx);
        if (raw is null || string.IsNullOrWhiteSpace(raw.Trim()))
        {
            return defaultWhenMissingOrEmpty;
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
    }

    private static Dictionary<string, int> BuildClientImportHeaderMap(IReadOnlyList<string> headerCells)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ClientName",
            "ClientShortName",
            "BusinessClassID",
            "ClientCategoryID",
            "ClientTypeID",
            "StatusID"
        };

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerCells.Count; i++)
        {
            var key = headerCells[i].Trim().TrimStart('\uFEFF');
            if (allowed.Contains(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private async Task PopulateListsAsync(ClientFormViewModel model, CancellationToken cancellationToken)
    {
        var statuses = await _referenceData.GetStatusesForEntityAsync(ClientEntityType, cancellationToken);
        var businessUnits = await _referenceData.GetBusinessUnitsAsync(activeOnly: true, cancellationToken);
        var businessClasses = model.BusinessUnitID > 0
            ? await _referenceData.GetBusinessClassesForBusinessUnitAsync(model.BusinessUnitID, cancellationToken)
            : [];
        var clientCategories = await _referenceData.GetClientCategoriesAsync(activeOnly: true, cancellationToken);
        var clientTypes = await _referenceData.GetClientTypesAsync(activeOnly: true, cancellationToken);

        if (model.StatusID == 0)
        {
            model.StatusID = statuses.FirstOrDefault(s => string.Equals(s.StatusCode, "A", StringComparison.OrdinalIgnoreCase))?.StatusID ?? 0;
        }

        model.StatusOptions = statuses
            .Select(s => new SelectListItem(s.DisplayName, s.StatusID.ToString(CultureInfo.InvariantCulture), s.StatusID == model.StatusID))
            .ToList();

        model.BusinessUnitOptions = businessUnits
            .Select(b => new SelectListItem(b.BusinessUnitName, b.BusinessUnitID.ToString(CultureInfo.InvariantCulture), b.BusinessUnitID == model.BusinessUnitID))
            .ToList();

        model.BusinessClassOptions = businessClasses
            .Select(c =>
            {
                var label = BusinessClass.FormatDisplayDescription(c.BusinessClassName, c.BusinessClassDescription);
                return new SelectListItem(label, c.BusinessClassID.ToString(CultureInfo.InvariantCulture), c.BusinessClassID == model.BusinessClassID);
            })
            .ToList();

        model.ClientCategoryOptions = clientCategories
            .Select(c => new SelectListItem(c.DisplayName, c.ClientCategoryID.ToString(CultureInfo.InvariantCulture), c.ClientCategoryID == model.ClientCategoryID))
            .ToList();

        model.ClientTypeOptions = clientTypes
            .Select(c => new SelectListItem(c.DisplayName, c.ClientTypeID.ToString(CultureInfo.InvariantCulture), c.ClientTypeID == model.ClientTypeID))
            .ToList();
    }

    private static string? Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= length ? value : value[..length];
    }
}
