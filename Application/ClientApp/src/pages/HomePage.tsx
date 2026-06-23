import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ChangeEvent, MouseEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import type { FlashFn } from '../App';
import { ApiError, apiGet, apiPostFormData, apiPostJson } from '../api';
import { normalizeClientProjectListModel, sortClientProjectListRows, clientProjectListSortIndicator, type ClientProjectListModel, type ClientProjectListSortDirection } from '../clientProjectList';
import { clientProjectListColumnHeaderForDisplay } from '../workflows/clientProjectListColumnLabels';
import type { ClientProjectListFilterOptions } from '../workflows/clientProjectListWorkflowTypes';
import {
  buildHomeClientProjectListUrl,
  buildHomeSummaryUrl,
  HOME_CLIENT_PROJECT_LIST_OPTIONS_URL,
} from '../workflows/homeClientProjectListWorkflow';

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
    n === 'clientstatus' ||
    n === 'projectstatus'
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
  uniqueClientCount: number;
  /** Distinct ProjectID in filtered list scope; omitted or zero when summary is global (unfiltered). */
  distinctProjectCount?: number;
  totalProjectRevenue: number;
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

function formatTableCell(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined) {
    return '';
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

export default function HomePage({ flash }: { flash: FlashFn }) {
  const navigate = useNavigate();
  const [summary, setSummary] = useState<HomeSummary | null>(null);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [list, setList] = useState<ClientProjectListModel | null>(null);
  const [listError, setListError] = useState<string | null>(null);
  const [listLoading, setListLoading] = useState(false);
  const [selectedRowIndex, setSelectedRowIndex] = useState<number | null>(null);
  const [sortColumnIndex, setSortColumnIndex] = useState<number | null>(null);
  const [sortDirection, setSortDirection] = useState<ClientProjectListSortDirection>('asc');

  const [filterOptions, setFilterOptions] = useState<ClientProjectListFilterOptions | null>(null);
  const [filterOptionsError, setFilterOptionsError] = useState<string | null>(null);
  const [filterBusinessUnit, setFilterBusinessUnit] = useState('');
  const [filterBusinessClass, setFilterBusinessClass] = useState('');
  // Home opens with the Status filter pre-set to "Active" so the initial list shows only active rows.
  const [filterStatus, setFilterStatus] = useState('Active');
  const [clientNameSearch, setClientNameSearch] = useState('');
  // ProjectType filter is sourced from unique PTDescription values in dbo.vProject_Current.
  const [filterProjectType, setFilterProjectType] = useState('');
  const [gridEditPrompt, setGridEditPrompt] = useState<HomeGridEditPromptState | null>(null);

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
    async (refresh: boolean) => {
      const bu = filterBusinessUnit;
      const bc = filterBusinessClass;
      const stat = filterStatus;
      const cli = clientNameSearch;
      const pt = filterProjectType;
      const listUrl = buildHomeClientProjectListUrl(refresh, bu, bc, stat, cli, pt);
      const summaryUrl = buildHomeSummaryUrl(bu, bc, stat, cli, pt);

      setListLoading(true);
      setListError(null);
      try {
        try {
          const data = await apiGet<ClientProjectListModel>(listUrl);
          setList(normalizeClientProjectListModel(data));
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
    [filterBusinessUnit, filterBusinessClass, filterStatus, clientNameSearch, filterProjectType, loadFilterOptions],
  );

  useEffect(() => {
    void loadProjectList(true);
    // Initial load only — changing Business Unit / Status / Client name requires Apply filters or Refresh.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Once the Status filter options come back from the API, snap our default
  // "Active" value to the canonical casing the database uses (e.g. "ACTIVE",
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

  const revenueLabel =
    summary != null
      ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(summary.totalProjectRevenue)
      : '—';

  const byTypeLines =
    summary?.projectTypeCounts?.length ?
      summary.projectTypeCounts
        .map((x) => {
          const label = (x.projectTypeDescription?.trim() || 'Unspecified').trim();
          return `${label}: ${x.projectCount}`;
        })
        .join(' · ')
    : '—';

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
    if (!gridEditPrompt) {
      return;
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setGridEditPrompt(null);
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [gridEditPrompt]);

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

      <section className="hero">
        <h1>Client Management System</h1>
        <p>
          Manage clients, projects, advisory services, administrative data, and dashboard reporting.
        </p>
      </section>

      {summaryError ? <div className="alert alert-error narrow-card">{summaryError}</div> : null}

      <section className="tile-grid tile-grid--home" aria-label="Summary statistics">
        <div className="workflow-tile workflow-tile--stat" role="region" aria-label="Active clients">
          <span className="tile-title">Active clients</span>
          <span className="tile-stat-value">{summary != null ? summary.uniqueClientCount : '…'}</span>
        </div>
        <div className="workflow-tile workflow-tile--stat" role="region" aria-label="Total Revenue (USD)">
          <span className="tile-title">Total Revenue (USD)</span>
          <span className="tile-stat-value">{summary != null ? revenueLabel : '…'}</span>
          <span className="tile-stat-detail">Active clients &amp; projects</span>
        </div>
        <div className="workflow-tile workflow-tile--stat" role="region" aria-label="Active projects by type">
          <span className="tile-title">Active projects by type</span>
          <span className="tile-stat-detail">{summary != null ? byTypeLines : '…'}</span>
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
          <p className="field-help home-client-project-list-intro">
            Use <strong>Bulk import</strong> to load clients and projects from CSV, click a column header to sort, or
            click a row and <strong>Edit</strong> (double-click a row to open the project).
          </p>

          {listError ? <div className="alert alert-error">{listError}</div> : null}
          {filterOptionsError ? <div className="alert alert-error">{filterOptionsError}</div> : null}

          <div
            className="dashboard-tabular-filters"
            role="search"
            aria-label="Client project list filters by business unit (BUName), business class (BCName), client status, client, and project type"
          >
            <div className="dashboard-filter-field">
              <label htmlFor="home-filter-business-unit">Business Unit</label>
              <select
                id="home-filter-business-unit"
                value={filterBusinessUnit}
                onChange={(e) => setFilterBusinessUnit(e.target.value)}
                disabled={listLoading}
              >
                <option value="">All business units</option>
                {(filterOptions?.businessUnitNames ?? []).map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field">
              <label htmlFor="home-filter-business-class">Business Class</label>
              <select
                id="home-filter-business-class"
                value={filterBusinessClass}
                onChange={(e) => setFilterBusinessClass(e.target.value)}
                disabled={listLoading}
              >
                <option value="">All business classes</option>
                {(filterOptions?.businessClassNames ?? []).map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field">
              <label htmlFor="home-filter-status">Status</label>
              <select
                id="home-filter-status"
                value={filterStatus}
                onChange={(e) => setFilterStatus(e.target.value)}
                disabled={listLoading}
              >
                <option value="">All statuses</option>
                {(filterOptions?.statusNames ?? []).map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field dashboard-filter-field--grow">
              <label htmlFor="home-filter-client">Client</label>
              <select
                id="home-filter-client"
                value={clientNameSearch}
                onChange={(e) => setClientNameSearch(e.target.value)}
                disabled={listLoading}
              >
                <option value="">All clients</option>
                {(filterOptions?.clientNames ?? []).map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div className="dashboard-filter-field">
              <label htmlFor="home-filter-project-type">Project Type</label>
              <select
                id="home-filter-project-type"
                value={filterProjectType}
                onChange={(e) => setFilterProjectType(e.target.value)}
                disabled={listLoading}
              >
                <option value="">All project types</option>
                {(filterOptions?.projectTypeNames ?? []).map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
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
                onClick={() => void loadProjectList(true)}
              >
                {listLoading ? 'Loading…' : 'Refresh table'}
              </button>
            </div>
          </div>

          {listLoading && !list ? <p className="field-help">Loading…</p> : null}

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
                    {list.columns.map((c, ci) => (
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
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {displayRows.map((row, ri) => {
                    const rowProjectId =
                      projectIdColumnIndex >= 0 ? parseProjectIdFromRow(row, projectIdColumnIndex) : null;
                    const rowKey =
                      rowProjectId !== null ? `project-${rowProjectId}` : `row-${ri}-${String(row[0] ?? '')}`;
                    return (
                    <tr
                      key={rowKey}
                      className={selectedRowIndex === ri ? 'home-project-list-row--selected' : undefined}
                      onClick={() => setSelectedRowIndex(ri)}
                      onDoubleClick={(e) => handleRowDoubleClick(e, row)}
                      title="Click to select; double-click to edit project with grid context"
                    >
                      {row.map((cell, ci) => (
                        <td key={ci} className={homeProjectListCellClass(list.columns[ci] ?? '')}>
                          {formatTableCell(cell)}
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
              <strong>ClientStatus</strong> (Active / Inactive / Prospect),{' '}
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
                  ✓
                </span>
                <span>
                  Verification passed — you can select Import to load clients and projects (user group and services
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
    </>
  );
}
