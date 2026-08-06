import { FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  applyGridRowToProjectModel,
  gridFilterIdsFromModel,
  type ProjectGridNavigationState,
} from '../workflows/projectGridPrefill';
import { formatBusinessClassDisplay } from '../utils/businessClassDisplay';
import { formatCalendarDateDisplay, toInputDateValue } from '../utils/dateInput';
import type { FlashFn } from '../App';
import { ApiError, apiDelete, apiGet, apiPostJson } from '../api';

interface SelectOption {
  value: string;
  text: string;
}

/** Coerce API select lists (camelCase or PascalCase) into { value, text } pairs. */
function normalizeSelectOptions(raw: unknown): SelectOption[] {
  if (!Array.isArray(raw)) {
    return [];
  }
  return raw
    .map((item): SelectOption | null => {
      if (item === null || typeof item !== 'object') {
        return null;
      }
      const o = item as Record<string, unknown>;
      const value = o.value ?? o.Value;
      const text = o.text ?? o.Text;
      if (value === undefined || value === null || String(value).trim() === '') {
        return null;
      }
      return { value: String(value), text: String(text ?? value) };
    })
    .filter((x): x is SelectOption => x !== null);
}

interface ProjectClientClassificationLookups {
  clientTypeOptions: SelectOption[];
  clientStatusOptions: SelectOption[];
}

interface ClientDirectoryRow {
  clientID: number;
  clientName?: string | null;
  clientShortName?: string | null;
  businessUnitID?: number | null;
  businessUnitName?: string | null;
  businessClassID?: number | null;
  businessClassName?: string | null;
  businessClassDescription?: string | null;
  clientCategoryID?: number | null;
  ccName?: string | null;
  clientTypeID?: number | null;
  ctName?: string | null;
  ctDescription?: string | null;
  statusID?: number | null;
  statusCode?: string | null;
  statusDescription?: string | null;
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
  /** When true, project is Online / currently in progress (green-dot on Home). */
  isOnline?: boolean;

  // Revenue entry (writes a row to dbo.tProjectRevenue)
  revenueAmount?: number | null;
  revenueEffectiveDate?: string | null;
  revenueChangeReason?: string | null;

  // Read-only client info
  clientName?: string | null;
  clientShortName?: string | null;
  clientBusinessUnitID?: number | null;
  clientBusinessUnitName?: string | null;
  clientBusinessClassID?: number | null;
  clientBusinessClassName?: string | null;
  clientCategoryID?: number | null;
  clientCategoryName?: string | null;
  clientTypeID?: number | null;
  clientTypeName?: string | null;
  clientStatusID?: number | null;
  clientStatusCode?: string | null;

  selectedServiceIds: number[];
  calendarItems?: ProjectCalendarItemDraft[];

  clientDirectoryRows?: ClientDirectoryRow[];
  clientOptions: SelectOption[];
  businessUnitOptions?: SelectOption[];
  businessClassOptions?: SelectOption[];
  clientCategoryOptions?: SelectOption[];
  clientTypeOptions?: SelectOption[];
  clientStatusOptions?: SelectOption[];
  userGroupOptions: SelectOption[];
  projectTypeOptions: SelectOption[];
  statusOptions: SelectOption[];
}

interface ProjectCalendarItemDraft {
  /** Client-only key for React list rendering before save. */
  tempId: string;
  calendarItemID?: number;
  itemType: 'Deliverable' | 'Meeting' | 'Task' | 'Other';
  title: string;
  ownerUserID: number | '';
  receivedDate: string;
  completedDate: string;
  notes: string;
  sortOrder?: number;
}

interface UserGroupMemberOption {
  userID: number;
  userFullName: string;
}

const CALENDAR_ITEM_TYPES: ProjectCalendarItemDraft['itemType'][] = [
  'Deliverable',
  'Meeting',
  'Task',
  'Other',
];

