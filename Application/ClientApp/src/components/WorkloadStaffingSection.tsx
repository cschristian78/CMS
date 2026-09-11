import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiGet } from '../api';
import DashboardBarChart from './DashboardBarChart';
import {
  DashboardComboBar,
  DashboardScatterChart,
} from './PmDashboardCharts';
import {
  blankLabel,
  cellNumber,
  cellText,
  cellYmd,
  groupMetrics,
  isBlankRoleValue,
  projectIdOf,
  splitPeople,
  summarizePortfolio,
  type DashCell,
  type DashColIndex,
  type DashboardInactiveProject,
} from '../dashboard/dashboardData';
import {
  formatNumber,
  formatUsd,
  projectStatusColor,
  revenueTierColor,
} from '../dashboard/dashboardColors';
import { formatCalendarDateDisplay } from '../utils/dateInput';
import {
  clientProjectListSortIndicator,
  compareClientProjectListCellValues,
  type ClientProjectListSortDirection,
} from '../clientProjectList';

export function WorkloadTab({
  rows, col, roleColumns, userGroupOptions, filterUserGroup, filterRole, filterPerson, personOptions, roleColumnIndex,
  onFilterUserGroup, onFilterRole, onFilterPerson, onDrill,
}: {
  rows: DashCell[][];
  col: DashColIndex;
  roleColumns: string[];
  userGroupOptions: string[];
  filterUserGroup: string;
  filterRole: string;
  filterPerson: string;
  personOptions: string[];
  roleColumnIndex: number;
  onFilterUserGroup: (v: string) => void;
  onFilterRole: (v: string) => void;
  onFilterPerson: (v: string) => void;
  onDrill: (title: string, rows: DashCell[][]) => void;
}) {
  const byGroup = groupMetrics(rows, col, col.userGroupName);
  const gapRows = roleColumnIndex >= 0 ? rows.filter((r) => isBlankRoleValue(cellText(r, roleColumnIndex))) : [];
  const bookRows =
    roleColumnIndex >= 0 && filterPerson
      ? rows.filter((r) => splitPeople(cellText(r, roleColumnIndex)).includes(filterPerson))
      : roleColumnIndex >= 0
        ? rows.filter((r) => !isBlankRoleValue(cellText(r, roleColumnIndex)))
        : [];

  return (
    <>
      <CatDepartmentHoursExhibit />
      <div className="filter-row dashboard-tabular-filters">
        <div className="dashboard-filter-field">
          <label htmlFor="dash-filter-ug">User Group</label>
          <select id="dash-filter-ug" value={filterUserGroup} onChange={(e) => onFilterUserGroup(e.target.value)}>
            <option value="">All groups</option>
            {userGroupOptions.map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </div>
        <div className="dashboard-filter-field">
          <label htmlFor="dash-filter-role">Role</label>
          <select id="dash-filter-role" value={filterRole} onChange={(e) => onFilterRole(e.target.value)}>
            <option value="">Select a role</option>
            {roleColumns.map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </div>
        <div className="dashboard-filter-field">
          <label htmlFor="dash-filter-person">Person</label>
          <select id="dash-filter-person" value={filterPerson} onChange={(e) => onFilterPerson(e.target.value)} disabled={roleColumnIndex < 0}>
            <option value="">All assigned</option>
            {personOptions.map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </div>
      </div>
      <div className="pm-dash-grid">
        <DashboardComboBar
          title="Workload by Group"
          ariaLabel="Project count and TotalHours by UserGroupName"
          items={byGroup.map((m) => ({ label: m.label, count: m.count, hours: m.hours }))}
          onSelect={(label) => onDrill(`Group ${label}`, rows.filter((r) => blankLabel(cellText(r, col.userGroupName)) === label))}
        />
        <DashboardScatterChart
          title="Hours vs Revenue Efficiency"
          ariaLabel="TotalHours versus LatestRevenue by UserGroupName"
          xLabel="TotalHours"
          yLabel="LatestRevenue"
          points={byGroup.map((m) => ({ label: m.label, x: m.hours, y: m.revenue }))}
          onSelect={(label) => onDrill(`Group ${label}`, rows.filter((r) => blankLabel(cellText(r, col.userGroupName)) === label))}
        />
      </div>
      <section className="dashboard-chart-card">
        <h2 className="dashboard-chart-title">Role Coverage Gaps</h2>
        {roleColumnIndex < 0 ? (
          <p className="field-help">Select a role column to list projects with a blank assignment.</p>
        ) : (
          <div className="dashboard-table-wrap">
            <table className="dashboard-data-table dashboard-data-table--full home-client-project-table">
              <thead>
                <tr><th>Project ID</th><th>Client</th><th>Project Number</th><th></th></tr>
              </thead>
              <tbody>
                {gapRows.slice(0, 80).map((row, i) => {
                  const pid = projectIdOf(row, col);
                  return (
                    <tr key={pid ?? i} onClick={() => onDrill('Coverage gap', [row])}>
                      <td>{pid ?? ''}</td>
                      <td>{cellText(row, col.clientName)}</td>
                      <td>{cellText(row, col.projectNumber)}</td>
                      <td>{pid ? <Link to={`/projects/${pid}`}>Edit</Link> : null}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
      <section className="dashboard-chart-card">
        <h2 className="dashboard-chart-title">My Book of Business</h2>
        {roleColumnIndex < 0 ? (
          <p className="field-help">Select a role, then optionally a person.</p>
        ) : (
          <div className="dashboard-table-wrap">
            <table className="dashboard-data-table dashboard-data-table--full home-client-project-table">
              <thead>
                <tr><th>Project Number</th><th>Client</th><th>Project Status</th><th>Latest Revenue</th><th></th></tr>
              </thead>
              <tbody>
                {bookRows.slice(0, 80).map((row, i) => {
                  const pid = projectIdOf(row, col);
                  const status = blankLabel(cellText(row, col.projectStatus), 'Active');
                  return (
                    <tr key={pid ?? i}>
                      <td>{cellText(row, col.projectNumber)}</td>
                      <td>{cellText(row, col.clientName)}</td>
                      <td><span className="pm-chip" style={{ background: projectStatusColor(status) }}>{status}</span></td>
                      <td>{formatUsd(cellNumber(row, col.latestRevenue))}</td>
                      <td>{pid ? <Link to={`/projects/${pid}`}>Edit</Link> : null}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}

type CatStaffHoursRow = {
  userID: number;
  userFullName: string;
  email?: string | null;
  totalHours: number;
  projectCount: number;
  clientCount: number;
};

type CatHoursNamedMetric = {
  label: string;
  hours: number;
  revenue: number;
};

type CatClientHoursMetric = {
  clientID?: number | null;
  clientName: string;
  hours: number;
  latestRevenue: number;
  projectCount: number;
};

type CatAnalystWorkloadDetail = {
  userID: number;
  userFullName: string;
  totalHours: number;
  byRole: CatHoursNamedMetric[];
  byClient: CatClientHoursMetric[];
  byRevenue: CatHoursNamedMetric[];
};

function normalizeCatStaffRow(raw: Record<string, unknown>): CatStaffHoursRow | null {
  const idRaw = raw.userID ?? raw.UserID;
  const id = typeof idRaw === 'number' ? idRaw : Number(idRaw);
  if (!Number.isFinite(id) || id <= 0) {
    return null;
  }
  const hoursRaw = raw.totalHours ?? raw.TotalHours ?? 0;
  const hours = typeof hoursRaw === 'number' ? hoursRaw : Number(hoursRaw);
  return {
    userID: Math.trunc(id),
    userFullName: String(raw.userFullName ?? raw.UserFullName ?? '').trim() || `User ${id}`,
    email: raw.email != null ? String(raw.email) : raw.Email != null ? String(raw.Email) : null,
    totalHours: Number.isFinite(hours) ? hours : 0,
    projectCount: Number(raw.projectCount ?? raw.ProjectCount ?? 0) || 0,
    clientCount: Number(raw.clientCount ?? raw.ClientCount ?? 0) || 0,
  };
}

function normalizeCatNamedMetric(raw: Record<string, unknown>): CatHoursNamedMetric {
  const hoursRaw = raw.hours ?? raw.Hours ?? 0;
  const revRaw = raw.revenue ?? raw.Revenue ?? 0;
  return {
    label: String(raw.label ?? raw.Label ?? '').trim() || '(Unassigned)',
    hours: Number.isFinite(Number(hoursRaw)) ? Number(hoursRaw) : 0,
    revenue: Number.isFinite(Number(revRaw)) ? Number(revRaw) : 0,
  };
}

function normalizeCatClientMetric(raw: Record<string, unknown>): CatClientHoursMetric {
  const cidRaw = raw.clientID ?? raw.ClientID;
  const cid = cidRaw == null || cidRaw === '' ? null : Number(cidRaw);
  const hoursRaw = raw.hours ?? raw.Hours ?? 0;
  const revRaw = raw.latestRevenue ?? raw.LatestRevenue ?? 0;
  return {
    clientID: cid != null && Number.isFinite(cid) && cid > 0 ? Math.trunc(cid) : null,
    clientName: String(raw.clientName ?? raw.ClientName ?? '').trim() || '(No client)',
    hours: Number.isFinite(Number(hoursRaw)) ? Number(hoursRaw) : 0,
    latestRevenue: Number.isFinite(Number(revRaw)) ? Number(revRaw) : 0,
    projectCount: Number(raw.projectCount ?? raw.ProjectCount ?? 0) || 0,
  };
}

function normalizeCatAnalystDetail(raw: unknown): CatAnalystWorkloadDetail | null {
  if (raw === null || typeof raw !== 'object') {
    return null;
  }
  const o = raw as Record<string, unknown>;
  const idRaw = o.userID ?? o.UserID;
  const id = typeof idRaw === 'number' ? idRaw : Number(idRaw);
  if (!Number.isFinite(id) || id <= 0) {
    return null;
  }
  const hoursRaw = o.totalHours ?? o.TotalHours ?? 0;
  const byRole = Array.isArray(o.byRole) ? o.byRole : Array.isArray(o.ByRole) ? o.ByRole : [];
  const byClient = Array.isArray(o.byClient) ? o.byClient : Array.isArray(o.ByClient) ? o.ByClient : [];
  const byRevenue = Array.isArray(o.byRevenue) ? o.byRevenue : Array.isArray(o.ByRevenue) ? o.ByRevenue : [];
  return {
    userID: Math.trunc(id),
    userFullName: String(o.userFullName ?? o.UserFullName ?? '').trim() || `User ${id}`,
    totalHours: Number.isFinite(Number(hoursRaw)) ? Number(hoursRaw) : 0,
    byRole: byRole
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeCatNamedMetric),
    byClient: byClient
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeCatClientMetric),
    byRevenue: byRevenue
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeCatNamedMetric),
  };
}

function CatDepartmentHoursExhibit() {
  const [staff, setStaff] = useState<CatStaffHoursRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedUserId, setSelectedUserId] = useState<number | null>(null);
  const [detail, setDetail] = useState<CatAnalystWorkloadDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void (async () => {
      try {
        const raw = await apiGet<unknown>('/api/dashboard/workload/cat-staff');
        if (cancelled) {
          return;
        }
        const list = Array.isArray(raw) ? raw : [];
        setStaff(
          list
            .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
            .map(normalizeCatStaffRow)
            .filter((x): x is CatStaffHoursRow => x !== null),
        );
      } catch (e) {
        if (!cancelled) {
          setStaff([]);
          setError(e instanceof Error ? e.message : 'Could not load CAT department hours.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (selectedUserId == null) {
      setDetail(null);
      setDetailError(null);
      setDetailLoading(false);
      return;
    }
    let cancelled = false;
    setDetailLoading(true);
    setDetailError(null);
    void (async () => {
      try {
        const raw = await apiGet<unknown>(`/api/dashboard/workload/cat-staff/${selectedUserId}`);
        if (cancelled) {
          return;
        }
        const normalized = normalizeCatAnalystDetail(raw);
        if (!normalized) {
          setDetail(null);
          setDetailError('Analyst detail was empty or invalid.');
          return;
        }
        setDetail(normalized);
      } catch (e) {
        if (!cancelled) {
          setDetail(null);
          setDetailError(e instanceof Error ? e.message : 'Could not load analyst hours summary.');
        }
      } finally {
        if (!cancelled) {
          setDetailLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [selectedUserId]);

  const totalHours = staff.reduce((s, u) => s + u.totalHours, 0);
  const selectedName =
    detail?.userFullName
    ?? staff.find((u) => u.userID === selectedUserId)?.userFullName
    ?? '';

  return (
    <section className="dashboard-chart-card pm-cat-hours-exhibit" aria-label="CAT department hours by user">
      <div className="pm-detail-head">
        <h2 className="dashboard-chart-title">CAT Department Hours by Analyst</h2>
        {selectedUserId != null ? (
          <button type="button" className="secondary-button" onClick={() => setSelectedUserId(null)}>
            Clear analyst
          </button>
        ) : null}
      </div>
      <p className="field-help">
        Total hours from time entries for users with Department = CAT. Select an analyst to view hours by role,
        client, and revenue tier.
      </p>
      {error ? <div className="alert alert-error">{error}</div> : null}
      {loading ? <p className="field-help">Loading CAT hours...</p> : null}
      {!loading && !error && staff.length === 0 ? (
        <p className="field-help" role="status">No CAT department users found.</p>
      ) : null}
      {!loading && staff.length > 0 ? (
        <>
          <DashboardBarChart
            title="Hours by analyst"
            ariaLabel="Total hours by CAT department user"
            orientation="horizontal"
            className="pm-cat-hours-chart"
            valueKey="Hours"
            items={staff.map((u) => ({
              label: u.userFullName,
              value: u.totalHours,
              count: u.projectCount,
              displayValue: formatNumber(u.totalHours, 1),
              fill: selectedUserId === u.userID ? '#f08700' : '#20d4e8',
            }))}
            onSelect={(item) => {
              const found = staff.find((u) => u.userFullName === item.label);
              if (!found) {
                return;
              }
              setSelectedUserId((prev) => (prev === found.userID ? null : found.userID));
            }}
          />
          <p className="pm-exhibit-totals">
            Total Â· {formatNumber(staff.length)} analysts Â· {formatNumber(totalHours, 1)} hours
          </p>
        </>
      ) : null}

      {selectedUserId != null ? (
        <div className="pm-cat-analyst-summary">
          <h3 className="pm-cat-analyst-summary__title">
            {selectedName || 'Selected analyst'} â€” hours summary
          </h3>
          {detailLoading ? <p className="field-help">Loading analyst summary...</p> : null}
          {detailError ? <div className="alert alert-error">{detailError}</div> : null}
          {detail && !detailLoading ? (
            <>
              <p className="pm-exhibit-totals">
                Analyst total Â· {formatNumber(detail.totalHours, 1)} hours
              </p>
              <div className="pm-dash-grid pm-cat-analyst-grid">
                <section className="dashboard-chart-card" aria-label="Hours by role">
                  <h4 className="dashboard-chart-title">By Role</h4>
                  <div className="dashboard-table-wrap">
                    <table className="dashboard-data-table dashboard-data-table--full">
                      <thead>
                        <tr>
                          <th>Role</th>
                          <th className="num">Hours</th>
                        </tr>
                      </thead>
                      <tbody>
                        {detail.byRole.length === 0 ? (
                          <tr><td colSpan={2}>No hours recorded.</td></tr>
                        ) : detail.byRole.map((row) => (
                          <tr key={row.label}>
                            <td>{row.label}</td>
                            <td className="num">{formatNumber(row.hours, 1)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
                <section className="dashboard-chart-card" aria-label="Hours by client">
                  <h4 className="dashboard-chart-title">By Client</h4>
                  <div className="dashboard-table-wrap">
                    <table className="dashboard-data-table dashboard-data-table--full">
                      <thead>
                        <tr>
                          <th>Client</th>
                          <th className="num">Hours</th>
                          <th className="num">Latest Revenue</th>
                          <th className="num">Projects</th>
                        </tr>
                      </thead>
                      <tbody>
                        {detail.byClient.length === 0 ? (
                          <tr><td colSpan={4}>No hours recorded.</td></tr>
                        ) : detail.byClient.map((row) => (
                          <tr key={`${row.clientID ?? 'x'}-${row.clientName}`}>
                            <td>{row.clientName}</td>
                            <td className="num">{formatNumber(row.hours, 1)}</td>
                            <td className="num">{formatUsd(row.latestRevenue)}</td>
                            <td className="num">{formatNumber(row.projectCount)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
                <section className="dashboard-chart-card" aria-label="Hours by revenue tier">
                  <h4 className="dashboard-chart-title">By Revenue Tier</h4>
                  <div className="dashboard-table-wrap">
                    <table className="dashboard-data-table dashboard-data-table--full">
                      <thead>
                        <tr>
                          <th>Revenue Tier</th>
                          <th className="num">Hours</th>
                          <th className="num">Latest Revenue</th>
                        </tr>
                      </thead>
                      <tbody>
                        {detail.byRevenue.length === 0 ? (
                          <tr><td colSpan={3}>No hours recorded.</td></tr>
                        ) : detail.byRevenue.map((row) => (
                          <tr key={row.label}>
                            <td>{row.label}</td>
                            <td className="num">{formatNumber(row.hours, 1)}</td>
                            <td className="num">{formatUsd(row.revenue)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
              </div>
            </>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

type DetailSortKey =
  | 'client'
  | 'projectNumber'
  | 'projectName'
  | 'type'
  | 'status'
  | 'tier'
  | 'revenue'
  | 'renewalDate'
  | 'accountExecutive'
  | 'analyticsLead';

const DETAIL_SORT_COLUMNS: {
  key: DetailSortKey;
  label: string;
  kind: 'text' | 'number';
}[] = [
  { key: 'client', label: 'Client', kind: 'text' },
  { key: 'projectNumber', label: 'Project Number', kind: 'text' },
  { key: 'projectName', label: 'Project Name', kind: 'text' },
  { key: 'type', label: 'Type', kind: 'text' },
  { key: 'status', label: 'Status', kind: 'text' },
  { key: 'tier', label: 'Tier', kind: 'text' },
  { key: 'revenue', label: 'Latest Revenue', kind: 'number' },
  { key: 'renewalDate', label: 'Renewal Date', kind: 'text' },
  { key: 'accountExecutive', label: 'Account Executive', kind: 'text' },
  { key: 'analyticsLead', label: 'Analytics Lead', kind: 'text' },
];

function detailSortValue(
  row: DashCell[],
  col: DashColIndex,
  key: DetailSortKey,
): string | number {
  switch (key) {
    case 'client':
      return cellText(row, col.clientName);
    case 'projectNumber':
      return cellText(row, col.projectNumber);
    case 'projectName':
      return cellText(row, col.projectName);
    case 'type':
      return cellText(row, col.ptDescription);
    case 'status':
      return blankLabel(cellText(row, col.projectStatus), 'Active');
    case 'tier':
      return blankLabel(cellText(row, col.revenueTier), 'Unknown');
    case 'revenue':
      return cellNumber(row, col.latestRevenue);
    case 'renewalDate':
      return cellYmd(row, col.renewalDate);
    case 'accountExecutive':
      return blankLabel(cellText(row, col.accountExecutive), '');
    case 'analyticsLead':
      return blankLabel(cellText(row, col.analyticsLead), '');
    default:
      return '';
  }
}


export function DetailGrid({
  title, rows, col, inactive, onClear,
}: {
  title: string;
  rows: DashCell[][];
  col: DashColIndex;
  inactive?: DashboardInactiveProject[];
  onClear?: () => void;
}) {
  const [sortKey, setSortKey] = useState<DetailSortKey | null>('client');
  const [sortDirection, setSortDirection] = useState<ClientProjectListSortDirection>('asc');

  useEffect(() => {
    setSortKey('client');
    setSortDirection('asc');
  }, [title]);

  const sortedRows = useMemo(() => {
    if (!sortKey) {
      return rows;
    }
    const colMeta = DETAIL_SORT_COLUMNS.find((c) => c.key === sortKey);
    const kind = colMeta?.kind ?? 'text';
    const indexed = rows.map((row, originalIndex) => ({ row, originalIndex }));
    indexed.sort((left, right) => {
      const cmp = compareClientProjectListCellValues(
        detailSortValue(left.row, col, sortKey),
        detailSortValue(right.row, col, sortKey),
        kind,
      );
      if (cmp !== 0) {
        return sortDirection === 'asc' ? cmp : -cmp;
      }
      return left.originalIndex - right.originalIndex;
    });
    return indexed.map((entry) => entry.row);
  }, [rows, col, sortKey, sortDirection]);

  const shownRows = sortedRows.slice(0, 200);
  const hiddenRows = sortedRows.slice(200);
  const hiddenKpis = hiddenRows.length > 0 ? summarizePortfolio(hiddenRows, col) : null;
  const allKpis = summarizePortfolio(sortedRows, col);

  const handleSort = (key: DetailSortKey) => {
    setSortKey((prev) => {
      if (prev === key) {
        setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
        return prev;
      }
      setSortDirection('asc');
      return key;
    });
  };

  return (
    <section className="dashboard-chart-card pm-detail">
      <div className="pm-detail-head">
        <h2 className="dashboard-chart-title">{title}</h2>
        {onClear ? <button type="button" className="secondary-button" onClick={onClear}>Clear chart filter</button> : null}
      </div>
      <div className="dashboard-table-wrap dashboard-table-wrap--full">
        <table className="dashboard-data-table dashboard-data-table--full home-client-project-table">
          <thead>
            <tr>
              {DETAIL_SORT_COLUMNS.map((column, ci) => (
                <th key={column.key} scope="col">
                  <button
                    type="button"
                    className="home-client-project-table__sort-btn"
                    onClick={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      handleSort(column.key);
                    }}
                    aria-sort={
                      sortKey === column.key ?
                        sortDirection === 'asc' ?
                          'ascending'
                        : 'descending'
                      : 'none'
                    }
                  >
                    {column.label}
                    <span className="home-client-project-table__sort-indicator" aria-hidden="true">
                      {clientProjectListSortIndicator(
                        ci,
                        sortKey === null ? null : DETAIL_SORT_COLUMNS.findIndex((c) => c.key === sortKey),
                        sortDirection,
                      )}
                    </span>
                  </button>
                </th>
              ))}
              <th scope="col"><span className="sr-only">Edit</span></th>
            </tr>
          </thead>
          <tbody>
            {inactive?.map((p) => (
              <tr key={`in-${p.projectID}`}>
                <td>{p.clientName}</td>
                <td>{p.projectNumber}</td>
                <td>{p.projectName}</td>
                <td></td>
                <td><span className="pm-chip" style={{ background: projectStatusColor('Inactive') }}>Inactive</span></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td><Link to={`/projects/${p.projectID}`}>Edit</Link></td>
              </tr>
            ))}
            {shownRows.map((row, i) => {
              const pid = projectIdOf(row, col);
              const status = blankLabel(cellText(row, col.projectStatus), 'Active');
              const tier = blankLabel(cellText(row, col.revenueTier), 'Unknown');
              const renewal = formatCalendarDateDisplay(cellYmd(row, col.renewalDate)) || 'â€”';
              return (
                <tr key={pid ?? i}>
                  <td>{cellText(row, col.clientName)}</td>
                  <td>{cellText(row, col.projectNumber)}</td>
                  <td>{cellText(row, col.projectName)}</td>
                  <td>{cellText(row, col.ptDescription)}</td>
                  <td><span className="pm-chip" style={{ background: projectStatusColor(status) }}>{status}</span></td>
                  <td><span className="pm-chip" style={{ background: revenueTierColor(tier) }}>{tier}</span></td>
                  <td>{formatUsd(cellNumber(row, col.latestRevenue))}</td>
                  <td>{renewal}</td>
                  <td>{blankLabel(cellText(row, col.accountExecutive), 'â€”')}</td>
                  <td>{blankLabel(cellText(row, col.analyticsLead), 'â€”')}</td>
                  <td>{pid ? <Link to={`/projects/${pid}`}>Edit</Link> : null}</td>
                </tr>
              );
            })}
          </tbody>
          <tfoot>
            {hiddenKpis ?
              <tr className="pm-exhibit-other-row">
                <td colSpan={6}>Other ({formatNumber(hiddenKpis.projects)} projects not shown)</td>
                <td>{formatUsd(hiddenKpis.revenue)}</td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
              </tr>
            : null}
            <tr className="pm-exhibit-total-row">
              <td colSpan={6}>Total</td>
              <td>{formatUsd(allKpis.revenue)}</td>
              <td></td>
              <td></td>
              <td></td>
              <td></td>
            </tr>
          </tfoot>
        </table>
      </div>
      {hiddenRows.length > 0 ?
        <p className="field-help">
          Showing first 200 of {sortedRows.length} projects. Remaining {hiddenKpis?.projects ?? 0} projects
          are grouped as Other.
        </p>
      : null}
    </section>
  );
}
