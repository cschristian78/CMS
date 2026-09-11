import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, apiGet } from '../api';
import { setCatAppMode } from '../catAppMode';
import DashboardBarChart from '../components/DashboardBarChart';
import { DashboardDonutChart } from '../components/PmDashboardCharts';
import { formatNumber } from '../dashboard/dashboardColors';
import { formatCalendarDateDisplay } from '../utils/dateInput';

type CatUser = { userID: number; userFullName: string; email?: string | null };

type NamedHours = {
  label: string;
  hours: number;
  id?: number | null;
  detail?: string | null;
  projectHours?: number;
  otherHours?: number;
  count?: number;
};

type MonthHours = {
  monthKey: string;
  label: string;
  projectHours: number;
  otherHours: number;
  totalHours: number;
};

type IncompleteWeek = {
  userID: number;
  userFullName: string;
  weekStartDate: string;
  hours: number;
};

type ReportKpis = {
  totalHours: number;
  projectHours: number;
  otherHours: number;
  projectSharePct: number;
  analystCount: number;
  projectCount: number;
  clientCount: number;
  capacityHours: number;
  utilizationPct: number;
  incompleteWeekCount: number;
  exactHours: number;
  allocatedHours: number;
};

type ReportResponse = {
  year: number;
  userID?: number | null;
  priorYear: number;
  availableYears: number[];
  catUsers: CatUser[];
  kpis: ReportKpis;
  priorYearKpis: ReportKpis;
  byAnalyst: NamedHours[];
  byCategory: NamedHours[];
  byClient: NamedHours[];
  byProject: NamedHours[];
  byTask: NamedHours[];
  byMonth: MonthHours[];
  byMonthPriorYear: MonthHours[];
  incompleteWeeks: IncompleteWeek[];
};

function num(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : 0;
}

function normalizeNamed(raw: Record<string, unknown>): NamedHours {
  return {
    label: String(raw.label ?? raw.Label ?? '').trim() || '(Unassigned)',
    hours: num(raw.hours ?? raw.Hours),
    id: raw.id != null || raw.Id != null ? num(raw.id ?? raw.Id) : null,
    detail: (raw.detail ?? raw.Detail ?? null) as string | null,
    projectHours: num(raw.projectHours ?? raw.ProjectHours),
    otherHours: num(raw.otherHours ?? raw.OtherHours),
    count: num(raw.count ?? raw.Count),
  };
}

function normalizeKpis(raw: Record<string, unknown> | null | undefined): ReportKpis {
  const o = raw ?? {};
  return {
    totalHours: num(o.totalHours ?? o.TotalHours),
    projectHours: num(o.projectHours ?? o.ProjectHours),
    otherHours: num(o.otherHours ?? o.OtherHours),
    projectSharePct: num(o.projectSharePct ?? o.ProjectSharePct),
    analystCount: num(o.analystCount ?? o.AnalystCount),
    projectCount: num(o.projectCount ?? o.ProjectCount),
    clientCount: num(o.clientCount ?? o.ClientCount),
    capacityHours: num(o.capacityHours ?? o.CapacityHours),
    utilizationPct: num(o.utilizationPct ?? o.UtilizationPct),
    incompleteWeekCount: num(o.incompleteWeekCount ?? o.IncompleteWeekCount),
    exactHours: num(o.exactHours ?? o.ExactHours),
    allocatedHours: num(o.allocatedHours ?? o.AllocatedHours),
  };
}

