import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { ChangeEvent, MouseEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import type { FlashFn } from '../App';
import { ApiError, apiGet, apiPostFormData, apiPostJson } from '../api';
import { normalizeClientProjectListModel, sortClientProjectListRows, clientProjectListSortIndicator, type ClientProjectListModel, type ClientProjectListSortDirection } from '../clientProjectList';
import {
  clientProjectListColumnHeaderForDisplay,
  isHomeProjectListHiddenColumn,
} from '../workflows/clientProjectListColumnLabels';
import type { ClientProjectListFilterOptions } from '../workflows/clientProjectListWorkflowTypes';
import {
  buildHomeClientProjectListUrl,
  buildHomeSummaryUrl,
  createDefaultHomeListFilters,
  HOME_CLIENT_PROJECT_LIST_OPTIONS_URL,
  loadHomeListFilters,
  saveHomeListFilters,
  type HomeListFilters,
} from '../workflows/homeClientProjectListWorkflow';
import { formatCalendarDateDisplay } from '../utils/dateInput';
import { APP_BUILD_NUMBER } from '../appBuild';
import { runProjectReportWorkflow, type ProjectReportRunMode } from '../project/generateProjectReportWorkflow';

function normalizeColumnName(name: string): string {
  return name.replace(/[\s_\-.]/g, '').toLowerCase();
}

/** Resolve SQL / API column name variants to the ProjectID column index. */
function findProjectIdColumnIndex(columns: string[]): number {
  if (!columns?.length) {
    return -1;
  }
  const normalized = columns.map((c) => normalizeColumnName(c));
  const exact = normalized.findIndex((n) => n === 'projectid');
  if (exact >= 0) {
    return exact;
  }
  const spaced = columns.findIndex((c) => /^project\s*id$/i.test(c.trim()));
  if (spaced >= 0) {
    return spaced;
  }
  return columns.findIndex((c) => c.trim().toLowerCase() === 'projectid');
}

/** Resolve column index for logical names such as ClientID, ClientName. */
function findGridColumnIndex(columns: string[], logicalName: string): number {
  if (!columns?.length) {
    return -1;
  }
  const want = normalizeColumnName(logicalName);
  const normalized = columns.map((c) => normalizeColumnName(c));
  const exact = normalized.findIndex((n) => n === want);
  if (exact >= 0) {
    return exact;
  }
  return columns.findIndex((c) => c.trim().toLowerCase() === logicalName.trim().toLowerCase());
}

function findClientIdColumnIndex(columns: string[]): number {
  return findGridColumnIndex(columns, 'ClientID');
}

function findIsOnlineColumnIndex(columns: string[]): number {
  const preferred = findGridColumnIndex(columns, 'ProjectOnline');
  if (preferred >= 0) {
    return preferred;
  }
  return findGridColumnIndex(columns, 'IsOnline');
}

function isOnlineStatusColumn(columnName: string): boolean {
  const n = normalizeColumnName(columnName);
  return n === 'projectonline' || n === 'isonline';
}

function cellIsTruthy(raw: string | number | boolean | null | undefined): boolean {
  if (typeof raw === 'boolean') {
    return raw;
  }
  if (typeof raw === 'number') {
    return raw !== 0;
  }
  if (typeof raw === 'string') {
    const t = raw.trim().toLowerCase();
    return t === '1' || t === 'true' || t === 'yes' || t === 'online' || t === 'y';
  }
  return false;
}

/** Widen key text columns so values stay on one line (table scrolls horizontally). */
function homeProjectListCellClass(columnName: string): string {
  const base = 'home-client-project-table__cell';
  const n = normalizeColumnName(columnName);
  if (
    n === 'clientname' ||
    n === 'clientcategory' ||
    n === 'clienttypename' ||
    n === 'programname' ||
    n === 'businessunitname' ||
    n === 'businessclassname' ||
    n === 'segmentname' ||
    n === 'statusdescription' ||
    n === 'projectstatus' ||
    n === 'analyticslead'
  ) {
    return `${base} ${base}--fit-one-line`;
  }
  return base;
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

interface HomeProjectTypeCount {
  projectTypeId: number;
  projectTypeCode?: string;
  projectTypeDescription?: string;
  projectCount: number;
}

interface HomeSummary {
  activeProjectCount: number;
  /** Distinct ProjectID in filtered list scope; omitted or zero when summary is global (unfiltered). */
  distinctProjectCount?: number;
  totalProjectRevenue: number;
  onlineProjectCount?: number;
  onlineProjectRevenue?: number;
  projectTypeCounts: HomeProjectTypeCount[];
}

interface HomeGridEditPromptState {
  clientLabel: string;
  gridColumns: string[];
  gridRow: (string | number | boolean | null)[];
  /** Parsed ProjectID for this row, when present and valid. */
  existingProjectId: number | null;
  /** Row includes a usable ClientID for New project prefill. */
  canCreateNew: boolean;
}

interface HomeClientProjectImportRow {
  lineNumber: number;
  clientName: string;
  clientShortName: string;
  clientDescription: string;
  clientStatus: string;
  accountExecutiveEmail: string;
  analyticsLeadEmail: string;
  businessUnit: string;
  businessClass: string;
  clientType: string;
  projectNumber: string;
  projectName: string;
  projectType: string;
  projectStatus: string;
  renewalDate: string;
  dueDate: string;
  revenueAmount: string;
  revenueEffectiveDate: string;
}

interface HomeBulkParseResult {
  rows: HomeClientProjectImportRow[];
  skippedEmptyRows: number;
}

interface HomeBulkVerifyResult {
  valid: boolean;
  errors: string[];
}

interface HomeBulkCommitResult {
  importedCount: number;
  skippedEmptyRows: number;
  rowErrors: string[];
}

function formatUsdAmount(n: number, maximumFractionDigits = 2): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits,
  }).format(n);
}

function isLatestRevenueColumn(columnName: string): boolean {
  const n = normalizeColumnName(columnName);
  // Home grid: LatestRevenue; avoid RevenueTier / LatestRevenueID / EffectiveDate.
  return n === 'latestrevenue' || n === 'revenueamount' || n === 'revenue';
}

function isTotalHoursColumn(columnName: string): boolean {
  const n = normalizeColumnName(columnName);
  return n === 'totalhours' || n === 'hours';
}

function isDateColumn(columnName: string): boolean {
  const n = normalizeColumnName(columnName);
  return n.includes('date');
}

