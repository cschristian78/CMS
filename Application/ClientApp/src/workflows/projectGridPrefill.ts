import { formatBusinessClassDisplay } from '../utils/businessClassDisplay';
import { toInputDateValue } from '../utils/dateInput';

/**
 * Maps a home / dashboard grid row (dbo.vProject_Current) onto the project form model.
 */
export interface ProjectGridNavigationState {
  fromGrid?: true;
  gridColumns: string[];
  gridRow: (string | number | boolean | null)[];
}

interface ClientDirectoryRow {
  clientID: number;
  clientName?: string | null;
  clientShortName?: string | null;
  businessUnitID?: number | null;
  businessUnitName?: string | null;
  businessClassID?: number | null;
  businessClassName?: string | null;
  businessClassDescription?: string | null;
  clientCategoryID?: number | null;
  ccName?: string | null;
  clientTypeID?: number | null;
  ctName?: string | null;
  statusID?: number | null;
  statusCode?: string | null;
  statusDescription?: string | null;
}

interface SelectOption {
  value: string;
  text: string;
}

export interface ProjectFormGridTarget {
  projectNumber: string;
  projectName: string;
  clientID: number;
  ugid: number;
  projectTypeID: number;
  renewalDate?: string | null;
  dueDate?: string | null;
  statusID: number;
  revenueAmount?: number | null;
  revenueEffectiveDate?: string | null;
  revenueChangeReason?: string | null;
  clientName?: string | null;
  clientShortName?: string | null;
  clientBusinessUnitID?: number | null;
  clientBusinessUnitName?: string | null;
  clientBusinessClassID?: number | null;
  clientBusinessClassName?: string | null;
  clientCategoryID?: number | null;
  clientCategoryName?: string | null;
  clientTypeID?: number | null;
  clientTypeName?: string | null;
  clientStatusID?: number | null;
  clientStatusCode?: string | null;
  clientDirectoryRows?: ClientDirectoryRow[];
  projectTypeOptions: SelectOption[];
  userGroupOptions: SelectOption[];
  statusOptions: SelectOption[];
}

function normalizeColumnName(name: string): string {
  return name.replace(/[\s_\-.]/g, '').toLowerCase();
}

function findGridColumnIndex(columns: string[], ...logicalNames: string[]): number {
  const normalized = columns.map((c) => normalizeColumnName(c));
  for (const logicalName of logicalNames) {
    const want = normalizeColumnName(logicalName);
    const exact = normalized.findIndex((n) => n === want);
    if (exact >= 0) {
      return exact;
    }
    const loose = columns.findIndex((c) => c.trim().toLowerCase() === logicalName.trim().toLowerCase());
    if (loose >= 0) {
      return loose;
    }
  }
  return -1;
}

function parsePositiveIntId(raw: string | number | boolean | null | undefined): number | null {
  if (raw === null || raw === undefined || raw === '') {
    return null;
  }
  if (typeof raw === 'boolean') {
    return null;
  }
  if (typeof raw === 'number') {
    if (!Number.isFinite(raw) || raw <= 0) {
      return null;
    }
    return Math.trunc(raw);
  }
  const t = String(raw).trim();
  if (!t) {
    return null;
  }
  const n = Number(t);
  if (!Number.isFinite(n) || n <= 0) {
    return null;
  }
  return Math.trunc(n);
}

function getCell(
  columns: string[],
  row: (string | number | boolean | null)[],
  ...logicalNames: string[]
): string | number | boolean | null {
  const idx = findGridColumnIndex(columns, ...logicalNames);
  if (idx < 0 || idx >= row.length) {
    return null;
  }
  return row[idx] ?? null;
}

function getStringCell(
  columns: string[],
  row: (string | number | boolean | null)[],
  ...logicalNames: string[]
): string | null {
  const v = getCell(columns, row, ...logicalNames);
  if (v === null || v === undefined) {
    return null;
  }
  const t = String(v).trim();
  return t || null;
}

function toIsoDateValue(raw: string | number | boolean | null | undefined): string | null {
  const iso = toInputDateValue(raw === null || raw === undefined ? null : String(raw));
  return iso || null;
}