function normalizeReport(raw: unknown): ReportResponse | null {
  if (raw === null || typeof raw !== 'object') {
    return null;
  }
  const o = raw as Record<string, unknown>;
  const catUsersRaw = Array.isArray(o.catUsers) ? o.catUsers : Array.isArray(o.CatUsers) ? o.CatUsers : [];
  const yearsRaw = Array.isArray(o.availableYears)
    ? o.availableYears
    : Array.isArray(o.AvailableYears)
      ? o.AvailableYears
      : [];
  return {
    year: num(o.year ?? o.Year) || new Date().getFullYear(),
    userID: o.userID != null || o.UserID != null ? num(o.userID ?? o.UserID) : null,
    priorYear: num(o.priorYear ?? o.PriorYear) || new Date().getFullYear() - 1,
    availableYears: yearsRaw.map((y) => num(y)).filter((y) => y >= 2000),
    catUsers: catUsersRaw
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map((u) => ({
        userID: num(u.userID ?? u.UserID),
        userFullName: String(u.userFullName ?? u.UserFullName ?? '').trim() || `User ${u.userID ?? u.UserID}`,
        email: (u.email ?? u.Email ?? null) as string | null,
      }))
      .filter((u) => u.userID > 0),
    kpis: normalizeKpis((o.kpis ?? o.Kpis) as Record<string, unknown>),
    priorYearKpis: normalizeKpis((o.priorYearKpis ?? o.PriorYearKpis) as Record<string, unknown>),
    byAnalyst: (Array.isArray(o.byAnalyst) ? o.byAnalyst : Array.isArray(o.ByAnalyst) ? o.ByAnalyst : [])
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeNamed),
    byCategory: (Array.isArray(o.byCategory) ? o.byCategory : Array.isArray(o.ByCategory) ? o.ByCategory : [])
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeNamed),
    byClient: (Array.isArray(o.byClient) ? o.byClient : Array.isArray(o.ByClient) ? o.ByClient : [])
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeNamed),
    byProject: (Array.isArray(o.byProject) ? o.byProject : Array.isArray(o.ByProject) ? o.ByProject : [])
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeNamed),
    byTask: (Array.isArray(o.byTask) ? o.byTask : Array.isArray(o.ByTask) ? o.ByTask : [])
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map(normalizeNamed),
    byMonth: (Array.isArray(o.byMonth) ? o.byMonth : Array.isArray(o.ByMonth) ? o.ByMonth : [])
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map((m) => ({
        monthKey: String(m.monthKey ?? m.MonthKey ?? ''),
        label: String(m.label ?? m.Label ?? m.monthKey ?? m.MonthKey ?? ''),
        projectHours: num(m.projectHours ?? m.ProjectHours),
        otherHours: num(m.otherHours ?? m.OtherHours),
        totalHours: num(m.totalHours ?? m.TotalHours),
      })),
    byMonthPriorYear: (Array.isArray(o.byMonthPriorYear)
      ? o.byMonthPriorYear
      : Array.isArray(o.ByMonthPriorYear)
        ? o.ByMonthPriorYear
        : []
    )
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map((m) => ({
        monthKey: String(m.monthKey ?? m.MonthKey ?? ''),
        label: String(m.label ?? m.Label ?? m.monthKey ?? m.MonthKey ?? ''),
        projectHours: num(m.projectHours ?? m.ProjectHours),
        otherHours: num(m.otherHours ?? m.OtherHours),
        totalHours: num(m.totalHours ?? m.TotalHours),
      })),
    incompleteWeeks: (Array.isArray(o.incompleteWeeks)
      ? o.incompleteWeeks
      : Array.isArray(o.IncompleteWeeks)
        ? o.IncompleteWeeks
        : []
    )
      .filter((x): x is Record<string, unknown> => x !== null && typeof x === 'object')
      .map((w) => ({
        userID: num(w.userID ?? w.UserID),
        userFullName: String(w.userFullName ?? w.UserFullName ?? '').trim(),
        weekStartDate: String(w.weekStartDate ?? w.WeekStartDate ?? ''),
        hours: num(w.hours ?? w.Hours),
      })),
  };
}

function pctChange(current: number, prior: number): string {
  if (prior === 0) {
    return current === 0 ? '—' : 'n/a';
  }
  const pct = ((current - prior) / prior) * 100;
  const sign = pct > 0 ? '+' : '';
  return `${sign}${pct.toFixed(1)}%`;
}

