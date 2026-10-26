import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiGet } from '../api';
import DashboardBarChart from '../components/DashboardBarChart';
import {
  DashboardDonutChart,
  DashboardHeatmap,
  DashboardLineChart,
  DashboardParetoChart,
  DashboardRevenueRenewalTimeline,
} from '../components/PmDashboardCharts';
import {
  DASH_OTHER_LABEL,
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
  isServiceActive,
  monthKey,
  normalizeDataset,
  onlineLabel,
  projectIdOf,
  quarterKey,
  sortTiers,
  summarizePortfolio,
  trendByMonth,
  withOtherRemainder,
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
  formatUsdThousands,
  projectStatusColor,
  revenueTierColor,
  seriesColor,
} from '../dashboard/dashboardColors';
import { exportExecutiveOverviewPdf } from '../dashboard/exportExecutiveOverviewPdf';
import { formatCalendarDateDisplay } from '../utils/dateInput';
import {
  clientProjectListSortIndicator,
  compareClientProjectListCellValues,
  type ClientProjectListSortDirection,
} from '../clientProjectList';

type DashTab = 'executive' | 'portfolio' | 'services' | 'health';

type DrillState = {
  title: string;
  rows: DashCell[][];
  inactive?: DashboardInactiveProject[];
};

const DATE_FIELD_BY_TAB: Record<DashTab, DateField> = {
  executive: 'latestRevenueDate',
  portfolio: 'createdDate',
  services: 'createdDate',
  health: 'latestRevenueDate',
};

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
  const [filterService, setFilterService] = useState('');
  const [matrixBy, setMatrixBy] = useState<'client' | 'project'>('client');
  const [drill, setDrill] = useState<DrillState | null>(null);
  const [execLink, setExecLink] = useState<{ rows: DashCell[][]; label: string } | null>(null);

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
      return { bu: [] as string[], bc: [] as string[], projectStatus: [] as string[], pt: [] as string[], clientStatus: [] as string[] };
    }
    return {
      bu: distinctValues(dataset.rows, col.businessUnit),
      bc: distinctValues(dataset.rows, col.businessClass),
      projectStatus: distinctValues(dataset.rows, col.projectStatus),
      pt: distinctValues(dataset.rows, col.ptDescription),
      clientStatus: distinctValues(dataset.rows, col.statusDescription),
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
    return rows;
  }, [dataset, col, filters, tab, filterPt]);

  const setFilter = (key: keyof GlobalDashFilters, value: string) => {
    setFilters((prev) => ({ ...prev, [key]: value }));
    setDrill(null);
    setExecLink(null);
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

  const detailRows = drill?.rows ?? (tab === 'executive' && execLink ? execLink.rows : scopedRows);
  const detailTitle = drill?.title ?? (tab === 'executive' && execLink ? `Renewals ${execLink.label}` : 'All filtered projects');

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
            {tab === 'executive' && dataset && col && !loading ? (
              <button
                className="primary-button"
                type="button"
                onClick={() => {
                  try {
                    exportExecutiveOverviewPdf({
                      rows: detailRows,
                      col,
                      filters,
                      detailTitle,
                    });
                  } catch (err) {
                    setError(err instanceof Error ? err.message : 'Could not open PDF report.');
                  }
                }}
              >
                Export PDF
              </button>
            ) : null}
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
            ['services', 'Service Coverage'],
            ['health', 'Client Health'],
          ] as const).map(([id, label]) => (
            <button
              key={id}
              type="button"
              className={tab === id ? 'active' : undefined}
              role="tab"
              aria-selected={tab === id}
              onClick={() => { setTab(id); setDrill(null); setExecLink(null); }}
            >
              {label}
            </button>
          ))}
        </div>

        {dataset && col && !loading ? (
          <>
            {tab === 'executive' ? (
              <ExecutiveTab
                rows={scopedRows}
                col={col}
                inactive={dataset.inactiveProjects}
                onDrill={openDrill}
                onLinkChange={setExecLink}
              />
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
              title={detailTitle}
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
  rows, col, onDrill, onLinkChange,
}: {
  rows: DashCell[][];
  col: DashColIndex;
  inactive: DashboardInactiveProject[];
  onDrill: (title: string, rows: DashCell[][], inactive?: DashboardInactiveProject[]) => void;
  onLinkChange: (link: { rows: DashCell[][]; label: string } | null) => void;
}) {
  const [timelineGrain, setTimelineGrain] = useState<TimelineGrain>('month');
  const [selectedTimelineKey, setSelectedTimelineKey] = useState<string | null>(null);
  const renewalTimeline = useMemo(
    () => buildRenewalTimeline(rows, col, timelineGrain),
    [rows, col, timelineGrain],
  );
  const linkedRows = useMemo(() => {
    if (!selectedTimelineKey) {
      return rows;
    }
    return rows.filter((r) => {
      const ymd = cellYmd(r, col.renewalDate);
      const key = timelineGrain === 'month' ? monthKey(ymd) : quarterKey(ymd);
      return key === selectedTimelineKey;
    });
  }, [rows, col, selectedTimelineKey, timelineGrain]);
  const byTier = sortTiers(groupMetrics(linkedRows, col, col.revenueTier, 'Unknown'));
  const byAccountExecutive = groupMetrics(linkedRows, col, col.accountExecutive, '(No account executive)')
    .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
  const byAnalyticsLead = withOtherRemainder(
    groupMetrics(linkedRows, col, col.analyticsLead, '(No analytics lead)')
      .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label)),
    8,
  );
  const topClientsAll = groupMetrics(linkedRows, col, col.clientName, '(No client)');
  // Dashboard: show all clients (report/export will still cap for layout)
  const topClients = topClientsAll;

  useEffect(() => {
    setSelectedTimelineKey(null);
  }, [timelineGrain, rows]);

  useEffect(() => {
    if (!selectedTimelineKey) {
      onLinkChange(null);
      return;
    }
    const period = renewalTimeline.find((p) => p.key === selectedTimelineKey);
    onLinkChange(period ? { rows: linkedRows, label: period.label } : null);
  }, [linkedRows, selectedTimelineKey, renewalTimeline, onLinkChange]);

  return (
    <div className="pm-dash-grid pm-dash-grid--executive">
      <div className="pm-dash-span">
        <DashboardRevenueRenewalTimeline
          title="Revenue and Renewals Timeline"
          ariaLabel="Total LatestRevenue and renewal counts by RenewalDate month or quarter"
          grain={timelineGrain}
          periods={renewalTimeline}
          selectedKey={selectedTimelineKey}
          chartHeight={280}
          wideChart
          onGrainChange={setTimelineGrain}
          onSelect={(period) => {
            setSelectedTimelineKey((prev) => (prev === period.key ? null : period.key));
          }}
        />
      </div>
      <div className="pm-dash-row pm-dash-row--donuts">
        <DashboardDonutChart
          title="Revenue Tier Distribution"
          ariaLabel="Count of projects by RevenueTier"
          items={byTier.map((m) => ({
            label: m.label,
            value: m.count,
            revenue: m.revenue,
            fill: revenueTierColor(m.label),
          }))}
          onSelect={(label) => onDrill(
            `Revenue tier ${label}`,
            linkedRows.filter((r) => blankLabel(cellText(r, col.revenueTier), 'Unknown') === label),
          )}
        />
        <DashboardDonutChart
          title="Projects by Account Executive"
          ariaLabel="Count of projects by Account Executive"
          items={byAccountExecutive.map((m, i) => ({
            label: m.label,
            value: m.count,
            revenue: m.revenue,
            fill: seriesColor(i, m.label),
          }))}
          onSelect={(label) => onDrill(
            `Account executive ${label}`,
            linkedRows.filter((r) => blankLabel(cellText(r, col.accountExecutive), '(No account executive)') === label),
          )}
        />
        <DashboardDonutChart
          title="Projects by Analytics Lead"
          ariaLabel="Count of projects by Analytics Lead"
          items={byAnalyticsLead.map((m, i) => ({
            label: m.label,
            value: m.count,
            revenue: m.revenue,
            fill: m.label === DASH_OTHER_LABEL ? '#8a96a3' : seriesColor(i, m.label),
          }))}
          onSelect={(label) => {
            if (label === DASH_OTHER_LABEL) {
              const shown = new Set(
                byAnalyticsLead.filter((m) => m.label !== DASH_OTHER_LABEL).map((m) => m.label),
              );
              onDrill(
                'Analytics lead Other',
                linkedRows.filter((r) => !shown.has(blankLabel(cellText(r, col.analyticsLead), '(No analytics lead)'))),
              );
              return;
            }
            onDrill(
              `Analytics lead ${label}`,
              linkedRows.filter((r) => blankLabel(cellText(r, col.analyticsLead), '(No analytics lead)') === label),
            );
          }}
        />
      </div>
      <div className="pm-dash-span">
        <DashboardBarChart
          title="Top Clients by Revenue"
          ariaLabel="Top 10 clients by sum of LatestRevenue"
          orientation="vertical"
          plotHeightPx={220}
          className="pm-dash-clients-chart pm-dash-simple-bars pm-dash-clients-slide"
          valueKey="Revenue ($000)"
          items={topClients.map((m) => ({
            label: `${m.label} (${m.count})`,
            value: m.revenue,
            count: m.count,
            displayValue: formatUsdThousands(m.revenue),
            fill: m.label === DASH_OTHER_LABEL ? '#8a96a3' : '#20d4e8',
          }))}
          onSelect={(item) => {
            const name = item.label.replace(/\s+\(\d+\)$/, '');
            onDrill(
              `Client ${name}`,
              linkedRows.filter((r) => blankLabel(cellText(r, col.clientName), '(No client)') === name),
            );
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
  const workStatuses = ['Planned', 'In Progress', 'Closed'] as const;
  const onlineMetrics = workStatuses.map((label) => ({
    label,
    count: rows.filter((r) => onlineLabel(r, col) === label).length,
  }));
  const crossed = workStatuses.flatMap((on) =>
    groupMetrics(rows.filter((r) => onlineLabel(r, col) === on), col, col.projectStatus, 'Active').map((m) => ({
      label: `${on} Â· ${m.label}`,
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
          title="Project status"
          ariaLabel="Count of projects by Planned, In Progress, and Closed"
          items={onlineMetrics.map((m) => ({
            label: m.label,
            value: m.count,
            fill: m.label === 'In Progress' ? '#30d880' : m.label === 'Planned' ? '#3d8bfd' : '#5a7080',
          }))}
          onSelect={(label) => onDrill(label, rows.filter((r) => onlineLabel(r, col) === label))}
        />
        <DashboardBarChart
          title="Project status × Active / Inactive"
          ariaLabel="Project status crossed with ProjectStatus"
          orientation="horizontal"
          items={crossed.map((m) => ({
            label: m.label,
            value: m.value,
            fill: m.label.startsWith('In Progress') ? '#30d880' : m.label.startsWith('Planned') ? '#3d8bfd' : '#5a7080',
          }))}
          onSelect={(item) => {
            const [on, status] = item.label.split(' Â· ');
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

function DetailGrid({
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