function parseDecimal(raw: string | number | boolean | null | undefined): number | null {
  if (raw === null || raw === undefined || raw === '') {
    return null;
  }
  if (typeof raw === 'number' && Number.isFinite(raw)) {
    return raw;
  }
  const t = String(raw).trim().replace(/[$,]/g, '');
  if (!t) {
    return null;
  }
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

function applyClientFromDirectoryRow(
  next: ProjectFormGridTarget,
  row: ClientDirectoryRow,
): ProjectFormGridTarget {
  return {
    ...next,
    clientID: row.clientID,
    clientName: row.clientName ?? '',
    clientShortName: row.clientShortName ?? '',
    clientBusinessUnitID: row.businessUnitID ?? null,
    clientBusinessUnitName: row.businessUnitName ?? '',
    clientBusinessClassID: row.businessClassID ?? null,
    clientBusinessClassName: formatBusinessClassDisplay(row.businessClassName, row.businessClassDescription),
    clientCategoryID: row.clientCategoryID ?? null,
    clientCategoryName: row.ccName ?? '',
    clientTypeID: row.clientTypeID ?? null,
    clientTypeName: row.ctName ?? '',
    clientStatusID: row.statusID ?? null,
    clientStatusCode: row.statusCode ?? row.statusDescription ?? '',
  };
}

function matchOptionId(options: SelectOption[], label: string | null): number | null {
  if (!label) {
    return null;
  }
  const want = label.trim().toLowerCase();
  const hit = options.find((o) => o.text.trim().toLowerCase() === want);
  return hit ? Number(hit.value) : null;
}

/** Merge grid row values onto the project form (client tab + project header fields). */
export function applyGridRowToProjectModel<T extends ProjectFormGridTarget>(
  model: T,
  columns: string[],
  row: (string | number | boolean | null)[],
): T {
  let next: ProjectFormGridTarget = { ...model };

  const clientId = parsePositiveIntId(getCell(columns, row, 'ClientID'));
  if (clientId) {
    next.clientID = clientId;
    const dirRow = model.clientDirectoryRows?.find((r) => r.clientID === clientId);
    if (dirRow) {
      next = applyClientFromDirectoryRow(next, dirRow);
    } else {
      next = {
        ...next,
        clientName: getStringCell(columns, row, 'ClientName') ?? next.clientName,
        clientBusinessUnitName:
          getStringCell(columns, row, 'BusinessUnitName', 'SegmentName') ?? next.clientBusinessUnitName,
        clientBusinessClassName:
          formatBusinessClassDisplay(
            getStringCell(columns, row, 'BusinessClassName'),
            getStringCell(columns, row, 'BusinessClassDescription'),
          ) || next.clientBusinessClassName,
        clientStatusCode:
          getStringCell(columns, row, 'ClientStatus', 'StatusDescription') ?? next.clientStatusCode,
      };
    }
  }

  const projectNumber = getStringCell(columns, row, 'ProjectNumber');
  if (projectNumber) {
    next.projectNumber = projectNumber;
  }

  const projectName = getStringCell(columns, row, 'ProjectName');
  if (projectName) {
    next.projectName = projectName;
  }

  const ptDescription = getStringCell(columns, row, 'PTDescription', 'ProjectTypeDescription');
  const typeId = matchOptionId(model.projectTypeOptions, ptDescription);
  if (typeId) {
    next.projectTypeID = typeId;
  }

  const ugId = parsePositiveIntId(getCell(columns, row, 'UserGroupID', 'UGID'));
  if (ugId) {
    next.ugid = ugId;
  } else {
    const ugName = getStringCell(columns, row, 'UserGroupName', 'UGName');
    const ugMatch = matchOptionId(model.userGroupOptions, ugName);
    if (ugMatch) {
      next.ugid = ugMatch;
    }
  }

  const renewal = toIsoDateValue(getCell(columns, row, 'RenewalDate'));
  if (renewal) {
    next.renewalDate = renewal;
  }

  const due = toIsoDateValue(getCell(columns, row, 'DueDate'));
  if (due) {
    next.dueDate = due;
  }

  const projectStatus = getStringCell(columns, row, 'ProjectStatus');
  const statusId = matchOptionId(model.statusOptions, projectStatus);
  if (statusId) {
    next.statusID = statusId;
  }

  const latestRevenue = parseDecimal(getCell(columns, row, 'LatestRevenue'));
  if (latestRevenue != null && latestRevenue > 0) {
    next.revenueAmount = latestRevenue;
  }

  const latestRevenueDate = toIsoDateValue(
    getCell(columns, row, 'LatestRevenueEffectiveDate', 'LatestRevenueDate'),
  );
  if (latestRevenueDate) {
    next.revenueEffectiveDate = latestRevenueDate;
  }

  return next as T;
}

export function gridFilterIdsFromModel(model: ProjectFormGridTarget): {
  businessUnitId: number | '';
  businessClassId: number | '';
  clientCategoryId: number | '';
} {
  return {
    businessUnitId: model.clientBusinessUnitID ?? '',
    businessClassId: model.clientBusinessClassID ?? '',
    clientCategoryId: model.clientCategoryID ?? '',
  };
}
