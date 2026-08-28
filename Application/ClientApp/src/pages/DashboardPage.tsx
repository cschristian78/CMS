import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiGet } from '../api';
import DashboardBarChart, { type BarChartItem } from '../components/DashboardBarChart';
import {
  DashboardComboBar,
  DashboardDonutChart,
  DashboardHeatmap,
  DashboardLineChart,
  DashboardParetoChart,
  DashboardRevenueRenewalTimeline,
  DashboardScatterChart,
  DashboardTreemap,
} from '../components/PmDashboardCharts';
import {
  applyGlobalFilters,
  blankLabel,
  buildAgingRows,
  buildColIndex,
  buildRenewalTimeline,
  cellNumber,
  cellText,
  cellYmd,
  displayServiceName,
  distinctValues,
  formatMonthLabel,
  groupMetrics,
  isBlankRoleValue,
  isServiceActive,
  monthKey,
  normalizeDataset,
  onlineLabel,
  projectIdOf,
  quarterKey,
  sortTiers,
  splitPeople,
  summarizePortfolio,
  trendByMonth,
  type DashCell,
  type DashColIndex,
  type DashboardDataset,
  type DashboardInactiveProject,
  type DateField,
  type GlobalDashFilters,
  type TimelineGrain,
} from '../dashboard/dashboardData';
import {
  agingBucketColor,
  formatNumber,
  formatUsd,
  projectStatusColor,
  revenueTierColor,
  seriesColor,
} from '../dashboard/dashboardColors';
import { regionBarColor } from '../utils/regionBarColors';

type DashTab = 'executive' | 'portfolio' | 'workload' | 'services' | 'health';

type DrillState = {
  title: string;
  rows: DashCell[][];
  inactive?: DashboardInactiveProject[];
};

const DATE_FIELD_BY_TAB: Record<DashTab, DateField> = {
  executive: 'latestRevenueDate',
  portfolio: 'createdDate',
  workload: 'createdDate',
  services: 'createdDate',
  health: 'latestRevenueDate',
};

function metricBars(
  metrics: { label: string; count: number; revenue: number }[],
  mode: 'count' | 'revenue',
  fillFor?: (label: string, i: number) => string,
): BarChartItem[] {
  return metrics.map((m, i) => ({
    label: m.label,
    value: mode === 'revenue' ? m.revenue : m.count,
    colorKey: m.label,
    fill: fillFor?.(m.label, i),
    displayValue: mode === 'revenue' ? formatUsd(m.revenue) : formatNumber(m.count),
  }));
}

