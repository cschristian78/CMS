using System.Globalization;
using System.Text;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

/// <summary>Parse, verify, and commit Home page client + project CSV bulk imports.</summary>
public sealed class HomeClientProjectBulkImportService
{
    private const string ProjectEntityType = "Project";
    private const int ClientNameMaxLength = 150;
    private const int ClientShortNameMaxLength = 50;
    private const int ClientDescriptionMaxLength = 500;
    private const int ProjectNumberMaxLength = 50;
    private const int ProjectNameMaxLength = 200;

    private static readonly string[] AllowedClientStatuses = ["Active", "Inactive", "Prospect"];

    private readonly IAdminService _adminService;
    private readonly IProjectService _projectService;
    private readonly IReferenceDataRepository _referenceData;

    public HomeClientProjectBulkImportService(
        IAdminService adminService,
        IProjectService projectService,
        IReferenceDataRepository referenceData)
    {
        _adminService = adminService;
        _projectService = projectService;
        _referenceData = referenceData;
    }

    public async Task<HomeClientProjectBulkParseResult> ParseCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("The CSV file is empty or missing a header row.");
        }

        var headerCells = CsvRowParser.SplitLine(headerLine);
        var columnIndex = BuildHeaderMap(headerCells);
        EnsureRequiredColumns(columnIndex);

        var rows = new List<HomeClientProjectImportRowDto>();
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
            var clientName = OptionalCell(cells, columnIndex, "ClientName")?.Trim();
            var projectNumber = OptionalCell(cells, columnIndex, "ProjectNumber")?.Trim();
            if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(projectNumber))
            {
                skippedEmpty++;
                continue;
            }

            rows.Add(new HomeClientProjectImportRowDto
            {
                LineNumber = lineNumber,
                ClientName = clientName,
                ClientShortName = NullIfBlank(OptionalCell(cells, columnIndex, "ClientShortName")) ?? string.Empty,
                ClientDescription = NullIfBlank(OptionalCell(cells, columnIndex, "ClientDescription")) ?? string.Empty,
                ClientStatus = NullIfBlank(OptionalCell(cells, columnIndex, "ClientStatus")) ?? "Active",
                AccountExecutiveEmail = NullIfBlank(
                    OptionalCell(cells, columnIndex, "AccountExecutiveEmail") ?? OptionalCell(cells, columnIndex, "AccountExecutive"))
                    ?? string.Empty,
                AnalyticsLeadEmail = NullIfBlank(
                    OptionalCell(cells, columnIndex, "AnalyticsLeadEmail") ?? OptionalCell(cells, columnIndex, "AnalyticsLead"))
                    ?? string.Empty,
                BusinessUnit = NullIfBlank(OptionalCell(cells, columnIndex, "BusinessUnit")) ?? string.Empty,
                BusinessClass = NullIfBlank(OptionalCell(cells, columnIndex, "BusinessClass")) ?? string.Empty,
                ClientType = NullIfBlank(OptionalCell(cells, columnIndex, "ClientType")) ?? string.Empty,
                ProjectNumber = projectNumber,
                ProjectName = NullIfBlank(OptionalCell(cells, columnIndex, "ProjectName")) ?? string.Empty,
                ProjectType = NullIfBlank(OptionalCell(cells, columnIndex, "ProjectType")) ?? string.Empty,
                ProjectStatus = NullIfBlank(OptionalCell(cells, columnIndex, "ProjectStatus")) ?? string.Empty,
                RenewalDate = NullIfBlank(OptionalCell(cells, columnIndex, "RenewalDate")) ?? string.Empty,
                DueDate = NullIfBlank(OptionalCell(cells, columnIndex, "DueDate")) ?? string.Empty,
                RevenueAmount = NullIfBlank(OptionalCell(cells, columnIndex, "RevenueAmount")) ?? string.Empty,
                RevenueEffectiveDate = NullIfBlank(OptionalCell(cells, columnIndex, "RevenueEffectiveDate")) ?? string.Empty
            });
        }

        return new HomeClientProjectBulkParseResult { Rows = rows, SkippedEmptyRows = skippedEmpty };
    }

    public async Task<HomeClientProjectBulkVerifyResult> VerifyRowsAsync(
        IReadOnlyList<HomeClientProjectImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (rows is null || rows.Count == 0)
        {
            errors.Add("There are no rows to verify. Choose a CSV file with at least one data row (ClientName and ProjectNumber).");
            return new HomeClientProjectBulkVerifyResult { Valid = false, Errors = errors };
        }

        var lookups = await LoadLookupsAsync(cancellationToken);
        var projectNumbersInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            VerifyRow(r, lookups, errors, projectNumbersInFile, out _);
        }

        return new HomeClientProjectBulkVerifyResult { Valid = errors.Count == 0, Errors = errors };
    }

    public async Task<HomeClientProjectBulkImportResult> CommitRowsAsync(
        IReadOnlyList<HomeClientProjectImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var verify = await VerifyRowsAsync(rows, cancellationToken);
        if (!verify.Valid)
        {
            throw new InvalidOperationException(string.Join(' ', verify.Errors));
        }

        var lookups = await LoadLookupsAsync(cancellationToken);
        var rowErrors = new List<string>();
        var imported = 0;
        var projectNumbersInFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in rows)
        {
            var perRow = new List<string>();
            if (!VerifyRow(r, lookups, perRow, projectNumbersInFile, out var resolved))
            {
                rowErrors.AddRange(perRow);
                continue;
            }

            try
            {
                var clientId = resolved.ExistingClientId;
                if (clientId is null or <= 0)
                {
                    clientId = await _adminService.SaveClientAsync(resolved.ClientSave, cancellationToken);
                }

                var projectModel = new ProjectFormViewModel
                {
                    ProjectID = 0,
                    ProjectNumber = resolved.ProjectNumber,
                    ProjectName = resolved.ProjectName,
                    ClientID = clientId.Value,
                    UGID = 0,
                    ProjectTypeID = resolved.ProjectTypeId,
                    StatusID = resolved.ProjectStatusId,
                    RenewalDate = resolved.RenewalDate,
                    DueDate = resolved.DueDate,
                    RevenueAmount = resolved.RevenueAmount,
                    RevenueEffectiveDate = resolved.RevenueEffectiveDate,
                    SelectedServiceIds = []
                };

                await _projectService.SaveAsync(projectModel, cancellationToken);
                imported++;
            }
            catch (Exception ex)
            {
                rowErrors.Add($"Line {r.LineNumber}: {ex.Message}");
            }
        }

        return new HomeClientProjectBulkImportResult
        {
            ImportedCount = imported,
            SkippedEmptyRows = 0,
            RowErrors = rowErrors
        };
    }

    private static void EnsureRequiredColumns(IReadOnlyDictionary<string, int> columnIndex)
    {
        var missing = new List<string>();
        foreach (var col in new[] { "ClientName", "ProjectNumber", "ProjectName", "ProjectType", "ProjectStatus" })
        {
            if (!columnIndex.ContainsKey(col))
            {
                missing.Add(col);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The CSV header row is missing required column(s): " + string.Join(", ", missing) + ". " +
                "Use ClientProjects_Template.csv in SqlScripts/Bulk Import Data as a guide.");
        }
    }

    private static bool VerifyRow(
        HomeClientProjectImportRowDto r,
        ImportLookups lookups,
        List<string> errors,
        Dictionary<string, int> projectNumbersInFile,
        out ResolvedImportRow resolved)
    {
        resolved = new ResolvedImportRow();
        var ok = true;

        var clientName = (r.ClientName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clientName))
        {
            errors.Add($"Line {r.LineNumber}: ClientName is required.");
            ok = false;
        }
        else if (clientName.Length > ClientNameMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ClientName exceeds {ClientNameMaxLength} characters.");
            ok = false;
        }

        if (!string.IsNullOrWhiteSpace(r.ClientShortName) && r.ClientShortName.Trim().Length > ClientShortNameMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ClientShortName exceeds {ClientShortNameMaxLength} characters.");
            ok = false;
        }

        if (!string.IsNullOrWhiteSpace(r.ClientDescription) && r.ClientDescription.Trim().Length > ClientDescriptionMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ClientDescription exceeds {ClientDescriptionMaxLength} characters.");
            ok = false;
        }

        var clientStatus = NormalizeClientStatus(r.ClientStatus, r.LineNumber, errors, ref ok);

        var projectNumber = (r.ProjectNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(projectNumber))
        {
            errors.Add($"Line {r.LineNumber}: ProjectNumber is required.");
            ok = false;
        }
        else if (projectNumber.Length > ProjectNumberMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ProjectNumber exceeds {ProjectNumberMaxLength} characters.");
            ok = false;
        }
        else if (projectNumbersInFile.TryGetValue(projectNumber, out var priorLine))
        {
            errors.Add($"Line {r.LineNumber}: ProjectNumber \"{projectNumber}\" is duplicated in this file (also on line {priorLine}).");
            ok = false;
        }
        else
        {
            projectNumbersInFile[projectNumber] = r.LineNumber;
        }

        var projectName = (r.ProjectName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            errors.Add($"Line {r.LineNumber}: ProjectName is required.");
            ok = false;
        }
        else if (projectName.Length > ProjectNameMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ProjectName exceeds {ProjectNameMaxLength} characters.");
            ok = false;
        }

        var projectTypeKey = (r.ProjectType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(projectTypeKey))
        {
            errors.Add($"Line {r.LineNumber}: ProjectType is required (use a value from tProjectType, e.g. Renewal, New Business).");
            ok = false;
        }
        else if (!lookups.ProjectTypesByKey.TryGetValue(projectTypeKey, out var ptId))
        {
            errors.Add($"Line {r.LineNumber}: ProjectType \"{projectTypeKey}\" was not found (match ProjectTypeDescription or ProjectTypeCode in tProjectType).");
            ok = false;
        }
        else
        {
            resolved.ProjectTypeId = ptId;
        }

        var projectStatusKey = (r.ProjectStatus ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(projectStatusKey))
        {
            errors.Add($"Line {r.LineNumber}: ProjectStatus is required (Active, Inactive, or Prospect from tStatus for projects).");
            ok = false;
        }
        else if (!lookups.ProjectStatusesByKey.TryGetValue(projectStatusKey, out var psId))
        {
            errors.Add($"Line {r.LineNumber}: ProjectStatus \"{projectStatusKey}\" was not found (match StatusDescription for entity Project).");
            ok = false;
        }
        else
        {
            resolved.ProjectStatusId = psId;
        }

        if (!TryParseOptionalDate(r.RenewalDate, r.LineNumber, "RenewalDate", errors, out var renewal))
        {
            ok = false;
        }
        else
        {
            resolved.RenewalDate = renewal;
        }

        if (!TryParseOptionalDate(r.DueDate, r.LineNumber, "DueDate", errors, out var due))
        {
            ok = false;
        }
        else
        {
            resolved.DueDate = due;
        }

        if (!TryParseOptionalRevenue(r, errors, out var revenue, out var revenueDate))
        {
            ok = false;
        }
        else
        {
            resolved.RevenueAmount = revenue;
            resolved.RevenueEffectiveDate = revenueDate;
        }

        int? accountExecutiveId = null;
        if (!string.IsNullOrWhiteSpace(r.AccountExecutiveEmail))
        {
            if (lookups.UsersByEmail.TryGetValue(r.AccountExecutiveEmail.Trim(), out var aeId))
            {
                accountExecutiveId = aeId;
            }
            else
            {
                errors.Add($"Line {r.LineNumber}: Account Executive \"{r.AccountExecutiveEmail.Trim()}\" was not found (expected a user email in tUser).");
                ok = false;
            }
        }

        int? analyticsLeadId = null;
        if (!string.IsNullOrWhiteSpace(r.AnalyticsLeadEmail))
        {
            if (lookups.UsersByEmail.TryGetValue(r.AnalyticsLeadEmail.Trim(), out var alId))
            {
                analyticsLeadId = alId;
            }
            else
            {
                errors.Add($"Line {r.LineNumber}: Analytics Lead \"{r.AnalyticsLeadEmail.Trim()}\" was not found (expected a user email in tUser).");
                ok = false;
            }
        }

        var hasBu = !string.IsNullOrWhiteSpace(r.BusinessUnit);
        var hasBc = !string.IsNullOrWhiteSpace(r.BusinessClass);
        var hasCt = !string.IsNullOrWhiteSpace(r.ClientType);
        int? buId = null;
        int? bcId = null;
        int? ctId = null;

        if (hasBu || hasBc || hasCt)
        {
            if (!(hasBu && hasBc && hasCt))
            {
                errors.Add($"Line {r.LineNumber}: BusinessUnit, BusinessClass, and ClientType must all be provided together when classifying a client.");
                ok = false;
            }
            else
            {
                if (!lookups.BusinessUnitsByName.TryGetValue(r.BusinessUnit.Trim(), out var resolvedBu))
                {
                    errors.Add($"Line {r.LineNumber}: Business Unit \"{r.BusinessUnit.Trim()}\" was not found (match BusinessUnitName in tBusinessUnit, e.g. US, UK, LA).");
                    ok = false;
                }
                else
                {
                    buId = resolvedBu;
                }

                if (!lookups.BusinessClassesByName.TryGetValue(r.BusinessClass.Trim(), out var resolvedBc))
                {
                    errors.Add($"Line {r.LineNumber}: Business Class \"{r.BusinessClass.Trim()}\" was not found (match BusinessClassName in tBusinessClass, e.g. Re, NM Re).");
                    ok = false;
                }
                else
                {
                    bcId = resolvedBc;
                }

                if (!lookups.ClientTypesByName.TryGetValue(r.ClientType.Trim(), out var resolvedCt))
                {
                    errors.Add($"Line {r.LineNumber}: Client Type \"{r.ClientType.Trim()}\" was not found (match ClientTypeName in tClientType, e.g. PIC, MGA).");
                    ok = false;
                }
                else
                {
                    ctId = resolvedCt;
                }
            }
        }

        if (!ok)
        {
            return false;
        }

        if (!lookups.ClientsByName.TryGetValue(clientName, out var existingClients))
        {
            if (!(buId is > 0 && bcId is > 0 && ctId is > 0))
            {
                errors.Add(
                    $"Line {r.LineNumber}: Client \"{clientName}\" was not found. Provide BusinessUnit, BusinessClass, and ClientType to create a new client.");
                return false;
            }

            resolved.ClientSave = new ClientAdminSaveRequest
            {
                ClientID = 0,
                ClientName = clientName,
                ClientShortName = NullIfBlank(r.ClientShortName),
                ClientDescription = NullIfBlank(r.ClientDescription),
                ClientStatus = clientStatus,
                IsActive = !string.Equals(clientStatus, "Inactive", StringComparison.OrdinalIgnoreCase),
                AccountExecutiveUserID = accountExecutiveId,
                AnalyticsLeadUserID = analyticsLeadId,
                BusinessUnitID = buId,
                BusinessClassID = bcId,
                ClientTypeID = ctId
            };
        }
        else
        {
            if (existingClients.Count > 1)
            {
                errors.Add($"Line {r.LineNumber}: Multiple clients named \"{clientName}\" exist in tClient; resolve duplicates before importing.");
                return false;
            }

            var existing = existingClients[0];
            resolved.ExistingClientId = existing.ClientID;

            if (hasBu || hasBc || hasCt)
            {
                if (!NamesMatch(existing.BusinessUnitName, r.BusinessUnit))
                {
                    errors.Add(
                        $"Line {r.LineNumber}: BusinessUnit \"{r.BusinessUnit.Trim()}\" does not match client \"{clientName}\" (database: {FormatDbValue(existing.BusinessUnitName)}).");
                    ok = false;
                }

                if (!NamesMatch(existing.BusinessClassName, r.BusinessClass))
                {
                    errors.Add(
                        $"Line {r.LineNumber}: BusinessClass \"{r.BusinessClass.Trim()}\" does not match client \"{clientName}\" (database: {FormatDbValue(existing.BusinessClassName)}).");
                    ok = false;
                }

                if (!NamesMatch(existing.ClientTypeName, r.ClientType))
                {
                    errors.Add(
                        $"Line {r.LineNumber}: ClientType \"{r.ClientType.Trim()}\" does not match client \"{clientName}\" (database: {FormatDbValue(existing.ClientTypeName)}).");
                    ok = false;
                }
            }
        }

        if (!ok)
        {
            return false;
        }

        resolved.ProjectNumber = projectNumber;
        resolved.ProjectName = projectName;
        return true;
    }

    private async Task<ImportLookups> LoadLookupsAsync(CancellationToken cancellationToken)
    {
        var businessUnits = await _referenceData.GetBusinessUnitsAsync(cancellationToken: cancellationToken);
        var businessClasses = await _referenceData.GetBusinessClassesAsync(cancellationToken);
        var clientTypes = await _referenceData.GetClientTypesAsync(cancellationToken: cancellationToken);
        var users = await _referenceData.GetAllUsersForPicklistAsync(cancellationToken);
        var projectTypes = await _referenceData.GetProjectTypesAsync(cancellationToken);
        var projectStatuses = await _referenceData.GetStatusesForEntityAsync(ProjectEntityType, cancellationToken);
        var clients = await _adminService.ListClientsAsync(null, includeInactive: true, cancellationToken);

        var buByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var bu in businessUnits)
        {
            var key = bu.BusinessUnitName?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !buByName.ContainsKey(key))
            {
                buByName[key] = bu.BusinessUnitID;
            }
        }

        var bcByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var bc in businessClasses)
        {
            var key = bc.BusinessClassName?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !bcByName.ContainsKey(key))
            {
                bcByName[key] = bc.BusinessClassID;
            }
        }

        var ctByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ct in clientTypes)
        {
            var key = ct.CTName?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !ctByName.ContainsKey(key))
            {
                ctByName[key] = ct.ClientTypeID;
            }
        }

        var usersByEmail = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in users)
        {
            var key = u.Email?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !usersByEmail.ContainsKey(key))
            {
                usersByEmail[key] = u.UserID;
            }
        }

        var ptByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pt in projectTypes)
        {
            AddKey(ptByKey, pt.PTDescription, pt.ProjectTypeID);
            AddKey(ptByKey, pt.PTCode, pt.ProjectTypeID);
        }

        var psByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in projectStatuses)
        {
            AddKey(psByKey, st.StatusDescription, st.StatusID);
            AddKey(psByKey, st.StatusCode, st.StatusID);
        }

        var clientsByName = new Dictionary<string, List<ClientAdminRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in clients)
        {
            var key = c.ClientName?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!clientsByName.TryGetValue(key, out var list))
            {
                list = [];
                clientsByName[key] = list;
            }

            list.Add(c);
        }

        return new ImportLookups(buByName, bcByName, ctByName, usersByEmail, ptByKey, psByKey, clientsByName);
    }

    private static void AddKey(Dictionary<string, int> map, string? key, int id)
    {
        var trimmed = key?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || map.ContainsKey(trimmed))
        {
            return;
        }

        map[trimmed] = id;
    }

    private static string NormalizeClientStatus(string? raw, int lineNumber, List<string> errors, ref bool ok)
    {
        var status = (raw ?? "Active").Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Active";
        }

        var match = AllowedClientStatuses.FirstOrDefault(
            s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            errors.Add(
                $"Line {lineNumber}: ClientStatus \"{status}\" is invalid (use Active, Inactive, or Prospect).");
            ok = false;
            return "Active";
        }

        return match;
    }

    private static bool TryParseOptionalDate(
        string raw,
        int lineNumber,
        string fieldName,
        List<string> errors,
        out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            || DateTime.TryParse(raw.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
        {
            value = dt.Date;
            return true;
        }

        errors.Add($"Line {lineNumber}: {fieldName} \"{raw.Trim()}\" is not a valid date (use YYYY-MM-DD).");
        return false;
    }

    private static bool TryParseOptionalRevenue(
        HomeClientProjectImportRowDto r,
        List<string> errors,
        out decimal? amount,
        out DateTime? effectiveDate)
    {
        amount = null;
        effectiveDate = null;
        if (string.IsNullOrWhiteSpace(r.RevenueAmount))
        {
            return true;
        }

        if (!decimal.TryParse(r.RevenueAmount.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var rev)
            && !decimal.TryParse(r.RevenueAmount.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out rev))
        {
            errors.Add($"Line {r.LineNumber}: RevenueAmount \"{r.RevenueAmount.Trim()}\" is not a valid number.");
            return false;
        }

        if (rev < 0m)
        {
            errors.Add($"Line {r.LineNumber}: RevenueAmount must be zero or greater.");
            return false;
        }

        if (rev == 0m)
        {
            return true;
        }

        amount = rev;
        if (!TryParseOptionalDate(r.RevenueEffectiveDate, r.LineNumber, "RevenueEffectiveDate", errors, out effectiveDate))
        {
            return false;
        }

        effectiveDate ??= DateTime.UtcNow.Date;
        return true;
    }

    private static bool NamesMatch(string? databaseValue, string csvValue)
        => string.Equals(databaseValue?.Trim(), csvValue.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FormatDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? "(not set)" : value.Trim();

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headerCells)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ClientName", "ClientShortName", "ClientDescription", "ClientStatus",
            "AccountExecutiveEmail", "AccountExecutive",
            "AnalyticsLeadEmail", "AnalyticsLead",
            "BusinessUnit", "BusinessClass", "ClientType",
            "ProjectNumber", "ProjectName", "ProjectType", "ProjectStatus",
            "RenewalDate", "DueDate", "RevenueAmount", "RevenueEffectiveDate"
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

    private static string? GetCell(IReadOnlyList<string> cells, int columnIndex)
        => columnIndex < 0 || columnIndex >= cells.Count ? null : cells[columnIndex];

    private static string? OptionalCell(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> columnIndex, string columnName)
        => columnIndex.TryGetValue(columnName, out var idx) ? GetCell(cells, idx) : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ImportLookups(
        IReadOnlyDictionary<string, int> BusinessUnitsByName,
        IReadOnlyDictionary<string, int> BusinessClassesByName,
        IReadOnlyDictionary<string, int> ClientTypesByName,
        IReadOnlyDictionary<string, int> UsersByEmail,
        IReadOnlyDictionary<string, int> ProjectTypesByKey,
        IReadOnlyDictionary<string, int> ProjectStatusesByKey,
        IReadOnlyDictionary<string, List<ClientAdminRow>> ClientsByName);

    private sealed class ResolvedImportRow
    {
        public int? ExistingClientId { get; set; }
        public ClientAdminSaveRequest ClientSave { get; set; } = new();
        public string ProjectNumber { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int ProjectTypeId { get; set; }
        public int ProjectStatusId { get; set; }
        public DateTime? RenewalDate { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal? RevenueAmount { get; set; }
        public DateTime? RevenueEffectiveDate { get; set; }
    }
}
