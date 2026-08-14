import { useCallback, useEffect, useMemo, useState } from 'react';
import { apiGet } from '../api';
import DashboardBarChart from '../components/DashboardBarChart';
import { namedRowsToChartItems } from '../utils/dashboardChartItems';

type DashboardFilterOptions = {
  analyticsLeadNames: string[];
};

type OnlineToggle = 'Online' | 'All';

function buildProjectsByBeginMonthUrl(analyticsLeadName: string, onlineStatus: OnlineToggle): string {
  const params = new URLSearchParams();
  params.set('onlineStatus', onlineStatus);
  if (analyticsLeadName.trim()) {
    params.set('analyticsLeadName', analyticsLeadName.trim());
  }
  return `/api/dashboard/projects-by-begin-month?${params.toString()}`;
}

/** API returns yyyy-MM; display as Jan ... Dec. */
function formatMonthLabel(label: string): string {
  const m = label.trim().match(/^(\d{4})-(\d{2})$/);
  if (!m) {
    return label;
  }
  const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const idx = Number(m[2]) - 1;
  return monthNames[idx] ?? m[2];
}

async function loadDashboardEndpoint<T>(path: string): Promise<{ ok: true; data: T } | { ok: false; error: string }> {
  try {
    return { ok: true, data: await apiGet<T>(path) };
  } catch (e) {
    console.warn(`Dashboard load failed: ${path}`, e);
    const message = e instanceof Error ? e.message : 'Request failed.';
    return { ok: false, error: message };
  }
}

export default function DashboardPage() {
  const [monthRaw, setMonthRaw] = useState<unknown>(null);
  const [analyticsLeadNames, setAnalyticsLeadNames] = useState<string[]>([]);
  const [filterAnalyticsLead, setFilterAnalyticsLead] = useState('');
  const [filterOnline, setFilterOnline] = useState<OnlineToggle>('Online');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const loadFilterOptions = useCallback(async () => {
    const options = await loadDashboardEndpoint<DashboardFilterOptions>('/api/dashboard/filter-options');
    if (options.ok && options.data?.analyticsLeadNames && Array.isArray(options.data.analyticsLeadNames)) {
      setAnalyticsLeadNames(
        options.data.analyticsLeadNames
          .map((s) => (typeof s === 'string' ? s.trim() : ''))
          .filter(Boolean),
      );
    } else {
      setAnalyticsLeadNames([]);
    }
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    await loadFilterOptions();

    const result = await loadDashboardEndpoint<unknown>(
      buildProjectsByBeginMonthUrl(filterAnalyticsLead, filterOnline),
    );

    if (!result.ok) {
      setMonthRaw([]);
      setError(result.error || 'Could not load project begin timeline. Restart the application and refresh.');
    } else if (!Array.isArray(result.data)) {
      setMonthRaw([]);
      setError('Invalid timeline response from the server.');
    } else {
      setMonthRaw(result.data);
    }

    setLoading(false);
  }, [filterAnalyticsLead, filterOnline, loadFilterOptions]);

  useEffect(() => {
    void load();
  }, [load]);

  const chartItems = useMemo(() => {
    const items = namedRowsToChartItems(monthRaw);
    return items.map((item) => ({
      ...item,
      label: formatMonthLabel(item.label),
      colorKey: 'timeline',
    }));
  }, [monthRaw]);

  const totalProjects = useMemo(
    () => chartItems.reduce((sum, x) => sum + x.value, 0),
    [chartItems],
  );

  const dataReady = monthRaw !== null;

  return (
    <>
      <section className="page-header">
        <h1>Dashboard</h1>
        <p>
          Monthly view for the current year (January-December) of projects by begin date (Created Date).
          Months with no starts stay blank. Filter by Analytics Lead and toggle Online vs All projects.
        </p>
      </section>

      <section className="card wide-card">
        <div
          className="filter-row dashboard-view-toolbar dashboard-tabular-filters"
          role="search"
          aria-label="Dashboard filters"
        >
          <div className="dashboard-filter-field dashboard-filter-field--grow">
            <label htmlFor="dashboard-filter-analytics-lead">Analytics Lead</label>
            <select
              id="dashboard-filter-analytics-lead"
              value={filterAnalyticsLead}
              onChange={(e) => setFilterAnalyticsLead(e.target.value)}
              disabled={loading}
              title={filterAnalyticsLead || 'All Analytics Leads'}
            >
              <option value="">All Analytics Leads</option>
              {analyticsLeadNames.map((name) => (
                <option key={name} value={name} title={name}>
                  {name}
                </option>
              ))}
            </select>
          </div>
          <div className="dashboard-filter-field dashboard-filter-field--online">
            <label htmlFor="dashboard-filter-online">Projects</label>
            <select
              id="dashboard-filter-online"
              value={filterOnline}
              onChange={(e) => setFilterOnline(e.target.value === 'All' ? 'All' : 'Online')}
              disabled={loading}
            >
              <option value="Online">Online</option>
              <option value="All">All</option>
            </select>
          </div>
          <div className="dashboard-filter-actions">
            <button className="secondary-button" type="button" onClick={() => void load()} disabled={loading}>
              {loading ? 'Loading...' : 'Refresh'}
            </button>
          </div>
        </div>

        {error ? <div className="alert alert-error">{error}</div> : null}

        {!error && dataReady && !loading ?
          <p className="field-help" role="status">
            {filterAnalyticsLead ? (
              <>
                Analytics Lead <strong>{filterAnalyticsLead}</strong>
                {' - '}
              </>
            ) : null}
            Showing <strong>{filterOnline === 'Online' ? 'Online' : 'All'}</strong> projects
            {' - '}
            Total in chart: <strong>{totalProjects}</strong>
          </p>
        : null}

        {!error && dataReady ?
          <div className="dashboard-charts-layout dashboard-charts-layout--timeline">
            <DashboardBarChart
              title="Projects beginning by month"
              items={chartItems}
              ariaLabel="Monthly bar chart of projects by begin month"
              orientation="vertical"
            />
          </div>
        : null}

        {loading && !dataReady ? <p className="field-help">Loading dashboard...</p> : null}
      </section>
    </>
  );
}