export default function DashboardPage() {
  const [dataset, setDataset] = useState<DashboardDataset | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<DashTab>('executive');
  const [filters, setFilters] = useState<GlobalDashFilters>({
    businessUnit: '',
    businessClass: '',
    projectStatus: '',
    statusDescription: 'Active',
    dateFrom: '',
    dateTo: '',
  });
  const [filterPt, setFilterPt] = useState('');
  const [filterUserGroup, setFilterUserGroup] = useState('');
  const [filterRole, setFilterRole] = useState('');
  const [filterPerson, setFilterPerson] = useState('');
  const [filterService, setFilterService] = useState('');
  const [matrixBy, setMatrixBy] = useState<'client' | 'project'>('client');
  const [drill, setDrill] = useState<DrillState | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const raw = await apiGet<unknown>('/api/dashboard/dataset');
      setDataset(normalizeDataset(raw));
      setDrill(null);
    } catch (e) {
      setDataset(null);
      setError(e instanceof Error ? e.message : 'Could not load dashboard dataset.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const col: DashColIndex | null = useMemo(
    () => (dataset ? buildColIndex(dataset.columns) : null),
    [dataset],
  );

  const optionLists = useMemo(() => {
    if (!dataset || !col) {
      return { bu: [] as string[], bc: [] as string[], projectStatus: [] as string[], pt: [] as string[], clientStatus: [] as string[], userGroup: [] as string[] };
    }
    return {
      bu: distinctValues(dataset.rows, col.businessUnit),
      bc: distinctValues(dataset.rows, col.businessClass),
      projectStatus: distinctValues(dataset.rows, col.projectStatus),
      pt: distinctValues(dataset.rows, col.ptDescription),
      clientStatus: distinctValues(dataset.rows, col.statusDescription),
      userGroup: distinctValues(dataset.rows, col.userGroupName),
    };
  }, [dataset, col]);

  const scopedRows = useMemo(() => {
    if (!dataset || !col) {
      return [] as DashCell[][];
    }
    let rows = applyGlobalFilters(dataset.rows, col, filters, DATE_FIELD_BY_TAB[tab]);
    if (tab === 'portfolio') {
      if (filterPt) {
        rows = rows.filter((r) => cellText(r, col.ptDescription) === filterPt);
      }
    }
    if (tab === 'workload' && filterUserGroup) {
      rows = rows.filter((r) => cellText(r, col.userGroupName) === filterUserGroup);
    }
    return rows;
  }, [dataset, col, filters, tab, filterPt, filterUserGroup]);

  const roleColumnIndex = useMemo(() => {
    if (!dataset || !filterRole) {
      return -1;
    }
    return dataset.columns.findIndex((c) => c === filterRole);
  }, [dataset, filterRole]);

  const personOptions = useMemo(() => {
    if (roleColumnIndex < 0) {
      return [] as string[];
    }
    const names = new Set<string>();
    scopedRows.forEach((row) => {
      splitPeople(cellText(row, roleColumnIndex)).forEach((n) => names.add(n));
    });
    return [...names].sort((a, b) => a.localeCompare(b));
  }, [scopedRows, roleColumnIndex]);

  const setFilter = (key: keyof GlobalDashFilters, value: string) => {
    setFilters((prev) => ({ ...prev, [key]: value }));
    setDrill(null);
  };

  const openDrill = (title: string, rows: DashCell[][], inactive?: DashboardInactiveProject[]) => {
    setDrill({ title, rows, inactive });
  };

  const healthRows = useMemo(() => {
    if (!dataset || !col) {
      return [] as DashCell[][];
    }
    return applyGlobalFilters(
      dataset.rows,
      col,
      { ...filters, statusDescription: '' },
      DATE_FIELD_BY_TAB.health,
    );
  }, [dataset, col, filters]);

  const kpis = useMemo(() => {
    if (!col) {
      return { projects: 0, clients: 0, revenue: 0, hours: 0 };
    }
    return summarizePortfolio(scopedRows, col);
  }, [scopedRows, col]);

  const detailRows = drill?.rows ?? scopedRows;

  return (
    <>
      <section className="page-header">
        <h1>Dashboard</h1>
        <p>
          Multi-tab project management view from <code>dbo.vProject_Current</code>, with TotalHours from
          time entries. Click a chart to filter the detail grid; each row opens Edit Project.
        </p>
      </section>

      <section className="card wide-card pm-dash-card">
        <div className="filter-row dashboard-tabular-filters pm-dash-global" role="search" aria-label="Dashboard filters">
          <div className="dashboard-filter-field">
            <label htmlFor="dash-filter-bu">Business Unit</label>
            <select id="dash-filter-bu" value={filters.businessUnit} onChange={(e) => setFilter('businessUnit', e.target.value)} disabled={loading}>
              <option value="">All</option>
              {optionLists.bu.map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>
          <div className="dashboard-filter-field">
            <label htmlFor="dash-filter-bc">Business Class</label>
            <select id="dash-filter-bc" value={filters.businessClass} onChange={(e) => setFilter('businessClass', e.target.value)} disabled={loading}>
              <option value="">All</option>
              {optionLists.bc.map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>
          <div className="dashboard-filter-field">
            <label htmlFor="dash-filter-ps">Project Status</label>
            <select id="dash-filter-ps" value={filters.projectStatus} onChange={(e) => setFilter('projectStatus', e.target.value)} disabled={loading}>
              <option value="">All</option>
              {optionLists.projectStatus.map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>
          <div className="dashboard-filter-field">
            <label htmlFor="dash-filter-cs">Client Status</label>
            <select id="dash-filter-cs" value={filters.statusDescription} onChange={(e) => setFilter('statusDescription', e.target.value)} disabled={loading}>
              <option value="">All</option>
              {optionLists.clientStatus.map((n) => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>
          <div className="dashboard-filter-field">
            <label htmlFor="dash-filter-from">From</label>
            <input id="dash-filter-from" type="date" value={filters.dateFrom} onChange={(e) => setFilter('dateFrom', e.target.value)} disabled={loading} />
          </div>
          <div className="dashboard-filter-field">
            <label htmlFor="dash-filter-to">To</label>
            <input id="dash-filter-to" type="date" value={filters.dateTo} onChange={(e) => setFilter('dateTo', e.target.value)} disabled={loading} />
          </div>
          <div className="dashboard-filter-actions">
            <button className="secondary-button" type="button" onClick={() => void load()} disabled={loading}>
              {loading ? 'Loading...' : 'Refresh'}
            </button>
          </div>
        </div>

        <p className="field-help">
          Totals use <code>dbo.vProject_Current</code> the same way as the home tiles (Active clients = distinct
          ClientID with an Active project). Date range applies to{' '}
          <strong>{tab === 'executive' || tab === 'health' ? 'LatestRevenueEffectiveDate' : 'CreatedDate'}</strong>
          {' '}on this tab.
        </p>

        {error ? <div className="alert alert-error">{error}</div> : null}

        <div className="pm-kpi-row" role="group" aria-label="Dashboard totals">
          <div className="pm-kpi"><span>Projects</span><strong>{formatNumber(kpis.projects)}</strong></div>
          <div className="pm-kpi">
            <span>{filters.statusDescription === 'Active' ? 'Active clients' : 'Clients'}</span>
            <strong>{formatNumber(kpis.clients)}</strong>
          </div>
          <div className="pm-kpi"><span>Latest revenue</span><strong>{formatUsd(kpis.revenue)}</strong></div>
          <div className="pm-kpi"><span>Hours</span><strong>{formatNumber(kpis.hours, 1)}</strong></div>
        </div>

        <div className="admin-tabs pm-dash-tabs" role="tablist" aria-label="Dashboard views">
          {([
            ['executive', 'Executive Overview'],
            ['portfolio', 'Project Portfolio & Pipeline'],
            ['workload', 'Workload & Staffing'],
            ['services', 'Service Coverage'],
            ['health', 'Client Health'],
          ] as const).map(([id, label]) => (
            <button
              key={id}
              type="button"
              className={tab === id ? 'active' : undefined}
              role="tab"
              aria-selected={tab === id}
              onClick={() => { setTab(id); setDrill(null); }}
            >
              {label}
            </button>
          ))}
        </div>

        {dataset && col && !loading ? (
          <>
            {tab === 'executive' ? (
              <ExecutiveTab rows={scopedRows} col={col} inactive={dataset.inactiveProjects} onDrill={openDrill} />
            ) : null}
            {tab === 'portfolio' ? (
              <PortfolioTab
                rows={scopedRows}
                col={col}
                ptOptions={optionLists.pt}
                filterPt={filterPt}
                onFilterPt={(v) => { setFilterPt(v); setDrill(null); }}
                onDrill={openDrill}
              />
            ) : null}
            {tab === 'workload' ? (
              <WorkloadTab
                rows={scopedRows}
                col={col}
                roleColumns={dataset.roleColumns}
                userGroupOptions={optionLists.userGroup}
                filterUserGroup={filterUserGroup}
                filterRole={filterRole}
                filterPerson={filterPerson}
                personOptions={personOptions}
                roleColumnIndex={roleColumnIndex}
                onFilterUserGroup={(v) => { setFilterUserGroup(v); setDrill(null); }}
                onFilterRole={(v) => { setFilterRole(v); setFilterPerson(''); setDrill(null); }}
                onFilterPerson={(v) => { setFilterPerson(v); setDrill(null); }}
                onDrill={openDrill}
              />
            ) : null}
            {tab === 'services' ? (
              <ServicesTab
                rows={scopedRows}
                col={col}
                columns={dataset.columns}
                serviceColumns={dataset.serviceColumns}
                filterService={filterService}
                matrixBy={matrixBy}
                onFilterService={(v) => { setFilterService(v); setDrill(null); }}
                onMatrixBy={setMatrixBy}
                onDrill={openDrill}
              />
            ) : null}
            {tab === 'health' ? (
              <HealthTab rows={healthRows} col={col} onDrill={openDrill} />
            ) : null}

            <DetailGrid
              title={drill ? drill.title : 'All filtered projects'}
              rows={detailRows}
              col={col}
              inactive={drill?.inactive}
              onClear={drill ? () => setDrill(null) : undefined}
            />
          </>
        ) : null}

        {loading ? <p className="field-help">Loading dashboard...</p> : null}
      </section>
    </>
  );
}

function ExecutiveTab({
  rows, col, inactive, onDrill,
}: {
  rows: DashCell[][];
  col: DashColIndex;
  inactive: DashboardInactiveProject[];
  onDrill: (title: string, rows: DashCell[][], inactive?: DashboardInactiveProject[]) => void;
}) {
  const [timelineGrain, setTimelineGrain] = useState<TimelineGrain>('month');
  const [selectedTimelineKey, setSelectedTimelineKey] = useState<string | null>(null);
  const byBu = groupMetrics(rows, col, col.businessUnit);
  const byTier = sortTiers(groupMetrics(rows, col, col.revenueTier, 'Unknown'));
  const byStatus = groupMetrics(rows, col, col.projectStatus, 'Active');
  const trend = trendByMonth(rows, col, col.latestRevenueDate, 'revenue');
  const topClients = groupMetrics(rows, col, col.clientName, '(No client)').slice(0, 15);
  const renewalTimeline = useMemo(
    () => buildRenewalTimeline(rows, col, timelineGrain),
    [rows, col, timelineGrain],
  );
  const statusItems = [
    ...byStatus.map((m) => ({ label: m.label, value: m.count, fill: projectStatusColor(m.label) })),
    ...(inactive.length > 0 && !byStatus.some((s) => s.label.toLowerCase() === 'inactive')
      ? [{ label: 'Inactive', value: inactive.length, fill: projectStatusColor('Inactive') }]
      : []),
  ];

  useEffect(() => {
    setSelectedTimelineKey(null);
  }, [timelineGrain, rows]);

  return (
    <div className="pm-dash-grid">
      <DashboardBarChart
        title="Revenue by Business Unit"
        ariaLabel="Sum of LatestRevenue by BusinessUnitName"
        orientation="horizontal"
        items={metricBars(byBu, 'revenue', (label) => regionBarColor(label))}
        onSelect={(item) => onDrill(`Business unit ${item.label}`, rows.filter((r) => blankLabel(cellText(r, col.businessUnit)) === item.label))}
      />
      <DashboardTreemap
        title="Revenue treemap by Business Unit"
        ariaLabel="Treemap of LatestRevenue by BusinessUnitName"
        items={byBu.map((m) => ({ label: m.label, value: m.revenue, fill: regionBarColor(m.label) }))}
        onSelect={(label) => onDrill(`Business unit ${label}`, rows.filter((r) => blankLabel(cellText(r, col.businessUnit)) === label))}
      />
      <DashboardDonutChart
        title="Revenue Tier Distribution"
        ariaLabel="Count of projects by RevenueTier"
        items={byTier.map((m) => ({ label: m.label, value: m.count, fill: revenueTierColor(m.label) }))}
        onSelect={(label) => onDrill(`Revenue tier ${label}`, rows.filter((r) => blankLabel(cellText(r, col.revenueTier), 'Unknown') === label))}
      />
      <DashboardDonutChart
        title="Active vs Inactive Projects"
        ariaLabel="Count of projects by ProjectStatus"
        items={statusItems}
        onSelect={(label) => {
          if (label.toLowerCase() === 'inactive' && inactive.length > 0) {
            onDrill('Inactive projects (not in vProject_Current)', [], inactive);
            return;
          }
          onDrill(`Project status ${label}`, rows.filter((r) => blankLabel(cellText(r, col.projectStatus), 'Active') === label));
        }}
      />
      <DashboardLineChart
        title="Revenue Trend"
        ariaLabel="Sum of LatestRevenue by LatestRevenueEffectiveDate month"
        valueMode="usd"
        items={trend.map((m) => ({ label: formatMonthLabel(m.label), value: m.revenue, displayValue: formatUsd(m.revenue) }))}
        onSelect={(item) => {
          const key = trend.find((t) => formatMonthLabel(t.label) === item.label)?.label ?? '';
          onDrill(`Revenue month ${item.label}`, rows.filter((r) => monthKey(cellYmd(r, col.latestRevenueDate)) === key));
        }}
      />
      <DashboardBarChart
        title="Top Clients by Revenue"
        ariaLabel="Top 15 clients by sum of LatestRevenue"
        orientation="horizontal"
        items={topClients.map((m) => ({ label: `${m.label} (${m.count})`, value: m.revenue, displayValue: formatUsd(m.revenue) }))}
        onSelect={(item) => {
          const name = item.label.replace(/\s+\(\d+\)$/, '');
          onDrill(`Client ${name}`, rows.filter((r) => blankLabel(cellText(r, col.clientName), '(No client)') === name));
        }}
      />
      <div className="pm-dash-span pm-timeline-fullbleed">
        <DashboardRevenueRenewalTimeline
          title="Revenue and Renewals Timeline"
          ariaLabel="Total LatestRevenue and renewal counts by RenewalDate month or quarter"
          grain={timelineGrain}
          periods={renewalTimeline}
          selectedKey={selectedTimelineKey}
          onGrainChange={setTimelineGrain}
          onSelect={(period) => {
            setSelectedTimelineKey(period.key);
            const filtered = rows.filter((r) => {
              const ymd = cellYmd(r, col.renewalDate);
              const key = timelineGrain === 'month' ? monthKey(ymd) : quarterKey(ymd);
              return key === period.key;
            });
            onDrill(`Renewals ${period.label}`, filtered);
          }}
        />
      </div>
    </div>
  );
}

function PortfolioTab({
  rows, col, ptOptions, filterPt, onFilterPt, onDrill,
}: {
  rows: DashCell[][];
  col: DashColIndex;
  ptOptions: string[];
  filterPt: string;
  onFilterPt: (v: string) => void;
  onDrill: (title: string, rows: DashCell[][]) => void;
}) {
  const byType = groupMetrics(rows, col, col.ptDescription, '(No type)');
  const byClientStatus = groupMetrics(rows, col, col.statusDescription, '(No status)');
  const onlineMetrics = ['Online', 'Offline'].map((label) => ({
    label,
    count: rows.filter((r) => onlineLabel(r, col) === label).length,
  }));
  const crossed = ['Online', 'Offline'].flatMap((on) =>
    groupMetrics(rows.filter((r) => onlineLabel(r, col) === on), col, col.projectStatus, 'Active').map((m) => ({
      label: `${on} · ${m.label}`,
      value: m.count,
    })),
  );
  const starts = trendByMonth(rows, col, col.createdDate, 'count');
  const aging = buildAgingRows(rows, col);

  return (
    <>
      <div className="filter-row dashboard-tabular-filters">
        <div className="dashboard-filter-field">
          <label htmlFor="dash-filter-pt">Project Type</label>
          <select id="dash-filter-pt" value={filterPt} onChange={(e) => onFilterPt(e.target.value)}>
            <option value="">All types</option>
            {ptOptions.map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </div>
      </div>
      <div className="pm-dash-grid">
        <DashboardDonutChart
          title="Project Mix by Type"
          ariaLabel="Count of projects by PTDescription"
          items={byType.map((m, i) => ({ label: m.label, value: m.count, fill: seriesColor(i, m.label) }))}
          onSelect={(label) => onDrill(`Type ${label}`, rows.filter((r) => blankLabel(cellText(r, col.ptDescription), '(No type)') === label))}
        />
        <DashboardBarChart
          title="Status Mix"
          ariaLabel="Count of projects by StatusDescription"
          orientation="horizontal"
          items={byClientStatus.map((m) => ({ label: m.label, value: m.count }))}
          onSelect={(item) => onDrill(`Client status ${item.label}`, rows.filter((r) => blankLabel(cellText(r, col.statusDescription), '(No status)') === item.label))}
        />
        <DashboardDonutChart
          title="In-Progress vs Offline"
          ariaLabel="Count of projects by IsOnline"
          items={onlineMetrics.map((m) => ({ label: m.label, value: m.count, fill: m.label === 'Online' ? '#30d880' : '#5a7080' }))}
          onSelect={(label) => onDrill(label, rows.filter((r) => onlineLabel(r, col) === label))}
        />
        <DashboardBarChart
          title="Online / Offline × Project Status"
          ariaLabel="IsOnline crossed with ProjectStatus"
          orientation="horizontal"
          items={crossed.map((m) => ({ label: m.label, value: m.value, fill: m.label.startsWith('Online') ? '#30d880' : '#5a7080' }))}
          onSelect={(item) => {
            const [on, status] = item.label.split(' · ');
            onDrill(item.label, rows.filter((r) => onlineLabel(r, col) === on && blankLabel(cellText(r, col.projectStatus), 'Active') === status));
          }}
        />
        <div className="pm-dash-span">
          <DashboardBarChart
            title="New Project Starts"
            ariaLabel="Count of projects by CreatedDate month"
            orientation="vertical"
            items={starts.map((m) => ({ label: formatMonthLabel(m.label), value: m.count, fill: '#f08700' }))}
            onSelect={(item) => {
              const key = starts.find((t) => formatMonthLabel(t.label) === item.label)?.label ?? '';
              onDrill(`Starts ${item.label}`, rows.filter((r) => monthKey(cellYmd(r, col.createdDate)) === key));
            }}
          />
        </div>
      </div>
      <section className="dashboard-chart-card">
        <h2 className="dashboard-chart-title">Renewal / Due Date Aging</h2>
        <div className="dashboard-table-wrap">
          <table className="dashboard-data-table dashboard-data-table--full home-client-project-table">
            <thead>
              <tr>
                <th>Client</th>
                <th>Project Number</th>
                <th>Project Status</th>
                <th>Renewal</th>
                <th>Due</th>
              </tr>
            </thead>
            <tbody>
              {aging.slice(0, 80).map((a, i) => {
                const pid = projectIdOf(a.row, col);
                return (
                  <tr key={pid ?? i} onClick={() => onDrill(a.projectNumber || a.projectName, [a.row])}>
                    <td>{a.clientName}</td>
                    <td>{a.projectNumber}</td>
                    <td><span className="pm-chip" style={{ background: projectStatusColor(a.projectStatus) }}>{a.projectStatus}</span></td>
                    <td><span className="pm-chip" style={{ background: agingBucketColor(a.renewalBucket) }}>{a.renewalBucket}</span></td>
                    <td><span className="pm-chip" style={{ background: agingBucketColor(a.dueBucket) }}>{a.dueBucket}</span></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}

function WorkloadTab({
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

function ServicesTab({
  rows, col, columns, serviceColumns, filterService, matrixBy, onFilterService, onMatrixBy, onDrill,
}: {
  rows: DashCell[][];
  col: DashColIndex;
  columns: string[];
  serviceColumns: string[];
  filterService: string;
  matrixBy: 'client' | 'project';
  onFilterService: (v: string) => void;
  onMatrixBy: (v: 'client' | 'project') => void;
  onDrill: (title: string, rows: DashCell[][]) => void;
}) {
  const shownServices = filterService ? serviceColumns.filter((c) => c === filterService) : serviceColumns;
  const serviceIndexes = shownServices.map((name) => ({ name, index: columns.indexOf(name) })).filter((x) => x.index >= 0);
  const seen = new Map<string, { label: string; matches: DashCell[][] }>();
  rows.forEach((row) => {
    const pid = projectIdOf(row, col);
    const key = matrixBy === 'client'
      ? `c:${blankLabel(cellText(row, col.clientName), '(No client)')}`
      : `p:${pid ?? cellText(row, col.projectNumber)}`;
    const label = matrixBy === 'client'
      ? blankLabel(cellText(row, col.clientName), '(No client)')
      : blankLabel(cellText(row, col.projectNumber) || cellText(row, col.projectName), `Project ${pid ?? ''}`);
    const entry = seen.get(key) ?? { label, matches: [] };
    entry.matches.push(row);
    seen.set(key, entry);
  });
  const rowKeys = [...seen.entries()].map(([key, v]) => ({ key, ...v }));
  const heatCells = rowKeys.flatMap((r) =>
    serviceIndexes.map((svc) => ({
      rowLabel: r.label,
      colLabel: svc.name,
      rowKey: r.key,
      active: r.matches.some((row) => isServiceActive(cellText(row, svc.index))),
    })),
  );
  const mix = serviceIndexes.map((svc) => {
    const activeRows = rows.filter((row) => isServiceActive(cellText(row, svc.index)));
    return {
      label: displayServiceName(svc.name),
      count: new Set(activeRows.map((row, i) => projectIdOf(row, col) ?? -(i + 1))).size,
      index: svc.index,
    };
  });

  return (
    <>
      <div className="filter-row dashboard-tabular-filters">
        <div className="dashboard-filter-field">
          <label htmlFor="dash-filter-svc">Service</label>
          <select id="dash-filter-svc" value={filterService} onChange={(e) => onFilterService(e.target.value)}>
            <option value="">All services</option>
            {serviceColumns.map((n) => <option key={n} value={n}>{displayServiceName(n)}</option>)}
          </select>
        </div>
        <div className="dashboard-filter-field">
          <label htmlFor="dash-matrix-by">Matrix rows</label>
          <select id="dash-matrix-by" value={matrixBy} onChange={(e) => onMatrixBy(e.target.value === 'project' ? 'project' : 'client')}>
            <option value="client">ClientName</option>
            <option value="project">Project</option>
          </select>
        </div>
      </div>
      <DashboardHeatmap
        title="Service Adoption Matrix"
        ariaLabel="Heatmap of CategoryName_ServiceName by client or project"
        rowLabels={rowKeys.map((r) => r.label)}
        colLabels={shownServices}
        cells={heatCells}
        onSelectRow={(rowKey) => {
          const found = rowKeys.find((r) => r.key === rowKey);
          onDrill(found?.label ?? rowKey, found?.matches ?? []);
        }}
      />
      <DashboardBarChart
        title="Service Mix"
        ariaLabel="Count of projects per service"
        orientation="horizontal"
        items={mix.map((m) => ({ label: m.label, value: m.count }))}
        onSelect={(item) => {
          const svc = mix.find((s) => s.label === item.label);
          if (!svc) return;
          onDrill(item.label, rows.filter((r) => isServiceActive(cellText(r, svc.index))));
        }}
      />
      <DashboardBarChart
        title="Selected rows by Business Class"
        ariaLabel="Count of projects grouped by BusinessClassName"
        orientation="horizontal"
        items={groupMetrics(
          filterService ? rows.filter((r) => {
            const idx = columns.indexOf(filterService);
            return idx >= 0 && isServiceActive(cellText(r, idx));
          }) : rows,
          col,
          col.businessClass,
        ).map((m) => ({ label: m.label, value: m.count }))}
        onSelect={(item) => onDrill(`Class ${item.label}`, rows.filter((r) => blankLabel(cellText(r, col.businessClass)) === item.label))}
      />
    </>
  );
}

function HealthTab({
  rows, col, onDrill,
}: {
  rows: DashCell[][];
  col: DashColIndex;
  onDrill: (title: string, rows: DashCell[][]) => void;
}) {
  const byStatus = groupMetrics(rows, col, col.statusDescription, '(No status)');
  const byClient = groupMetrics(rows, col, col.clientName, '(No client)');
  return (
    <div className="pm-dash-grid">
      <DashboardDonutChart
        title="Client Status Mix"
        ariaLabel="Distinct ClientName count by StatusDescription"
        items={byStatus.map((m) => ({ label: m.label, value: m.extra ?? m.count }))}
        onSelect={(label) => onDrill(`Client status ${label}`, rows.filter((r) => blankLabel(cellText(r, col.statusDescription), '(No status)') === label))}
      />
      <div className="pm-dash-span">
        <DashboardParetoChart
          title="Revenue Concentration (client view)"
          ariaLabel="Cumulative percent of LatestRevenue by ClientName"
          items={byClient.map((m) => ({ label: m.label, value: m.revenue }))}
          onSelect={(label) => onDrill(`Client ${label}`, rows.filter((r) => blankLabel(cellText(r, col.clientName), '(No client)') === label))}
        />
      </div>
    </div>
  );
}

function DetailGrid({
  title, rows, col, inactive, onClear,
}: {
  title: string;
  rows: DashCell[][];
  col: DashColIndex;
  inactive?: DashboardInactiveProject[];
  onClear?: () => void;
}) {
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
              <th>Client</th><th>Project Number</th><th>Project Name</th><th>Type</th>
              <th>Status</th><th>Tier</th><th>Latest Revenue</th><th>Hours</th><th></th>
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
                <td></td><td></td><td></td>
                <td><Link to={`/projects/${p.projectID}`}>Edit</Link></td>
              </tr>
            ))}
            {rows.slice(0, 200).map((row, i) => {
              const pid = projectIdOf(row, col);
              const status = blankLabel(cellText(row, col.projectStatus), 'Active');
              const tier = blankLabel(cellText(row, col.revenueTier), 'Unknown');
              return (
                <tr key={pid ?? i}>
                  <td>{cellText(row, col.clientName)}</td>
                  <td>{cellText(row, col.projectNumber)}</td>
                  <td>{cellText(row, col.projectName)}</td>
                  <td>{cellText(row, col.ptDescription)}</td>
                  <td><span className="pm-chip" style={{ background: projectStatusColor(status) }}>{status}</span></td>
                  <td><span className="pm-chip" style={{ background: revenueTierColor(tier) }}>{tier}</span></td>
                  <td>{formatUsd(cellNumber(row, col.latestRevenue))}</td>
                  <td>{formatNumber(cellNumber(row, col.totalHours), 1)}</td>
                  <td>{pid ? <Link to={`/projects/${pid}`}>Edit</Link> : null}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {rows.length > 200 ? <p className="field-help">Showing first 200 of {rows.length} projects.</p> : null}
    </section>
  );
}