function formatTableCell(
  value: string | number | boolean | null | undefined,
  columnName?: string,
): string {
  if (value === null || value === undefined) {
    return '';
  }
  if (columnName && isLatestRevenueColumn(columnName)) {
    const n =
      typeof value === 'number' ?
        value
      : Number(String(value).replace(/[$,\s]/g, ''));
    if (!Number.isFinite(n)) {
      return String(value);
    }
    return formatUsdAmount(n);
  }
  if (columnName && isTotalHoursColumn(columnName)) {
    const n =
      typeof value === 'number' ?
        value
      : Number(String(value).replace(/[,\s]/g, ''));
    if (!Number.isFinite(n)) {
      return String(value);
    }
    const rounded = Math.round(n * 100) / 100;
    return rounded === 0 ? '0' : String(rounded);
  }
  if (columnName && isDateColumn(columnName)) {
    const formatted = formatCalendarDateDisplay(String(value));
    return formatted === '-' ? '' : formatted;
  }
  if (typeof value === 'number' && !Number.isInteger(value)) {
    return String(Math.round(value * 100) / 100);
  }
  return String(value);
}

function parseProjectIdFromRow(
  row: (string | number | boolean | null)[] | unknown,
  projectIdColumnIndex: number,
): number | null {
  if (!Array.isArray(row) || projectIdColumnIndex < 0 || projectIdColumnIndex >= row.length) {
    return null;
  }
  return parsePositiveIntId(row[projectIdColumnIndex]);
}

function buildClientLabelFromRow(
  columns: string[],
  row: (string | number | boolean | null)[],
  clientId: number | null,
): string {
  const nameIdx = findGridColumnIndex(columns, 'ClientName');
  const rawName = nameIdx >= 0 && nameIdx < row.length ? row[nameIdx] : null;
  if (rawName !== null && rawName !== undefined && String(rawName).trim() !== '') {
    return String(rawName).trim();
  }
  if (clientId !== null) {
    return `Client ID ${clientId}`;
  }
  return 'this client';
}

function gridCellText(
  columns: string[],
  row: (string | number | boolean | null)[],
  logicalName: string,
): string {
  const idx = findGridColumnIndex(columns, logicalName);
  if (idx < 0 || idx >= row.length) {
    return '';
  }
  const raw = row[idx];
  if (raw === null || raw === undefined) {
    return '';
  }
  return String(raw).trim();
}

function buildProjectLabelFromRow(
  columns: string[],
  row: (string | number | boolean | null)[],
  projectId: number | null,
): string {
  const number = gridCellText(columns, row, 'ProjectNumber');
  const name = gridCellText(columns, row, 'ProjectName');
  const parts = [number, name].filter((p) => p.length > 0);
  if (parts.length > 0) {
    return parts.join(' — ');
  }
  if (projectId !== null) {
    return `Project ID ${projectId}`;
  }
  return 'this project';
}

interface HomeHardDeletePromptState {
  projectId: number;
  label: string;
}

function isAnalyticsLeadColumn(columnName: string): boolean {
  return normalizeColumnName(columnName) === 'analyticslead';
}

/** Size a native select to the selected label so the box ends just after the text. */
function sizeSelectToSelectedText(select: HTMLSelectElement) {
  const option = select.options[select.selectedIndex];
  const text = option?.text?.trim() || '';
  const measure = document.createElement('span');
  const cs = window.getComputedStyle(select);
  measure.style.cssText = [
    'position:absolute',
    'left:-9999px',
    'top:0',
    'visibility:hidden',
    'white-space:nowrap',
    `font:${cs.font}`,
    `letter-spacing:${cs.letterSpacing}`,
  ].join(';');
  measure.textContent = text || ' ';
  document.body.appendChild(measure);
  const caretAndPad = 28;
  select.style.width = `${Math.ceil(measure.getBoundingClientRect().width + caretAndPad)}px`;
  measure.remove();
}

