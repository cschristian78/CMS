import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { ApiError, apiGet } from '../api';
import { formatCalendarDateDisplay } from '../utils/dateInput';

interface SelectOption {
  value: string;
  text: string;
}

interface ProjectFormModel {
  projectID?: number | null;
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

  clientName?: string | null;
  clientShortName?: string | null;
  clientBusinessUnitName?: string | null;
  clientBusinessClassName?: string | null;
  clientCategoryName?: string | null;
  clientTypeName?: string | null;
  clientStatusCode?: string | null;

  selectedServiceIds: number[];

  clientOptions: SelectOption[];
  userGroupOptions: SelectOption[];
  projectTypeOptions: SelectOption[];
  statusOptions: SelectOption[];
}

interface CatalogServiceRow {
  serviceID: number;
  serviceName: string;
  categoryName?: string | null;
  serviceCategoryDescription?: string | null;
}

interface ProjectRevenueRow {
  revenueID: number;
  effectiveDate: string;
  revenue: number;
  changeReason?: string | null;
  recordedByName?: string | null;
}

function formatDate(iso?: string | null): string {
  return formatCalendarDateDisplay(iso);
}

function formatMoney(n: number | null | undefined): string {
  if (n == null) {
    return '-';
  }
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 2,
  }).format(n);
}

/** Read-only project snapshot. Use the Edit button to open the editable form. */
export default function ProjectSummaryPage() {
  const navigate = useNavigate();
  const { id: idParam } = useParams();
  const projectId = idParam !== undefined ? Number(idParam) : NaN;

  const [model, setModel] = useState<ProjectFormModel | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [catalog, setCatalog] = useState<CatalogServiceRow[]>([]);
  const [revenueHistory, setRevenueHistory] = useState<ProjectRevenueRow[]>([]);

  const load = useCallback(async () => {
    if (!Number.isFinite(projectId)) {
      setLoadError('Invalid project id.');
      return;
    }
    try {
      setLoadError(null);
      const data = await apiGet<ProjectFormModel>(`/api/projects/form/${projectId}`);
      setModel({
        ...data,
        selectedServiceIds: Array.isArray(data.selectedServiceIds) ? data.selectedServiceIds : [],
      });
    } catch (e) {
      setLoadError(e instanceof ApiError ? e.message : 'Could not load project summary.');
    }
  }, [projectId]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    void (async () => {
      try {
        setCatalog(await apiGet<CatalogServiceRow[]>('/api/projects/services-catalog'));
      } catch {
        setCatalog([]);
      }
    })();
  }, []);

  useEffect(() => {
    if (!Number.isFinite(projectId)) {
      return;
    }
    setRevenueHistory([]);
    void (async () => {
      try {
        setRevenueHistory(await apiGet<ProjectRevenueRow[]>(`/api/projects/${projectId}/revenue`));
      } catch {
        setRevenueHistory([]);
      }
    })();
  }, [projectId]);

  useEffect(() => {
    if (revenueHistory.length === 0) {
      setModel((prev) =>
        prev ? { ...prev, revenueAmount: null, revenueEffectiveDate: null } : prev,
      );
      return;
    }
    const latest = revenueHistory[0];
    setModel((prev) =>
      prev
        ? {
            ...prev,
            revenueAmount: latest.revenue,
            revenueEffectiveDate: latest.effectiveDate,
          }
        : prev,
    );
  }, [revenueHistory]);

  if (loadError) {
    return (
      <div className="client-workflow-fill client-workflow-error">
        <section className="card narrow-card">
          <h1>Project Summary</h1>
          <p>{loadError}</p>
          <Link className="secondary-button" to="/">
            Home
          </Link>
        </section>
      </div>
    );
  }

  if (!model) {
    return (
      <div className="client-workflow-fill client-workflow-loading">
        <p>Loading...</p>
      </div>
    );
  }

  const selectedServices = catalog.filter((c) => model.selectedServiceIds.includes(c.serviceID));

  return (
    <div className="client-workflow-fill">
      <section className="page-header client-workflow-header">
        <div className="page-header-top">
          <h1>Project Summary</h1>
          <div className="page-header-actions">
            <button
              type="button"
              className="primary-button"
              onClick={() => navigate(`/projects/${projectId}`)}
            >
              Edit
            </button>
            <Link className="secondary-button" to="/">
              Home
            </Link>
          </div>
        </div>
      </section>

      <section className="card form-card">
        <h2>Project</h2>
        <dl className="summary-grid">
          <dt>Project Number</dt>
          <dd>{model.projectNumber || '-'}</dd>
          <dt>Project Name</dt>
          <dd>{model.projectName || '-'}</dd>
          <dt>Renewal Date</dt>
          <dd>{formatDate(model.renewalDate)}</dd>
          <dt>Due Date</dt>
          <dd>{formatDate(model.dueDate)}</dd>
          <dt>Latest Revenue</dt>
          <dd>{formatMoney(model.revenueAmount)}</dd>
        </dl>
      </section>

      <section className="card form-card">
        <h2>Client</h2>
        <dl className="summary-grid">
          <dt>Client</dt>
          <dd>{model.clientName ?? '-'}</dd>
          <dt>Business Unit</dt>
          <dd>{model.clientBusinessUnitName ?? '-'}</dd>
          <dt>Business Class</dt>
          <dd>{model.clientBusinessClassName ?? '-'}</dd>
          <dt>Client Type</dt>
          <dd>{model.clientTypeName ?? '-'}</dd>
          <dt>Client Status</dt>
          <dd>{model.clientStatusCode ?? '-'}</dd>
        </dl>
      </section>

      <section className="card form-card">
        <h2>Services</h2>
        {selectedServices.length === 0 ? (
          <p>No services linked.</p>
        ) : (
          <ul>
            {selectedServices.map((s) => (
              <li key={s.serviceID}>
                {s.serviceName}
                {s.categoryName ? ` - ${s.categoryName}` : ''}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="card form-card">
        <h2>Revenue history</h2>
        <table className="catalog-table">
          <thead>
            <tr>
              <th>Effective Date</th>
              <th>Revenue</th>
              <th>Reason</th>
              <th>Recorded By</th>
            </tr>
          </thead>
          <tbody>
            {revenueHistory.length === 0 ? (
              <tr>
                <td colSpan={4}>No revenue entries yet.</td>
              </tr>
            ) : (
              revenueHistory.map((r) => (
                <tr key={r.revenueID}>
                  <td>{formatDate(r.effectiveDate)}</td>
                  <td>{formatMoney(r.revenue)}</td>
                  <td>{r.changeReason ?? ''}</td>
                  <td>{r.recordedByName ?? ''}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </section>
    </div>
  );
}
