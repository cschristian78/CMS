import { FormEvent, MouseEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { DragEvent } from 'react';
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  applyGridRowToProjectModel,
  gridFilterIdsFromModel,
  type ProjectGridNavigationState,
} from '../workflows/projectGridPrefill';
import { formatBusinessClassDisplay } from '../utils/businessClassDisplay';
import { formatCalendarDateDisplay, toApiDateValue, toInputDateValue } from '../utils/dateInput';
import type { FlashFn } from '../App';
import { ApiError, apiDelete, apiGet, apiPostFormData, apiPostJson } from '../api';
import ProjectTimeTrackerTab, { type ProjectTimeTrackerHandle } from '../components/ProjectTimeTrackerTab';
import {
  PROJECT_TAB_LABELS,
  clientSliceEqual,
  describeClientChanges,
  describeProjectChanges,
  pickClientSlice,
  pickProjectSlice,
  projectSliceEqual,
  sanitizeProjectFormForApi,
  serviceIdsEqual,
  type ProjectTabId,
} from '../projectTabPersistence';

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

/** Prefer a positive user-group id. 0 is a valid JSON number, so do not use ?? on ugid. */
function readUserGroupId(raw: {
  ugid?: number | null;
  userGroupID?: number | null;
  uGID?: number | null;
}): number {
  for (const v of [raw.ugid, raw.userGroupID, raw.uGID]) {
    const n = Number(v);
    if (Number.isFinite(n) && n > 0) {
      return n;
    }
  }
  return 0;
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

interface ProjectFileRow {
  projectFileID: number;
  projectID: number;
  originalFileName: string;
  contentType?: string | null;
  byteLength: number;
  category?: string | null;
  description?: string | null;
  versionLabel?: string | null;
  dataAsOf?: string | null;
  /** Process-Active: the file currently marked for processing. */
  isCurrent?: boolean;
  uploadedByUserID: number;
  uploadedByName?: string | null;
  createdDate?: string | null;
  downloadUrl?: string | null;
  blobUrl?: string | null;
}

type ProjectTab = 'client' | 'project' | 'projectData' | 'services' | 'calendar' | 'time' | 'revenue' | 'notes';

const PROJECT_TAB_IDS: ProjectTab[] = [
  'client',
  'project',
  'projectData',
  'services',
  'calendar',
  'time',
  'revenue',
  'notes',
];

function parseProjectTabParam(value: string | null): ProjectTab | null {
  if (!value) {
    return null;
  }
  return PROJECT_TAB_IDS.includes(value as ProjectTab) ? (value as ProjectTab) : null;
}

const PROJECT_DATA_CATEGORIES = [
  { value: 'ClaimsData', label: 'Claims Data' },
  { value: 'CorrespondenceData', label: 'Correspondence' },
  { value: 'ExposureData', label: 'Exposure Data' },
  { value: 'LossData', label: 'Loss Data' },
  { value: 'SourceData', label: 'Source Data' },
  { value: 'SubmissionData', label: 'Submission' },
] as const;

const PROJECT_DATA_VERSIONS = Array.from({ length: 20 }, (_, i) => String(i + 1));

type ProjectDataCategory = (typeof PROJECT_DATA_CATEGORIES)[number]['value'];

type ProjectFileUploadMeta = {
  category: ProjectDataCategory;
  description: string;
  versionLabel: string;
  dataAsOf: string;
};

type ProjectFileDraft = {
  category: ProjectDataCategory;
  description: string;
  versionLabel: string;
  dataAsOf: string;
  isCurrent: boolean;
};

function formatProjectDataCategory(category?: string | null): string {
  const match = PROJECT_DATA_CATEGORIES.find((c) => c.value === category);
  return match?.label ?? (category?.trim() || 'Source Data');
}

function normalizeProjectDataCategory(value?: string | null): ProjectDataCategory {
  const trimmed = (value ?? '').trim();
  const match = PROJECT_DATA_CATEGORIES.find((c) => c.value === trimmed);
  if (match) {
    return match.value;
  }
  const byLabel = PROJECT_DATA_CATEGORIES.find(
    (c) => c.label.localeCompare(trimmed, undefined, { sensitivity: 'base' }) === 0,
  );
  return byLabel?.value ?? 'SourceData';
}

/** Coerce API project-file rows (camelCase or PascalCase) for list/sync. */
function normalizeProjectFileRow(raw: unknown): ProjectFileRow | null {
  if (raw === null || typeof raw !== 'object') {
    return null;
  }
  const o = raw as Record<string, unknown>;
  const id = Number(o.projectFileID ?? o.ProjectFileID);
  if (!Number.isFinite(id) || id <= 0) {
    return null;
  }
  return {
    projectFileID: id,
    projectID: Number(o.projectID ?? o.ProjectID ?? 0),
    originalFileName: String(o.originalFileName ?? o.OriginalFileName ?? ''),
    contentType: (o.contentType ?? o.ContentType ?? null) as string | null,
    byteLength: Number(o.byteLength ?? o.ByteLength ?? 0),
    category: normalizeProjectDataCategory(String(o.category ?? o.Category ?? 'SourceData')),
    description: (o.description ?? o.Description ?? null) as string | null,
    versionLabel: (o.versionLabel ?? o.VersionLabel ?? null) as string | null,
    dataAsOf: (o.dataAsOf ?? o.DataAsOf ?? null) as string | null,
    isCurrent: !!(o.isCurrent ?? o.IsCurrent),
    uploadedByUserID: Number(o.uploadedByUserID ?? o.UploadedByUserID ?? 0),
    uploadedByName: (o.uploadedByName ?? o.UploadedByName ?? null) as string | null,
    createdDate: (o.createdDate ?? o.CreatedDate ?? null) as string | null,
    downloadUrl: (o.downloadUrl ?? o.DownloadUrl ?? null) as string | null,
    blobUrl: (o.blobUrl ?? o.BlobUrl ?? null) as string | null,
  };
}

function normalizeProjectDataVersion(value?: string | null): string {
  const trimmed = (value ?? '').trim();
  if (PROJECT_DATA_VERSIONS.includes(trimmed)) {
    return trimmed;
  }
  const asNum = Number.parseInt(trimmed.replace(/^v/i, ''), 10);
  if (Number.isFinite(asNum) && asNum >= 1 && asNum <= 20) {
    return String(asNum);
  }
  return '1';
}

function mapCalendarItemFromApi(row: {
  calendarItemID: number;
  itemType?: string | null;
  title?: string | null;
  ownerUserID?: number | null;
  receivedDate?: string | null;
  completedDate?: string | null;
  notes?: string | null;
  sortOrder?: number;
}): ProjectCalendarItemDraft {
  const rawType = (row.itemType ?? 'Task').trim();
  const itemType = (CALENDAR_ITEM_TYPES as string[]).includes(rawType)
    ? (rawType as ProjectCalendarItemDraft['itemType'])
    : 'Task';
  return {
    tempId: `cal-${row.calendarItemID}`,
    calendarItemID: row.calendarItemID,
    itemType,
    title: row.title ?? '',
    ownerUserID: row.ownerUserID && row.ownerUserID > 0 ? row.ownerUserID : '',
    receivedDate: toInputDateValue(row.receivedDate),
    completedDate: toInputDateValue(row.completedDate),
    notes: row.notes ?? '',
    sortOrder: row.sortOrder ?? 0,
  };
}

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
        placeholder={placeholder ?? 'Type to search client name...'}
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
            - Select client -
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
  const [searchParams, setSearchParams] = useSearchParams();
  const { id: idParam } = useParams();
  const projectId = idParam !== undefined ? Number(idParam) : undefined;
  const isEdit = projectId !== undefined && !Number.isNaN(projectId);
  const projectNameInputRef = useRef<HTMLInputElement | null>(null);
  const timeTrackerRef = useRef<ProjectTimeTrackerHandle | null>(null);
  const [baselineModel, setBaselineModel] = useState<ProjectFormModel | null>(null);
  const [baselineCalendarItems, setBaselineCalendarItems] = useState<ProjectCalendarItemDraft[]>([]);
  const [baselineFileDrafts, setBaselineFileDrafts] = useState<Record<number, ProjectFileDraft>>({});
  const [baselineNotes, setBaselineNotes] = useState<ProjectNoteRow[]>([]);
  const [tabSaveBusy, setTabSaveBusy] = useState(false);
  const [pendingSaveTabs, setPendingSaveTabs] = useState<ProjectTabId[] | null>(null);

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
  const [projectFiles, setProjectFiles] = useState<ProjectFileRow[]>([]);
  const [projectFilesBusy, setProjectFilesBusy] = useState(false);
  const [projectFileUploadMeta, setProjectFileUploadMeta] = useState<ProjectFileUploadMeta>({
    category: 'SourceData',
    description: '',
    versionLabel: '1',
    dataAsOf: '',
  });
  const [pendingProjectFiles, setPendingProjectFiles] = useState<File[]>([]);
  const [showAddDataForm, setShowAddDataForm] = useState(false);
  const [selectedProjectFileId, setSelectedProjectFileId] = useState<number | ''>('');
  const [projectFileEditId, setProjectFileEditId] = useState<number | null>(null);
  const [projectFileEditDraft, setProjectFileEditDraft] = useState<ProjectFileDraft | null>(null);
  const [projectFileDrafts, setProjectFileDrafts] = useState<Record<number, ProjectFileDraft>>({});
  const projectFilesInputRef = useRef<HTMLInputElement | null>(null);
  const projectDataDropDepthRef = useRef(0);
  const [projectDataDropActive, setProjectDataDropActive] = useState(false);
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
  const [calendarEditTempId, setCalendarEditTempId] = useState<string | null>(null);
  const [calendarEditDraft, setCalendarEditDraft] = useState<ProjectCalendarItemDraft | null>(null);
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
    return [...rows].sort((a, b) => {
      const catCmp = (a.categoryName ?? '').localeCompare(b.categoryName ?? '', undefined, {
        sensitivity: 'base',
      });
      if (catCmp !== 0) {
        return catCmp;
      }
      return (a.serviceName ?? '').localeCompare(b.serviceName ?? '', undefined, {
        sensitivity: 'base',
      });
    });
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

      // Drop aliased user-group fields from the API payload so a stale userGroupID cannot
      // overwrite the select-bound ugid when we POST (System.Text.Json last-property-wins).
      const {
        userGroupID: _ignoreUserGroupID,
        uGID: _ignoreUGID,
        ...formData
      } = data as ProjectFormModel & { userGroupID?: number; uGID?: number };

      let merged: ProjectFormModel = {
        ...formData,
        ugid: readUserGroupId(data as { ugid?: number; userGroupID?: number; uGID?: number }),
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
      setBaselineModel(JSON.parse(JSON.stringify(merged)) as ProjectFormModel);
      if (!isEdit) {
        setBaselineCalendarItems([]);
        setBaselineFileDrafts({});
        setBaselineNotes([]);
      }

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

  // Keep the User Group dropdown on the full active list (optionally sorted by client usage).
  const refreshUserGroupOptions = useCallback(async (clientId: number) => {
    try {
      const qs = clientId > 0 ? `?clientId=${clientId}` : '';
      const opts = normalizeSelectOptions(
        await apiGet<SelectOption[]>(`/api/projects/user-groups${qs}`),
      );
      setUserGroupOptionsForClient(opts);
      setModel((prev) => (prev ? { ...prev, userGroupOptions: opts } : prev));
      return opts;
    } catch {
      return null;
    }
  }, []);

  useEffect(() => {
    if (!model) {
      return;
    }
    void refreshUserGroupOptions(model.clientID > 0 ? model.clientID : 0);
  }, [model?.clientID, refreshUserGroupOptions]);

  // Refresh groups when returning to this page (e.g. after creating a group in Admin).
  useEffect(() => {
    const onVisible = () => {
      if (document.visibilityState === 'visible' && model) {
        void refreshUserGroupOptions(model.clientID > 0 ? model.clientID : 0);
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    window.addEventListener('focus', onVisible);
    return () => {
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('focus', onVisible);
    };
  }, [model, refreshUserGroupOptions]);

  // Apply Admin "create group -> return to project" query params.
  useEffect(() => {
    if (!model) {
      return;
    }
    const selectRaw = searchParams.get('selectUserGroup');
    const focusName = searchParams.get('focusProjectName') === '1';
    if (!selectRaw && !focusName) {
      return;
    }

    const selectId = Number(selectRaw);
    let cancelled = false;
    void (async () => {
      const opts =
        (await refreshUserGroupOptions(model.clientID > 0 ? model.clientID : 0)) ??
        userGroupOptionsForClient;
      if (cancelled) {
        return;
      }
      if (Number.isFinite(selectId) && selectId > 0) {
        const value = String(selectId);
        const inList = (opts ?? []).some((o) => o.value === value);
        setModel((prev) => {
          if (!prev) {
            return prev;
          }
          const nextOptions = inList
            ? prev.userGroupOptions
            : [{ value, text: `User Group ${value}` }, ...prev.userGroupOptions];
          return { ...prev, ugid: selectId, userGroupOptions: nextOptions };
        });
        setActiveTab('project');
      }
      if (focusName) {
        setActiveTab('project');
        window.setTimeout(() => projectNameInputRef.current?.focus(), 0);
      }
      const next = new URLSearchParams(searchParams);
      next.delete('selectUserGroup');
      next.delete('focusProjectName');
      setSearchParams(next, { replace: true });
    })();
    return () => {
      cancelled = true;
    };
    // Only react to the inbound query flags, not every options refresh.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [model != null, searchParams.get('selectUserGroup'), searchParams.get('focusProjectName')]);

  // Deep-link to a project tab, e.g. /projects/90?tab=calendar from notification emails.
  useEffect(() => {
    const tab = parseProjectTabParam(searchParams.get('tab'));
    if (!tab) {
      return;
    }
    if (!isEdit && (tab === 'time' || tab === 'revenue' || tab === 'notes')) {
      return;
    }
    setActiveTab(tab);
  }, [isEdit, searchParams.get('tab')]);

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

  // Preload edit-mode sub-resource tabs (revenue, notes, calendar) on open.
  useEffect(() => {
    if (!isEdit || !projectId) {
      setSubresourcesLoaded(false);
      if (!isEdit) {
        setCalendarItems([]);
        setNotes([]);
        setProjectFiles([]);
        setPendingProjectFiles([]);
      }
      return;
    }
    setRevenueHistory([]);
    setCalendarItems([]);
    setProjectFiles([]);
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
      const filesResult = await apiGet<ProjectFileRow[]>(`/api/projects/${projectId}/files`).then(
        (data) => ({ ok: true as const, data }),
        () => ({ ok: false as const, data: [] as ProjectFileRow[] }),
      );
      const calendarResult = await apiGet<
        {
          calendarItemID: number;
          itemType?: string | null;
          title?: string | null;
          ownerUserID?: number | null;
          receivedDate?: string | null;
          completedDate?: string | null;
          notes?: string | null;
          sortOrder?: number;
        }[]
      >(`/api/projects/${projectId}/calendar`).then(
        (data) => ({ ok: true as const, data }),
        () => ({ ok: false as const, data: [] }),
      );
      if (!cancelled) {
        setRevenueHistory(revResult.data);
        const noteRows = Array.isArray(notesResult.data) ? notesResult.data : [];
        setNotes(noteRows);
        setBaselineNotes(JSON.parse(JSON.stringify(noteRows)) as ProjectNoteRow[]);
        syncProjectFiles(Array.isArray(filesResult.data) ? filesResult.data : [], true);
        const calRows = (Array.isArray(calendarResult.data) ? calendarResult.data : []).map(
          mapCalendarItemFromApi,
        );
        setCalendarItems(calRows);
        setBaselineCalendarItems(JSON.parse(JSON.stringify(calRows)) as ProjectCalendarItemDraft[]);
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
    const nextRevenue =
      revenueHistory.length === 0
        ? {
            revenueAmount: null as number | null,
            revenueEffectiveDate: null as string | null,
            revenueChangeReason: null as string | null,
          }
        : {
            revenueAmount: revenueHistory[0].revenue,
            revenueEffectiveDate: toInputDateValue(revenueHistory[0].effectiveDate) || null,
            revenueChangeReason: revenueHistory[0].changeReason ?? null,
          };
    setModel((prev) => (prev ? { ...prev, ...nextRevenue } : prev));
    setBaselineModel((prev) => (prev ? { ...prev, ...nextRevenue } : prev));
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

  const handleAddServicesToProject = () => {
    if (!model || model.selectedServiceIds.length === 0) {
      return;
    }

    const selectedRows = catalog.filter((row) => model.selectedServiceIds.includes(row.serviceID));
    if (selectedRows.length === 0) {
      return;
    }

    setCalendarItems((prev) => {
      const existingTitles = new Set(
        prev.map((row) => row.title.trim().toLowerCase()).filter((t) => t.length > 0),
      );
      const next = [...prev];
      for (const service of selectedRows) {
        const title = (service.serviceName ?? '').trim();
        if (!title) {
          continue;
        }
        const key = title.toLowerCase();
        if (existingTitles.has(key)) {
          continue;
        }
        existingTitles.add(key);
        next.push({
          ...newCalendarDraft(next.length),
          itemType: 'Deliverable',
          title,
        });
      }
      return next;
    });

    flash(
      'success',
      `${selectedRows.length} service${selectedRows.length === 1 ? '' : 's'} added to the Calendar. Save the project to persist.`,
    );
    setActiveTab('calendar');
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
    if (calendarEditTempId === tempId) {
      setCalendarEditTempId(null);
      setCalendarEditDraft(null);
    }
  };

  const openCalendarEdit = (row: ProjectCalendarItemDraft) => {
    setCalendarEditTempId(row.tempId);
    setCalendarEditDraft({ ...row });
  };

  const closeCalendarEdit = () => {
    setCalendarEditTempId(null);
    setCalendarEditDraft(null);
  };

  const applyCalendarEdit = () => {
    if (!calendarEditTempId || !calendarEditDraft) {
      return;
    }
    const title = calendarEditDraft.title.trim();
    if (!title) {
      flash('error', 'Calendar item title is required.');
      return;
    }
    updateCalendarItem(calendarEditTempId, {
      itemType: calendarEditDraft.itemType,
      title,
      ownerUserID: calendarEditDraft.ownerUserID,
      receivedDate: calendarEditDraft.receivedDate,
      completedDate: calendarEditDraft.completedDate,
      notes: calendarEditDraft.notes,
    });
    closeCalendarEdit();
  };

  const buildCalendarPayload = () =>
    calendarItems
      .filter((row) => row.title.trim().length > 0)
      .map((row, index) => ({
        calendarItemID: row.calendarItemID && row.calendarItemID > 0 ? row.calendarItemID : 0,
        itemType: row.itemType,
        title: row.title.trim(),
        ownerUserID: typeof row.ownerUserID === 'number' && row.ownerUserID > 0 ? row.ownerUserID : null,
        receivedDate: toApiDateValue(row.receivedDate),
        completedDate: toApiDateValue(row.completedDate),
        notes: row.notes.trim().length > 0 ? row.notes.trim() : null,
        sortOrder: index,
        isActive: true,
      }));

  const calendarSignature = (rows: ProjectCalendarItemDraft[]) =>
    JSON.stringify(
      rows.map((r) => ({
        calendarItemID: r.calendarItemID ?? 0,
        itemType: r.itemType,
        title: r.title,
        ownerUserID: r.ownerUserID === '' ? 0 : r.ownerUserID,
        receivedDate: r.receivedDate,
        completedDate: r.completedDate,
        notes: r.notes,
        sortOrder: r.sortOrder,
      })),
    );

  const fileDraftsSignature = (drafts: Record<number, ProjectFileDraft>) => JSON.stringify(drafts);

  const isTabDirty = useCallback(
    (tab: ProjectTabId): boolean => {
      if (!model || !baselineModel) {
        return false;
      }
      switch (tab) {
        case 'client':
          return !clientSliceEqual(pickClientSlice(baselineModel), pickClientSlice(model));
        case 'project':
          return !projectSliceEqual(pickProjectSlice(baselineModel), pickProjectSlice(model));
        case 'services':
          return !serviceIdsEqual(baselineModel.selectedServiceIds ?? [], model.selectedServiceIds ?? []);
        case 'calendar':
          return calendarSignature(calendarItems) !== calendarSignature(baselineCalendarItems);
        case 'projectData':
          return (
            pendingProjectFiles.length > 0 ||
            fileDraftsSignature(projectFileDrafts) !== fileDraftsSignature(baselineFileDrafts)
          );
        case 'notes':
          return (
            noteDraft.noteText.trim().length > 0 ||
            notes.some((n) => n.noteID < 0) ||
            JSON.stringify(notes.filter((n) => n.noteID > 0)) !==
              JSON.stringify(baselineNotes.filter((n) => n.noteID > 0))
          );
        case 'time':
        case 'revenue':
          return false;
        default:
          return false;
      }
    },
    [
      model,
      baselineModel,
      calendarItems,
      baselineCalendarItems,
      pendingProjectFiles,
      projectFileDrafts,
      baselineFileDrafts,
      noteDraft.noteText,
      notes,
      baselineNotes,
    ],
  );

  const getDirtyTabs = useCallback((): ProjectTabId[] => {
    const tabs: ProjectTabId[] = [
      'client',
      'project',
      'projectData',
      'services',
      'calendar',
      'notes',
    ];
    return tabs.filter((t) => isTabDirty(t));
  }, [isTabDirty]);

  const describeTabChanges = useCallback(
    (tab: ProjectTabId): string[] => {
      if (!model || !baselineModel) {
        return [];
      }
      switch (tab) {
        case 'client':
          return describeClientChanges(pickClientSlice(baselineModel), pickClientSlice(model));
        case 'project':
          return describeProjectChanges(pickProjectSlice(baselineModel), pickProjectSlice(model));
        case 'services': {
          const before = new Set(baselineModel.selectedServiceIds ?? []);
          const after = new Set(model.selectedServiceIds ?? []);
          const added = [...after].filter((id) => !before.has(id)).length;
          const removed = [...before].filter((id) => !after.has(id)).length;
          const parts: string[] = [];
          if (added) {
            parts.push(`${added} service(s) added`);
          }
          if (removed) {
            parts.push(`${removed} service(s) removed`);
          }
          return parts.length > 0 ? parts : ['Service selection changed'];
        }
        case 'calendar':
          return [`${calendarItems.length} calendar row(s) (was ${baselineCalendarItems.length})`];
        case 'projectData': {
          const parts: string[] = [];
          if (pendingProjectFiles.length > 0) {
            parts.push(`${pendingProjectFiles.length} file(s) ready to upload`);
          }
          if (fileDraftsSignature(projectFileDrafts) !== fileDraftsSignature(baselineFileDrafts)) {
            parts.push('File metadata / Active changes');
          }
          return parts;
        }
        case 'notes': {
          const parts: string[] = [];
          if (noteDraft.noteText.trim()) {
            parts.push('New note draft');
          }
          const pending = notes.filter((n) => n.noteID < 0).length;
          if (pending) {
            parts.push(`${pending} queued note(s)`);
          }
          return parts.length > 0 ? parts : ['Notes changed'];
        }
        default:
          return [];
      }
    },
    [
      model,
      baselineModel,
      calendarItems,
      baselineCalendarItems,
      pendingProjectFiles,
      projectFileDrafts,
      baselineFileDrafts,
      noteDraft.noteText,
      notes,
    ],
  );

  const persistProjectFormTabs = async (tabs: ProjectTabId[]) => {
    if (!model || !baselineModel || projectId === undefined) {
      throw new Error('Project is not ready to save yet. Reload the page and try again.');
    }
    const saveClient = tabs.includes('client');
    const saveProject = tabs.includes('project');
    const saveServices = tabs.includes('services');
    const saveCalendar = tabs.includes('calendar');

    if (saveClient || saveProject || saveServices) {
      const projectSlice = saveProject ? pickProjectSlice(model) : pickProjectSlice(baselineModel);
      const revenueDirty =
        saveProject &&
        (Number(baselineModel.revenueAmount ?? 0) !== Number(model.revenueAmount ?? 0) ||
          (baselineModel.revenueEffectiveDate ?? '') !== (model.revenueEffectiveDate ?? '') ||
          (baselineModel.revenueChangeReason ?? '') !== (model.revenueChangeReason ?? ''));

      const {
        userGroupID: _dropUg,
        uGID: _dropUg2,
        ...baselineWithoutUgAlias
      } = baselineModel as ProjectFormModel & { userGroupID?: number; uGID?: number };

      const merged: ProjectFormModel = {
        ...baselineWithoutUgAlias,
        ...(saveClient ? pickClientSlice(model) : pickClientSlice(baselineModel)),
        ...projectSlice,
        selectedServiceIds: saveServices
          ? [...(model.selectedServiceIds ?? [])]
          : [...(baselineModel.selectedServiceIds ?? [])],
        projectID: projectId,
        ugid: projectSlice.ugid > 0 ? projectSlice.ugid : 0,
        clientID: model.clientID,
      };
      // Only post a new revenue row when the user actually edited revenue on Project Info.
      // Re-posting the latest history amount on every save can fail the transaction and
      // roll back UserGroup / status updates.
      if (!revenueDirty) {
        merged.revenueAmount = null;
        merged.revenueEffectiveDate = null;
        merged.revenueChangeReason = null;
      }
      const calendarPayload = saveCalendar
        ? buildCalendarPayload()
        : baselineCalendarItems.map((row, index) => ({
            calendarItemID: row.calendarItemID && row.calendarItemID > 0 ? row.calendarItemID : null,
            itemType: row.itemType,
            title: row.title.trim(),
            ownerUserID: row.ownerUserID === '' ? null : row.ownerUserID,
            receivedDate: row.receivedDate ? row.receivedDate : null,
            completedDate: row.completedDate ? row.completedDate : null,
            notes: row.notes.trim().length > 0 ? row.notes.trim() : null,
            sortOrder: index,
            isActive: true,
          }));

      const resolvedUgid = merged.ugid > 0 ? merged.ugid : 0;
      const payload = sanitizeProjectFormForApi({
        ...merged,
        projectID: projectId,
        ugid: resolvedUgid,
        userGroupID: resolvedUgid,
        calendarItems: calendarPayload,
      } as ProjectFormModel);

      await apiPostJson<{ projectId: number; message: string }>('/api/projects', payload);
      await apiPostJson(`/api/projects/${projectId}/calendar`, {
        items: calendarPayload,
        calendarItems: calendarPayload,
      });
    } else if (saveCalendar) {
      const calendarPayload = buildCalendarPayload();
      await apiPostJson(`/api/projects/${projectId}/calendar`, {
        items: calendarPayload,
        calendarItems: calendarPayload,
      });
    }

    if (tabs.includes('projectData')) {
      if (pendingProjectFiles.length > 0) {
        await uploadProjectFilesTo(projectId, pendingProjectFiles, projectFileUploadMeta);
      }
      for (const [idStr, draft] of Object.entries(projectFileDrafts)) {
        const fileId = Number(idStr);
        const base = baselineFileDrafts[fileId];
        if (!base) {
          continue;
        }
        if (
          draft.description !== base.description ||
          draft.versionLabel !== base.versionLabel ||
          draft.dataAsOf !== base.dataAsOf ||
          draft.isCurrent !== base.isCurrent ||
          draft.category !== base.category
        ) {
          if (!draft.dataAsOf.trim()) {
            throw new Error('Data As Of is required for file metadata changes.');
          }
          await apiPostJson(`/api/projects/${projectId}/files/${fileId}`, {
            category: draft.category,
            description: draft.description,
            versionLabel: normalizeProjectDataVersion(draft.versionLabel),
            dataAsOf: draft.dataAsOf.trim(),
            clearDataAsOf: false,
            isCurrent: draft.isCurrent,
          });
        }
      }
      syncProjectFiles(await apiGet<ProjectFileRow[]>(`/api/projects/${projectId}/files`), true);
    }

    if (tabs.includes('notes')) {
      if (noteDraft.noteText.trim()) {
        await apiPostJson(`/api/projects/${projectId}/notes`, {
          projectID: projectId,
          userID: 0,
          noteType: noteDraft.noteType,
          noteText: noteDraft.noteText.trim(),
          isPinned: noteDraft.isPinned,
        });
        setNoteDraft({ noteType: 'General', noteText: '', isPinned: false });
      }
      for (const n of notes.filter((x) => x.noteID < 0 && (x.noteText ?? '').trim())) {
        await apiPostJson(`/api/projects/${projectId}/notes`, {
          projectID: projectId,
          userID: 0,
          noteType: n.noteType || 'General',
          noteText: (n.noteText ?? '').trim(),
          isPinned: !!n.isPinned,
        });
      }
      const refreshed = await apiGet<ProjectNoteRow[]>(`/api/projects/${projectId}/notes`);
      setNotes(refreshed);
      setBaselineNotes(JSON.parse(JSON.stringify(refreshed)) as ProjectNoteRow[]);
    }

    if (tabs.includes('time')) {
      await timeTrackerRef.current?.save();
    }

    // Refresh baselines for form-backed tabs.
    if (saveClient || saveProject || saveServices) {
      const refreshed = await apiGet<ProjectFormModel>(`/api/projects/form/${projectId}`);
      const {
        userGroupID: _ignoreRefreshUg,
        uGID: _ignoreRefreshUg2,
        ...refreshedWithoutUgAlias
      } = refreshed as ProjectFormModel & { userGroupID?: number; uGID?: number };
      const nextModel: ProjectFormModel = {
        ...model,
        ...refreshedWithoutUgAlias,
        ugid: readUserGroupId(refreshed as { ugid?: number; userGroupID?: number; uGID?: number }),
        selectedServiceIds: Array.isArray(refreshed.selectedServiceIds)
          ? refreshed.selectedServiceIds
          : model.selectedServiceIds,
        businessUnitOptions: normalizeSelectOptions(refreshed.businessUnitOptions),
        businessClassOptions: normalizeSelectOptions(refreshed.businessClassOptions),
        clientTypeOptions: normalizeSelectOptions(refreshed.clientTypeOptions),
        clientStatusOptions: normalizeSelectOptions(refreshed.clientStatusOptions),
        projectTypeOptions: normalizeSelectOptions(refreshed.projectTypeOptions),
        statusOptions: normalizeSelectOptions(refreshed.statusOptions),
        userGroupOptions: normalizeSelectOptions(refreshed.userGroupOptions),
        clientOptions: normalizeSelectOptions(refreshed.clientOptions),
      };
      setModel(nextModel);
      setBaselineModel(JSON.parse(JSON.stringify(nextModel)) as ProjectFormModel);
      setClientBuFilter(nextModel.clientBusinessUnitID ?? '');
      setClientBcFilter(nextModel.clientBusinessClassID ?? '');
      setClientTypeFilter(nextModel.clientTypeID ?? '');
      setClientStatusFilter(nextModel.clientStatusID ?? '');
      await refreshUserGroupOptions(nextModel.clientID > 0 ? nextModel.clientID : 0);
      await refreshRevenueHistory();
    }

    if (saveCalendar || saveClient || saveProject || saveServices) {
      try {
        const cal = await apiGet<
          {
            calendarItemID: number;
            itemType?: string | null;
            title?: string | null;
            ownerUserID?: number | null;
            receivedDate?: string | null;
            completedDate?: string | null;
            notes?: string | null;
            sortOrder?: number;
          }[]
        >(`/api/projects/${projectId}/calendar`);
        const mapped = (Array.isArray(cal) ? cal : []).map(mapCalendarItemFromApi);
        setCalendarItems(mapped);
        setBaselineCalendarItems(JSON.parse(JSON.stringify(mapped)) as ProjectCalendarItemDraft[]);
      } catch {
        if (saveCalendar) {
          setBaselineCalendarItems(JSON.parse(JSON.stringify(calendarItems)) as ProjectCalendarItemDraft[]);
        }
      }
    }
  };

  const revertTab = (tab: ProjectTabId) => {
    if (!model || !baselineModel) {
      return;
    }
    switch (tab) {
      case 'client':
        setModel({ ...model, ...pickClientSlice(baselineModel) });
        setClientBuFilter(baselineModel.clientBusinessUnitID ?? '');
        setClientBcFilter(baselineModel.clientBusinessClassID ?? '');
        setClientTypeFilter(baselineModel.clientTypeID ?? '');
        setClientStatusFilter(baselineModel.clientStatusID ?? '');
        break;
      case 'project':
        setModel({ ...model, ...pickProjectSlice(baselineModel) });
        break;
      case 'services':
        setModel({ ...model, selectedServiceIds: [...(baselineModel.selectedServiceIds ?? [])] });
        break;
      case 'calendar':
        setCalendarItems(JSON.parse(JSON.stringify(baselineCalendarItems)) as ProjectCalendarItemDraft[]);
        break;
      case 'projectData':
        setPendingProjectFiles([]);
        setProjectFileDrafts(JSON.parse(JSON.stringify(baselineFileDrafts)));
        if (projectFilesInputRef.current) {
          projectFilesInputRef.current.value = '';
        }
        break;
      case 'notes':
        setNoteDraft({ noteType: 'General', noteText: '', isPinned: false });
        setNotes(JSON.parse(JSON.stringify(baselineNotes)) as ProjectNoteRow[]);
        break;
      case 'time':
        void timeTrackerRef.current?.reload();
        break;
      case 'revenue':
        break;
      default:
        break;
    }
    flash('success', `${PROJECT_TAB_LABELS[tab]} changes discarded.`);
  };

  const commitSaveTabs = async (tabs: ProjectTabId[]) => {
    if (tabs.length === 0) {
      flash('success', 'No changes to save on this tab.');
      return;
    }
    setTabSaveBusy(true);
    setValidationMessage(null);
    try {
      await persistProjectFormTabs(tabs);
      flash(
        'success',
        `Saved: ${tabs.map((t) => PROJECT_TAB_LABELS[t]).join(', ')}.`,
      );
      setPendingSaveTabs(null);
    } catch (err) {
      const message = err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'Save failed.';
      setValidationMessage(message);
      flash('error', message);
    } finally {
      setTabSaveBusy(false);
    }
  };

  const requestSaveTab = (tab: ProjectTabId) => {
    if (!isEdit) {
      flash('error', 'Use Save Project to create the project first.');
      return;
    }
    if (tab === 'revenue') {
      flash('success', 'Revenue history deletes save immediately. Add new revenue on Project Info, then Save.');
      return;
    }
    if (tab === 'time') {
      void commitSaveTabs(['time']);
      return;
    }
    const dirty = getDirtyTabs();
    if (dirty.length === 0) {
      flash('success', 'No changes to save on this tab.');
      return;
    }
    // Confirm when multiple tabs have pending changes (or Save pressed from a clean tab while others are dirty).
    if (dirty.length > 1 || !dirty.includes(tab)) {
      setPendingSaveTabs(dirty);
      return;
    }
    void commitSaveTabs(dirty);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!model) {
      return;
    }
    // Edit mode uses per-tab Save buttons.
    if (isEdit) {
      requestSaveTab(activeTab as ProjectTabId);
      return;
    }
    setValidationMessage(null);

    if (!isEdit && pendingProjectFiles.length > 0 && !projectFileUploadMeta.dataAsOf.trim()) {
      setValidationMessage('Data As Of is required on the Project Data tab before uploading selected files.');
      flash('error', 'Data As Of is required before uploading selected files.');
      setActiveTab('projectData');
      return;
    }

    try {
      const calendarPayload = buildCalendarPayload();

      const {
        userGroupID: _newIgnoreUg,
        uGID: _newIgnoreUg2,
        ...modelWithoutUgAlias
      } = model as ProjectFormModel & { userGroupID?: number; uGID?: number };
      const resolvedUgid = model.ugid > 0 ? model.ugid : 0;
      const payload = sanitizeProjectFormForApi({
        ...modelWithoutUgAlias,
        projectID: isEdit ? projectId : model.projectID,
        ugid: resolvedUgid,
        // Canonical name expected by ProjectFormViewModel.UserGroupID
        ...( { userGroupID: resolvedUgid } as object ),
        calendarItems: calendarPayload as ProjectFormModel['calendarItems'],
      } as ProjectFormModel);
      const result = await apiPostJson<{ projectId: number; message: string }>('/api/projects', payload);
      const savedId = result.projectId;
      let projectMessage = result.message;

      // Explicit calendar save - do not rely only on nested form binding (edit path was previously skipped).
      try {
        const calResult = await apiPostJson<{ savedCount: number; message: string }>(
          `/api/projects/${savedId}/calendar`,
          { items: calendarPayload, calendarItems: calendarPayload },
        );
        projectMessage =
          `${projectMessage} ${calResult.message ?? `Saved ${calendarPayload.length} calendar item(s).`}`.trim();
      } catch (calErr) {
        flash(
          'error',
          calErr instanceof ApiError
            ? `Project saved, but calendar save failed: ${calErr.message}`
            : 'Project saved, but calendar save failed.',
        );
      }

      // New project: upload any files queued on the Project Data tab.
      if (!isEdit && pendingProjectFiles.length > 0) {
        setProjectFilesBusy(true);
        try {
          const files = pendingProjectFiles;
          const uploadMeta = projectFileUploadMeta;
          await uploadProjectFilesTo(savedId, files, uploadMeta);
          projectMessage = `${projectMessage} Uploaded ${files.length} project data file(s).`.trim();
        } catch (fileErr) {
          flash(
            'error',
            fileErr instanceof ApiError
              ? `Project saved, but file upload failed: ${fileErr.message}`
              : fileErr instanceof Error
                ? `Project saved, but file upload failed: ${fileErr.message}`
                : 'Project saved, but file upload failed.',
          );
        } finally {
          setProjectFilesBusy(false);
        }
      }

      // New project: persist notes queued on the Notes tab.
      if (!isEdit) {
        const pendingNotes = notes.filter((n) => n.noteID < 0 && (n.noteText ?? '').trim());
        if (pendingNotes.length > 0) {
          try {
            for (const n of pendingNotes) {
              await apiPostJson(`/api/projects/${savedId}/notes`, {
                projectID: savedId,
                userID: 0,
                noteType: n.noteType || 'General',
                noteText: (n.noteText ?? '').trim(),
                isPinned: !!n.isPinned,
              });
            }
            projectMessage = `${projectMessage} Saved ${pendingNotes.length} note(s).`.trim();
          } catch (noteErr) {
            flash(
              'error',
              noteErr instanceof ApiError
                ? `Project saved, but notes failed: ${noteErr.message}`
                : 'Project saved, but notes failed to save.',
            );
          }
        }
      }

      flash('success', projectMessage);

      if (isEdit && projectId !== undefined) {
        await load();
        await refreshRevenueHistory();
        try {
          const cal = await apiGet<
            {
              calendarItemID: number;
              itemType?: string | null;
              title?: string | null;
              ownerUserID?: number | null;
              receivedDate?: string | null;
              completedDate?: string | null;
              notes?: string | null;
              sortOrder?: number;
            }[]
          >(`/api/projects/${projectId}/calendar`);
          setCalendarItems((Array.isArray(cal) ? cal : []).map(mapCalendarItemFromApi));
        } catch {
          /* keep local calendar state if reload fails */
        }
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
    if (!noteDraft.noteText.trim()) {
      return;
    }

    // New project: queue notes locally until Save Project.
    if (!isEdit || projectId === undefined) {
      const tempId = -Date.now();
      setNotes((prev) => [
        {
          noteID: tempId,
          projectID: 0,
          userID: 0,
          userFullName: 'You (pending save)',
          noteDate: new Date().toISOString(),
          noteType: noteDraft.noteType,
          noteText: noteDraft.noteText.trim(),
          isPinned: noteDraft.isPinned,
          createdDate: new Date().toISOString(),
        },
        ...prev,
      ]);
      setNoteDraft({ noteType: 'General', noteText: '', isPinned: false });
      flash('success', 'Note queued. Click Save Project to create the project and save notes.');
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

    // Pending (unsaved) notes use negative IDs.
    if (noteId < 0) {
      setNotes((prev) => prev.filter((n) => n.noteID !== noteId));
      flash('success', 'Queued note removed.');
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

  const syncProjectFiles = (rows: ProjectFileRow[], captureBaseline = false) => {
    const list = (Array.isArray(rows) ? rows : [])
      .map(normalizeProjectFileRow)
      .filter((row): row is ProjectFileRow => row !== null);
    setProjectFiles(list);
    const drafts = Object.fromEntries(
      list.map((f) => [
        f.projectFileID,
        {
          category: normalizeProjectDataCategory(f.category),
          description: f.description ?? '',
          versionLabel: normalizeProjectDataVersion(f.versionLabel),
          dataAsOf: toInputDateValue(f.dataAsOf),
          isCurrent: !!f.isCurrent,
        },
      ]),
    ) as Record<number, ProjectFileDraft>;
    setProjectFileDrafts(drafts);
    if (captureBaseline) {
      setBaselineFileDrafts(JSON.parse(JSON.stringify(drafts)) as Record<number, ProjectFileDraft>);
    }
    setSelectedProjectFileId((prev) => {
      if (prev !== '' && list.some((f) => f.projectFileID === prev)) {
        return prev;
      }
      return list.length > 0 ? list[0].projectFileID : '';
    });
  };

  const handleProjectFilesSelected = (fileList: FileList | null) => {
    setPendingProjectFiles(fileList && fileList.length > 0 ? Array.from(fileList) : []);
  };

  const assignFilesToProjectInput = (files: File[]) => {
    const input = projectFilesInputRef.current;
    if (!input) {
      return;
    }
    try {
      const transfer = new DataTransfer();
      files.forEach((file) => transfer.items.add(file));
      input.files = transfer.files;
    } catch {
      // FileList assignment is best-effort; pendingProjectFiles remains the source of truth.
    }
  };

  const dataTransferHasFiles = (transfer: DataTransfer | null | undefined): boolean => {
    if (!transfer) {
      return false;
    }
    if (transfer.types && Array.from(transfer.types).includes('Files')) {
      return true;
    }
    return transfer.files.length > 0;
  };

  const filesFromDataTransfer = (transfer: DataTransfer | null | undefined): File[] => {
    if (!transfer) {
      return [];
    }
    return Array.from(transfer.files).filter((file) => file && file.name);
  };

  const attachDroppedProjectFiles = (files: File[]) => {
    if (projectFilesBusy || files.length === 0) {
      return;
    }
    setPendingProjectFiles(files);
    setShowAddDataForm(true);
  };

  useEffect(() => {
    if (!showAddDataForm || pendingProjectFiles.length === 0) {
      return;
    }
    assignFilesToProjectInput(pendingProjectFiles);
  }, [showAddDataForm, pendingProjectFiles]);

  const clearProjectDataDropState = () => {
    projectDataDropDepthRef.current = 0;
    setProjectDataDropActive(false);
  };

  const handleProjectDataDragEnter = (e: DragEvent<HTMLDivElement>) => {
    if (projectFilesBusy || !dataTransferHasFiles(e.dataTransfer)) {
      return;
    }
    e.preventDefault();
    e.stopPropagation();
    projectDataDropDepthRef.current += 1;
    setProjectDataDropActive(true);
  };

  const handleProjectDataDragOver = (e: DragEvent<HTMLDivElement>) => {
    if (projectFilesBusy || !dataTransferHasFiles(e.dataTransfer)) {
      return;
    }
    e.preventDefault();
    e.stopPropagation();
    e.dataTransfer.dropEffect = 'copy';
  };

  const handleProjectDataDragLeave = (e: DragEvent<HTMLDivElement>) => {
    if (!dataTransferHasFiles(e.dataTransfer) && projectDataDropDepthRef.current === 0) {
      return;
    }
    e.preventDefault();
    e.stopPropagation();
    projectDataDropDepthRef.current = Math.max(0, projectDataDropDepthRef.current - 1);
    if (projectDataDropDepthRef.current === 0) {
      setProjectDataDropActive(false);
    }
  };

  const handleProjectDataDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    e.stopPropagation();
    clearProjectDataDropState();
    attachDroppedProjectFiles(filesFromDataTransfer(e.dataTransfer));
  };

  const uploadProjectFilesTo = async (
    targetProjectId: number,
    files: File[],
    meta: ProjectFileUploadMeta = projectFileUploadMeta,
  ) => {
    if (targetProjectId <= 0 || files.length === 0) {
      return;
    }

    const uploadMeta: ProjectFileUploadMeta = {
      category: meta.category,
      description: meta.description,
      versionLabel: meta.versionLabel,
      dataAsOf: meta.dataAsOf,
    };

    if (!uploadMeta.dataAsOf.trim()) {
      throw new Error('Data As Of is required before uploading.');
    }

    let lastRows: ProjectFileRow[] | null = null;
    for (const file of files) {
      const start = await apiPostJson<{
        sessionId: string;
        chunkSizeBytes: number;
        maxUploadBytes: number;
      }>(`/api/projects/${targetProjectId}/files/sessions`, {
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
        byteLength: file.size,
        category: uploadMeta.category,
        description: uploadMeta.description.trim() || null,
        versionLabel: normalizeProjectDataVersion(uploadMeta.versionLabel),
        dataAsOf: uploadMeta.dataAsOf.trim(),
      });

      const chunkSize = start.chunkSizeBytes > 0 ? start.chunkSizeBytes : 4 * 1024 * 1024;
      let offset = 0;
      let chunkIndex = 0;
      while (offset < file.size) {
        const blob = file.slice(offset, offset + chunkSize);
        const formData = new FormData();
        formData.append('chunkIndex', String(chunkIndex));
        formData.append('chunk', blob, `${file.name}.part${chunkIndex}`);
        await apiPostFormData(
          `/api/projects/${targetProjectId}/files/sessions/${start.sessionId}/chunks`,
          formData,
        );
        offset += chunkSize;
        chunkIndex += 1;
      }

      lastRows = await apiPostJson<ProjectFileRow[]>(
        `/api/projects/${targetProjectId}/files/sessions/${start.sessionId}/complete`,
        {},
      );
    }

    if (lastRows) {
      syncProjectFiles(lastRows);
    } else {
      syncProjectFiles(await apiGet<ProjectFileRow[]>(`/api/projects/${targetProjectId}/files`));
    }
    setProjectFileUploadMeta((prev) => ({
      category: uploadMeta.category,
      description: '',
      versionLabel: '1',
      dataAsOf: '',
    }));
    setPendingProjectFiles([]);
    if (projectFilesInputRef.current) {
      projectFilesInputRef.current.value = '';
    }
  };

  const handleUploadProjectFiles = async () => {
    if (pendingProjectFiles.length === 0) {
      return;
    }

    if (!projectFileUploadMeta.dataAsOf.trim()) {
      flash('error', 'Data As Of is required before uploading.');
      return;
    }

    // New project: keep files queued until the project is saved.
    if (!isEdit || projectId === undefined) {
      flash(
        'success',
        `${pendingProjectFiles.length} file(s) ready. Click Save Project to create the project and upload them.`,
      );
      setShowAddDataForm(false);
      return;
    }

    setProjectFilesBusy(true);
    try {
      const files = pendingProjectFiles;
      await uploadProjectFilesTo(projectId, files, projectFileUploadMeta);
      flash('success', `Uploaded ${files.length} file(s).`);
      setShowAddDataForm(false);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : err instanceof Error ? err.message : 'File upload failed.');
    } finally {
      setProjectFilesBusy(false);
    }
  };

  const closeAddDataForm = (discardSelection = false) => {
    if (projectFilesBusy) {
      return;
    }
    if (discardSelection) {
      setPendingProjectFiles([]);
      if (projectFilesInputRef.current) {
        projectFilesInputRef.current.value = '';
      }
    }
    setShowAddDataForm(false);
  };

  const handleDeleteProjectFile = async (fileId: number) => {
    if (!isEdit || projectId === undefined) {
      return;
    }
    if (!window.confirm('Remove this file from the project?')) {
      return;
    }

    setProjectFilesBusy(true);
    try {
      await apiDelete(`/api/projects/${projectId}/files/${fileId}`);
      syncProjectFiles(await apiGet<ProjectFileRow[]>(`/api/projects/${projectId}/files`));
      flash('success', 'File removed.');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not remove file.');
    } finally {
      setProjectFilesBusy(false);
    }
  };

  const projectFileDraftFor = (file: ProjectFileRow): ProjectFileDraft => {
    const existing = projectFileDrafts[file.projectFileID];
    if (existing) {
      return { ...existing };
    }
    return {
      category: normalizeProjectDataCategory(file.category),
      description: file.description ?? '',
      versionLabel: normalizeProjectDataVersion(file.versionLabel),
      dataAsOf: toInputDateValue(file.dataAsOf),
      isCurrent: !!file.isCurrent,
    };
  };

  const openProjectFileEdit = (fileId: number) => {
    if (!isEdit || projectFilesBusy) {
      return;
    }
    const file = projectFiles.find((f) => f.projectFileID === fileId);
    if (!file) {
      return;
    }
    setSelectedProjectFileId(fileId);
    setProjectFileEditId(fileId);
    setProjectFileEditDraft(projectFileDraftFor(file));
  };

  const closeProjectFileEdit = () => {
    setProjectFileEditId(null);
    setProjectFileEditDraft(null);
  };

  const handleSaveProjectFileMetadata = async (fileId: number, draftOverride?: ProjectFileDraft) => {
    if (!isEdit || projectId === undefined) {
      return false;
    }

    const file = projectFiles.find((f) => f.projectFileID === fileId);
    const draft = draftOverride ??
      projectFileDrafts[fileId] ?? {
        category: normalizeProjectDataCategory(file?.category),
        description: '',
        versionLabel: '1',
        dataAsOf: '',
        isCurrent: !!file?.isCurrent,
      };
    if (!draft.dataAsOf.trim()) {
      flash('error', 'Data As Of is required.');
      return false;
    }

    setProjectFilesBusy(true);
    try {
      const rows = await apiPostJson<ProjectFileRow[]>(`/api/projects/${projectId}/files/${fileId}`, {
        category: draft.category,
        description: draft.description,
        versionLabel: normalizeProjectDataVersion(draft.versionLabel),
        dataAsOf: draft.dataAsOf.trim(),
        clearDataAsOf: false,
        isCurrent: draft.isCurrent,
      });
      syncProjectFiles(rows, true);
      flash('success', 'File metadata saved.');
      return true;
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not save file metadata.');
      return false;
    } finally {
      setProjectFilesBusy(false);
    }
  };

  const applyProjectFileEdit = async () => {
    if (projectFileEditId === null || !projectFileEditDraft) {
      return;
    }
    const saved = await handleSaveProjectFileMetadata(projectFileEditId, projectFileEditDraft);
    if (saved) {
      closeProjectFileEdit();
    }
  };

  const handleDeleteProjectFileFromEdit = async () => {
    if (projectFileEditId === null) {
      return;
    }
    const fileId = projectFileEditId;
    closeProjectFileEdit();
    await handleDeleteProjectFile(fileId);
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

  // Must stay above early returns - Rules of Hooks.
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

  const adminGroupsReturnPath = isEdit && projectId !== undefined ? `/projects/${projectId}` : '/projects/new';
  const adminGroupsHref = `/admin?tab=groups&returnTo=${encodeURIComponent(adminGroupsReturnPath)}`;

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
        <p>Loading...</p>
      </div>
    );
  }

  const title = isEdit ? 'Edit Project' : 'New Project';

  const projectTabClass = (tab: ProjectTab) => 'admin-tab-panel' + (activeTab === tab ? ' active' : '');

  const renderTabActions = (tab: ProjectTabId) => {
    if (!isEdit) {
      return null;
    }
    return (
      <div className="project-tab-actions">
        <button
          type="button"
          className="primary-button"
          disabled={tabSaveBusy}
          onClick={() => requestSaveTab(tab)}
        >
          {tabSaveBusy ? 'Saving...' : 'Save'}
        </button>
        <button
          type="button"
          className="secondary-button"
          disabled={tabSaveBusy}
          onClick={() => revertTab(tab)}
        >
          Cancel
        </button>
        {isTabDirty(tab) ? <span className="field-help project-tab-dirty-hint">Unsaved changes</span> : null}
      </div>
    );
  };

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
    <>
      {calendarEditDraft ?
        <div
          className="home-modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="project-calendar-edit-title"
          onClick={closeCalendarEdit}
        >
          <div
            className="home-modal-dialog project-calendar-edit-dialog"
            onClick={(e: MouseEvent) => e.stopPropagation()}
          >
            <h2 id="project-calendar-edit-title" className="home-modal-title">
              Edit calendar item
            </h2>
            <p className="home-modal-body">
              Update this service / calendar row. Click <strong>Apply</strong>, then <strong>Save Project</strong> to
              persist changes to <code>dbo.tProjectCalendarItem</code>.
            </p>
            <div className="form-grid">
              <label htmlFor="cal-edit-type">Type</label>
              <select
                id="cal-edit-type"
                value={calendarEditDraft.itemType}
                onChange={(e) =>
                  setCalendarEditDraft({
                    ...calendarEditDraft,
                    itemType: e.target.value as ProjectCalendarItemDraft['itemType'],
                  })
                }
              >
                {CALENDAR_ITEM_TYPES.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </select>

              <label htmlFor="cal-edit-title">Title</label>
              <input
                id="cal-edit-title"
                value={calendarEditDraft.title}
                onChange={(e) => setCalendarEditDraft({ ...calendarEditDraft, title: e.target.value })}
                maxLength={200}
              />

              <label htmlFor="cal-edit-owner">Owner</label>
              <select
                id="cal-edit-owner"
                value={calendarEditDraft.ownerUserID === '' ? '' : String(calendarEditDraft.ownerUserID)}
                onChange={(e) =>
                  setCalendarEditDraft({
                    ...calendarEditDraft,
                    ownerUserID: e.target.value ? Number(e.target.value) : '',
                  })
                }
                disabled={!model || model.ugid <= 0}
              >
                <option value="">-- Owner --</option>
                {ownerOptions.map((o) => (
                  <option key={o.userID} value={o.userID}>
                    {o.userFullName}
                  </option>
                ))}
              </select>

              <label htmlFor="cal-edit-received">Received</label>
              <input
                id="cal-edit-received"
                type="date"
                value={calendarEditDraft.receivedDate}
                onChange={(e) => setCalendarEditDraft({ ...calendarEditDraft, receivedDate: e.target.value })}
              />

              <label htmlFor="cal-edit-completed">Completed</label>
              <input
                id="cal-edit-completed"
                type="date"
                value={calendarEditDraft.completedDate}
                onChange={(e) => setCalendarEditDraft({ ...calendarEditDraft, completedDate: e.target.value })}
              />

              <label htmlFor="cal-edit-notes">Notes</label>
              <textarea
                id="cal-edit-notes"
                value={calendarEditDraft.notes}
                onChange={(e) => setCalendarEditDraft({ ...calendarEditDraft, notes: e.target.value })}
                rows={5}
              />
            </div>
            <div className="home-modal-actions">
              <button type="button" className="secondary-button" onClick={closeCalendarEdit}>
                Cancel
              </button>
              <button type="button" className="primary-button" onClick={applyCalendarEdit}>
                Apply
              </button>
            </div>
          </div>
        </div>
      : null}

      {projectFileEditId !== null && projectFileEditDraft ?
        <div
          className="home-modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="project-file-edit-title"
          onClick={closeProjectFileEdit}
        >
          <div
            className="home-modal-dialog project-data-file-edit-dialog"
            onClick={(e: MouseEvent) => e.stopPropagation()}
          >
            <h2 id="project-file-edit-title" className="home-modal-title">
              Edit project data
            </h2>
            <p className="home-modal-body">
              {projectFiles.find((f) => f.projectFileID === projectFileEditId)?.originalFileName ??
                'Project data file'}
            </p>
            <div className="form-grid">
              <label htmlFor="projectFileEditCategory">Classification</label>
              <select
                id="projectFileEditCategory"
                value={projectFileEditDraft.category}
                disabled={projectFilesBusy}
                onChange={(e) =>
                  setProjectFileEditDraft((prev) =>
                    prev ?
                      { ...prev, category: e.target.value as ProjectDataCategory }
                    : prev,
                  )
                }
              >
                {PROJECT_DATA_CATEGORIES.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>

              <label htmlFor="projectFileEditVersion">Version</label>
              <select
                id="projectFileEditVersion"
                value={normalizeProjectDataVersion(projectFileEditDraft.versionLabel)}
                disabled={projectFilesBusy}
                onChange={(e) =>
                  setProjectFileEditDraft((prev) => (prev ? { ...prev, versionLabel: e.target.value } : prev))
                }
              >
                {PROJECT_DATA_VERSIONS.map((v) => (
                  <option key={v} value={v}>
                    {v}
                  </option>
                ))}
              </select>

              <label htmlFor="projectFileEditDataAsOf">Data As Of</label>
              <input
                id="projectFileEditDataAsOf"
                type="date"
                required
                value={projectFileEditDraft.dataAsOf}
                disabled={projectFilesBusy}
                onChange={(e) =>
                  setProjectFileEditDraft((prev) => (prev ? { ...prev, dataAsOf: e.target.value } : prev))
                }
              />

              <label htmlFor="projectFileEditDescription">Description</label>
              <textarea
                id="projectFileEditDescription"
                className="project-file-upload-description"
                value={projectFileEditDraft.description}
                disabled={projectFilesBusy}
                onChange={(e) =>
                  setProjectFileEditDraft((prev) => (prev ? { ...prev, description: e.target.value } : prev))
                }
                maxLength={1000}
                rows={4}
                placeholder="Short note about this file"
              />

              <label htmlFor="projectFileEditActive">Active</label>
              <label className="project-file-active-inline" htmlFor="projectFileEditActive">
                <input
                  id="projectFileEditActive"
                  type="checkbox"
                  checked={projectFileEditDraft.isCurrent}
                  disabled={projectFilesBusy}
                  onChange={(e) =>
                    setProjectFileEditDraft((prev) => (prev ? { ...prev, isCurrent: e.target.checked } : prev))
                  }
                />
                Mark as the active file for this classification
              </label>
            </div>
            <div className="home-modal-actions">
              <button
                type="button"
                className="danger-button"
                disabled={projectFilesBusy}
                onClick={() => void handleDeleteProjectFileFromEdit()}
              >
                Remove file
              </button>
              <button type="button" className="secondary-button" disabled={projectFilesBusy} onClick={closeProjectFileEdit}>
                Cancel
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={projectFilesBusy}
                onClick={() => void applyProjectFileEdit()}
              >
                Save
              </button>
            </div>
          </div>
        </div>
      : null}

      {showAddDataForm ? (
        <div
          className="home-modal-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="project-add-data-title"
          onClick={() => closeAddDataForm(true)}
        >
          <div
            className="home-modal-dialog project-add-data-dialog"
            onClick={(e: MouseEvent) => e.stopPropagation()}
          >
            <h2 id="project-add-data-title" className="home-modal-title">
              Add Data
            </h2>
            <p className="home-modal-body">
              Upload project data files to Azure Blob storage and classify them as Source, Exposure, Loss, Claims,
              Correspondence, or Submission data.
              {!isEdit
                ? ' On a new project, choose files and metadata here, then click Save Project to create the project and upload them.'
                : ' Choose files and metadata, then Upload to send them to blob storage.'}
            </p>

            <div className="page-header-actions project-services-select-actions project-file-choose-row">
              <input
                ref={projectFilesInputRef}
                id="projectClientDataFiles"
                type="file"
                multiple
                disabled={projectFilesBusy}
                onChange={(e) => handleProjectFilesSelected(e.target.files)}
              />
              {pendingProjectFiles.length > 0 ? (
                <span className="field-help project-file-pending-count">
                  {pendingProjectFiles.length} file(s) selected
                </span>
              ) : null}
            </div>

            <div className="project-file-upload-meta">
              <div className="project-file-upload-meta-row">
                <div className="project-file-upload-meta-field">
                  <label htmlFor="projectFileUploadCategory">Classification</label>
                  <select
                    id="projectFileUploadCategory"
                    value={projectFileUploadMeta.category}
                    onChange={(e) =>
                      setProjectFileUploadMeta((prev) => ({
                        ...prev,
                        category: e.target.value as ProjectDataCategory,
                      }))
                    }
                    disabled={projectFilesBusy}
                  >
                    {PROJECT_DATA_CATEGORIES.map((c) => (
                      <option key={c.value} value={c.value}>
                        {c.label}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="project-file-upload-meta-field">
                  <label htmlFor="projectFileUploadVersion">Version</label>
                  <select
                    id="projectFileUploadVersion"
                    value={normalizeProjectDataVersion(projectFileUploadMeta.versionLabel)}
                    onChange={(e) =>
                      setProjectFileUploadMeta((prev) => ({ ...prev, versionLabel: e.target.value }))
                    }
                    disabled={projectFilesBusy}
                  >
                    {PROJECT_DATA_VERSIONS.map((v) => (
                      <option key={v} value={v}>
                        {v}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="project-file-upload-meta-field">
                  <label htmlFor="projectFileUploadDataAsOf">Data As Of</label>
                  <input
                    id="projectFileUploadDataAsOf"
                    type="date"
                    required
                    value={projectFileUploadMeta.dataAsOf}
                    onChange={(e) =>
                      setProjectFileUploadMeta((prev) => ({ ...prev, dataAsOf: e.target.value }))
                    }
                    disabled={projectFilesBusy}
                  />
                </div>
              </div>
              <div className="project-file-upload-meta-field project-file-upload-description-field">
                <label htmlFor="projectFileUploadDescription">Description (optional)</label>
                <textarea
                  id="projectFileUploadDescription"
                  className="project-file-upload-description"
                  value={projectFileUploadMeta.description}
                  onChange={(e) =>
                    setProjectFileUploadMeta((prev) => ({ ...prev, description: e.target.value }))
                  }
                  maxLength={1000}
                  rows={5}
                  placeholder="Short note about this upload"
                  disabled={projectFilesBusy}
                />
              </div>
            </div>

            <div className="project-add-data-assigned" aria-label="Data already assigned to this project">
              <h3 className="project-add-data-assigned__title">Assigned project data</h3>
              <p className="field-help project-add-data-assigned__help">
                Files already linked to this project, with their source classification.
              </p>
              <div className="project-add-data-assigned__table-wrap">
                <table className="project-add-data-assigned__table">
                  <thead>
                    <tr>
                      <th scope="col">Name</th>
                      <th scope="col">Classification</th>
                      <th scope="col">Version</th>
                      <th scope="col">Data As Of</th>
                      <th scope="col">Uploaded by</th>
                    </tr>
                  </thead>
                  <tbody>
                    {projectFiles.length === 0 ? (
                      <tr>
                        <td colSpan={5} className="project-add-data-assigned__empty">
                          {!isEdit
                            ? 'No files assigned yet. Upload after you save the project, or queue files above.'
                            : 'No files assigned to this project yet.'}
                        </td>
                      </tr>
                    ) : (
                      [...projectFiles]
                        .sort((a, b) => {
                          const cat = formatProjectDataCategory(a.category).localeCompare(
                            formatProjectDataCategory(b.category),
                          );
                          if (cat !== 0) {
                            return cat;
                          }
                          return (a.originalFileName || '').localeCompare(b.originalFileName || '');
                        })
                        .map((f) => (
                          <tr key={f.projectFileID}>
                            <td title={f.originalFileName}>{f.originalFileName}</td>
                            <td>{formatProjectDataCategory(f.category)}</td>
                            <td>{normalizeProjectDataVersion(f.versionLabel)}</td>
                            <td>{formatCalendarDateDisplay(f.dataAsOf) || '—'}</td>
                            <td>{(f.uploadedByName ?? '').trim() || '—'}</td>
                          </tr>
                        ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="home-modal-actions">
              <button
                type="button"
                className="secondary-button"
                disabled={projectFilesBusy}
                onClick={() => closeAddDataForm(true)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={projectFilesBusy || pendingProjectFiles.length === 0}
                onClick={() => void handleUploadProjectFiles()}
              >
                {projectFilesBusy ? 'Uploading...' : isEdit ? 'Upload' : 'Add files'}
              </button>
            </div>
          </div>
        </div>
      ) : null}

    <div className="client-workflow-fill">
      <section className="page-header client-workflow-header">
        <div className="page-header-top">
          <h1>{title}</h1>
        </div>
        <p>
          Project header lives in <code>dbo.tProject</code>; revenue, notes, services, and calendar are sub-resources.
        </p>
      </section>

      <section className="card admin-card project-workflow-card">
        <div className="admin-tabs" role="tablist" aria-label="Project tabs">
          {projectTabBtn('client', 'Client Info')}
          {projectTabBtn('project', 'Project Info')}
          {projectTabBtn('projectData', 'Project Data')}
          {projectTabBtn('services', 'Services')}
          {projectTabBtn('calendar', 'Calendar')}
          {isEdit ? projectTabBtn('time', 'Time tracker') : null}
          {isEdit ? projectTabBtn('revenue', 'Revenue history') : null}
          {projectTabBtn('notes', 'Notes')}
        </div>

        <form autoComplete="off" onSubmit={(e) => void handleSubmit(e)}>
          {validationMessage ? <div className="validation-summary">{validationMessage}</div> : null}

          <div className="project-tab-panels">
          <div id="project-tab-client" className={projectTabClass('client')}>
            <h2>Client Info</h2>
            <div className="admin-edit-card project-panel-tab-card">
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
                      : 'Type to search client name...'
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
            {renderTabActions('client')}
            </div>
          </div>

          <div id="project-tab-project" className={projectTabClass('project')}>
            <h2>Project Info</h2>
            <div className="admin-edit-card project-panel-tab-card">
            <div className="form-grid">
              <label htmlFor="projectNumber">Project Number</label>
              <div className="project-number-with-online">
                <input
                  id="projectNumber"
                  value={model.projectNumber}
                  onChange={(e) => setModel({ ...model, projectNumber: e.target.value })}
                  required
                  maxLength={50}
                />
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
                  <label htmlFor="isOnline" className="project-online-toggle-label">
                    {model.isOnline ? 'Online (in progress)' : 'Offline'}
                  </label>
                </span>
              </div>

              <label htmlFor="projectName">Project Name</label>
              <input
                ref={projectNameInputRef}
                id="projectName"
                value={model.projectName}
                onChange={(e) => setModel({ ...model, projectName: e.target.value })}
                required
                maxLength={200}
              />

              <label htmlFor="ugid">User Group</label>
              <div className="project-number-with-online">
                <select
                  id="ugid"
                  value={model.ugid > 0 ? model.ugid : ''}
                  onChange={(e) => {
                    const nextUgid = Number(e.target.value) || 0;
                    setModel({ ...model, ugid: nextUgid });
                    // Ownership options change with the group - clear owners that may no longer apply.
                    setCalendarItems((prev) => prev.map((row) => ({ ...row, ownerUserID: '' })));
                  }}
                  onFocus={() => {
                    void refreshUserGroupOptions(model.clientID > 0 ? model.clientID : 0);
                  }}
                >
                  <option value="">-- Select user group --</option>
                  {groupOptions.map((o) => (
                    <option key={o.value} value={o.value} title={o.text}>
                      {o.text}
                    </option>
                  ))}
                </select>
                <Link className="secondary-button" to={adminGroupsHref} title="Create or edit user groups">
                  New / manage groups
                </Link>
              </div>
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
            {renderTabActions('project')}
            </div>
          </div>

          <div id="project-tab-project-data" className={projectTabClass('projectData')}>
            <div className="project-data-tab-header">
              <h2>Project Data</h2>
              <button
                type="button"
                className="primary-button"
                disabled={projectFilesBusy}
                onClick={() => setShowAddDataForm(true)}
              >
                Add Data
              </button>
            </div>
            <div className="admin-edit-card project-panel-tab-card">
              <p className="field-help">
                Read-only list of project data files in Azure Blob storage. Double-click a row to edit classification,
                version, data-as-of date, description, or the <strong>Active</strong> flag (one active file per
                classification). Use <strong>Add Data</strong> or drag files onto the list to upload.
                {!isEdit
                  ? ' On a new project, add files first, then click Save Project to create the project and upload them.'
                  : null}
              </p>
              {!isEdit && pendingProjectFiles.length > 0 ? (
                <p className="field-help project-tab-dirty-hint">
                  {pendingProjectFiles.length} file(s) queued for upload when you save the project
                  {pendingProjectFiles.length <= 3
                    ? `: ${pendingProjectFiles.map((f) => f.name).join(', ')}`
                    : ''}
                  .
                </p>
              ) : null}

              <div className="project-data-browser">
                <div className="project-data-listbox-panel">
                  <label id="projectDataFileListLabel">Uploaded files</label>
                  <div
                    className={
                      'project-data-file-table-wrap' +
                      (projectDataDropActive ? ' project-data-file-table-wrap--drop-active' : '')
                    }
                    role="listbox"
                    aria-labelledby="projectDataFileListLabel"
                    onDragEnter={handleProjectDataDragEnter}
                    onDragOver={handleProjectDataDragOver}
                    onDragLeave={handleProjectDataDragLeave}
                    onDrop={handleProjectDataDrop}
                  >
                    {projectDataDropActive ? (
                      <div className="project-data-drop-hint" aria-hidden="true">
                        Drop files to add data
                      </div>
                    ) : null}
                    <table className="project-data-file-table project-data-file-table--readonly">
                      <thead>
                        <tr>
                          <th scope="col">Classification</th>
                          <th scope="col">Name</th>
                          <th scope="col">Version</th>
                          <th scope="col">Data As Of</th>
                          <th scope="col">Active</th>
                          <th scope="col">Download</th>
                        </tr>
                      </thead>
                      <tbody>
                        {projectFiles.map((f) => {
                          const url = f.downloadUrl || f.blobUrl || '';
                          const selected = selectedProjectFileId === f.projectFileID;
                          const isActive = !!f.isCurrent;
                          return (
                            <tr
                              key={f.projectFileID}
                              className={
                                'project-data-file-row' +
                                (selected ? ' project-data-file-row--selected' : '') +
                                (isActive ? ' project-data-file-row--active' : '')
                              }
                              aria-selected={selected}
                              title={isEdit ? 'Double-click to edit metadata' : undefined}
                              onClick={() => setSelectedProjectFileId(f.projectFileID)}
                              onDoubleClick={() => openProjectFileEdit(f.projectFileID)}
                            >
                              <td>{formatProjectDataCategory(f.category)}</td>
                              <td className="project-data-file-name" title={f.originalFileName}>
                                {f.originalFileName}
                              </td>
                              <td>{normalizeProjectDataVersion(f.versionLabel)}</td>
                              <td>{formatCalendarDateDisplay(f.dataAsOf) || '—'}</td>
                              <td className="project-data-file-active-cell">
                                {isActive ? 'Yes' : 'No'}
                              </td>
                              <td>
                                {url ? (
                                  <a
                                    className="project-file-download-url"
                                    href={url}
                                    target="_blank"
                                    rel="noreferrer"
                                    title={url}
                                    onClick={(e) => e.stopPropagation()}
                                  >
                                    Download file
                                  </a>
                                ) : (
                                  <span className="project-file-download-url project-file-download-url--empty">
                                    -
                                  </span>
                                )}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                    {projectFiles.length === 0 && !projectDataDropActive ? (
                      <p className="field-help project-data-file-empty">
                        No project data files uploaded yet. Drag files here or click Add Data.
                      </p>
                    ) : null}
                  </div>
                </div>

                {renderTabActions('projectData')}
              </div>
            </div>
          </div>

          <div id="project-tab-services" className={projectTabClass('services')}>
            <h2>Services</h2>
            <div className="admin-edit-card project-panel-tab-card">
            <p className="field-help">
              Filter by service function and category, tick services to include, then choose{' '}
              <strong>Add to Project</strong> to link them and create matching calendar rows for scheduling (
              <code>dbo.tProjectService</code> / calendar). Use <strong>Select all</strong> for every service, or the
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
              <button
                type="button"
                className="primary-button"
                disabled={!model?.selectedServiceIds.length}
                onClick={handleAddServicesToProject}
              >
                Add to Project
              </button>
            </div>
            <div className="project-tab-table-wrap">
              <table className="dashboard-data-table dashboard-data-table--full home-client-project-table project-services-table">
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
                    <th>Service Category</th>
                    <th>Service</th>
                  </tr>
                </thead>
                <tbody>
                  {catalog.length === 0 ? (
                    <tr>
                      <td colSpan={3}>No services in dbo.tService.</td>
                    </tr>
                  ) : filteredCatalogServices.length === 0 ? (
                    <tr>
                      <td colSpan={3}>No services match the selected filters.</td>
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
                        <td>{(row.categoryName ?? '').trim() || '-'}</td>
                        <td>{row.serviceName}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
            {renderTabActions('services')}
            </div>
          </div>

          <div id="project-tab-calendar" className={projectTabClass('calendar')}>
              <h2>Analytics Calendar</h2>
              <div className="admin-edit-card project-panel-tab-card">
                <p className="field-help">
                  Project services and other deliverables live here. Use <strong>Add to Project</strong> on the Services
                  tab to pull selected services into this calendar, or add rows manually. Double-click a row to edit in a
                  popup. Ownership is limited to members of the selected <strong>User Group</strong> on Project Info.
                  Changes are stored in <code>dbo.tProjectCalendarItem</code> when you save this tab.
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
                  <table className="dashboard-data-table dashboard-data-table--full home-client-project-table project-calendar-table">
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
                          <td colSpan={7}>No calendar items yet. Click Add row, or add services from the Services tab.</td>
                        </tr>
                      ) : (
                        calendarItems.map((row) => (
                          <tr
                            key={row.tempId}
                            onDoubleClick={() => openCalendarEdit(row)}
                            title="Double-click to edit"
                            style={{ cursor: 'pointer' }}
                          >
                            <td>
                              <select
                                className="project-calendar-cell-input"
                                value={row.itemType}
                                onChange={(e) =>
                                  updateCalendarItem(row.tempId, {
                                    itemType: e.target.value as ProjectCalendarItemDraft['itemType'],
                                  })
                                }
                                onClick={(e) => e.stopPropagation()}
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
                                className="project-calendar-cell-input"
                                value={row.title}
                                onChange={(e) => updateCalendarItem(row.tempId, { title: e.target.value })}
                                onDoubleClick={(e) => {
                                  e.stopPropagation();
                                  openCalendarEdit(row);
                                }}
                                maxLength={200}
                                placeholder="Deliverable / meeting / task"
                                aria-label="Calendar item title"
                              />
                            </td>
                            <td>
                              <select
                                className="project-calendar-cell-input"
                                value={row.ownerUserID === '' ? '' : String(row.ownerUserID)}
                                onChange={(e) =>
                                  updateCalendarItem(row.tempId, {
                                    ownerUserID: e.target.value ? Number(e.target.value) : '',
                                  })
                                }
                                onClick={(e) => e.stopPropagation()}
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
                                className="project-calendar-cell-input"
                                type="date"
                                value={row.receivedDate}
                                onChange={(e) => updateCalendarItem(row.tempId, { receivedDate: e.target.value })}
                                onDoubleClick={(e) => {
                                  e.stopPropagation();
                                  openCalendarEdit(row);
                                }}
                                aria-label="Received date"
                              />
                            </td>
                            <td>
                              <input
                                className="project-calendar-cell-input"
                                type="date"
                                value={row.completedDate}
                                onChange={(e) => updateCalendarItem(row.tempId, { completedDate: e.target.value })}
                                onDoubleClick={(e) => {
                                  e.stopPropagation();
                                  openCalendarEdit(row);
                                }}
                                aria-label="Completed date"
                              />
                            </td>
                            <td>
                              <input
                                className="project-calendar-cell-input"
                                value={row.notes}
                                onChange={(e) => updateCalendarItem(row.tempId, { notes: e.target.value })}
                                onDoubleClick={(e) => {
                                  e.stopPropagation();
                                  openCalendarEdit(row);
                                }}
                                placeholder="Notes"
                                aria-label="Calendar item notes"
                              />
                            </td>
                            <td>
                              <button
                                type="button"
                                className="danger-button"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  removeCalendarItem(row.tempId);
                                }}
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
                {renderTabActions('calendar')}
              </div>
            </div>

          {isEdit && projectId !== undefined ? (
            <div id="project-tab-time" className={projectTabClass('time')}>
              <h2>Time tracker</h2>
              <div className="admin-edit-card project-panel-tab-card">
              <ProjectTimeTrackerTab
                ref={timeTrackerRef}
                projectId={projectId}
                isActive={activeTab === 'time'}
                flash={flash}
                fallbackTasks={calendarItems
                  .filter((row) => (row.calendarItemID ?? 0) > 0 && row.title.trim().length > 0)
                  .map((row) => ({
                    calendarItemID: row.calendarItemID as number,
                    itemType: row.itemType,
                    title: row.title.trim(),
                    sortOrder: row.sortOrder ?? 0,
                  }))}
              />
              {renderTabActions('time')}
              </div>
            </div>
          ) : null}

          {isEdit ? (
            <div id="project-tab-revenue" className={projectTabClass('revenue')}>
            <h2>Revenue history</h2>
            <div className="admin-edit-card project-panel-tab-card">
            <p className="field-help">
              Active rows from <code>dbo.tProjectRevenue</code> for this project, newest effective date first
              (same order as the home grid Latest Revenue). Delete marks a row inactive.
            </p>
            <div className="project-tab-table-wrap">
              <table className="dashboard-data-table dashboard-data-table--full home-client-project-table catalog-table">
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
            {renderTabActions('revenue')}
            </div>
          </div>
          ) : null}

          <div id="project-tab-notes" className={projectTabClass('notes')}>
            <h2>Notes</h2>
            <div className="admin-edit-card project-panel-tab-card">
            <p className="field-help">
              Project notes are stored in <code>dbo.tProjectNote</code>. Add a note below, or delete existing rows from
              the list.
              {!isEdit
                ? ' On a new project, notes are queued here and saved when you click Save Project.'
                : null}
            </p>
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

              <span className="field-label">Pinned</span>
              <label htmlFor="isPinned" className="project-notes-pinned-label">
                <input
                  id="isPinned"
                  type="checkbox"
                  checked={noteDraft.isPinned}
                  onChange={(e) => setNoteDraft({ ...noteDraft, isPinned: e.target.checked })}
                />
                Pin this note
              </label>

              <span className="field-label" aria-hidden="true" />
              <div className="page-header-actions project-services-select-actions">
                <button type="button" className="secondary-button" onClick={() => void handleAddNote()}>
                  Add note
                </button>
              </div>
            </div>

            <div className="project-tab-table-wrap">
              <table className="dashboard-data-table dashboard-data-table--full home-client-project-table catalog-table">
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
                        {n.noteID < 0 ? ' (pending save)' : ''}
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
            {renderTabActions('notes')}
            </div>
          </div>

          </div>

          <div className="button-row">
          {!isEdit ? (
            <button type="submit" className="primary-button" disabled={tabSaveBusy}>
              Save Project
            </button>
          ) : (
            <button
              type="button"
              className="primary-button"
              disabled={tabSaveBusy}
              onClick={() => {
                const dirty = getDirtyTabs();
                if (dirty.length === 0) {
                  flash('success', 'No unsaved tab changes.');
                  return;
                }
                if (dirty.length > 1) {
                  setPendingSaveTabs(dirty);
                } else {
                  void commitSaveTabs(dirty);
                }
              }}
            >
              Save all changes
            </button>
          )}
          <Link className="secondary-button" to="/">
            {isEdit ? 'Close' : 'Cancel'}
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

      {pendingSaveTabs && pendingSaveTabs.length > 0 ? (
        <div
          className="project-changes-dialog-backdrop"
          role="presentation"
          onClick={() => !tabSaveBusy && setPendingSaveTabs(null)}
        >
          <div
            className="project-changes-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="project-changes-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="project-changes-title">Confirm upcoming changes</h2>
            <p className="field-help">
              You have unsaved updates on more than one tab. Review the pending changes below, then confirm to save
              all of them.
            </p>
            <ul className="project-changes-list">
              {pendingSaveTabs.map((tab) => (
                <li key={tab}>
                  <strong>{PROJECT_TAB_LABELS[tab]}</strong>
                  <ul>
                    {describeTabChanges(tab).map((line) => (
                      <li key={line}>{line}</li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
            <div className="button-row">
              <button
                type="button"
                className="primary-button"
                disabled={tabSaveBusy}
                onClick={() => void commitSaveTabs(pendingSaveTabs)}
              >
                {tabSaveBusy ? 'Saving...' : 'Save all listed changes'}
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={tabSaveBusy}
                onClick={() => setPendingSaveTabs(null)}
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