export default function HomePage({ flash }: { flash: FlashFn }) {
  const navigate = useNavigate();
  const [summary, setSummary] = useState<HomeSummary | null>(null);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [list, setList] = useState<ClientProjectListModel | null>(null);
  const [listError, setListError] = useState<string | null>(null);
  const [listLoading, setListLoading] = useState(false);
  /** Local online overrides so the green dot updates immediately even if the grid column is missing. */
  const [onlineOverrides, setOnlineOverrides] = useState<Record<number, boolean>>({});
  const [selectedRowIndex, setSelectedRowIndex] = useState<number | null>(null);
  const [projectContextMenu, setProjectContextMenu] = useState<{
    projectId: number;
    label: string;
    x: number;
    y: number;
  } | null>(null);
  const [projectReportBusy, setProjectReportBusy] = useState(false);
  const [sortColumnIndex, setSortColumnIndex] = useState<number | null>(null);
  const [sortDirection, setSortDirection] = useState<ClientProjectListSortDirection>('asc');

  const [filterOptions, setFilterOptions] = useState<ClientProjectListFilterOptions | null>(null);
  const [filterOptionsError, setFilterOptionsError] = useState<string | null>(null);
  // Restore persisted Main filters on mount (survives navigating away and back in this tab).
  const initialHomeFilters = useMemo(() => loadHomeListFilters(), []);
  const [filterBusinessUnit, setFilterBusinessUnit] = useState(initialHomeFilters.businessUnit);
  const [filterBusinessClass, setFilterBusinessClass] = useState(initialHomeFilters.businessClass);
  // Default: All clients + Online (unless a prior Main visit saved something else).
  const [filterStatus, setFilterStatus] = useState(initialHomeFilters.status);
  /** '' = all, 'Online', or 'Offline' - filters by dbo.tProject.IsOnline. */
  const [filterOnline, setFilterOnline] = useState(initialHomeFilters.online);
  const [clientNameSearch, setClientNameSearch] = useState(initialHomeFilters.client);
  // ProjectType filter is sourced from unique PTDescription values in dbo.vProject_Current.
  const [filterProjectType, setFilterProjectType] = useState(initialHomeFilters.projectType);
  const [filterAnalyticsLeads, setFilterAnalyticsLeads] = useState<string[]>(() => {
    const leads = initialHomeFilters.analyticsLeads;
    return leads.length > 0 ? [leads[0]] : [];
  });
  const homeFilterBarRef = useRef<HTMLDivElement | null>(null);
  const [gridEditPrompt, setGridEditPrompt] = useState<HomeGridEditPromptState | null>(null);
  const [hardDeletePrompt, setHardDeletePrompt] = useState<HomeHardDeletePromptState | null>(null);
  const [hardDeleteConfirmed, setHardDeleteConfirmed] = useState(false);
  const [hardDeleteBusy, setHardDeleteBusy] = useState(false);

  const [bulkWizardOpen, setBulkWizardOpen] = useState(false);
  const [bulkRows, setBulkRows] = useState<HomeClientProjectImportRow[]>([]);
  const [bulkSkippedEmpty, setBulkSkippedEmpty] = useState(0);
  const [bulkVerifyOk, setBulkVerifyOk] = useState(false);
  const [bulkVerifyErrors, setBulkVerifyErrors] = useState<string[]>([]);
  const [bulkRowErrors, setBulkRowErrors] = useState<string[]>([]);
  const [bulkBusy, setBulkBusy] = useState(false);

  const loadFilterOptions = useCallback(async () => {
    setFilterOptionsError(null);
    try {
      const data = await apiGet<ClientProjectListFilterOptions>(HOME_CLIENT_PROJECT_LIST_OPTIONS_URL);
      setFilterOptions(data);
    } catch (e) {
      setFilterOptions(null);
      setFilterOptionsError(e instanceof ApiError ? e.message : 'Could not load filter options.');
    }
  }, []);

  const loadProjectList = useCallback(
    async (refresh: boolean, filterOverride?: HomeListFilters) => {
      const bu = filterOverride?.businessUnit ?? filterBusinessUnit;
      const bc = filterOverride?.businessClass ?? filterBusinessClass;
      const stat = filterOverride?.status ?? filterStatus;
      const online = filterOverride?.online ?? filterOnline;
      const cli = filterOverride?.client ?? clientNameSearch;
      const pt = filterOverride?.projectType ?? filterProjectType;
      const leads = filterOverride?.analyticsLeads ?? filterAnalyticsLeads;
      const listUrl = buildHomeClientProjectListUrl(refresh, bu, bc, stat, cli, pt, online, leads);
      const summaryUrl = buildHomeSummaryUrl(bu, bc, stat, cli, pt, online, leads);

      setListLoading(true);
      setListError(null);
      try {
        try {
          const data = await apiGet<ClientProjectListModel>(listUrl);
          setList(normalizeClientProjectListModel(data));
          setOnlineOverrides({});
          setSelectedRowIndex(null);
        } catch (e) {
          setList(null);
          setListError(e instanceof ApiError ? e.message : 'Could not load the client/project list.');
          setSelectedRowIndex(null);
          return;
        }

        try {
          setSummaryError(null);
          const sum = await apiGet<HomeSummary>(summaryUrl);
          setSummary(sum);
        } catch (e) {
          setSummaryError(
            e instanceof ApiError ? e.message : 'Could not load summary statistics for the current filters.',
          );
        }

        if (refresh) {
          await loadFilterOptions();
        }
      } finally {
        setListLoading(false);
      }
    },
    [filterBusinessUnit, filterBusinessClass, filterStatus, filterOnline, clientNameSearch, filterProjectType, filterAnalyticsLeads, loadFilterOptions],
  );

  const clearAllFilters = useCallback(() => {
    const defaults = createDefaultHomeListFilters();
    setFilterBusinessUnit(defaults.businessUnit);
    setFilterBusinessClass(defaults.businessClass);
    setFilterStatus(defaults.status);
    setFilterOnline(defaults.online);
    setClientNameSearch(defaults.client);
    setFilterProjectType(defaults.projectType);
    setFilterAnalyticsLeads([]);
    saveHomeListFilters(defaults);
    void loadProjectList(false, defaults);
  }, [loadProjectList]);

  useEffect(() => {
    void loadProjectList(true);
    // Initial load (and remount after navigating back) - Apply filters / Refresh for mid-page changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Keep Main filters across navigation until the user changes them again.
  useEffect(() => {
    saveHomeListFilters({
      businessUnit: filterBusinessUnit,
      businessClass: filterBusinessClass,
      status: filterStatus,
      online: filterOnline,
      client: clientNameSearch,
      projectType: filterProjectType,
      analyticsLeads: filterAnalyticsLeads,
    });
  }, [
    filterBusinessUnit,
    filterBusinessClass,
    filterStatus,
    filterOnline,
    clientNameSearch,
    filterProjectType,
    filterAnalyticsLeads,
  ]);

  useLayoutEffect(() => {
    const root = homeFilterBarRef.current;
    if (!root) {
      return;
    }
    root.querySelectorAll<HTMLSelectElement>('select.home-filter-select--fit').forEach(sizeSelectToSelectedText);
  }, [
    filterOptions,
    filterBusinessUnit,
    filterBusinessClass,
    filterStatus,
    filterOnline,
    filterProjectType,
    filterAnalyticsLeads,
    listLoading,
  ]);

  // Once the Status filter options come back from the API, snap a selected
  // status value to the canonical casing the database uses (e.g. "ACTIVE",
  // "Active", "active") so the <select> highlights the right <option>.
  useEffect(() => {
    if (!filterOptions?.statusNames || !filterStatus) {
      return;
    }
    const match = filterOptions.statusNames.find(
      (s) => s.trim().toLowerCase() === filterStatus.trim().toLowerCase(),
    );
    if (match && match !== filterStatus) {
      setFilterStatus(match);
    }
  }, [filterOptions, filterStatus]);

  const formatUsd = (n: number) => formatUsdAmount(n, 0);

  const revenueLabel = summary != null ? formatUsd(summary.totalProjectRevenue) : '-';
  const onlineRevenueLabel =
    summary != null ? formatUsd(summary.onlineProjectRevenue ?? 0) : '-';
  const onlineProjectCountLabel =
    summary != null ? String(summary.onlineProjectCount ?? 0) : '...';

  const projectIdColumnIndex = useMemo(() => {
    if (!list?.columns?.length) {
      return -1;
    }
    return findProjectIdColumnIndex(list.columns);
  }, [list?.columns]);

  const clientIdColumnIndex = useMemo(() => {
    if (!list?.columns?.length) {
      return -1;
    }
    return findClientIdColumnIndex(list.columns);
  }, [list?.columns]);

  const isOnlineColumnIndex = useMemo(() => {
    if (!list?.columns?.length) {
      return -1;
    }
    return findIsOnlineColumnIndex(list.columns);
  }, [list?.columns]);

  /** Data columns shown in the grid (online flags are the leading green-dot; IDs and service pivots stay in the payload). */
  const visibleColumnIndexes = useMemo(() => {
    if (!list?.columns?.length) {
      return [] as number[];
    }
    return list.columns
      .map((c, i) => ({ c, i }))
      .filter(({ c }) => !isOnlineStatusColumn(c) && !isHomeProjectListHiddenColumn(c))
      .map(({ i }) => i);
  }, [list?.columns]);

  const displayRows = useMemo((): (string | number | boolean | null)[][] => {
    if (!list?.rows?.length) {
      return [];
    }
    if (sortColumnIndex === null || sortColumnIndex < 0 || sortColumnIndex >= list.columns.length) {
      return list.rows as (string | number | boolean | null)[][];
    }
    return sortClientProjectListRows(
      list.rows as (string | number | boolean | null)[][],
      sortColumnIndex,
      list.columns[sortColumnIndex] ?? '',
      sortDirection,
    );
  }, [list?.rows, list?.columns, sortColumnIndex, sortDirection]);

  useEffect(() => {
    if (sortColumnIndex === null || !list?.columns?.length) {
      return;
    }
    if (sortColumnIndex < 0 || sortColumnIndex >= list.columns.length) {
      setSortColumnIndex(null);
    }
  }, [list?.columns, sortColumnIndex]);

  const handleSortColumn = useCallback((columnIndex: number) => {
    setSelectedRowIndex(null);
    setSortColumnIndex((prevIndex) => {
      if (prevIndex === columnIndex) {
        setSortDirection((prevDirection) => (prevDirection === 'asc' ? 'desc' : 'asc'));
        return prevIndex;
      }
      setSortDirection('asc');
      return columnIndex;
    });
  }, []);

  useEffect(() => {
    if (selectedRowIndex === null || !displayRows.length) {
      return;
    }
    if (selectedRowIndex < 0 || selectedRowIndex >= displayRows.length) {
      setSelectedRowIndex(null);
    }
  }, [displayRows, selectedRowIndex]);

  const selectedRow = useMemo((): (string | number | boolean | null)[] | null => {
    if (!displayRows.length || selectedRowIndex === null) {
      return null;
    }
    if (selectedRowIndex < 0 || selectedRowIndex >= displayRows.length) {
      return null;
    }
    const r = displayRows[selectedRowIndex];
    return Array.isArray(r) ? r : null;
  }, [displayRows, selectedRowIndex]);

  const selectedProjectId =
    selectedRow && projectIdColumnIndex >= 0 ?
      parseProjectIdFromRow(selectedRow, projectIdColumnIndex)
    : null;

  const selectedClientId =
    selectedRow && clientIdColumnIndex >= 0 && clientIdColumnIndex < selectedRow.length ?
      parsePositiveIntId(selectedRow[clientIdColumnIndex])
    : null;

  const canOpenGridEditPrompt =
    selectedRow !== null &&
    list != null &&
    list.columns.length > 0 &&
    (clientIdColumnIndex >= 0 || projectIdColumnIndex >= 0) &&
    (selectedProjectId !== null || selectedClientId !== null);

  const openEditForRow = useCallback(
    (row: (string | number | boolean | null)[] | unknown) => {
      if (!list?.columns?.length) {
        return;
      }
      if (!Array.isArray(row)) {
        return;
      }
      if (clientIdColumnIndex < 0 && projectIdColumnIndex < 0) {
        return;
      }

      const projectId =
        projectIdColumnIndex >= 0 ? parseProjectIdFromRow(row, projectIdColumnIndex) : null;
      const clientId =
        clientIdColumnIndex >= 0 && clientIdColumnIndex < row.length ?
          parsePositiveIntId(row[clientIdColumnIndex] as string | number | boolean | null)
        : null;

      if (projectId === null && clientId === null) {
        return;
      }

      const clientLabel = buildClientLabelFromRow(list.columns, row as (string | number | boolean | null)[], clientId);

      setGridEditPrompt({
        clientLabel,
        gridColumns: list.columns,
        gridRow: row as (string | number | boolean | null)[],
        existingProjectId: projectId,
        canCreateNew: clientId !== null,
      });
    },
    [list, projectIdColumnIndex, clientIdColumnIndex],
  );

  const handleRowDoubleClick = useCallback(
    (e: MouseEvent<HTMLTableRowElement>, row: (string | number | boolean | null)[] | unknown) => {
      e.preventDefault();
      e.stopPropagation();
      if (!list?.columns?.length || !Array.isArray(row)) {
        return;
      }

      const projectId =
        projectIdColumnIndex >= 0 ? parseProjectIdFromRow(row, projectIdColumnIndex) : null;

      if (projectId !== null) {
        navigate(`/projects/${projectId}`, {
          state: {
            fromGrid: true as const,
            gridColumns: list.columns,
            gridRow: row as (string | number | boolean | null)[],
          },
        });
        return;
      }

      openEditForRow(row);
    },
    [list, navigate, openEditForRow, projectIdColumnIndex],
  );

  const handleEditSelected = useCallback(() => {
    if (selectedRowIndex === null || !displayRows.length) {
      return;
    }
    const row = displayRows[selectedRowIndex];
    openEditForRow(row);
  }, [displayRows, openEditForRow, selectedRowIndex]);

  const handleOpenHardDelete = useCallback(() => {
    if (!list?.columns?.length || selectedRowIndex === null || !displayRows.length) {
      flash('error', 'Select a project row first.');
      return;
    }
    if (selectedProjectId === null) {
      flash('error', 'This row has no ProjectID, so it cannot be deleted.');
      return;
    }
    const row = displayRows[selectedRowIndex];
    if (!Array.isArray(row)) {
      return;
    }
    setHardDeleteConfirmed(false);
    setHardDeletePrompt({
      projectId: selectedProjectId,
      label: buildProjectLabelFromRow(list.columns, row, selectedProjectId),
    });
  }, [displayRows, flash, list, selectedProjectId, selectedRowIndex]);

  const handleConfirmHardDelete = useCallback(async () => {
    if (!hardDeletePrompt || !hardDeleteConfirmed || hardDeleteBusy) {
      return;
    }
    setHardDeleteBusy(true);
    try {
      const result = await apiPostJson<{ message: string }>(
        `/api/projects/${hardDeletePrompt.projectId}/hard-delete`,
        {},
      );
      setHardDeletePrompt(null);
      setHardDeleteConfirmed(false);
      setSelectedRowIndex(null);
      flash('success', result.message ?? 'Project permanently deleted.');
      await loadProjectList(true);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not permanently delete the project.');
    } finally {
      setHardDeleteBusy(false);
    }
  }, [flash, hardDeleteBusy, hardDeleteConfirmed, hardDeletePrompt, loadProjectList]);

  const handleToggleOnline = useCallback(
    async (e: MouseEvent, projectId: number, currentlyOnline: boolean) => {
      e.preventDefault();
      e.stopPropagation();
      if (projectId <= 0) {
        return;
      }
      const nextOnline = !currentlyOnline;
      setOnlineOverrides((prev) => ({ ...prev, [projectId]: nextOnline }));
      try {
        const result = await apiPostJson<{ message: string; isOnline: boolean }>(
          `/api/projects/${projectId}/online`,
          { isOnline: nextOnline },
        );
        setList((prev) => {
          if (!prev || isOnlineColumnIndex < 0) {
            return prev;
          }
          const rows = prev.rows.map((row) => {
            if (!Array.isArray(row)) {
              return row;
            }
            const id =
              projectIdColumnIndex >= 0 ? parseProjectIdFromRow(row, projectIdColumnIndex) : null;
            if (id !== projectId) {
              return row;
            }
            const next = [...row];
            next[isOnlineColumnIndex] = nextOnline;
            return next;
          });
          return { ...prev, rows };
        });
        flash('success', result.message ?? (nextOnline ? 'Project is now Online.' : 'Project is now Offline.'));
      } catch (err) {
        setOnlineOverrides((prev) => ({ ...prev, [projectId]: currentlyOnline }));
        flash('error', err instanceof ApiError ? err.message : 'Could not update online status.');
      }
    },
    [flash, isOnlineColumnIndex, projectIdColumnIndex],
  );

  const runHomeProjectReport = useCallback(
    async (projectId: number, mode: ProjectReportRunMode) => {
      setProjectContextMenu(null);
      if (projectId <= 0 || projectReportBusy) {
        return;
      }
      setProjectReportBusy(true);
      try {
        await runProjectReportWorkflow({
          projectId,
          mode,
          flash,
        });
      } catch (err) {
        flash(
          'error',
          err instanceof ApiError ? err.message : 'Could not generate the project report.',
        );
      } finally {
        setProjectReportBusy(false);
      }
    },
    [flash, projectReportBusy],
  );

  const resetBulkWizard = () => {
    setBulkWizardOpen(false);
    setBulkRows([]);
    setBulkSkippedEmpty(0);
    setBulkVerifyOk(false);
    setBulkVerifyErrors([]);
    setBulkRowErrors([]);
    setBulkBusy(false);
  };

  const handleBulkImportFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) {
      return;
    }
    const formData = new FormData();
    formData.append('file', file);
    setBulkBusy(true);
    setBulkVerifyOk(false);
    setBulkVerifyErrors([]);
    setBulkRowErrors([]);
    try {
      const result = await apiPostFormData<HomeBulkParseResult>(
        '/api/home/client-project/bulk-import/parse',
        formData,
      );
      setBulkRows(result.rows);
      setBulkSkippedEmpty(result.skippedEmptyRows);
      setBulkWizardOpen(true);
      if (result.rows.length === 0) {
        flash('error', 'No importable rows were found (each row needs ClientName and ProjectNumber).');
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not read the CSV file.');
    } finally {
      setBulkBusy(false);
    }
  };

  const handleBulkVerify = async () => {
    if (bulkRows.length === 0) {
      return;
    }
    setBulkBusy(true);
    setBulkVerifyOk(false);
    setBulkVerifyErrors([]);
    setBulkRowErrors([]);
    try {
      const result = await apiPostJson<HomeBulkVerifyResult>('/api/home/client-project/bulk-import/verify', {
        rows: bulkRows,
      });
      setBulkVerifyErrors(result.errors ?? []);
      setBulkVerifyOk(!!result.valid);
      if (result.valid) {
        flash(
          'success',
          'Verification passed. You can import these rows (clients and projects; user group and services are left empty).',
        );
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Verification request failed.');
    } finally {
      setBulkBusy(false);
    }
  };

  const handleBulkCommit = async () => {
    if (!bulkVerifyOk || bulkRows.length === 0) {
      return;
    }
    setBulkBusy(true);
    try {
      const result = await apiPostJson<HomeBulkCommitResult>('/api/home/client-project/bulk-import/commit', {
        rows: bulkRows,
      });
      const errs = result.rowErrors ?? [];
      setBulkRowErrors(errs);
      let msg = `Imported ${result.importedCount} client/project row(s).`;
      if (errs.length > 0) {
        msg += ` ${errs.length} row(s) reported errors (see preview).`;
        flash('error', msg);
      } else {
        flash('success', msg);
        resetBulkWizard();
      }
      await loadProjectList(true);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Import failed.');
    } finally {
      setBulkBusy(false);
    }
  };

  useEffect(() => {
    if (!gridEditPrompt && !hardDeletePrompt) {
      return;
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        if (hardDeleteBusy) {
          return;
        }
        setGridEditPrompt(null);
        setHardDeletePrompt(null);
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [gridEditPrompt, hardDeleteBusy, hardDeletePrompt]);

  return (
    <>
      {gridEditPrompt ?
        <div
          className="home-modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="home-grid-edit-title"
          onClick={() => setGridEditPrompt(null)}
        >
          <div className="home-modal-dialog" onClick={(e) => e.stopPropagation()}>
            <h2 id="home-grid-edit-title" className="home-modal-title">
              Open or create project
            </h2>
            <p className="home-modal-body">
              {gridEditPrompt.existingProjectId !== null ?
                <>
                  Client <strong>{gridEditPrompt.clientLabel}</strong> has project ID{' '}
                  <strong>{gridEditPrompt.existingProjectId}</strong> on this row.
                </>
              : <>
                  No project is on file for client <strong>{gridEditPrompt.clientLabel}</strong> for this row (for
                  example, there may be no project or broker assignment yet).
                </>}
              {' '}
              Choose whether to open the existing project or start a new one for this client.
            </p>
            {!gridEditPrompt.canCreateNew ?
              <p className="field-help home-modal-hint" role="note">
                New project requires a <strong>ClientID</strong> column value on this row.
              </p>
            : null}
            <div className="home-modal-actions">
              <button type="button" className="secondary-button" onClick={() => setGridEditPrompt(null)}>
                Cancel
              </button>
              {gridEditPrompt.canCreateNew ?
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => {
                    navigate('/projects/new', {
                      state: {
                        fromGrid: true as const,
                        gridColumns: gridEditPrompt.gridColumns,
                        gridRow: gridEditPrompt.gridRow,
                      },
                    });
                    setGridEditPrompt(null);
                  }}
                >
                  Create new project
                </button>
              : null}
              {gridEditPrompt.existingProjectId !== null ?
                <button
                  type="button"
                  className="primary-button"
                  onClick={() => {
                    navigate(`/projects/${gridEditPrompt.existingProjectId}`, {
                      state: {
                        fromGrid: true as const,
                        gridColumns: gridEditPrompt.gridColumns,
                        gridRow: gridEditPrompt.gridRow,
                      },
                    });
                    setGridEditPrompt(null);
                  }}
                >
                  Open project
                </button>
              : null}
            </div>
          </div>
        </div>
      : null}

      {hardDeletePrompt ?
        <div
          className="home-modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="home-hard-delete-title"
          onClick={() => {
            if (!hardDeleteBusy) {
              setHardDeletePrompt(null);
            }
          }}
        >
          <div className="home-modal-dialog" onClick={(e) => e.stopPropagation()}>
            <h2 id="home-hard-delete-title" className="home-modal-title">
              Permanently delete project
            </h2>
            <p className="home-modal-body">
              This hard-deletes <strong>{hardDeletePrompt.label}</strong> (Project ID{' '}
              {hardDeletePrompt.projectId}). It cannot be undone.
            </p>
            <p className="field-help home-modal-hint">
              Related time entries, calendar rows, files, tags, services, users, notes, and revenue for this
              project are removed. Azure blobs for project files are deleted. Client records are kept. Activity log
              history remains.
            </p>
            <label className="home-hard-delete-confirm">
              <input
                type="checkbox"
                checked={hardDeleteConfirmed}
                disabled={hardDeleteBusy}
                onChange={(e) => setHardDeleteConfirmed(e.target.checked)}
              />
              I understand this is a permanent hard delete
            </label>
            <div className="home-modal-actions">
              <button
                type="button"
                className="secondary-button"
                disabled={hardDeleteBusy}
                onClick={() => setHardDeletePrompt(null)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="danger-button"
                disabled={!hardDeleteConfirmed || hardDeleteBusy}
                onClick={() => void handleConfirmHardDelete()}
              >
                {hardDeleteBusy ? 'Deleting...' : 'Hard delete'}
              </button>
            </div>
          </div>
        </div>
      : null}

      <section className="hero">
        <h1>Client Management System</h1>
        <p>
          Manage clients, projects, advisory services, administrative data, and dashboard reporting.
        </p>
        <p className="home-build" aria-label="Application build">
          Build {APP_BUILD_NUMBER}
        </p>
        <p className="hero-feedback">
          <a
            className="home-send-feedback-link"
            href="mailto:scott.christian@bmsgroup.com?cc=catbottest%40bmsgroup.com&subject=CMS%20Feedback"
          >
            Send Feedback
          </a>
        </p>
      </section>

      {summaryError ? <div className="alert alert-error narrow-card">{summaryError}</div> : null}

      <section className="tile-grid tile-grid--home" aria-label="Summary statistics">
        <div className="workflow-tile workflow-tile--stat" role="region" aria-label="Active Projects">
          <span className="tile-title">Active Projects</span>
          <span className="tile-stat-value">{summary != null ? summary.activeProjectCount : '...'}</span>
          <span className="tile-stat-detail">All Active projects</span>
        </div>
        <div className="workflow-tile workflow-tile--stat" role="region" aria-label="Total Revenue (USD)">
          <span className="tile-title">Total Revenue (USD)</span>
          <span className="tile-stat-value">{summary != null ? revenueLabel : '...'}</span>
          <span className="tile-stat-detail">All Active projects</span>
        </div>
        <div className="workflow-tile workflow-tile--stat" role="region" aria-label="Online Projects">
          <span className="tile-title">Online Projects</span>
          <span className="tile-stat-value">{onlineProjectCountLabel}</span>
          <span className="tile-stat-detail">Currently online</span>
        </div>
        <div className="workflow-tile workflow-tile--stat" role="region" aria-label="Online Revenue">
          <span className="tile-title">Online Revenue</span>
          <span className="tile-stat-value">{summary != null ? onlineRevenueLabel : '...'}</span>
          <span className="tile-stat-detail">Online projects</span>
        </div>
      </section>

      <div className="dashboard-tabular-fullbleed home-client-project-list-bleed" aria-label="Client and project list">
        <div className="dashboard-tabular-inner">
          <div className="home-client-project-list-header">
            <h2 className="home-client-project-list-title">Client / Project list</h2>
            <div className="page-header-actions">
              <input
                id="home-bulk-csv"
                type="file"
                accept=".csv,text/csv"
                className="visually-hidden"
                disabled={bulkBusy}
                onChange={(ev) => void handleBulkImportFile(ev)}
              />
              <label
                htmlFor="home-bulk-csv"
                className={
                  'secondary-button bulk-import-label' + (bulkBusy || bulkWizardOpen ? ' disabled-label' : '')
                }
              >
                Bulk import
              </label>
            </div>
          </div>

          {listError ? <div className="alert alert-error">{listError}</div> : null}
          {filterOptionsError ? <div className="alert alert-error">{filterOptionsError}</div> : null}

          <div
            ref={homeFilterBarRef}
            className="dashboard-tabular-filters dashboard-tabular-filters--home"
            role="search"
            aria-label="Client project list filters by business unit, business class, client status, online status, client, project type, and analytics lead"
          >
            <div className="home-filter-fields">
            <div className="dashboard-filter-field dashboard-filter-field--bu">
              <label htmlFor="home-filter-business-unit">Business Unit</label>
              <select
                id="home-filter-business-unit"
                className="home-filter-select--fit"
                value={filterBusinessUnit}
                onChange={(e) => setFilterBusinessUnit(e.target.value)}
                disabled={listLoading}
                title={filterBusinessUnit || 'All business units'}
              >
                <option value="">All business units</option>
                {(filterOptions?.businessUnitNames ?? []).map((s) => (
                  <option key={s} value={s} title={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field dashboard-filter-field--bc">
              <label htmlFor="home-filter-business-class">Business Class</label>
              <select
                id="home-filter-business-class"
                className="home-filter-select--fit"
                value={filterBusinessClass}
                onChange={(e) => setFilterBusinessClass(e.target.value)}
                disabled={listLoading}
                title={filterBusinessClass || 'All business classes'}
              >
                <option value="">All business classes</option>
                {(filterOptions?.businessClassNames ?? []).map((s) => (
                  <option key={s} value={s} title={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field dashboard-filter-field--status">
              <label htmlFor="home-filter-status">Status</label>
              <select
                id="home-filter-status"
                className="home-filter-select--fit"
                value={filterStatus}
                onChange={(e) => setFilterStatus(e.target.value)}
                disabled={listLoading}
                title={filterStatus || 'All statuses'}
              >
                <option value="">All statuses</option>
                {(filterOptions?.statusNames ?? []).map((s) => (
                  <option key={s} value={s} title={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field dashboard-filter-field--online">
              <label htmlFor="home-filter-online">Online</label>
              <select
                id="home-filter-online"
                className="home-filter-select--fit"
                value={filterOnline}
                onChange={(e) => setFilterOnline(e.target.value)}
                disabled={listLoading}
              >
                <option value="">All</option>
                <option value="Online">Online</option>
                <option value="Offline">Offline</option>
              </select>
            </div>
            <div className="dashboard-filter-field dashboard-filter-field--analytics-lead">
              <label htmlFor="home-filter-analytics-lead">Analytics Lead</label>
              <select
                id="home-filter-analytics-lead"
                className="home-filter-select--fit"
                value={filterAnalyticsLeads[0] ?? ''}
                onChange={(e) => {
                  const next = e.target.value.trim();
                  setFilterAnalyticsLeads(next ? [next] : []);
                }}
                disabled={listLoading}
                title={filterAnalyticsLeads[0] || 'All analytics leads'}
              >
                <option value="">All analytics leads</option>
                {(filterOptions?.analyticsLeadNames ?? []).map((s) => (
                  <option key={s} value={s} title={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field dashboard-filter-field--grow dashboard-filter-field--client">
              <label htmlFor="home-filter-client">Client</label>
              <select
                id="home-filter-client"
                value={clientNameSearch}
                onChange={(e) => setClientNameSearch(e.target.value)}
                disabled={listLoading}
                title={clientNameSearch || 'All clients'}
              >
                <option value="">All clients</option>
                {(filterOptions?.clientNames ?? []).map((s) => (
                  <option key={s} value={s} title={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field dashboard-filter-field--project-type">
              <label htmlFor="home-filter-project-type">Project Type</label>
              <select
                id="home-filter-project-type"
                className="home-filter-select--fit"
                value={filterProjectType}
                onChange={(e) => setFilterProjectType(e.target.value)}
                disabled={listLoading}
                title={filterProjectType || 'All project types'}
              >
                <option value="">All project types</option>
                {(filterOptions?.projectTypeNames ?? []).map((s) => (
                  <option key={s} value={s} title={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            </div>
            <div className="dashboard-filter-actions">
              <button
                type="button"
                className="primary-button"
                onClick={handleEditSelected}
                disabled={!canOpenGridEditPrompt || listLoading}
              >
                Edit
              </button>
              <button
                type="button"
                className="danger-button"
                onClick={handleOpenHardDelete}
                disabled={selectedProjectId === null || listLoading || hardDeleteBusy}
              >
                Delete
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={listLoading}
                onClick={() => void loadProjectList(false)}
              >
                Apply filters
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={listLoading}
                onClick={clearAllFilters}
                title="Reset to All clients with Online status Online"
              >
                Clear all
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={listLoading}
                onClick={() => void loadProjectList(true)}
              >
                {listLoading ? 'Loading...' : 'Refresh table'}
              </button>
            </div>
          </div>

          {listLoading && !list ? <p className="field-help">Loading...</p> : null}

          {list && list.columns.length > 0 && projectIdColumnIndex < 0 ?
            <div className="alert alert-error" role="alert">
              This grid has no <strong>ProjectID</strong> column, so Edit and double-click cannot open a project. Ensure the
              refreshed list includes <strong>ProjectID</strong>.
            </div>
          : null}

          {list && list.columns.length > 0 ?
            <div className="dashboard-table-wrap dashboard-table-wrap--full home-client-project-table-wrap">
              <table className="dashboard-data-table dashboard-data-table--full home-client-project-table">
                <thead>
                  <tr>
                    <th className="home-online-col" scope="col" title="Online / in progress">
                      <span className="sr-only">Online</span>
                    </th>
                    {visibleColumnIndexes.map((ci) => {
                      const c = list.columns[ci] ?? '';
                      return (
                      <th key={`${c}-${ci}`} className={homeProjectListCellClass(c)}>
                        <button
                          type="button"
                          className="home-client-project-table__sort-btn"
                          onClick={(e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            handleSortColumn(ci);
                          }}
                          aria-sort={
                            sortColumnIndex === ci ?
                              sortDirection === 'asc' ?
                                'ascending'
                              : 'descending'
                            : 'none'
                          }
                        >
                          {clientProjectListColumnHeaderForDisplay(c)}
                          <span className="home-client-project-table__sort-indicator" aria-hidden="true">
                            {clientProjectListSortIndicator(ci, sortColumnIndex, sortDirection)}
                          </span>
                        </button>
                        {isAnalyticsLeadColumn(c) && filterAnalyticsLeads.length > 0 ? (
                          <span className="home-column-filter-badge" title={`${filterAnalyticsLeads.length} analytics lead(s) selected`}>
                            {filterAnalyticsLeads.length}
                          </span>
                        ) : null}
                      </th>
                      );
                    })}
                  </tr>
                </thead>
                <tbody>
                  {displayRows.map((row, ri) => {
                    const rowProjectId =
                      projectIdColumnIndex >= 0 ? parseProjectIdFromRow(row, projectIdColumnIndex) : null;
                    const fromRow =
                      isOnlineColumnIndex >= 0 && isOnlineColumnIndex < row.length ?
                        cellIsTruthy(row[isOnlineColumnIndex])
                      : false;
                    const isOnline =
                      rowProjectId !== null && Object.prototype.hasOwnProperty.call(onlineOverrides, rowProjectId) ?
                        onlineOverrides[rowProjectId]
                      : fromRow;
                    const rowKey =
                      rowProjectId !== null ? `project-${rowProjectId}` : `row-${ri}-${String(row[0] ?? '')}`;
                    return (
                    <tr
                      key={rowKey}
                      className={selectedRowIndex === ri ? 'home-project-list-row--selected' : undefined}
                      onClick={() => setSelectedRowIndex(ri)}
                      onDoubleClick={(e) => handleRowDoubleClick(e, row)}
                      onContextMenu={(e) => {
                        e.preventDefault();
                        setSelectedRowIndex(ri);
                        if (rowProjectId == null) {
                          flash('error', 'This row has no ProjectID, so a report cannot be generated.');
                          return;
                        }
                        setProjectContextMenu({
                          projectId: rowProjectId,
                          label: buildProjectLabelFromRow(list.columns, row, rowProjectId),
                          x: e.clientX,
                          y: e.clientY,
                        });
                      }}
                      title="Click to select; right-click for report options; double-click to edit project"
                    >
                      <td className="home-online-col">
                        <button
                          type="button"
                          className={
                            'home-online-dot' +
                            (isOnline ? ' home-online-dot--on' : ' home-online-dot--off')
                          }
                          disabled={rowProjectId === null}
                          aria-pressed={isOnline}
                          aria-label={
                            isOnline ?
                              'Online - click to set Offline'
                            : 'Offline - click to set online'
                          }
                          title={
                            isOnline ?
                              'Online (in progress) - click to set Offline'
                            : 'Offline - click to set online'
                          }
                          onClick={(e) => {
                            if (rowProjectId === null) {
                              return;
                            }
                            void handleToggleOnline(e, rowProjectId, isOnline);
                          }}
                        />
                      </td>
                      {visibleColumnIndexes.map((ci) => (
                        <td key={ci} className={homeProjectListCellClass(list.columns[ci] ?? '')}>
                          {formatTableCell(row[ci], list.columns[ci])}
                        </td>
                      ))}
                    </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          : null}
          {list && list.columns.length === 0 ?
            <p className="field-help" role="status">
              No rows match the current filters.
            </p>
          : null}
        </div>
      </div>

      {bulkWizardOpen ?
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="home-bulk-import-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="home-bulk-import-title">Client / project bulk import preview</h2>
            <p className="field-help">
              CSV columns (no IDs): <strong>ClientName</strong>, <strong>ProjectNumber</strong>,{' '}
              <strong>ProjectName</strong>, <strong>ProjectType</strong>, <strong>ProjectStatus</strong> (required);
              optional client fields <strong>ClientShortName</strong>, <strong>ClientDescription</strong>,{' '}
              <strong>Status</strong> (<strong>StatusDescription</strong> from ClientRef to tStatus),{' '}
              <strong>BusinessUnit</strong>, <strong>BusinessClass</strong>, <strong>ClientType</strong> (all three
              together for new clients; must match tBusinessUnit / tBusinessClass / tClientType for existing clients),{' '}
              <strong>AccountExecutiveEmail</strong>, <strong>AnalyticsLeadEmail</strong>; optional project fields{' '}
              <strong>RenewalDate</strong>, <strong>DueDate</strong>, <strong>RevenueAmount</strong>,{' '}
              <strong>RevenueEffectiveDate</strong>. User group and services are not imported.
              {bulkSkippedEmpty > 0 ?
                ` ${bulkSkippedEmpty} CSV row(s) were skipped because ClientName or ProjectNumber was empty.`
              : ''}{' '}
              See <strong>ClientProjects_Template.csv</strong> in SqlScripts/Bulk Import Data.
            </p>

            {bulkVerifyOk ?
              <div className="bulk-verify-pass" role="status">
                <span className="bulk-verify-check" aria-hidden="true">
                  OK
                </span>
                <span>
                  Verification passed - you can select Import to load clients and projects (user group and services
                  remain empty).
                </span>
              </div>
            : null}

            {bulkVerifyErrors.length > 0 ?
              <div className="bulk-verify-fail" role="alert">
                <strong>Verification failed</strong>
                <ul className="bulk-verify-error-list">
                  {bulkVerifyErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            : null}

            {bulkRowErrors.length > 0 ?
              <div className="bulk-verify-fail" role="alert">
                <strong>Import row messages</strong>
                <ul className="bulk-verify-error-list">
                  {bulkRowErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            : null}

            <div className="bulk-preview-scroll">
              <table className="bulk-preview-table">
                <thead>
                  <tr>
                    <th>Line</th>
                    <th>Client</th>
                    <th>Status</th>
                    <th>BU</th>
                    <th>BC</th>
                    <th>Type</th>
                    <th>Project #</th>
                    <th>Project name</th>
                    <th>Proj. type</th>
                    <th>Proj. status</th>
                  </tr>
                </thead>
                <tbody>
                  {bulkRows.length === 0 ?
                    <tr>
                      <td colSpan={10}>
                        No rows to display. Ensure the CSV has a header row and at least one row with ClientName and
                        ProjectNumber.
                      </td>
                    </tr>
                  : bulkRows.map((r) => (
                      <tr key={r.lineNumber}>
                        <td>{r.lineNumber}</td>
                        <td>{r.clientName}</td>
                        <td>{r.clientStatus}</td>
                        <td>{r.businessUnit}</td>
                        <td>{r.businessClass}</td>
                        <td>{r.clientType}</td>
                        <td>{r.projectNumber}</td>
                        <td>{r.projectName}</td>
                        <td>{r.projectType}</td>
                        <td>{r.projectStatus}</td>
                      </tr>
                    ))
                  }
                </tbody>
              </table>
            </div>

            <div className="button-row bulk-import-actions">
              <button type="button" className="secondary-button" disabled={bulkBusy} onClick={resetBulkWizard}>
                Cancel
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={bulkBusy || bulkRows.length === 0}
                onClick={() => void handleBulkVerify()}
              >
                Verify
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={bulkBusy || !bulkVerifyOk || bulkRows.length === 0}
                onClick={() => void handleBulkCommit()}
              >
                Import
              </button>
            </div>
          </div>
        </div>
      : null}

      {projectContextMenu ? (
        <>
          <button
            type="button"
            className="admin-context-menu-backdrop"
            aria-label="Close project menu"
            onClick={() => setProjectContextMenu(null)}
          />
          <div
            className="home-project-context-menu"
            role="menu"
            aria-label={`Report options for ${projectContextMenu.label}`}
            style={{ top: projectContextMenu.y, left: projectContextMenu.x }}
          >
            <button
              type="button"
              role="menuitem"
              className="home-project-context-menu__item"
              disabled={projectReportBusy}
              onClick={() => void runHomeProjectReport(projectContextMenu.projectId, 'interactive')}
            >
              {projectReportBusy ? 'Generating…' : 'Generate Report'}
            </button>
            <button
              type="button"
              role="menuitem"
              className="home-project-context-menu__item"
              disabled={projectReportBusy}
              onClick={() =>
                void runHomeProjectReport(projectContextMenu.projectId, 'saveAndSendAttachment')
              }
            >
              {projectReportBusy ? 'Generating…' : 'Generate Report & Send'}
            </button>
          </div>
        </>
      ) : null}
    </>
  );
}
