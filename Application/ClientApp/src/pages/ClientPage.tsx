import { ChangeEvent, FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import type { FlashFn } from '../App';
import { ApiError, apiGet, apiPostFormData, apiPostJson } from '../api';
import { formatBusinessClassDisplay } from '../utils/businessClassDisplay';

interface SelectOption {
  value: string;
  text: string;
}

/// Row shape from dbo.vClient_WithBU via /api/clients/directory (CMS_PhaseI_All).
interface ExistingClientRow {
  clientID: number;
  clientName?: string | null;
  clientShortName?: string | null;
  statusID?: number | null;
  statusCode?: string | null;
  statusDescription?: string | null;
  businessUnitID?: number | null;
  businessUnitName?: string | null;
  businessClassID?: number | null;
  businessClassName?: string | null;
  businessClassDescription?: string | null;
  clientCategoryID?: number | null;
  ccName?: string | null;
  clientTypeID?: number | null;
  ctName?: string | null;
  isDeleted?: boolean;
  deletedDate?: string | null;
  /** Convenience display label assembled server-side. */
  displayName?: string | null;
}

function rowDisplayName(row: ExistingClientRow): string {
  if (row.displayName && row.displayName.trim()) {
    return row.displayName;
  }
  const name = (row.clientName ?? '').trim();
  const shortName = (row.clientShortName ?? '').trim();
  if (name && shortName) {
    return `${name} (${shortName})`;
  }
  return name || `Client ${row.clientID}`;
}

function clientDirectoryMatchesSearch(row: ExistingClientRow, rawQuery: string): boolean {
  const q = rawQuery.trim().toLowerCase();
  if (!q) {
    return true;
  }
  const haystack = [
    String(row.clientID),
    rowDisplayName(row),
    row.clientName,
    row.clientShortName,
    row.statusCode,
    row.statusDescription,
    row.businessUnitName,
    formatBusinessClassDisplay(row.businessClassName, row.businessClassDescription),
    row.ccName,
    row.ctName,
  ];
  return haystack.some((part) => part != null && String(part).toLowerCase().includes(q));
}

interface ClientImportRow {
  lineNumber: number;
  clientName: string;
  clientShortName: string;
  businessUnitID: number;
  businessClassID: number;
  clientCategoryID: number;
  clientTypeID: number;
  statusID: number;
}

interface BulkParseResult {
  rows: ClientImportRow[];
  skippedEmptyRows: number;
}

interface BulkVerifyResult {
  valid: boolean;
  errors: string[];
}

interface BulkCommitResult {
  importedCount: number;
  skippedEmptyRows: number;
  rowErrors: string[];
}

interface ClientFormModel {
  clientID?: number | null;
  clientName: string;
  clientShortName?: string | null;
  businessUnitID: number;
  businessClassID: number;
  clientCategoryID: number;
  clientTypeID: number;
  statusID: number;
  isDeleted?: boolean;
  existingClients: ExistingClientRow[];
  statusOptions: SelectOption[];
  businessUnitOptions: SelectOption[];
  businessClassOptions: SelectOption[];
  clientCategoryOptions: SelectOption[];
  clientTypeOptions: SelectOption[];
}

export default function ClientPage({ flash }: { flash: FlashFn }) {
  const navigate = useNavigate();
  const { id: idParam } = useParams();
  const clientId = idParam !== undefined ? Number(idParam) : undefined;
  const isEdit = clientId !== undefined && !Number.isNaN(clientId);

  const [model, setModel] = useState<ClientFormModel | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [validationMessage, setValidationMessage] = useState<string | null>(null);
  const [selectedListId, setSelectedListId] = useState<number | ''>('');
  const [showDeleted, setShowDeleted] = useState(false);

  const [bulkWizardOpen, setBulkWizardOpen] = useState(false);
  const [bulkRows, setBulkRows] = useState<ClientImportRow[]>([]);
  const [bulkSkippedEmpty, setBulkSkippedEmpty] = useState(0);
  const [bulkVerifyOk, setBulkVerifyOk] = useState(false);
  const [bulkVerifyErrors, setBulkVerifyErrors] = useState<string[]>([]);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [directorySearch, setDirectorySearch] = useState('');

  const filteredExistingClients = useMemo(() => {
    const rows = model?.existingClients ?? [];
    return rows.filter((c) => clientDirectoryMatchesSearch(c, directorySearch));
  }, [model?.existingClients, directorySearch]);

  const load = useCallback(
    async (opts?: { edit?: boolean; clientId?: number }) => {
      try {
        setLoadError(null);
        const useEdit = opts?.edit ?? isEdit;
        const id = opts?.clientId ?? clientId;
        const path =
          useEdit && id !== undefined && !Number.isNaN(id)
            ? `/api/clients/form/${id}`
            : '/api/clients/form';
        const data = await apiGet<ClientFormModel>(path);
        setModel(data);
      } catch (e) {
        if (e instanceof ApiError && e.status === 404) {
          setLoadError('Client not found.');
        } else {
          setLoadError(e instanceof ApiError ? e.message : 'Could not load client form.');
        }
      }
    },
    [clientId, isEdit],
  );

  const refreshDirectoryOnly = useCallback(async () => {
    try {
      const rows = await apiGet<ExistingClientRow[]>(
        `/api/clients/directory?includeDeleted=${showDeleted ? 'true' : 'false'}`,
      );
      setModel((prev) => (prev ? { ...prev, existingClients: rows } : null));
    } catch {
      /* full reload happens on next nav */
    }
  }, [showDeleted]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    void refreshDirectoryOnly();
  }, [refreshDirectoryOnly]);

  // Cascading BU -> BC dropdown.
  useEffect(() => {
    if (!model) {
      return;
    }
    const buid = model.businessUnitID;
    if (!buid || buid <= 0) {
      return;
    }

    let cancelled = false;
    void (async () => {
      try {
        const opts = await apiGet<SelectOption[]>(`/api/clients/business-classes?businessUnitId=${buid}`);
        if (cancelled) {
          return;
        }
        setModel((prev) => {
          if (!prev || prev.businessUnitID !== buid) {
            return prev;
          }
          const validBc = prev.businessClassID > 0 && opts.some((o) => Number(o.value) === prev.businessClassID);
          return {
            ...prev,
            businessClassOptions: opts,
            businessClassID: validBc ? prev.businessClassID : 0,
          };
        });
      } catch (e) {
        if (!cancelled) {
          flash('error', e instanceof ApiError ? e.message : 'Could not load business classes.');
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [model?.businessUnitID, flash]);

  const handleNavigateSelected = () => {
    if (selectedListId === '' || selectedListId === undefined) {
      window.alert('Select a row first.');
      return;
    }
    navigate(`/clients/${selectedListId}`);
  };

  const handleOpenClientFromList = async (clientIdToOpen: number) => {
    if (!Number.isFinite(clientIdToOpen) || clientIdToOpen <= 0) {
      return;
    }
    try {
      setLoadError(null);
      const data = await apiGet<ClientFormModel>(`/api/clients/form/${clientIdToOpen}`);
      setModel(data);
      setSelectedListId(clientIdToOpen);
      navigate(`/clients/${clientIdToOpen}`, { replace: true });
    } catch (e) {
      const msg = e instanceof ApiError ? e.message : 'Could not open selected client.';
      setValidationMessage(msg);
      flash('error', msg);
    }
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!model) {
      return;
    }
    setValidationMessage(null);

    const payload = {
      clientID: model.clientID ?? null,
      clientName: model.clientName,
      clientShortName: model.clientShortName || null,
      businessUnitID: model.businessUnitID,
      businessClassID: model.businessClassID,
      clientCategoryID: model.clientCategoryID,
      clientTypeID: model.clientTypeID,
      statusID: model.statusID,
      isDeleted: model.isDeleted ?? false,
      existingClients: model.existingClients,
      statusOptions: model.statusOptions,
      businessUnitOptions: model.businessUnitOptions,
      businessClassOptions: model.businessClassOptions,
      clientCategoryOptions: model.clientCategoryOptions,
      clientTypeOptions: model.clientTypeOptions,
    };

    try {
      const result = await apiPostJson<{ clientId: number; message: string }>('/api/clients', payload);
      flash('success', result.message);
      const savedId = result.clientId;
      navigate(`/clients/${savedId}`, { replace: true });
      await load({ edit: true, clientId: savedId });
      await refreshDirectoryOnly();
      setDirectorySearch('');
      setSelectedListId(savedId);
    } catch (err) {
      if (err instanceof ApiError) {
        setValidationMessage(err.message);
        flash('error', err.message);
      }
    }
  };

  const resetBulkWizard = () => {
    setBulkWizardOpen(false);
    setBulkRows([]);
    setBulkSkippedEmpty(0);
    setBulkVerifyOk(false);
    setBulkVerifyErrors([]);
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
    try {
      const result = await apiPostFormData<BulkParseResult>('/api/clients/bulk-import/parse', formData);
      setBulkRows(result.rows);
      setBulkSkippedEmpty(result.skippedEmptyRows);
      setBulkWizardOpen(true);
      if (result.rows.length === 0) {
        flash('error', 'No importable rows were found (every data row was missing ClientName).');
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
    try {
      const result = await apiPostJson<BulkVerifyResult>('/api/clients/bulk-import/verify', { rows: bulkRows });
      setBulkVerifyErrors(result.errors ?? []);
      setBulkVerifyOk(!!result.valid);
      if (result.valid) {
        flash('success', 'Verification passed. You can import these rows into the database.');
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
      const result = await apiPostJson<BulkCommitResult>('/api/clients/bulk-import/commit', { rows: bulkRows });
      flash(
        'success',
        `Imported ${result.importedCount} client(s).${
          result.skippedEmptyRows ? ` Skipped ${result.skippedEmptyRows} empty row(s).` : ''
        }`,
      );
      resetBulkWizard();
      await load();
      await refreshDirectoryOnly();
      setDirectorySearch('');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Import failed.');
    } finally {
      setBulkBusy(false);
    }
  };

  const handleSoftDelete = async () => {
    if (!isEdit || clientId === undefined) {
      return;
    }
    if (!window.confirm('Soft-delete this client?')) {
      return;
    }
    try {
      const result = await apiPostJson<{ message: string }>(`/api/clients/${clientId}/soft-delete`, {});
      flash('success', result.message);
      await load();
      await refreshDirectoryOnly();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Soft-delete failed.';
      flash('error', msg);
    }
  };

  const handleRestore = async () => {
    if (!isEdit || clientId === undefined) {
      return;
    }
    if (!window.confirm('Restore this client?')) {
      return;
    }
    try {
      const result = await apiPostJson<{ message: string }>(`/api/clients/${clientId}/restore`, {});
      flash('success', result.message);
      await load();
      await refreshDirectoryOnly();
    } catch (err) {
      const msg = err instanceof ApiError ? err.message : 'Restore failed.';
      flash('error', msg);
    }
  };

  if (loadError) {
    return (
      <div className="client-workflow-fill client-workflow-error">
        <section className="card narrow-card">
          <h1>{isEdit ? 'Edit Client' : 'New Client'}</h1>
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

  const title = isEdit ? 'Edit Client' : 'New Client';

  return (
    <div className="client-workflow-fill">
      <section className="page-header client-workflow-header">
        <div className="page-header-top">
          <h1>{title}</h1>
          {!isEdit ? (
            <div className="page-header-actions">
              <input
                id="client-bulk-csv"
                type="file"
                accept=".csv,text/csv"
                className="visually-hidden"
                disabled={bulkBusy}
                onChange={(ev) => void handleBulkImportFile(ev)}
              />
              <label
                htmlFor="client-bulk-csv"
                className={
                  'secondary-button bulk-import-label' +
                  (bulkBusy || bulkWizardOpen ? ' disabled-label' : '')
                }
              >
                Bulk import
              </label>
            </div>
          ) : null}
        </div>
        <p>Use this screen to add a new client or update an existing client from the directory on the right.</p>
      </section>

      <div className="two-column-layout client-two-column">
        <section className="card form-card client-form-card">
          <form className="client-form" autoComplete="off" onSubmit={(e) => void handleSubmit(e)}>
            {validationMessage && <div className="validation-summary">{validationMessage}</div>}
            {model.isDeleted ? (
              <div className="validation-summary" role="status">
                This client is currently soft-deleted. Use Restore in the danger zone to bring it back.
              </div>
            ) : null}

            <div className="form-grid client-form-grid">
              <label htmlFor="clientName">Client Name</label>
              <input
                id="clientName"
                value={model.clientName}
                onChange={(e) => setModel({ ...model, clientName: e.target.value })}
              />

              <label htmlFor="clientShortName">Client Short Name</label>
              <input
                id="clientShortName"
                value={model.clientShortName ?? ''}
                onChange={(e) => setModel({ ...model, clientShortName: e.target.value })}
              />

              <label htmlFor="businessUnitID">Business Unit</label>
              <select
                id="businessUnitID"
                value={model.businessUnitID > 0 ? model.businessUnitID : ''}
                onChange={(e) =>
                  setModel({
                    ...model,
                    businessUnitID: Number(e.target.value),
                    businessClassID: 0,
                  })
                }
              >
                <option value="">-- Select business unit --</option>
                {model.businessUnitOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="businessClassID">Business Class</label>
              <select
                id="businessClassID"
                value={model.businessClassID > 0 ? model.businessClassID : ''}
                onChange={(e) =>
                  setModel({
                    ...model,
                    businessClassID: e.target.value ? Number(e.target.value) : 0,
                  })
                }
              >
                <option value="">-- Select business class --</option>
                {model.businessClassOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="clientCategoryID">Client Category</label>
              <select
                id="clientCategoryID"
                value={model.clientCategoryID > 0 ? model.clientCategoryID : ''}
                onChange={(e) =>
                  setModel({
                    ...model,
                    clientCategoryID: e.target.value ? Number(e.target.value) : 0,
                  })
                }
              >
                <option value="">-- Select client category --</option>
                {model.clientCategoryOptions.map((o) => (
                  <option key={`cc-${o.value}`} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="clientTypeID">Client Type</label>
              <select
                id="clientTypeID"
                value={model.clientTypeID > 0 ? model.clientTypeID : ''}
                onChange={(e) =>
                  setModel({
                    ...model,
                    clientTypeID: e.target.value ? Number(e.target.value) : 0,
                  })
                }
              >
                <option value="">-- Select client type --</option>
                {model.clientTypeOptions.map((o) => (
                  <option key={`ct-${o.value}`} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="statusID">Status</label>
              <select
                id="statusID"
                value={model.statusID || ''}
                onChange={(e) => setModel({ ...model, statusID: Number(e.target.value) })}
              >
                {model.statusOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>
            </div>

            <div className="button-row">
              <button type="submit" className="primary-button">
                Save Client
              </button>
              <Link className="secondary-button" to="/">
                Cancel
              </Link>
            </div>
          </form>

          {isEdit && (
            <div className="danger-zone">
              {model.isDeleted ? (
                <button type="button" className="secondary-button" onClick={() => void handleRestore()}>
                  Restore Client
                </button>
              ) : (
                <button type="button" className="danger-button" onClick={() => void handleSoftDelete()}>
                  Soft-Delete Client
                </button>
              )}
              <span>
                {model.isDeleted
                  ? 'Restore will clear IsDeleted in dbo.tClient.'
                  : 'Soft-delete sets IsDeleted = 1 in dbo.tClient and writes to dbo.tActivityLog.'}
              </span>
            </div>
          )}
        </section>

        <section className="card client-list-card" aria-labelledby="client-directory-heading">
          <div className="card-heading">
            <h2 id="client-directory-heading">Clients</h2>
            <p>Search and scroll the full list. Click a row to select; double-click to edit.</p>
          </div>
          <label className="client-directory-search-label" htmlFor="client-directory-search">
            Search clients
          </label>
          <input
            id="client-directory-search"
            type="search"
            className="client-directory-search"
            placeholder="Filter by name, ID, status, business unit, category..."
            value={directorySearch}
            onChange={(e) => setDirectorySearch(e.target.value)}
            autoComplete="off"
          />
          <label className="client-directory-filter-hint" style={{ display: 'inline-flex', gap: '0.5rem', alignItems: 'center' }}>
            <input
              type="checkbox"
              checked={showDeleted}
              onChange={(e) => setShowDeleted(e.target.checked)}
              aria-label="Include soft-deleted clients"
            />
            Show deleted
          </label>
          {directorySearch.trim() ? (
            <p className="field-help client-directory-filter-hint">
              Showing {filteredExistingClients.length} of {model.existingClients.length} clients.
            </p>
          ) : null}
          <div className="client-directory-scroll">
            <table className="client-directory-table">
              <thead>
                <tr>
                  <th scope="col">Client</th>
                  <th scope="col">Short name</th>
                  <th scope="col">Status</th>
                  <th scope="col">Business Unit</th>
                  <th scope="col">Business Class</th>
                  <th scope="col">Category</th>
                  <th scope="col">Type</th>
                </tr>
              </thead>
              <tbody>
                {model.existingClients.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="client-directory-empty">
                      No clients in the directory yet.
                    </td>
                  </tr>
                ) : filteredExistingClients.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="client-directory-empty">
                      No clients match your search.
                    </td>
                  </tr>
                ) : (
                  filteredExistingClients.map((c) => {
                    const selected = selectedListId === c.clientID;
                    return (
                      <tr
                        key={c.clientID}
                        className={
                          (selected ? 'client-directory-row client-directory-row-selected' : 'client-directory-row') +
                          (c.isDeleted ? ' client-directory-row-deleted' : '')
                        }
                        tabIndex={0}
                        onClick={() => setSelectedListId(c.clientID)}
                        onDoubleClick={() => void handleOpenClientFromList(c.clientID)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault();
                            setSelectedListId(c.clientID);
                          }
                        }}
                      >
                        <td>{rowDisplayName(c)}</td>
                        <td>{c.clientShortName ?? ''}</td>
                        <td>{c.statusDescription ?? c.statusCode ?? ''}</td>
                        <td>{c.businessUnitName ?? ''}</td>
                        <td>{formatBusinessClassDisplay(c.businessClassName, c.businessClassDescription)}</td>
                        <td>{c.ccName ?? ''}</td>
                        <td>{c.ctName ?? ''}</td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
          <button type="button" className="secondary-button full-width client-directory-edit-btn" onClick={handleNavigateSelected}>
            Edit Selected Client
          </button>
        </section>
      </div>

      {bulkWizardOpen ? (
        <div className="bulk-import-overlay" role="dialog" aria-modal="true" aria-labelledby="bulk-import-title">
          <div className="bulk-import-panel card">
            <h2 id="bulk-import-title">Bulk import preview</h2>
            <p className="field-help">
              Bulk import accepts CSV with columns: ClientName (required), ClientShortName, BusinessUnitID,
              BusinessClassID, ClientCategoryID, ClientTypeID, StatusID.
              {bulkSkippedEmpty > 0
                ? ` ${bulkSkippedEmpty} CSV row(s) were skipped because ClientName was empty.`
                : ''}
            </p>

            {bulkVerifyOk ? (
              <div className="bulk-verify-pass" role="status">
                <span className="bulk-verify-check" aria-hidden="true">
                  OK
                </span>
                <span>Verification passed - you can select Import to load rows into tClient.</span>
              </div>
            ) : null}

            {bulkVerifyErrors.length > 0 ? (
              <div className="bulk-verify-fail" role="alert">
                <strong>Verification failed</strong>
                <ul className="bulk-verify-error-list">
                  {bulkVerifyErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            ) : null}

            <div className="bulk-preview-scroll">
              <table className="bulk-preview-table">
                <thead>
                  <tr>
                    <th>Line</th>
                    <th>ClientName</th>
                    <th>ClientShortName</th>
                    <th>BusinessUnitID</th>
                    <th>BusinessClassID</th>
                    <th>ClientCategoryID</th>
                    <th>ClientTypeID</th>
                    <th>StatusID</th>
                  </tr>
                </thead>
                <tbody>
                  {bulkRows.length === 0 ? (
                    <tr>
                      <td colSpan={8}>No rows to display.</td>
                    </tr>
                  ) : (
                    bulkRows.map((r) => (
                      <tr key={r.lineNumber}>
                        <td>{r.lineNumber}</td>
                        <td>{r.clientName}</td>
                        <td>{r.clientShortName}</td>
                        <td>{r.businessUnitID}</td>
                        <td>{r.businessClassID}</td>
                        <td>{r.clientCategoryID}</td>
                        <td>{r.clientTypeID}</td>
                        <td>{r.statusID}</td>
                      </tr>
                    ))
                  )}
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
                disabled={bulkBusy || !bulkVerifyOk}
                onClick={() => void handleBulkCommit()}
              >
                Import
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