function newCalendarDraft(sortOrder = 0): ProjectCalendarItemDraft {
  return {
    tempId: `cal-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    itemType: 'Task',
    title: '',
    ownerUserID: '',
    receivedDate: '',
    completedDate: '',
    notes: '',
    sortOrder,
  };
}

interface CatalogServiceRow {
  serviceID: number;
  serviceName: string;
  serviceCategoryID: number;
  categoryName?: string | null;
  serviceCategoryDescription?: string | null;
}

interface ProjectRevenueRow {
  revenueID: number;
  projectID: number;
  effectiveDate: string;
  revenue: number;
  changeReason?: string | null;
  recordedBy?: number | null;
  recordedByName?: string | null;
  createdDate?: string | null;
}

interface ProjectNoteRow {
  noteID: number;
  projectID: number;
  userID: number;
  userFullName?: string | null;
  noteDate?: string | null;
  noteType?: string | null;
  noteText?: string | null;
  isPinned: boolean;
  createdDate?: string | null;
}

interface ActivityLogRow {
  logID: number;
  entityType: string;
  entityID: number;
  actionDate?: string | null;
  action: string;
  fieldName?: string | null;
  oldValue?: string | null;
  newValue?: string | null;
}

type ProjectTab = 'client' | 'project' | 'services' | 'calendar' | 'revenue' | 'notes' | 'activity';

/** Matches admin/client Client Type dropdown (name and description). */
function optionTextForValue(options: SelectOption[] | undefined, value: number | '' | null | undefined): string {
  if (value === '' || value == null || value === 0) {
    return '';
  }
  return options?.find((o) => o.value === String(value))?.text ?? '';
}

function classificationSelectValue(
  modelValue: number | null | undefined,
  filterValue: number | '',
): string {
  const v = modelValue != null && modelValue > 0 ? modelValue : filterValue;
  return v === '' || v == null ? '' : String(v);
}

function formatClientTypeDisplay(ctName?: string | null, ctDescription?: string | null): string {
  const name = (ctName ?? '').trim();
  const desc = (ctDescription ?? '').trim();
  if (!desc) {
    return name;
  }
  return name ? `${name} - ${desc}` : desc;
}

function formatMoney(n: number | null | undefined): string {
  if (n == null) {
    return '';
  }
  return new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 2,
  }).format(n);
}

function clientComboLabel(row: ClientDirectoryRow): string {
  const name = (row.clientName ?? '').trim();
  const shortName = (row.clientShortName ?? '').trim();
  if (name && shortName) {
    return `${name} (${shortName})`;
  }
  return name || `Client ${row.clientID}`;
}

function clientComboMatchesSearch(row: ClientDirectoryRow, rawQuery: string): boolean {
  const q = rawQuery.trim().toLowerCase();
  if (!q) {
    return true;
  }
  const haystack = [String(row.clientID), row.clientName, row.clientShortName];
  return haystack.some((part) => part != null && String(part).toLowerCase().includes(q));
}

interface ClientComboProps {
  id: string;
  rows: ClientDirectoryRow[];
  value: number;
  onSelect: (row: ClientDirectoryRow | null) => void;
  placeholder?: string;
}

/** Searchable client picker for new projects (filtered by BU / business class). */
function ClientCombo({ id, rows, value, onSelect, placeholder }: ClientComboProps) {
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);

  const selected = useMemo(() => rows.find((r) => r.clientID === value) ?? null, [rows, value]);

  const ordered = useMemo(
    () =>
      [...rows].sort((a, b) =>
        clientComboLabel(a).toLowerCase().localeCompare(clientComboLabel(b).toLowerCase()),
      ),
    [rows],
  );

  const filtered = useMemo(() => {
    if (!query.trim()) {
      return ordered;
    }
    return ordered.filter((r) => clientComboMatchesSearch(r, query));
  }, [ordered, query]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const onDoc = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) {
        setOpen(false);
        setQuery('');
      }
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  const choose = (clientID: number) => {
    if (clientID <= 0) {
      onSelect(null);
    } else {
      const row = rows.find((r) => r.clientID === clientID);
      if (row) {
        onSelect(row);
      }
    }
    setOpen(false);
    setQuery('');
  };

  const selectedLabel = selected ? clientComboLabel(selected) : '';

  return (
    <div className="user-combo" ref={wrapRef}>
      <input
        id={id}
        type="text"
        className="user-combo-input"
        role="combobox"
        aria-expanded={open}
        aria-controls={`${id}-list`}
        autoComplete="off"
        placeholder={placeholder ?? 'Type to search client name…'}
        value={open ? query : selectedLabel}
        onFocus={() => {
          setOpen(true);
          setQuery('');
        }}
        onChange={(e) => {
          setQuery(e.target.value);
          setOpen(true);
        }}
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            setOpen(false);
            setQuery('');
            (e.target as HTMLInputElement).blur();
          }
        }}
      />
      {open ?
        <ul className="user-combo-list" id={`${id}-list`} role="listbox">
          <li
            role="option"
            aria-selected={value === 0}
            className="user-combo-option user-combo-option--none"
            onMouseDown={(e) => {
              e.preventDefault();
              choose(0);
            }}
          >
            — Select client —
          </li>
          {filtered.length === 0 ?
            <li className="user-combo-empty" role="status">
              No clients match your search
            </li>
          : filtered.map((r) => (
              <li
                key={r.clientID}
                role="option"
                aria-selected={r.clientID === value}
                className={'user-combo-option' + (r.clientID === value ? ' selected' : '')}
                onMouseDown={(e) => {
                  e.preventDefault();
                  choose(r.clientID);
                }}
              >
                <span className="user-combo-name">{clientComboLabel(r)}</span>
                {(r.statusDescription ?? r.statusCode) ?
                  <span className="user-combo-dept">{r.statusDescription ?? r.statusCode}</span>
                : null}
              </li>
            ))
          }
        </ul>
      : null}
    </div>
  );
}

export default function ProjectPage({ flash }: { flash: FlashFn }) {
  const navigate = useNavigate();
  const location = useLocation();
  const { id: idParam } = useParams();
  const projectId = idParam !== undefined ? Number(idParam) : undefined;
  const isEdit = projectId !== undefined && !Number.isNaN(projectId);

  const [activeTab, setActiveTab] = useState<ProjectTab>('client');
  const [model, setModel] = useState<ProjectFormModel | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [validationMessage, setValidationMessage] = useState<string | null>(null);

  const [catalog, setCatalog] = useState<CatalogServiceRow[]>([]);
  const [serviceFunctionFilter, setServiceFunctionFilter] = useState('');
  const [serviceCategoryFilter, setServiceCategoryFilter] = useState<number | ''>('');
  const [revenueHistory, setRevenueHistory] = useState<ProjectRevenueRow[]>([]);
  const [subresourcesLoaded, setSubresourcesLoaded] = useState(false);
  const [notes, setNotes] = useState<ProjectNoteRow[]>([]);
  const [activity, setActivity] = useState<ActivityLogRow[]>([]);
  const [noteDraft, setNoteDraft] = useState<{ noteType: string; noteText: string; isPinned: boolean }>({
    noteType: 'General',
    noteText: '',
    isPinned: false,
  });
  const [userGroupOptionsForClient, setUserGroupOptionsForClient] = useState<SelectOption[]>([]);
  const [clientBuFilter, setClientBuFilter] = useState<number | ''>('');
  const [clientBcFilter, setClientBcFilter] = useState<number | ''>('');
  const [clientTypeFilter, setClientTypeFilter] = useState<number | ''>('');
  const [clientStatusFilter, setClientStatusFilter] = useState<number | ''>('');
  const [calendarItems, setCalendarItems] = useState<ProjectCalendarItemDraft[]>([]);
  const [ownerOptions, setOwnerOptions] = useState<UserGroupMemberOption[]>([]);

  const applyClientFromRow = useCallback((row: ClientDirectoryRow) => {
    setClientBuFilter(row.businessUnitID ?? '');
    setClientBcFilter(row.businessClassID ?? '');
    setClientTypeFilter(row.clientTypeID ?? '');
    setClientStatusFilter(row.statusID ?? '');
    setModel((prev) =>
      prev
        ? {
            ...prev,
            clientID: row.clientID,
            clientName: row.clientName ?? '',
            clientShortName: row.clientShortName ?? '',
            clientBusinessUnitID: row.businessUnitID ?? null,
            clientBusinessUnitName: row.businessUnitName ?? '',
            clientBusinessClassID: row.businessClassID ?? null,
            clientBusinessClassName: formatBusinessClassDisplay(
              row.businessClassName,
              row.businessClassDescription,
            ),
            clientTypeID: row.clientTypeID ?? null,
            clientTypeName: formatClientTypeDisplay(row.ctName, row.ctDescription),
            clientStatusID: row.statusID ?? null,
            clientStatusCode: row.statusCode ?? row.statusDescription ?? '',
          }
        : prev,
    );
  }, []);

  const clearClientSelection = useCallback(() => {
    setModel((prev) =>
      prev
        ? {
            ...prev,
            clientID: 0,
            clientName: '',
            clientShortName: '',
            clientBusinessUnitID: null,
            clientBusinessUnitName: '',
            clientBusinessClassID: null,
            clientBusinessClassName: '',
            clientTypeID: null,
            clientTypeName: '',
            clientStatusID: null,
            clientStatusCode: '',
          }
        : prev,
    );
  }, []);

  const handleClientClassificationChange = useCallback(
    (field: 'bu' | 'bc' | 'type' | 'status', raw: string) => {
      const id = raw ? Number(raw) : '';
      const numericId = id === '' ? null : id;

      if (field === 'bu') {
        setClientBuFilter(id);
        if (!isEdit) {
          setClientBcFilter('');
        }
      } else if (field === 'bc') {
        setClientBcFilter(id);
      } else if (field === 'type') {
        setClientTypeFilter(id);
      } else {
        setClientStatusFilter(id);
      }

      if (!isEdit) {
        clearClientSelection();
      }

      setModel((prev) => {
        if (!prev) {
          return prev;
        }
        const next = { ...prev };
        if (field === 'bu') {
          next.clientBusinessUnitID = numericId;
          next.clientBusinessUnitName = optionTextForValue(prev.businessUnitOptions, id);
        } else if (field === 'bc') {
          next.clientBusinessClassID = numericId;
          next.clientBusinessClassName = optionTextForValue(prev.businessClassOptions, id);
        } else if (field === 'type') {
          next.clientTypeID = numericId;
          next.clientTypeName = optionTextForValue(prev.clientTypeOptions, id);
        } else {
          next.clientStatusID = numericId;
          next.clientStatusCode = optionTextForValue(prev.clientStatusOptions, id);
        }
        return next;
      });
    },
    [clearClientSelection, isEdit],
  );

  const filteredClientRows = useMemo(() => {
    if (isEdit && model && model.clientID > 0) {
      const projectClient = (model.clientDirectoryRows ?? []).filter((r) => r.clientID === model.clientID);
      return projectClient.length > 0 ? projectClient : [];
    }

    let rows = model?.clientDirectoryRows ?? [];
    if (clientBuFilter !== '') {
      rows = rows.filter((r) => r.businessUnitID === clientBuFilter);
    }
    if (clientBcFilter !== '') {
      rows = rows.filter((r) => r.businessClassID === clientBcFilter);
    }
    if (clientTypeFilter !== '') {
      rows = rows.filter((r) => r.clientTypeID === clientTypeFilter);
    }
    if (clientStatusFilter !== '') {
      rows = rows.filter((r) => r.statusID === clientStatusFilter);
    }
    return rows;
  }, [isEdit, model, clientBuFilter, clientBcFilter, clientTypeFilter, clientStatusFilter]);

  const filteredClientOptions = useMemo(
    () =>
      filteredClientRows.map((r) => ({
        value: String(r.clientID),
        text: r.clientName?.trim() || `Client ${r.clientID}`,
      })),
    [filteredClientRows],
  );

  const serviceFunctionFilterOptions = useMemo(() => {
    const seen = new Set<string>();
    const options: string[] = [];
    for (const row of catalog) {
      const fn = (row.serviceCategoryDescription ?? '').trim();
      if (!fn || seen.has(fn)) {
        continue;
      }
      seen.add(fn);
      options.push(fn);
    }
    return options.sort((a, b) => a.localeCompare(b));
  }, [catalog]);

  const serviceCategoryFilterOptions = useMemo(() => {
    let rows = catalog;
    if (serviceFunctionFilter !== '') {
      rows = rows.filter(
        (r) => (r.serviceCategoryDescription ?? '').trim() === serviceFunctionFilter,
      );
    }
    const map = new Map<number, string>();
    for (const row of rows) {
      if (row.serviceCategoryID > 0 && row.categoryName) {
        map.set(row.serviceCategoryID, row.categoryName.trim());
      }
    }
    return [...map.entries()]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [catalog, serviceFunctionFilter]);

  const filteredCatalogServices = useMemo(() => {
    let rows = catalog;
    if (serviceFunctionFilter !== '') {
      rows = rows.filter(
        (r) => (r.serviceCategoryDescription ?? '').trim() === serviceFunctionFilter,
      );
    }
    if (serviceCategoryFilter !== '') {
      rows = rows.filter((r) => r.serviceCategoryID === serviceCategoryFilter);
    }
    return rows;
  }, [catalog, serviceFunctionFilter, serviceCategoryFilter]);

  useEffect(() => {
    if (
      serviceCategoryFilter !== '' &&
      !serviceCategoryFilterOptions.some((o) => o.id === serviceCategoryFilter)
    ) {
      setServiceCategoryFilter('');
    }
  }, [serviceCategoryFilterOptions, serviceCategoryFilter]);

  const load = useCallback(async () => {
    try {
      setLoadError(null);
      const path = isEdit ? `/api/projects/form/${projectId}` : '/api/projects/form';
      const [data, classificationLookups] = await Promise.all([
        apiGet<ProjectFormModel>(path),
        apiGet<ProjectClientClassificationLookups>('/api/projects/lookups/client-classification').catch(
          () => ({ clientTypeOptions: [], clientStatusOptions: [] }),
        ),
      ]);

      const clientTypeOptions = normalizeSelectOptions(
        classificationLookups.clientTypeOptions?.length
          ? classificationLookups.clientTypeOptions
          : data.clientTypeOptions,
      );
      const clientStatusOptions = normalizeSelectOptions(
        classificationLookups.clientStatusOptions?.length
          ? classificationLookups.clientStatusOptions
          : data.clientStatusOptions,
      );

      let merged: ProjectFormModel = {
        ...data,
        ugid: Number(
          (data as { ugid?: number; userGroupID?: number; uGID?: number }).ugid
            ?? (data as { userGroupID?: number }).userGroupID
            ?? (data as { uGID?: number }).uGID
            ?? 0,
        ),
        selectedServiceIds: Array.isArray(data.selectedServiceIds) ? data.selectedServiceIds : [],
        businessUnitOptions: normalizeSelectOptions(data.businessUnitOptions),
        businessClassOptions: normalizeSelectOptions(data.businessClassOptions),
        clientTypeOptions,
        clientStatusOptions,
        projectTypeOptions: normalizeSelectOptions(data.projectTypeOptions),
        statusOptions: normalizeSelectOptions(data.statusOptions),
        userGroupOptions: normalizeSelectOptions(data.userGroupOptions),
        clientOptions: normalizeSelectOptions(data.clientOptions),
      };

      const gridState = location.state as ProjectGridNavigationState | null;
      if (gridState?.fromGrid && gridState.gridColumns?.length && gridState.gridRow?.length) {
        merged = applyGridRowToProjectModel(merged, gridState.gridColumns, gridState.gridRow);
        const filters = gridFilterIdsFromModel(merged);
        setClientBuFilter(filters.businessUnitId);
        setClientBcFilter(filters.businessClassId);
        setActiveTab('client');
      }

      setModel(merged);

      if (merged.clientID > 0) {
        setClientBuFilter(merged.clientBusinessUnitID ?? '');
        setClientBcFilter(merged.clientBusinessClassID ?? '');
        setClientTypeFilter(merged.clientTypeID ?? '');
        setClientStatusFilter(merged.clientStatusID ?? '');
      } else {
        setClientTypeFilter('');
        setClientStatusFilter('');
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) {
        setLoadError('Project not found.');
      } else {
        setLoadError(e instanceof ApiError ? e.message : 'Could not load project form.');
      }
    }
  }, [projectId, isEdit, location.state]);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    void (async () => {
      try {
        const rows = await apiGet<CatalogServiceRow[]>('/api/projects/services-catalog');
        setCatalog(rows);
      } catch {
        setCatalog([]);
      }
    })();
  }, []);

  // Refresh user-group options when client changes (full active list).
  useEffect(() => {
    if (!model || model.clientID <= 0) {
      setUserGroupOptionsForClient([]);
      return;
    }
    let cancelled = false;
    void (async () => {
      try {
        const opts = normalizeSelectOptions(
          await apiGet<SelectOption[]>(`/api/projects/user-groups?clientId=${model.clientID}`),
        );
        if (!cancelled) {
          setUserGroupOptionsForClient(opts);
        }
      } catch {
        if (!cancelled) {
          setUserGroupOptionsForClient([]);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [model?.clientID]);

  useEffect(() => {
    if (!model || model.clientID <= 0) {
      return;
    }
    const inDirectory = (model.clientDirectoryRows ?? []).some((r) => r.clientID === model.clientID);
    if (!inDirectory) {
      setModel((prev) =>
        prev
          ? {
              ...prev,
              clientID: 0,
              clientName: '',
              clientShortName: '',
              clientBusinessUnitID: null,
              clientBusinessUnitName: '',
              clientBusinessClassID: null,
              clientBusinessClassName: '',
              clientTypeID: null,
              clientTypeName: '',
              clientStatusID: null,
              clientStatusCode: '',
            }
          : prev,
      );
    }
  }, [model?.clientDirectoryRows, model?.clientID]);

  const refreshRevenueHistory = useCallback(async () => {
    if (!isEdit || projectId === undefined) {
      return;
    }
    try {
      setRevenueHistory(await apiGet<ProjectRevenueRow[]>(`/api/projects/${projectId}/revenue`));
    } catch {
      setRevenueHistory([]);
    }
  }, [isEdit, projectId]);

  // Preload edit-mode sub-resource tabs (revenue, notes, activity) on open.
  useEffect(() => {
    if (!isEdit || !projectId) {
      setSubresourcesLoaded(false);
      return;
    }
    setRevenueHistory([]);
    setSubresourcesLoaded(false);
    let cancelled = false;
    void (async () => {
      const revResult = await apiGet<ProjectRevenueRow[]>(`/api/projects/${projectId}/revenue`).then(
        (data) => ({ ok: true as const, data }),
        () => ({ ok: false as const, data: [] as ProjectRevenueRow[] }),
      );
      const notesResult = await apiGet<ProjectNoteRow[]>(`/api/projects/${projectId}/notes`).then(
        (data) => ({ ok: true as const, data }),
        () => ({ ok: false as const, data: [] as ProjectNoteRow[] }),
      );
      const activityResult = await apiGet<ActivityLogRow[]>(
        `/api/projects/${projectId}/activity?top=100`,
      ).then(
        (data) => ({ ok: true as const, data }),
        () => ({ ok: false as const, data: [] as ActivityLogRow[] }),
      );
      if (!cancelled) {
        setRevenueHistory(revResult.data);
        setNotes(notesResult.data);
        setActivity(activityResult.data);
        setSubresourcesLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [isEdit, projectId]);

  // Project Info "latest revenue" must match the history tab (same API / sort as dbo.tProjectRevenue).
  useEffect(() => {
    if (!isEdit || !subresourcesLoaded) {
      return;
    }
    if (revenueHistory.length === 0) {
      setModel((prev) =>
        prev
          ? {
              ...prev,
              revenueAmount: null,
              revenueEffectiveDate: null,
              revenueChangeReason: null,
            }
          : prev,
      );
      return;
    }
    const latest = revenueHistory[0];
    setModel((prev) =>
      prev
        ? {
            ...prev,
            revenueAmount: latest.revenue,
            revenueEffectiveDate: toInputDateValue(latest.effectiveDate) || null,
            revenueChangeReason: latest.changeReason ?? null,
          }
        : prev,
    );
  }, [revenueHistory, isEdit, subresourcesLoaded]);

  const handleServiceToggle = (serviceID: number) => {
    if (!model) {
      return;
    }
    const has = model.selectedServiceIds.includes(serviceID);
    setModel({
      ...model,
      selectedServiceIds: has
        ? model.selectedServiceIds.filter((x) => x !== serviceID)
        : [...model.selectedServiceIds, serviceID],
    });
  };

  const allFilteredServicesSelected = useMemo(() => {
    if (!model || filteredCatalogServices.length === 0) {
      return false;
    }
    return filteredCatalogServices.every((row) => model.selectedServiceIds.includes(row.serviceID));
  }, [filteredCatalogServices, model]);

  const someFilteredServicesSelected = useMemo(() => {
    if (!model || filteredCatalogServices.length === 0) {
      return false;
    }
    const selectedCount = filteredCatalogServices.filter((row) =>
      model.selectedServiceIds.includes(row.serviceID),
    ).length;
    return selectedCount > 0 && selectedCount < filteredCatalogServices.length;
  }, [filteredCatalogServices, model]);

  const handleToggleAllFilteredServices = () => {
    if (!model || filteredCatalogServices.length === 0) {
      return;
    }
    const filteredIds = filteredCatalogServices.map((row) => row.serviceID);
    if (allFilteredServicesSelected) {
      setModel({
        ...model,
        selectedServiceIds: model.selectedServiceIds.filter((id) => !filteredIds.includes(id)),
      });
      return;
    }
    setModel({
      ...model,
      selectedServiceIds: [...new Set([...model.selectedServiceIds, ...filteredIds])],
    });
  };

  const handleSelectAllCatalogServices = () => {
    if (!model || catalog.length === 0) {
      return;
    }
    setModel({
      ...model,
      selectedServiceIds: [...new Set([...model.selectedServiceIds, ...catalog.map((row) => row.serviceID)])],
    });
  };

  const handleClearAllServices = () => {
    if (!model) {
      return;
    }
    setModel({ ...model, selectedServiceIds: [] });
  };

  useEffect(() => {
    const ugid = model?.ugid ?? 0;
    if (ugid <= 0) {
      setOwnerOptions([]);
      return;
    }

    let cancelled = false;
    void (async () => {
      try {
        const rows = await apiGet<UserGroupMemberOption[]>(`/api/projects/user-groups/${ugid}/members`);
        if (!cancelled) {
          setOwnerOptions(Array.isArray(rows) ? rows : []);
        }
      } catch {
        if (!cancelled) {
          setOwnerOptions([]);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [model?.ugid]);

  const updateCalendarItem = (tempId: string, patch: Partial<ProjectCalendarItemDraft>) => {
    setCalendarItems((prev) => prev.map((row) => (row.tempId === tempId ? { ...row, ...patch } : row)));
  };

  const addCalendarItem = () => {
    setCalendarItems((prev) => [...prev, newCalendarDraft(prev.length)]);
  };

  const removeCalendarItem = (tempId: string) => {
    setCalendarItems((prev) => prev.filter((row) => row.tempId !== tempId));
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!model) {
      return;
    }
    setValidationMessage(null);

    try {
      const calendarPayload = !isEdit
        ? calendarItems
            .filter((row) => row.title.trim().length > 0)
            .map((row, index) => ({
              calendarItemID: row.calendarItemID ?? 0,
              itemType: row.itemType,
              title: row.title.trim(),
              ownerUserID: row.ownerUserID === '' ? null : row.ownerUserID,
              receivedDate: row.receivedDate || null,
              completedDate: row.completedDate || null,
              notes: row.notes.trim() || null,
              sortOrder: index,
            }))
        : undefined;

      const payload: ProjectFormModel = {
        ...model,
        projectID: isEdit ? projectId : model.projectID,
        ugid: model.ugid > 0 ? model.ugid : 0,
        // Canonical name expected by ProjectFormViewModel.UserGroupID
        ...( { userGroupID: model.ugid > 0 ? model.ugid : 0 } as object ),
        calendarItems: calendarPayload as ProjectFormModel['calendarItems'],
      } as ProjectFormModel;
      const result = await apiPostJson<{ projectId: number; message: string }>('/api/projects', payload);
      flash('success', result.message);
      const savedId = result.projectId;
      if (isEdit && projectId !== undefined) {
        await load();
        await refreshRevenueHistory();
      } else {
        navigate(`/projects/${savedId}`, { replace: true });
        await load();
      }
    } catch (err) {
      if (err instanceof ApiError) {
        setValidationMessage(err.message);
        flash('error', err.message);
      }
    }
  };

  const handleSoftDelete = async () => {
    if (!isEdit || projectId === undefined) {
      return;
    }
    if (!window.confirm('Soft-delete this project?')) {
      return;
    }
    try {
      const r = await apiPostJson<{ message: string }>(`/api/projects/${projectId}/soft-delete`, {});
      flash('success', r.message);
      navigate('/projects/new');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Soft-delete failed.');
    }
  };

  const handleRestore = async () => {
    if (!isEdit || projectId === undefined) {
      return;
    }
    try {
      const r = await apiPostJson<{ message: string }>(`/api/projects/${projectId}/restore`, {});
      flash('success', r.message);
      await load();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Restore failed.');
    }
  };

  const handleAddNote = async () => {
    if (!isEdit || projectId === undefined || !noteDraft.noteText.trim()) {
      return;
    }
    try {
      await apiPostJson(`/api/projects/${projectId}/notes`, {
        projectID: projectId,
        userID: 0, // server fills via context; placeholder
        noteType: noteDraft.noteType,
        noteText: noteDraft.noteText,
        isPinned: noteDraft.isPinned,
      });
      setNoteDraft({ noteType: 'General', noteText: '', isPinned: false });
      setNotes(await apiGet<ProjectNoteRow[]>(`/api/projects/${projectId}/notes`));
      flash('success', 'Note added.');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not add note.');
    }
  };

  const handleDeleteNote = async (noteId: number) => {
    if (!window.confirm('Delete this note?')) {
      return;
    }
    try {
      await apiDelete(`/api/projects/notes/${noteId}`);
      if (projectId !== undefined) {
        setNotes(await apiGet<ProjectNoteRow[]>(`/api/projects/${projectId}/notes`));
      }
      flash('success', 'Note deleted.');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not delete note.');
    }
  };

  const handleDeleteRevenue = async (revenueId: number) => {
    if (!window.confirm('Delete this revenue entry? It will be removed from the project history.')) {
      return;
    }
    try {
      await apiDelete(`/api/projects/revenue/${revenueId}`);
      await refreshRevenueHistory();
      flash('success', 'Revenue entry deleted.');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not delete revenue entry.');
    }
  };

  if (loadError) {
    return (
      <div className="client-workflow-fill client-workflow-error">
        <section className="card narrow-card">
          <h1>{isEdit ? 'Edit Project' : 'New Project'}</h1>
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
        <p>Loading…</p>
      </div>
    );
  }

  const groupOptions = useMemo(() => {
    const base =
      userGroupOptionsForClient.length > 0 ? userGroupOptionsForClient : (model?.userGroupOptions ?? []);
    if (!model || model.ugid <= 0) {
      return base;
    }
    const selected = String(model.ugid);
    if (base.some((o) => o.value === selected)) {
      return base;
    }
    // Keep the current assignment visible even if it is inactive / missing from the list.
    return [{ value: selected, text: `User Group ${selected}` }, ...base];
  }, [userGroupOptionsForClient, model?.userGroupOptions, model?.ugid]);
  const title = isEdit ? 'Edit Project' : 'New Project';

  const projectTabClass = (tab: ProjectTab) => 'admin-tab-panel' + (activeTab === tab ? ' active' : '');

  const projectTabBtn = (id: ProjectTab, label: string) => (
    <button
      key={id}
      type="button"
      role="tab"
      aria-selected={activeTab === id}
      className={activeTab === id ? 'active' : ''}
      onClick={() => setActiveTab(id)}
    >
      {label}
    </button>
  );

  return (
    <div className="client-workflow-fill">
      <section className="page-header client-workflow-header">
        <div className="page-header-top">
          <h1>{title}</h1>
        </div>
        <p>
          Project header lives in <code>dbo.tProject</code>; revenue/notes/services/activity are sub-resources.
        </p>
      </section>

      <section className="card admin-card project-workflow-card">
        <div className="admin-tabs" role="tablist" aria-label="Project tabs">
          {projectTabBtn('client', 'Client Info')}
          {projectTabBtn('project', 'Project Info')}
          {projectTabBtn('services', 'Services')}
          {!isEdit ? projectTabBtn('calendar', 'Calendar') : null}
          {isEdit ? projectTabBtn('revenue', 'Revenue history') : null}
          {isEdit ? projectTabBtn('notes', 'Notes') : null}
          {isEdit ? projectTabBtn('activity', 'Activity') : null}
        </div>

        <form autoComplete="off" onSubmit={(e) => void handleSubmit(e)}>
          {validationMessage ? <div className="validation-summary">{validationMessage}</div> : null}

          <div className="project-tab-panels">
          <div id="project-tab-client" className={projectTabClass('client')}>
            <h2>Client Info</h2>
            <div className="admin-edit-card">
            <p className="field-help">
              {isEdit
                ? 'This project is tied to the client below. You can update business unit, class, client type, and status from the dropdowns.'
                : 'Use the dropdowns to narrow the client list, then search and select a client. Only active clients with status Active (A) or Prospect (P) are shown.'}
            </p>

            <div className="form-grid">
              <label htmlFor="clientID">Client</label>
              {!isEdit ?
                <ClientCombo
                  id="clientID"
                  rows={filteredClientRows}
                  value={model.clientID > 0 ? model.clientID : 0}
                  onSelect={(row) => {
                    if (row) {
                      applyClientFromRow(row);
                    } else {
                      clearClientSelection();
                    }
                  }}
                  placeholder={
                    filteredClientRows.length === 0
                      ? 'No clients match the selected filters'
                      : 'Type to search client name…'
                  }
                />
              : <select
                  id="clientID"
                  value={model.clientID > 0 ? model.clientID : ''}
                  disabled
                  onChange={(e) => {
                    const id = Number(e.target.value);
                    const row = filteredClientRows.find((r) => r.clientID === id);
                    if (row) {
                      applyClientFromRow(row);
                    } else {
                      setModel({ ...model, clientID: id });
                    }
                  }}
                >
                  <option value="">-- Select client --</option>
                  {filteredClientOptions.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.text}
                    </option>
                  ))}
                </select>
              }

              <label>Client name</label>
              <input value={model.clientName ?? ''} readOnly aria-readonly />

              <label>Short name</label>
              <input value={model.clientShortName ?? ''} readOnly aria-readonly />

              <label htmlFor="clientBusinessUnitID">Business Unit</label>
              <select
                id="clientBusinessUnitID"
                value={classificationSelectValue(model.clientBusinessUnitID, clientBuFilter)}
                onChange={(e) => handleClientClassificationChange('bu', e.target.value)}
              >
                <option value="">{isEdit ? '-- Select business unit --' : 'All business units'}</option>
                {(model.businessUnitOptions ?? []).map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="clientBusinessClassID">Business Class</label>
              <select
                id="clientBusinessClassID"
                value={classificationSelectValue(model.clientBusinessClassID, clientBcFilter)}
                onChange={(e) => handleClientClassificationChange('bc', e.target.value)}
              >
                <option value="">{isEdit ? '-- Select business class --' : 'All business classes'}</option>
                {(model.businessClassOptions ?? []).map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="clientTypeID">Client Type</label>
              <select
                id="clientTypeID"
                value={classificationSelectValue(model.clientTypeID, clientTypeFilter)}
                onChange={(e) => handleClientClassificationChange('type', e.target.value)}
              >
                <option value="">{isEdit ? '-- Select client type --' : 'All client types'}</option>
                {(model.clientTypeOptions ?? []).map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="clientStatusID">Status</label>
              <select
                id="clientStatusID"
                value={classificationSelectValue(model.clientStatusID, clientStatusFilter)}
                onChange={(e) => handleClientClassificationChange('status', e.target.value)}
              >
                <option value="">{isEdit ? '-- Select status --' : 'All statuses'}</option>
                {(model.clientStatusOptions ?? []).map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>
            </div>
            </div>
          </div>

          <div id="project-tab-project" className={projectTabClass('project')}>
            <h2>Project Info</h2>
            <div className="admin-edit-card">
            <div className="form-grid">
              <label htmlFor="projectNumber">Project Number</label>
              <input
                id="projectNumber"
                value={model.projectNumber}
                onChange={(e) => setModel({ ...model, projectNumber: e.target.value })}
                required
                maxLength={50}
              />

              <label htmlFor="projectName">Project Name</label>
              <input
                id="projectName"
                value={model.projectName}
                onChange={(e) => setModel({ ...model, projectName: e.target.value })}
                required
                maxLength={200}
              />

              <label htmlFor="ugid">User Group</label>
              <select
                id="ugid"
                value={model.ugid > 0 ? model.ugid : ''}
                onChange={(e) => {
                  const nextUgid = Number(e.target.value) || 0;
                  setModel({ ...model, ugid: nextUgid });
                  // Ownership options change with the group — clear owners that may no longer apply.
                  setCalendarItems((prev) => prev.map((row) => ({ ...row, ownerUserID: '' })));
                }}
              >
                <option value="">-- Select user group --</option>
                {groupOptions.map((o) => (
                  <option key={o.value} value={o.value} title={o.text}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="projectTypeID">Project Type</label>
              <select
                id="projectTypeID"
                value={model.projectTypeID > 0 ? model.projectTypeID : ''}
                onChange={(e) => setModel({ ...model, projectTypeID: Number(e.target.value) })}
              >
                <option value="">-- Select project type --</option>
                {model.projectTypeOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="renewalDate">Renewal Date</label>
              <input
                id="renewalDate"
                type="date"
                value={toInputDateValue(model.renewalDate)}
                onChange={(e) => setModel({ ...model, renewalDate: e.target.value || null })}
              />

              <label htmlFor="dueDate">Due Date</label>
              <input
                id="dueDate"
                type="date"
                value={toInputDateValue(model.dueDate)}
                onChange={(e) => setModel({ ...model, dueDate: e.target.value || null })}
              />

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

              <label htmlFor="isOnline">Online</label>
              <span className="project-online-toggle">
                <input
                  id="isOnline"
                  type="checkbox"
                  checked={!!model.isOnline}
                  onChange={(e) => {
                    const next = e.target.checked;
                    setModel({ ...model, isOnline: next });
                    if (isEdit && projectId !== undefined) {
                      void (async () => {
                        try {
                          const r = await apiPostJson<{ message: string }>(
                            `/api/projects/${projectId}/online`,
                            { isOnline: next },
                          );
                          flash('success', r.message ?? (next ? 'Project is now Online.' : 'Project is now Offline.'));
                        } catch (err) {
                          setModel({ ...model, isOnline: !next });
                          flash(
                            'error',
                            err instanceof ApiError ? err.message : 'Could not update online status.',
                          );
                        }
                      })();
                    }
                  }}
                />{' '}
                <span
                  className={'home-online-dot' + (model.isOnline ? ' home-online-dot--on' : ' home-online-dot--off')}
                  aria-hidden="true"
                />{' '}
                {model.isOnline ? 'Online (in progress)' : 'Offline'}
              </span>

              <label htmlFor="revenueAmount">Revenue</label>
              <input
                id="revenueAmount"
                type="number"
                step="0.01"
                min="0"
                value={model.revenueAmount ?? ''}
                onChange={(e) =>
                  setModel({
                    ...model,
                    revenueAmount: e.target.value === '' ? null : Number(e.target.value),
                  })
                }
              />

              <label htmlFor="revenueEffectiveDate">Revenue Effective Date</label>
              <input
                id="revenueEffectiveDate"
                type="date"
                value={toInputDateValue(model.revenueEffectiveDate)}
                onChange={(e) =>
                  setModel({ ...model, revenueEffectiveDate: e.target.value || null })
                }
              />

              <label htmlFor="revenueChangeReason">Revenue Change Reason</label>
              <input
                id="revenueChangeReason"
                value={model.revenueChangeReason ?? ''}
                onChange={(e) => setModel({ ...model, revenueChangeReason: e.target.value })}
                maxLength={500}
              />
            </div>
            <p className="field-help">
              Latest revenue is shown below. Change the amount and save to append a new row to{' '}
              <code>dbo.tProjectRevenue</code>.
            </p>
            </div>
          </div>

          <div id="project-tab-services" className={projectTabClass('services')}>
            <h2>Services</h2>
            <div className="admin-edit-card">
            <p className="field-help">
              Filter by service function and category, then tick services to link on this project (
              <code>dbo.tProjectService</code>). Use <strong>Select all</strong> to add every service, or the
              header checkbox to toggle all services shown in the list.
            </p>
            <div className="form-grid">
              <label htmlFor="proj-svc-function-filter">Service Function</label>
              <select
                id="proj-svc-function-filter"
                value={serviceFunctionFilter}
                onChange={(e) => {
                  setServiceFunctionFilter(e.target.value);
                  setServiceCategoryFilter('');
                }}
              >
                <option value="">All service functions</option>
                {serviceFunctionFilterOptions.map((fn) => (
                  <option key={fn} value={fn}>
                    {fn}
                  </option>
                ))}
              </select>

              <label htmlFor="proj-svc-category-filter">Service Category</label>
              <select
                id="proj-svc-category-filter"
                value={serviceCategoryFilter === '' ? '' : String(serviceCategoryFilter)}
                onChange={(e) =>
                  setServiceCategoryFilter(e.target.value ? Number(e.target.value) : '')
                }
              >
                <option value="">All service categories</option>
                {serviceCategoryFilterOptions.map((o) => (
                  <option key={o.id} value={String(o.id)}>
                    {o.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="page-header-actions project-services-select-actions">
              <button
                type="button"
                className="secondary-button"
                disabled={catalog.length === 0}
                onClick={handleSelectAllCatalogServices}
              >
                Select all services
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={!model?.selectedServiceIds.length}
                onClick={handleClearAllServices}
              >
                Clear all
              </button>
            </div>
            <div className="project-tab-table-wrap">
              <table className="client-directory-table">
                <thead>
                  <tr>
                    <th>
                      <input
                        type="checkbox"
                        checked={allFilteredServicesSelected}
                        ref={(el) => {
                          if (el) {
                            el.indeterminate = someFilteredServicesSelected;
                          }
                        }}
                        onChange={handleToggleAllFilteredServices}
                        disabled={filteredCatalogServices.length === 0}
                        aria-label="Select or clear all services shown in the list"
                      />
                    </th>
                    <th>Service</th>
                  </tr>
                </thead>
                <tbody>
                  {catalog.length === 0 ? (
                    <tr>
                      <td colSpan={2}>No services in dbo.tService.</td>
                    </tr>
                  ) : filteredCatalogServices.length === 0 ? (
                    <tr>
                      <td colSpan={2}>No services match the selected filters.</td>
                    </tr>
                  ) : (
                    filteredCatalogServices.map((row) => (
                      <tr key={row.serviceID}>
                        <td>
                          <input
                            type="checkbox"
                            checked={model.selectedServiceIds.includes(row.serviceID)}
                            onChange={() => handleServiceToggle(row.serviceID)}
                            aria-label={`Toggle service ${row.serviceName}`}
                          />
                        </td>
                        <td>{row.serviceName}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
            </div>
          </div>

          {!isEdit ? (
            <div id="project-tab-calendar" className={projectTabClass('calendar')}>
              <h2>Analytics Calendar</h2>
              <div className="admin-edit-card">
                <p className="field-help">
                  Document deliverables, meetings, and tasks for this project. Ownership is limited to members of the
                  selected <strong>User Group</strong> on Project Info. Rows with a title are saved with the project.
                </p>
                <div className="page-header-actions project-services-select-actions">
                  <button type="button" className="secondary-button" onClick={addCalendarItem}>
                    Add row
                  </button>
                </div>
                {model.ugid <= 0 ? (
                  <p className="field-help" role="status">
                    Select a User Group on the Project Info tab to enable the Owner dropdown.
                  </p>
                ) : null}
                <div className="project-tab-table-wrap">
                  <table className="catalog-table project-calendar-table">
                    <thead>
                      <tr>
                        <th>Type</th>
                        <th>Title</th>
                        <th>Owner</th>
                        <th>Received</th>
                        <th>Completed</th>
                        <th>Notes</th>
                        <th>&nbsp;</th>
                      </tr>
                    </thead>
                    <tbody>
                      {calendarItems.length === 0 ? (
                        <tr>
                          <td colSpan={7}>No calendar items yet. Click Add row to begin.</td>
                        </tr>
                      ) : (
                        calendarItems.map((row) => (
                          <tr key={row.tempId}>
                            <td>
                              <select
                                value={row.itemType}
                                onChange={(e) =>
                                  updateCalendarItem(row.tempId, {
                                    itemType: e.target.value as ProjectCalendarItemDraft['itemType'],
                                  })
                                }
                                aria-label="Calendar item type"
                              >
                                {CALENDAR_ITEM_TYPES.map((t) => (
                                  <option key={t} value={t}>
                                    {t}
                                  </option>
                                ))}
                              </select>
                            </td>
                            <td>
                              <input
                                value={row.title}
                                onChange={(e) => updateCalendarItem(row.tempId, { title: e.target.value })}
                                maxLength={200}
                                placeholder="Deliverable / meeting / task"
                                aria-label="Calendar item title"
                              />
                            </td>
                            <td>
                              <select
                                value={row.ownerUserID === '' ? '' : String(row.ownerUserID)}
                                onChange={(e) =>
                                  updateCalendarItem(row.tempId, {
                                    ownerUserID: e.target.value ? Number(e.target.value) : '',
                                  })
                                }
                                disabled={model.ugid <= 0}
                                aria-label="Calendar item owner"
                              >
                                <option value="">-- Owner --</option>
                                {ownerOptions.map((o) => (
                                  <option key={o.userID} value={o.userID}>
                                    {o.userFullName}
                                  </option>
                                ))}
                              </select>
                            </td>
                            <td>
                              <input
                                type="date"
                                value={row.receivedDate}
                                onChange={(e) => updateCalendarItem(row.tempId, { receivedDate: e.target.value })}
                                aria-label="Received date"
                              />
                            </td>
                            <td>
                              <input
                                type="date"
                                value={row.completedDate}
                                onChange={(e) => updateCalendarItem(row.tempId, { completedDate: e.target.value })}
                                aria-label="Completed date"
                              />
                            </td>
                            <td>
                              <input
                                value={row.notes}
                                onChange={(e) => updateCalendarItem(row.tempId, { notes: e.target.value })}
                                placeholder="Notes"
                                aria-label="Calendar item notes"
                              />
                            </td>
                            <td>
                              <button
                                type="button"
                                className="danger-button"
                                onClick={() => removeCalendarItem(row.tempId)}
                              >
                                Remove
                              </button>
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          ) : null}

          {isEdit ? (
            <div id="project-tab-revenue" className={projectTabClass('revenue')}>
            <h2>Revenue history</h2>
            <div className="admin-edit-card">
            <p className="field-help">
              Active rows from <code>dbo.tProjectRevenue</code> for this project, newest effective date first
              (same order as the home grid Latest Revenue). Delete marks a row inactive.
            </p>
            <table className="catalog-table">
              <thead>
                <tr>
                  <th>Effective Date</th>
                  <th>Revenue</th>
                  <th>Reason</th>
                  <th>Recorded By</th>
                  <th>Created</th>
                  <th>&nbsp;</th>
                </tr>
              </thead>
              <tbody>
                {revenueHistory.length === 0 ? (
                  <tr>
                    <td colSpan={6}>No revenue entries yet.</td>
                  </tr>
                ) : (
                  revenueHistory.map((r) => (
                    <tr key={r.revenueID}>
                      <td>{formatCalendarDateDisplay(r.effectiveDate)}</td>
                      <td>{formatMoney(r.revenue)}</td>
                      <td>{r.changeReason ?? ''}</td>
                      <td>{r.recordedByName ?? ''}</td>
                      <td>{formatCalendarDateDisplay(r.createdDate)}</td>
                      <td>
                        <button
                          type="button"
                          className="danger-button"
                          onClick={() => void handleDeleteRevenue(r.revenueID)}
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
            </div>
          </div>
          ) : null}

          {isEdit ? (
            <div id="project-tab-notes" className={projectTabClass('notes')}>
            <h2>Notes</h2>
            <div className="admin-edit-card">
            <div className="form-grid">
              <label htmlFor="noteType">Note type</label>
              <select
                id="noteType"
                value={noteDraft.noteType}
                onChange={(e) => setNoteDraft({ ...noteDraft, noteType: e.target.value })}
              >
                <option value="General">General</option>
                <option value="Status">Status</option>
                <option value="Risk">Risk</option>
                <option value="Decision">Decision</option>
              </select>

              <label htmlFor="noteText">Note</label>
              <textarea
                id="noteText"
                value={noteDraft.noteText}
                rows={3}
                onChange={(e) => setNoteDraft({ ...noteDraft, noteText: e.target.value })}
              />

              <label htmlFor="isPinned">
                <input
                  id="isPinned"
                  type="checkbox"
                  checked={noteDraft.isPinned}
                  onChange={(e) => setNoteDraft({ ...noteDraft, isPinned: e.target.checked })}
                />
                Pinned
              </label>
              <div>
                <button type="button" className="secondary-button" onClick={() => void handleAddNote()}>
                  Add note
                </button>
              </div>
            </div>

            <table className="catalog-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Type</th>
                  <th>Note</th>
                  <th>By</th>
                  <th>&nbsp;</th>
                </tr>
              </thead>
              <tbody>
                {notes.length === 0 ? (
                  <tr>
                    <td colSpan={5}>No notes yet.</td>
                  </tr>
                ) : (
                  notes.map((n) => (
                    <tr key={n.noteID}>
                      <td>{formatCalendarDateDisplay(n.noteDate)}</td>
                      <td>
                        {n.noteType ?? ''}
                        {n.isPinned ? ' (pinned)' : ''}
                      </td>
                      <td>{n.noteText ?? ''}</td>
                      <td>{n.userFullName ?? ''}</td>
                      <td>
                        <button
                          type="button"
                          className="danger-button"
                          onClick={() => void handleDeleteNote(n.noteID)}
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
            </div>
          </div>
          ) : null}

          {isEdit ? (
            <div id="project-tab-activity" className={projectTabClass('activity')}>
            <h2>Activity log</h2>
            <div className="admin-edit-card">
            <p className="field-help">From <code>dbo.tActivityLog</code> for this project.</p>
            <table className="catalog-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Action</th>
                  <th>Field</th>
                  <th>Old</th>
                  <th>New</th>
                </tr>
              </thead>
              <tbody>
                {activity.length === 0 ? (
                  <tr>
                    <td colSpan={5}>No activity yet.</td>
                  </tr>
                ) : (
                  activity.map((a) => (
                    <tr key={a.logID}>
                      <td>{formatCalendarDateDisplay(a.actionDate)}</td>
                      <td>{a.action}</td>
                      <td>{a.fieldName ?? ''}</td>
                      <td>{a.oldValue ?? ''}</td>
                      <td>{a.newValue ?? ''}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
            </div>
          </div>
          ) : null}

          </div>

          <div className="button-row">
          <button type="submit" className="primary-button">
            Save Project
          </button>
          <Link className="secondary-button" to="/">
            Cancel
          </Link>
          {isEdit ? (
            <>
              <button type="button" className="danger-button" onClick={() => void handleSoftDelete()}>
                Soft-Delete
              </button>
              <button type="button" className="secondary-button" onClick={() => void handleRestore()}>
                Restore
              </button>
            </>
          ) : null}
          </div>
        </form>
      </section>
    </div>
  );
}