function downloadCsv(filename: string, rows: string[][]) {
  const csv = rows.map((r) => r.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(',')).join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export default function TmsDashboardPage() {
  const currentYear = new Date().getFullYear();
  const [year, setYear] = useState(currentYear);
  const [userId, setUserId] = useState<number | ''>('');
  const [report, setReport] = useState<ReportResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [drillTitle, setDrillTitle] = useState('Detail');
  const [drillRows, setDrillRows] = useState<NamedHours[]>([]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const qs = new URLSearchParams();
      qs.set('year', String(year));
      if (userId !== '' && userId > 0) {
        qs.set('userId', String(userId));
      }
      const raw = await apiGet<unknown>(`/api/tms/report?${qs.toString()}`);
      const normalized = normalizeReport(raw);
      if (!normalized) {
        throw new Error('Report payload was empty or invalid.');
      }
      setReport(normalized);
      if (normalized.availableYears.length > 0 && !normalized.availableYears.includes(year)) {
        // keep selected year even if no data yet
      }
      setDrillTitle(`${normalized.year} analysts`);
      setDrillRows(normalized.byAnalyst);
    } catch (e) {
      setReport(null);
      setError(e instanceof ApiError ? e.message : e instanceof Error ? e.message : 'Could not load TM report.');
    } finally {
      setLoading(false);
    }
  }, [year, userId]);

  useEffect(() => {
    void load();
  }, [load]);

  const yearOptions = useMemo(() => {
    const fromReport = report?.availableYears ?? [];
    const set = new Set<number>([...fromReport, currentYear, currentYear - 1, year]);
    return [...set].filter((y) => y >= 2000 && y <= 2100).sort((a, b) => b - a);
  }, [report, currentYear, year]);

  const kpis = report?.kpis;
  const prior = report?.priorYearKpis;
  const priorYear = report?.priorYear ?? year - 1;

  const exportUtilization = () => {
    if (!report) {
      return;
    }
    const rows = [
      ['Year', 'User', 'Analyst', 'ProjectHours', 'OtherHours', 'TotalHours'],
      ...report.byAnalyst.map((a) => [
        String(report.year),
        userId === '' ? 'All CAT' : String(userId),
        a.label,
        String(a.projectHours ?? 0),
        String(a.otherHours ?? 0),
        String(a.hours),
      ]),
    ];
    downloadCsv(`tm-utilization-${report.year}.csv`, rows);
  };

  const exportTimesheetStyle = () => {
    if (!report) {
      return;
    }
    const rows = [
      ['Year', 'Type', 'Label', 'Detail', 'Hours'],
      ...report.byProject.map((p) => [
        String(report.year),
        'Project',
        p.label,
        p.detail ?? '',
        String(p.hours),
      ]),
      ...report.byCategory.map((c) => [
        String(report.year),
        'Other',
        c.label,
        '',
        String(c.hours),
      ]),
    ];
    downloadCsv(`tm-hours-detail-${report.year}.csv`, rows);
  };

  return (
    <>
      <section className="page-header client-workflow-header">
        <div className="page-header-top">
          <h1>
            <span className="logo-accent">T</span>
            <span className="logo-rest">M</span>
            <span className="tms-home-title-rest"> Dashboard</span>
          </h1>
          <div className="page-header-actions">
            <Link className="secondary-button" to="/tms" onClick={() => setCatAppMode('tm')}>
              Time entry
            </Link>
            <Link className="secondary-button" to="/" onClick={() => setCatAppMode('hub')}>
              Back to home
            </Link>
          </div>
        </div>
        <p>
          Utilization, project delivery, other-time mix, and timesheet completeness — filter by CAT user and year.
          YoY comparison uses {priorYear} as the prior period.
        </p>
      </section>

      <section className="card wide-card dashboard-shell tms-report-shell">
        <div className="filter-row dashboard-tabular-filters tms-report-filters">
          <div className="dashboard-filter-field">
            <label htmlFor="tms-report-year">Year</label>
            <select
              id="tms-report-year"
              value={year}
              disabled={loading}
              onChange={(e) => setYear(Number(e.target.value))}
            >
              {yearOptions.map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
          </div>
          <div className="dashboard-filter-field">
            <label htmlFor="tms-report-user">CAT user</label>
            <select
              id="tms-report-user"
              value={userId === '' ? '' : String(userId)}
              disabled={loading}
              onChange={(e) => setUserId(e.target.value ? Number(e.target.value) : '')}
            >
              <option value="">All CAT users</option>
              {(report?.catUsers ?? []).map((u) => (
                <option key={u.userID} value={u.userID}>
                  {u.userFullName}
                </option>
              ))}
            </select>
          </div>
          <div className="dashboard-filter-actions">
            <button type="button" className="secondary-button" disabled={loading} onClick={() => void load()}>
              {loading ? 'Loading…' : 'Refresh'}
            </button>
            <button type="button" className="secondary-button" disabled={!report || loading} onClick={exportUtilization}>
              Export utilization CSV
            </button>
            <button
              type="button"
              className="secondary-button"
              disabled={!report || loading}
              onClick={exportTimesheetStyle}
            >
              Export hours detail CSV
            </button>
          </div>
        </div>

        {error ? <div className="alert alert-error">{error}</div> : null}

        {kpis ? (
          <div className="pm-kpi-row tms-report-kpis" role="group" aria-label="TM report totals">
            <div className="pm-kpi">
              <span>Total hours</span>
              <strong>{formatNumber(kpis.totalHours, 1)}</strong>
              <em className="tms-kpi-yoy">YoY {pctChange(kpis.totalHours, prior?.totalHours ?? 0)}</em>
            </div>
            <div className="pm-kpi">
              <span>Project / other</span>
              <strong>
                {formatNumber(kpis.projectHours, 1)} / {formatNumber(kpis.otherHours, 1)}
              </strong>
              <em className="tms-kpi-yoy">{formatNumber(kpis.projectSharePct, 1)}% project</em>
            </div>
            <div className="pm-kpi">
              <span>Utilization</span>
              <strong>{formatNumber(kpis.utilizationPct, 1)}%</strong>
              <em className="tms-kpi-yoy">
                vs {formatNumber(kpis.capacityHours, 0)}h capacity · YoY{' '}
                {pctChange(kpis.utilizationPct, prior?.utilizationPct ?? 0)}
              </em>
            </div>
            <div className="pm-kpi">
              <span>Clients / projects</span>
              <strong>
                {formatNumber(kpis.clientCount)} / {formatNumber(kpis.projectCount)}
              </strong>
              <em className="tms-kpi-yoy">
                Incomplete weeks · {formatNumber(kpis.incompleteWeekCount)}
              </em>
            </div>
          </div>
        ) : null}

        {loading ? <p className="field-help">Loading TM report…</p> : null}

        {report && !loading ? (
          <>
            <div className="pm-dash-grid">
              <DashboardBarChart
                title={`Hours by analyst (${year})`}
                ariaLabel="Total hours by CAT analyst"
                orientation="horizontal"
                valueKey="Hours"
                items={report.byAnalyst.map((a) => ({
                  label: a.label,
                  value: a.hours,
                  displayValue: formatNumber(a.hours, 1),
                  count: a.count,
                }))}
                onSelect={(item) => {
                  const row = report.byAnalyst.find((a) => a.label === item.label);
                  setDrillTitle(`Analyst · ${item.label}`);
                  setDrillRows(
                    row
                      ? [
                          {
                            label: 'Project hours',
                            hours: row.projectHours ?? 0,
                          },
                          {
                            label: 'Other hours',
                            hours: row.otherHours ?? 0,
                          },
                        ]
                      : [],
                  );
                }}
              />
              <DashboardDonutChart
                title="Project vs other time"
                ariaLabel="Share of project hours versus other-time categories"
                items={[
                  { label: 'Project', value: report.kpis.projectHours },
                  { label: 'Other time', value: report.kpis.otherHours },
                ]}
                onSelect={(label) => {
                  if (label === 'Other time') {
                    setDrillTitle('Other-time categories');
                    setDrillRows(report.byCategory);
                  } else {
                    setDrillTitle('Projects');
                    setDrillRows(report.byProject);
                  }
                }}
              />
            </div>

            <div className="pm-dash-grid">
              <DashboardBarChart
                title="Other-time by category"
                ariaLabel="Hours by non-project category"
                orientation="horizontal"
                valueKey="Hours"
                items={report.byCategory.map((c) => ({
                  label: c.label,
                  value: c.hours,
                  displayValue: formatNumber(c.hours, 1),
                }))}
                onSelect={(item) => {
                  setDrillTitle(`Category · ${item.label}`);
                  setDrillRows(report.byCategory.filter((c) => c.label === item.label));
                }}
              />
              <DashboardDonutChart
                title="Exact vs allocated"
                ariaLabel="Hours entered as Exact versus Allocated"
                items={[
                  { label: 'Exact', value: report.kpis.exactHours },
                  { label: 'Allocated', value: report.kpis.allocatedHours },
                ]}
              />
            </div>

            <div className="pm-dash-grid">
              <DashboardBarChart
                title="Hours by client"
                ariaLabel="Project hours by client"
                orientation="horizontal"
                valueKey="Hours"
                items={report.byClient.slice(0, 20).map((c) => ({
                  label: c.label,
                  value: c.hours,
                  displayValue: formatNumber(c.hours, 1),
                  count: c.count,
                }))}
                onSelect={(item) => {
                  setDrillTitle(`Client · ${item.label}`);
                  setDrillRows(report.byClient.filter((c) => c.label === item.label));
                }}
              />
              <DashboardBarChart
                title="Hours by project"
                ariaLabel="Project hours by project"
                orientation="horizontal"
                valueKey="Hours"
                items={report.byProject.slice(0, 20).map((p) => ({
                  label: p.label,
                  value: p.hours,
                  displayValue: formatNumber(p.hours, 1),
                }))}
                onSelect={(item) => {
                  const row = report.byProject.find((p) => p.label === item.label);
                  setDrillTitle(`Project · ${item.label}`);
                  setDrillRows(row ? [row] : []);
                }}
              />
            </div>

            <div className="pm-dash-grid">
              <DashboardBarChart
                title={`Monthly trend (${year})`}
                ariaLabel="Monthly total hours for selected year"
                valueKey="Hours"
                items={report.byMonth.map((m) => ({
                  label: m.label,
                  value: m.totalHours,
                  displayValue: formatNumber(m.totalHours, 1),
                }))}
              />
              <DashboardBarChart
                title={`Monthly trend (${priorYear})`}
                ariaLabel="Monthly total hours for prior year"
                valueKey="Hours"
                items={report.byMonthPriorYear.map((m) => ({
                  label: m.label,
                  value: m.totalHours,
                  displayValue: formatNumber(m.totalHours, 1),
                  fill: '#5a7080',
                }))}
              />
            </div>

            <DashboardBarChart
              title="Top services / tasks"
              ariaLabel="Hours by calendar service or task"
              orientation="horizontal"
              valueKey="Hours"
              items={report.byTask.map((t) => ({
                label: t.label,
                value: t.hours,
                displayValue: formatNumber(t.hours, 1),
              }))}
              onSelect={(item) => {
                const row = report.byTask.find((t) => t.label === item.label);
                setDrillTitle(`Task · ${item.label}`);
                setDrillRows(row ? [row] : []);
              }}
            />

            <section className="dashboard-chart-card" aria-label="Incomplete weeks">
              <h2 className="dashboard-chart-title">Incomplete weeks (&lt; 32h logged)</h2>
              <p className="field-help">
                Weeks where the analyst logged some time but less than 32 hours (project + other time combined).
              </p>
              {report.incompleteWeeks.length === 0 ? (
                <p className="field-help" role="status">
                  No incomplete weeks in this filter.
                </p>
              ) : (
                <div className="dashboard-table-wrap">
                  <table className="dashboard-data-table dashboard-data-table--full">
                    <thead>
                      <tr>
                        <th>Week starting</th>
                        <th>Analyst</th>
                        <th className="num">Hours</th>
                      </tr>
                    </thead>
                    <tbody>
                      {report.incompleteWeeks.map((w) => (
                        <tr key={`${w.userID}-${w.weekStartDate}`}>
                          <td>{formatCalendarDateDisplay(w.weekStartDate) || w.weekStartDate}</td>
                          <td>{w.userFullName}</td>
                          <td className="num">{formatNumber(w.hours, 1)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="dashboard-chart-card pm-detail" aria-label="Report drill detail">
              <div className="pm-detail-head">
                <h2 className="dashboard-chart-title">{drillTitle}</h2>
              </div>
              <div className="dashboard-table-wrap">
                <table className="dashboard-data-table dashboard-data-table--full">
                  <thead>
                    <tr>
                      <th>Label</th>
                      <th>Detail</th>
                      <th className="num">Project h</th>
                      <th className="num">Other h</th>
                      <th className="num">Hours</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {drillRows.length === 0 ? (
                      <tr>
                        <td colSpan={6}>Select a chart slice to inspect detail.</td>
                      </tr>
                    ) : (
                      drillRows.map((row, i) => (
                        <tr key={`${row.label}-${i}`}>
                          <td>{row.label}</td>
                          <td>{row.detail ?? '—'}</td>
                          <td className="num">
                            {row.projectHours != null ? formatNumber(row.projectHours, 1) : '—'}
                          </td>
                          <td className="num">
                            {row.otherHours != null ? formatNumber(row.otherHours, 1) : '—'}
                          </td>
                          <td className="num">{formatNumber(row.hours, 1)}</td>
                          <td>
                            {row.id != null && row.id > 0 && drillTitle.startsWith('Project') ? (
                              <Link to={`/projects/${row.id}?tab=time`}>Open</Link>
                            ) : null}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </section>
          </>
        ) : null}
      </section>
    </>
  );
}
