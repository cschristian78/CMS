import { useCallback, useEffect, useMemo, useState } from 'react';
import { apiGet } from '../api';
import DashboardBarChart from '../components/DashboardBarChart';
import {
  businessUnitRowsToChartItems,
  ensureNamedChartItems,
  namedRowsToChartItems,
} from '../utils/dashboardChartItems';
import { REGION_BAR_COLORS } from '../utils/regionBarColors';

async function loadDashboardEndpoint<T>(path: string): Promise<T | null> {
  try {
    return await apiGet<T>(path);
  } catch (e) {
    console.warn(`Dashboard load failed: ${path}`, e);
    return null;
  }
}

export default function DashboardPage() {
  const [clientBuRaw, setClientBuRaw] = useState<unknown>(null);
  const [projectBuRaw, setProjectBuRaw] = useState<unknown>(null);
  const [clientBcRaw, setClientBcRaw] = useState<unknown>(null);
  const [projectBcRaw, setProjectBcRaw] = useState<unknown>(null);
  const [loadWarnings, setLoadWarnings] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    setLoadWarnings([]);

    const paths = [
      ['/api/dashboard/clients-by-business-unit', setClientBuRaw] as const,
      ['/api/dashboard/projects-by-business-unit', setProjectBuRaw] as const,
      ['/api/dashboard/clients-by-business-class', setClientBcRaw] as const,
      ['/api/dashboard/projects-by-business-class', setProjectBcRaw] as const,
    ];

    const warnings: string[] = [];
    await Promise.all(
      paths.map(async ([path, setter]) => {
        const data = await loadDashboardEndpoint<unknown>(path);
        if (data == null) {
          warnings.push(path);
          setter([]);
        } else if (!Array.isArray(data)) {
          warnings.push(`${path} (invalid response)`);
          setter([]);
        } else {
          setter(data);
        }
      }),
    );

    setLoadWarnings(warnings);
    if (warnings.length === paths.length) {
      setError('Could not load any dashboard data. Restart the application (dotnet run) and refresh.');
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const clientBuChartItems = useMemo(
    () => businessUnitRowsToChartItems(clientBuRaw),
    [clientBuRaw],
  );

  const projectBuChartItems = useMemo(
    () => businessUnitRowsToChartItems(projectBuRaw),
    [projectBuRaw],
  );

  const clientBcChartItems = useMemo(
    () => ensureNamedChartItems(namedRowsToChartItems(clientBcRaw)),
    [clientBcRaw],
  );

  const projectBcChartItems = useMemo(
    () => ensureNamedChartItems(namedRowsToChartItems(projectBcRaw)),
    [projectBcRaw],
  );

  const totalClients = useMemo(
    () => clientBuChartItems.reduce((sum, x) => sum + x.value, 0),
    [clientBuChartItems],
  );

  const totalProjects = useMemo(
    () => projectBuChartItems.reduce((sum, x) => sum + x.value, 0),
    [projectBcChartItems],
  );

  const dataReady = clientBuRaw !== null;

  return (
    <>
      <section className="page-header">
        <h1>Dashboard</h1>
        <p>
          Client and project counts by business unit and business class. Bar colors:{' '}
          <span className="dashboard-legend-item">
            <span className="dashboard-legend-swatch" style={{ background: REGION_BAR_COLORS.US }} /> US
          </span>
          ,{' '}
          <span className="dashboard-legend-item">
            <span className="dashboard-legend-swatch" style={{ background: REGION_BAR_COLORS.UK }} /> UK
          </span>
          ,{' '}
          <span className="dashboard-legend-item">
            <span className="dashboard-legend-swatch" style={{ background: REGION_BAR_COLORS.LatAM }} /> LatAM
          </span>
          .
        </p>
      </section>

      <section className="card wide-card">
        <div className="filter-row dashboard-view-toolbar">
          <button className="secondary-button" type="button" onClick={() => void load()} disabled={loading}>
            {loading ? 'Loading…' : 'Refresh'}
          </button>
        </div>

        {error ? <div className="alert alert-error">{error}</div> : null}
        {!error && loadWarnings.length > 0 ?
          <div className="alert alert-error" role="status">
            Some charts could not load ({loadWarnings.length} endpoint
            {loadWarnings.length === 1 ? '' : 's'}). Stop the app (Ctrl+C), run{' '}
            <code>dotnet build</code> then <code>dotnet run</code>, and refresh.
          </div>
        : null}

        {!error && dataReady && !loading ?
          <p className="field-help" role="status">
            Active clients: <strong>{totalClients}</strong> · Projects (by unit):{' '}
            <strong>{totalProjects}</strong>
          </p>
        : null}

        <div className="dashboard-charts-layout">
          <div className="dashboard-charts-layout__client-bu">
            <DashboardBarChart
              title="Clients by business unit"
              items={clientBuChartItems}
              ariaLabel="Clients by business unit"
              orientation="vertical"
            />
          </div>
          <div className="dashboard-charts-layout__client-bc">
            <DashboardBarChart
              title="Clients by business class"
              items={clientBcChartItems}
              ariaLabel="Clients by business class"
              orientation="vertical"
            />
          </div>
          <div className="dashboard-charts-layout__project-bu">
            <DashboardBarChart
              title="Projects by business unit"
              items={projectBuChartItems}
              ariaLabel="Projects by business unit"
              orientation="vertical"
            />
          </div>
          <div className="dashboard-charts-layout__project-bc">
            <DashboardBarChart
              title="Projects by business class"
              items={projectBcChartItems}
              ariaLabel="Projects by business class"
              orientation="vertical"
            />
          </div>
        </div>
      </section>
    </>
  );
}
