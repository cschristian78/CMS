import { ChangeEvent, FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import type { FlashFn } from '../App';
import { ApiError, apiDelete, apiGet, apiPostFormData, apiPostJson, apiPutJson } from '../api';
import { fetchAuthSession } from '../authSession';
import {
  downloadDataAuditReportPdf,
  type DataAuditExportReport,
} from '../admin/exportDataAuditReportPdf';
import { formatCalendarDateDisplay } from '../utils/dateInput';

interface ServerDraft {
  server: string;
  database: string;
  userName: string;
  password: string;
  useIntegratedSecurity: boolean;
}

interface AdminUserAddress {
  addressID: number;
  userID: number;
  addressType?: string;
  address1?: string;
  address2?: string;
  city?: string;
  state?: string;
  postalCode?: string;
  country?: string;
}

/** Normalizes one address row from GET /api/admin/users/{id}/addresses (or embedded primaryAddress). */
function coerceAdminUserAddress(raw: unknown): AdminUserAddress | null {
  if (raw === null || typeof raw !== 'object') {
    return null;
  }
  const o = raw as Record<string, unknown>;
  const num = (camel: string, pascal: string) => {
    const v = o[camel] ?? o[pascal];
    if (typeof v === 'number' && Number.isFinite(v)) {
      return v;
    }
    if (typeof v === 'string' && v.trim() !== '') {
      const n = Number(v);
      return Number.isFinite(n) ? n : 0;
    }
    return 0;
  };
  const str = (camel: string, pascal: string) => {
    const v = o[camel] ?? o[pascal];
    if (v == null) {
      return '';
    }
    return typeof v === 'string' ? v : String(v);
  };
  const addressID = num('addressID', 'AddressID');
  const userID = num('userID', 'UserID');
  const address1 = str('address1', 'Address1');
  const city = str('city', 'City');
  const country = str('country', 'Country');
  const postal = str('postalCode', 'PostalCode');
  const state = str('state', 'State');
  const address2 = str('address2', 'Address2');
  const addressType = str('addressType', 'AddressType');
  if (
    addressID <= 0 &&
    !address1.trim() &&
    !city.trim() &&
    !postal.trim() &&
    !state.trim() &&
    !country.trim()
  ) {
    return null;
  }
  return {
    addressID,
    userID,
    addressType: addressType || 'Work',
    address1,
    address2: address2 || undefined,
    city,
    state: state || undefined,
    postalCode: postal || undefined,
    country: country || undefined,
  };
}

function parseAddressListPayload(raw: unknown): AdminUserAddress[] {
  if (Array.isArray(raw)) {
    return raw.map(coerceAdminUserAddress).filter((x): x is AdminUserAddress => x !== null);
  }
  if (raw && typeof raw === 'object') {
    const inner = (raw as { data?: unknown }).data;
    if (Array.isArray(inner)) {
      return parseAddressListPayload(inner);
    }
  }
  return [];
}

function pickPreferredUserAddress(rows: AdminUserAddress[]): AdminUserAddress | null {
  if (rows.length === 0) {
    return null;
  }
  const work = rows.find((a) => (a.addressType ?? 'Work').trim().toLowerCase() === 'work');
  return work ?? rows[0]!;
}

interface AdminUser {
  userID: number;
  username?: string;
  firstName?: string;
  lastName?: string;
  department?: string;
  title?: string;
  email?: string;
  phone?: string;
  company?: string;
  userType?: string;
  statusID?: number;
  statusCode?: string;
  statusDescription?: string;
  isDeleted?: boolean;
  deletedDate?: string | null;
  displayName: string;
  primaryAddress?: AdminUserAddress | null;
  addresses?: AdminUserAddress[];
}

interface AdminRole {
  userRoleID: number;
  userRoleName?: string;
  roleDescription?: string;
  isActive?: boolean;
}

interface AdminGroupMember {
  ugid: number;
  userGroupNumber?: string;
  ugName?: string;
  userID: number;
  userRoleID?: number | null;
  statusID?: number;
  statusCode?: string;
  statusDescription?: string;
  joinedDate?: string | null;
  displayName: string;
  username?: string;
  userFullName?: string;
  email?: string;
  userRoleName?: string;
}

interface UserGroupListRow {
  ugid: number;
  userGroupNumber: string;
  ugName?: string;
  memberCount: number;
}

interface PendingGroupMember {
  tempId: string;
  userID: number;
  userRoleID: number;
}

interface AdminProjectType {
  projectTypeID: number;
  projectTypeCode?: string;
  projectTypeDescription?: string;
  displayName: string;
}

interface AdminBusinessUnit {
  businessUnitID: number;
  businessUnitName?: string;
  businessUnitDescription?: string;
  isActive?: boolean;
}

interface AdminBusinessClass {
  businessClassID: number;
  businessUnitID: number;
  businessClassName?: string;
  businessClassDescription?: string;
  businessUnitName?: string;
}

interface AdminClientCategory {
  clientCategoryID: number;
  ccName?: string;
  ccDescription?: string;
  isActive?: boolean;
}

interface AdminClientType {
  clientTypeID: number;
  ctName?: string;
  ctDescription?: string;
  isActive?: boolean;
}

interface AdminServiceCategory {
  serviceCategoryID: number;
  categoryName?: string;
  functionID: number;
  /** dbo.tServiceCategory.ServiceCategoryDescription */
  serviceCategoryDescription?: string;
  isActive?: boolean;
}

interface AdminServiceGroup {
  serviceGroupID: number;
  serviceGroupNumber?: string;
  serviceGroupName?: string;
  serviceGroupDescription?: string | null;
  isActive?: boolean;
}

interface AdminTag {
  tagID: number;
  tagName: string;
  tagScope?: string;
}

interface CatalogServiceRow {
  serviceID: number;
  serviceCategoryID?: number | null;
  serviceGroupID?: number | null;
  serviceGroupName?: string | null;
  functionID?: number | null;
  categoryName?: string | null;
  serviceCategoryDescription?: string | null;
  serviceName: string;
  serviceDescription?: string | null;
  statusID?: number | null;
  statusCode?: string | null;
  statusDescription?: string | null;
  isActive: boolean;
  isDeleted?: boolean;
  deletedDate?: string | null;
  createdDate?: string | null;
  tags?: AdminTag[];
}

type ServiceFormState = {
  serviceID: number;
  serviceCategoryID: number;
  serviceGroupID: number;
  functionID: number;
  categoryName: string;
  serviceCategoryDescription: string;
  serviceName: string;
  serviceDescription: string;
  tagIDs: number[];
  isActive: boolean;
  createdDateDisplay: string;
};

const emptyServiceForm = (): ServiceFormState => ({
  serviceID: 0,
  serviceCategoryID: 0,
  serviceGroupID: 0,
  functionID: 0,
  categoryName: '',
  serviceCategoryDescription: '',
  serviceName: '',
  serviceDescription: '',
  tagIDs: [],
  isActive: true,
  createdDateDisplay: '',
});

function formatServiceDateDisplay(iso: string | null | undefined): string {
  return formatCalendarDateDisplay(iso);
}

/** Map a catalog row + lookup tables to form state so dropdowns match the service's tServiceRef. */
function buildServiceFormState(
  s: CatalogServiceRow,
  catalog: Pick<AdminModel, 'serviceCategories' | 'serviceGroups'>,
): ServiceFormState {
  let serviceCategoryID = Number(s.serviceCategoryID) || 0;
  let serviceGroupID = Number(s.serviceGroupID) || 0;
  let functionID = Number(s.functionID) || 0;
  let categoryName = (s.categoryName ?? '').trim();
  let serviceCategoryDescription = (s.serviceCategoryDescription ?? '').trim();
  const groupNameFromRow = (s.serviceGroupName ?? '').trim();

  if (serviceCategoryID > 0) {
    const cat = catalog.serviceCategories.find((c) => c.serviceCategoryID === serviceCategoryID);
    if (cat) {
      functionID = cat.functionID ?? 0;
      categoryName = (cat.categoryName ?? categoryName).trim();
      serviceCategoryDescription = (cat.serviceCategoryDescription ?? serviceCategoryDescription).trim();
    }
  } else {
    const catByName = catalog.serviceCategories.find(
      (c) =>
        categoryName !== '' &&
        (c.categoryName ?? '').trim().toLowerCase() === categoryName.toLowerCase(),
    );
    const catByDesc = catalog.serviceCategories.find(
      (c) =>
        serviceCategoryDescription !== '' &&
        (c.serviceCategoryDescription ?? '').trim().toLowerCase() ===
          serviceCategoryDescription.toLowerCase(),
    );
    const cat = catByName ?? catByDesc;
    if (cat) {
      serviceCategoryID = cat.serviceCategoryID;
      functionID = cat.functionID ?? 0;
      categoryName = (cat.categoryName ?? '').trim();
      serviceCategoryDescription = (cat.serviceCategoryDescription ?? '').trim();
    }
  }

  if (serviceGroupID <= 0 && groupNameFromRow !== '') {
    const grp = (catalog.serviceGroups ?? []).find(
      (g) => (g.serviceGroupName ?? '').trim().toLowerCase() === groupNameFromRow.toLowerCase(),
    );
    if (grp) {
      serviceGroupID = grp.serviceGroupID;
    }
  }

  return {
    serviceID: s.serviceID,
    serviceCategoryID,
    serviceGroupID,
    functionID,
    categoryName,
    serviceCategoryDescription,
    serviceName: s.serviceName ?? '',
    serviceDescription: s.serviceDescription ?? '',
    tagIDs: (s.tags ?? []).map((t) => t.tagID),
    isActive: serviceIsActive(s),
    createdDateDisplay: formatServiceDateDisplay(s.createdDate),
  };
}

type ClientStatus = 'Active' | 'Inactive' | 'Prospect';

const CLIENT_STATUSES: ClientStatus[] = ['Active', 'Inactive', 'Prospect'];

/** Active flag from dbo.tService.IsActive (API may send isActive and/or isDeleted). */
const serviceIsActive = (s: { isActive?: boolean; isDeleted?: boolean }): boolean =>
  s.isActive ?? s.isDeleted !== true;

/** Resolve a client's lifecycle status, tolerating older rows that only carry isActive. */
const clientStatusOf = (c: { clientStatus?: string | null; isActive?: boolean }): ClientStatus => {
  const s = (c.clientStatus ?? '').trim().toLowerCase();
  if (s === 'inactive') return 'Inactive';
  if (s === 'prospect') return 'Prospect';
  if (s === 'active') return 'Active';
  return c.isActive === false ? 'Inactive' : 'Active';
};

type ClientRefTagKind = 'active' | 'inactive' | 'prospect' | 'needupdate';

/**
 * The directory badge reflects the status carried on the client's primary dbo.tClientRef row
 * (tClientRef.StatusID -> tStatus). When the client has no classification row, it is flagged
 * "Need Update" so it can be triaged.
 */
const clientRefTag = (c: AdminClient): { label: string; kind: ClientRefTagKind } => {
  if (c.clientRefID == null) {
    return { label: 'Need Update', kind: 'needupdate' };
  }
  const desc = (c.refStatusDescription ?? '').trim().toLowerCase();
  if (desc === 'inactive') return { label: 'Inactive', kind: 'inactive' };
  if (desc === 'prospect') return { label: 'Prospect', kind: 'prospect' };
  if (desc === 'active') return { label: 'Active', kind: 'active' };
  return { label: 'Need Update', kind: 'needupdate' };
};

// Distinct, readable colors assigned to departments by their sorted position. Reused by the
// header count chips and the per-row department badge so a department is always the same color.
const DEPT_PALETTE: { bg: string; fg: string }[] = [
  { bg: 'rgba(32, 212, 232, 0.18)', fg: '#20d4e8' },
  { bg: 'rgba(48, 216, 128, 0.16)', fg: '#30d880' },
  { bg: 'rgba(240, 135, 0, 0.18)', fg: '#f08700' },
  { bg: 'rgba(99, 102, 241, 0.2)', fg: '#a5b4fc' },
  { bg: 'rgba(244, 63, 94, 0.18)', fg: '#fb7185' },
  { bg: 'rgba(14, 165, 233, 0.18)', fg: '#38bdf8' },
  { bg: 'rgba(168, 85, 247, 0.18)', fg: '#c084fc' },
  { bg: 'rgba(239, 68, 68, 0.18)', fg: '#f87171' },
  { bg: 'rgba(16, 185, 129, 0.16)', fg: '#34d399' },
  { bg: 'rgba(245, 158, 11, 0.18)', fg: '#fbbf24' },
  { bg: 'rgba(99, 102, 241, 0.22)', fg: '#818cf8' },
  { bg: 'rgba(249, 115, 22, 0.18)', fg: '#fb923c' },
];

const NO_DEPT_COLOR = { bg: 'rgba(90, 112, 128, 0.2)', fg: '#8aa0b0' };

const deptColorByIndex = (index: number) =>
  index < 0 ? NO_DEPT_COLOR : DEPT_PALETTE[index % DEPT_PALETTE.length];

interface UserComboProps {
  id: string;
  users: AdminUser[];
  value: number;
  onChange: (userID: number) => void;
  placeholder?: string;
}

const comboUserLabel = (u: AdminUser) => (u.displayName?.trim() || `User ${u.userID}`);
const comboUserDept = (u: AdminUser) => (u.department ?? '').trim();

/**
 * Searchable user picker for Account Executive / Analytics Lead. The list is ordered by
 * department (blank departments last) then name, and the user can filter by typing a person's
 * name and/or their department.
 */
function UserCombo({ id, users, value, onChange, placeholder }: UserComboProps) {
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);

  const selected = useMemo(() => users.find((u) => u.userID === value) ?? null, [users, value]);

  const ordered = useMemo(() => {
    return [...users].sort((a, b) => {
      const da = comboUserDept(a).toLowerCase();
      const db = comboUserDept(b).toLowerCase();
      if (da !== db) {
        if (da === '') return 1;
        if (db === '') return -1;
        return da < db ? -1 : 1;
      }
      return comboUserLabel(a).toLowerCase().localeCompare(comboUserLabel(b).toLowerCase());
    });
  }, [users]);

  const filtered = useMemo(() => {
    const tokens = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (tokens.length === 0) return ordered;
    return ordered.filter((u) => {
      const hay = `${comboUserLabel(u)} ${comboUserDept(u)}`.toLowerCase();
      return tokens.every((t) => hay.includes(t));
    });
  }, [ordered, query]);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) {
        setOpen(false);
        setQuery('');
      }
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  const choose = (userID: number) => {
    onChange(userID);
    setOpen(false);
    setQuery('');
  };

  const selectedLabel = selected
    ? `${comboUserLabel(selected)}${comboUserDept(selected) ? ` - ${comboUserDept(selected)}` : ''}`
    : '';

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
        placeholder={placeholder ?? 'Type a name or department...'}
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
      {open && (
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
            - None -
          </li>
          {filtered.length === 0 ? (
            <li className="user-combo-empty" role="status">
              No matches
            </li>
          ) : (
            filtered.map((u) => (
              <li
                key={u.userID}
                role="option"
                aria-selected={u.userID === value}
                className={'user-combo-option' + (u.userID === value ? ' selected' : '')}
                onMouseDown={(e) => {
                  e.preventDefault();
                  choose(u.userID);
                }}
              >
                <span className="user-combo-name">{comboUserLabel(u)}</span>
                {comboUserDept(u) ? <span className="user-combo-dept">{comboUserDept(u)}</span> : null}
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
}

/** Row from GET /api/admin (Clients) - dbo.tClient joined to its primary dbo.tClientRef. */
interface AdminClient {
  clientID: number;
  clientName?: string | null;
  clientShortName?: string | null;
  clientDescription?: string | null;
  isActive: boolean;
  clientStatus?: string | null;
  accountExecutiveUserID?: number | null;
  accountExecutiveName?: string | null;
  analyticsLeadUserID?: number | null;
  analyticsLeadName?: string | null;
  clientRefID?: number | null;
  businessUnitID?: number | null;
  businessUnitName?: string | null;
  businessClassID?: number | null;
  businessClassName?: string | null;
  clientTypeID?: number | null;
  clientTypeName?: string | null;
  refStatusID?: number | null;
  refStatusCode?: string | null;
  refStatusDescription?: string | null;
}

interface ClientImportRow {
  lineNumber: number;
  clientName: string;
  clientShortName: string;
  clientDescription: string;
  isActive: boolean;
  accountExecutiveEmail: string;
  analyticsLeadEmail: string;
  businessUnit: string;
  businessClass: string;
  clientType: string;
}

interface ClientBulkParseResult {
  rows: ClientImportRow[];
  skippedEmptyRows: number;
}

interface ClientBulkVerifyResult {
  valid: boolean;
  errors: string[];
}

interface ClientBulkCommitResult {
  importedCount: number;
  skippedEmptyRows: number;
  rowErrors: string[];
}

interface AdminStatus {
  statusId: number;
  statusCode?: string | null;
  statusDescription?: string | null;
  isActive?: boolean;
}

interface AdminModel {
  serverSettings: ServerDraft & { hasSqlCredentials?: boolean };
  serverOptions: string[];
  users: AdminUser[];
  bmsUsers: AdminUser[];
  userRoles: AdminRole[];
  userGroupMembers: AdminGroupMember[];
  userGroupSummaries?: UserGroupListRow[];
  projectTypes: AdminProjectType[];
  businessUnits: AdminBusinessUnit[];
  businessClasses: AdminBusinessClass[];
  clientCategories: AdminClientCategory[];
  clientTypes: AdminClientType[];
  serviceCategories: AdminServiceCategory[];
  serviceGroups?: AdminServiceGroup[];
  statuses?: AdminStatus[];
  tags: AdminTag[];
  services: CatalogServiceRow[];
  clients: AdminClient[];
}

interface UserImportRow {
  lineNumber: number;
  email: string;
  firstName: string;
  lastName: string;
  department: string;
  title: string;
  phone: string;
  company: string;
  userType: string;
  addressType: string;
  address1: string;
  address2: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  isActive: boolean;
}

interface UserBulkParseResult {
  rows: UserImportRow[];
  skippedEmptyRows: number;
}

interface UserBulkVerifyResult {
  valid: boolean;
  errors: string[];
}

interface UserBulkCommitResult {
  importedCount: number;
  skippedEmptyRows: number;
  rowErrors: string[];
}

interface ServiceImportRow {
  lineNumber: number;
  serviceID: number;
  categoryName: string;
  serviceCategoryDescription: string;
  serviceGroupName: string;
  serviceName: string;
  serviceDescription: string;
  /** Comma- or semicolon-separated list of tag names; resolved via usp_Tag_GetOrCreate. */
  tags: string;
  isActive: boolean;
  /** ISO date from server; omit or null on insert -> SQL uses GETDATE() */
  createdDate?: string | null;
}

interface ServiceBulkParseResult {
  rows: ServiceImportRow[];
  skippedEmptyRows: number;
}

interface ServiceBulkVerifyResult {
  valid: boolean;
  errors: string[];
}

interface ServiceBulkCommitResult {
  importedCount: number;
  skippedEmptyRows: number;
  rowErrors: string[];
}

type TabId =
  | 'server'
  | 'catai'
  | 'correspondence'
  | 'users'
  | 'projectTypes'
  | 'businessUnits'
  | 'businessClasses'
  | 'clientTypes'
  | 'clients'
  | 'services'
  | 'dataAudit';

type CorrespondenceOption = { value: string; text: string };

type CorrespondenceDraft = {
  enabled: boolean;
  sequence: string;
  dayOfWeek: string;
  sendTimeLocal: string;
  timeZoneId: string;
  recipientType: string;
  communicationType: string;
  ccAddress: string;
  recipientOptions: CorrespondenceOption[];
  sequenceOptions: CorrespondenceOption[];
  communicationOptions: CorrespondenceOption[];
  dayOfWeekOptions: string[];
};

const DEFAULT_CORRESPONDENCE_DRAFT: CorrespondenceDraft = {
  enabled: true,
  sequence: 'Weekly',
  dayOfWeek: 'Monday',
  sendTimeLocal: '07:30',
  timeZoneId: 'Eastern Standard Time',
  recipientType: 'EachCatMember',
  communicationType: 'CatMemberProjectStatus',
  ccAddress: 'Scott.Christian@bmsgroup.com',
  recipientOptions: [
    { value: 'AnalyticsLead', text: 'Analytics Lead' },
    { value: 'EachCatMember', text: 'Each Cat Member' },
  ],
  sequenceOptions: [{ value: 'Weekly', text: 'Weekly' }],
  communicationOptions: [
    {
      value: 'CatMemberProjectStatus',
      text: 'CAT member project status (Online assigned projects, tasks, % complete, time tracking)',
    },
  ],
  dayOfWeekOptions: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
};

/** Nested tabs inside the Admin Users section. */
type UsersSubTab = 'users' | 'roles' | 'groups';

/** Nested tabs inside the Admin Services section. */
type ServicesSubTab = 'groups' | 'categories' | 'services';

interface DataAuditCheck {
  checkId: string;
  title: string;
  description: string;
  count: number;
  columns: string[];
  rows: (string | null)[][];
}

interface DataAuditReport {
  generatedAtUtc: string;
  checks: DataAuditCheck[];
}

type DataAuditCheckGroup = {
  key: string;
  label: string;
  checks: DataAuditCheck[];
};

function dataAuditCheckGroupKey(check: DataAuditCheck): string {
  const id = (check.checkId ?? '').toLowerCase();
  const title = (check.title ?? '').trim().toLowerCase();
  if (id.startsWith('clients-') || title.startsWith('clients ')) {
    return 'clients';
  }
  if (id.startsWith('projects-') || title.startsWith('projects ')) {
    return 'projects';
  }
  if (id.startsWith('user-groups-') || title.startsWith('user groups ')) {
    return 'user-groups';
  }
  if (id.startsWith('user-logins-') || title.startsWith('user login')) {
    return 'logins';
  }
  return 'other';
}

const DATA_AUDIT_GROUP_ORDER: { key: string; label: string }[] = [
  { key: 'clients', label: 'Clients' },
  { key: 'projects', label: 'Projects' },
  { key: 'user-groups', label: 'User groups' },
  { key: 'logins', label: 'Logins' },
  { key: 'other', label: 'Other' },
];

function groupDataAuditChecks(checks: DataAuditCheck[]): DataAuditCheckGroup[] {
  const buckets = new Map<string, DataAuditCheck[]>();
  for (const check of checks) {
    const key = dataAuditCheckGroupKey(check);
    const list = buckets.get(key);
    if (list) {
      list.push(check);
    } else {
      buckets.set(key, [check]);
    }
  }
  return DATA_AUDIT_GROUP_ORDER.filter((g) => (buckets.get(g.key)?.length ?? 0) > 0).map((g) => ({
    key: g.key,
    label: g.label,
    checks: buckets.get(g.key) ?? [],
  }));
}

type AuditGroupHighlight = 'members' | 'project-link' | 'client-link';

function auditRowUgid(check: DataAuditCheck, row: (string | null)[]): number | null {
  const idx = check.columns.findIndex((c) => c.trim().toLowerCase() === 'ugid');
  if (idx < 0) {
    return null;
  }
  const n = Number(String(row[idx] ?? '').trim());
  return Number.isFinite(n) && n > 0 ? n : null;
}

function auditRowUserId(check: DataAuditCheck, row: (string | null)[]): number | null {
  const idx = check.columns.findIndex((c) => {
    const name = c.trim().toLowerCase();
    return name === 'userid' || name === 'user id';
  });
  if (idx < 0) {
    return null;
  }
  const n = Number(String(row[idx] ?? '').trim());
  return Number.isFinite(n) && n > 0 ? n : null;
}

function auditRowClientId(check: DataAuditCheck, row: (string | null)[]): number | null {
  const idx = check.columns.findIndex((c) => {
    const name = c.trim().toLowerCase();
    return name === 'clientid' || name === 'client id';
  });
  if (idx < 0) {
    return null;
  }
  const n = Number(String(row[idx] ?? '').trim());
  return Number.isFinite(n) && n > 0 ? n : null;
}

function isClientsMissingProjectCheck(check: DataAuditCheck): boolean {
  const id = (check.checkId ?? '').toLowerCase();
  const title = (check.title ?? '').trim().toLowerCase();
  return id === 'clients-missing-project' || title === 'clients with no project';
}

function auditRowClientLabel(check: DataAuditCheck, row: (string | null)[]): string {
  const idx = check.columns.findIndex((c) => c.trim().toLowerCase() === 'client');
  const name = idx >= 0 ? String(row[idx] ?? '').trim() : '';
  if (name && name !== '-' && name !== '—') {
    return name;
  }
  const clientId = auditRowClientId(check, row);
  return clientId != null ? `Client ${clientId}` : 'this client';
}

function auditRowProjectId(check: DataAuditCheck, row: (string | null)[]): number | null {
  const idx = check.columns.findIndex((c) => {
    const name = c.trim().toLowerCase();
    return name === 'projectid' || name === 'project id';
  });
  if (idx < 0) {
    return null;
  }
  const n = Number(String(row[idx] ?? '').trim());
  return Number.isFinite(n) && n > 0 ? n : null;
}

function auditRowGroupName(check: DataAuditCheck, row: (string | null)[]): string | null {
  const idx = check.columns.findIndex((c) => {
    const name = c.trim().toLowerCase();
    return name === 'group' || name === 'ugname' || name === 'user group' || name === 'usergroup';
  });
  if (idx < 0) {
    return null;
  }
  const value = String(row[idx] ?? '').trim();
  return value.length > 0 ? value : null;
}

function auditHighlightForCheck(
  checkId: string,
): { highlight: AuditGroupHighlight; message: string } | null {
  switch (checkId) {
    case 'user-groups-missing-analytics-lead':
      return {
        highlight: 'members',
        message:
          'This user group has no Analytics Lead. Add a member with the Analytics Lead role, then save.',
      };
    case 'user-groups-missing-account-executive':
      return {
        highlight: 'members',
        message:
          'This user group has no Account Executive. Add a member with the Account Executive role, then save.',
      };
    case 'user-groups-without-members':
      return {
        highlight: 'members',
        message: 'This user group has no members. Add at least one user/role, then save.',
      };
    case 'user-groups-missing-project':
      return {
        highlight: 'project-link',
        message:
          'This user group is not linked to any project. Assign it on a project (Project → User group) to clear this finding.',
      };
    case 'projects-inactive-or-missing-user-group':
      return {
        highlight: 'project-link',
        message:
          'A project points at this user group, but the group is inactive or incomplete. Review group details and membership, then re-check project assignment.',
      };
    default:
      return null;
  }
}

/** US states and DC - two-letter postal codes for the user address State field. */
const US_STATE_CODES: readonly string[] = [
  'AL',
  'AK',
  'AZ',
  'AR',
  'CA',
  'CO',
  'CT',
  'DE',
  'DC',
  'FL',
  'GA',
  'HI',
  'ID',
  'IL',
  'IN',
  'IA',
  'KS',
  'KY',
  'LA',
  'ME',
  'MD',
  'MA',
  'MI',
  'MN',
  'MS',
  'MO',
  'MT',
  'NE',
  'NV',
  'NH',
  'NJ',
  'NM',
  'NY',
  'NC',
  'ND',
  'OH',
  'OK',
  'OR',
  'PA',
  'RI',
  'SC',
  'SD',
  'TN',
  'TX',
  'UT',
  'VT',
  'VA',
  'WA',
  'WV',
  'WI',
  'WY',
];

const US_STATE_CODE_SET = new Set(US_STATE_CODES);

function userStateSelectValue(state: string): string {
  const t = state.trim();
  if (!t) {
    return '';
  }
  const upper = t.toUpperCase();
  if (US_STATE_CODE_SET.has(upper)) {
    return upper;
  }
  return t;
}

function safeInternalReturnPath(raw: string | null): string | null {
  const s = raw?.trim();
  if (!s || !s.startsWith('/') || s.startsWith('//')) {
    return null;
  }
  if (s.includes('://')) {
    return null;
  }
  return s;
}

type UserTypeOption = 'Basic' | 'External' | 'Administrator' | 'Manager' | 'Analyst';

function normalizeUserType(value?: string | null): UserTypeOption {
  const v = (value ?? '').trim().toLowerCase();
  if (v === 'external') {
    return 'External';
  }
  if (v === 'administrator') {
    return 'Administrator';
  }
  if (v === 'manager') {
    return 'Manager';
  }
  if (v === 'analyst') {
    return 'Analyst';
  }
  return 'Basic';
}

export default function AdminPage({ flash }: { flash: FlashFn }) {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const rawTab = searchParams.get('tab') || 'server';
  const tab: TabId =
    rawTab === 'roles' || rawTab === 'groups'
      ? 'users'
      : rawTab === 'serviceCategories'
        ? 'services'
        : (rawTab as TabId);

  const setTab = (t: TabId) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('tab', t);
      return next;
    });
  };

  const [usersSubTab, setUsersSubTab] = useState<UsersSubTab>(() => {
    if (rawTab === 'roles') {
      return 'roles';
    }
    if (rawTab === 'groups') {
      return 'groups';
    }
    return 'users';
  });

  const [servicesSubTab, setServicesSubTab] = useState<ServicesSubTab>(() =>
    rawTab === 'serviceCategories' ? 'categories' : 'groups',
  );

  useEffect(() => {
    if (rawTab === 'roles' || rawTab === 'groups') {
      setUsersSubTab(rawTab);
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.set('tab', 'users');
          return next;
        },
        { replace: true },
      );
      return;
    }
    if (rawTab !== 'serviceCategories') {
      return;
    }
    setServicesSubTab('categories');
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.set('tab', 'services');
        return next;
      },
      { replace: true },
    );
  }, [rawTab, setSearchParams]);

  const [admin, setAdmin] = useState<AdminModel | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [serverDraft, setServerDraft] = useState<ServerDraft>({
    server: '',
    database: '',
    userName: '',
    password: '',
    useIntegratedSecurity: true,
  });

  const [isAdministrator, setIsAdministrator] = useState(false);
  const [catAiDraft, setCatAiDraft] = useState({
    apiKey: '',
    apiKeyConfigured: false,
    model: 'gpt-4o',
    baseUrl: '',
    deployment: '',
    apiVersion: '2024-06-01',
    systemPrompt: '',
    temperature: 0.2,
    maxTokens: 1500,
    maxToolRounds: 5,
  });
  const [catAiKeyReplace, setCatAiKeyReplace] = useState('');
  const [catAiLoaded, setCatAiLoaded] = useState(false);
  const [correspondenceDraft, setCorrespondenceDraft] = useState<CorrespondenceDraft>(DEFAULT_CORRESPONDENCE_DRAFT);
  const [correspondenceLoaded, setCorrespondenceLoaded] = useState(false);
  const [correspondenceBusy, setCorrespondenceBusy] = useState(false);

  const reload = useCallback(async () => {
    const data = await apiGet<AdminModel>('/api/admin');
    setAdmin(data);
    setServerDraft({
      server: data.serverSettings.server,
      database: data.serverSettings.database,
      userName: data.serverSettings.userName ?? '',
      password: '',
      useIntegratedSecurity: data.serverSettings.useIntegratedSecurity,
    });
  }, []);

  // Data Audit (state before load/effect that use it)
  const [dataAudit, setDataAudit] = useState<DataAuditReport | null>(null);
  const [dataAuditLoading, setDataAuditLoading] = useState(false);
  const [dataAuditExporting, setDataAuditExporting] = useState(false);
  const [dataAuditError, setDataAuditError] = useState<string | null>(null);
  const [expandedAuditCheckId, setExpandedAuditCheckId] = useState<string | null>(null);
  const [dataAuditClientMenu, setDataAuditClientMenu] = useState<{
    clientId: number;
    clientName: string;
    x: number;
    y: number;
  } | null>(null);
  const [dataAuditClientBusy, setDataAuditClientBusy] = useState(false);
  const [auditGroupHighlight, setAuditGroupHighlight] = useState<AuditGroupHighlight | null>(null);
  const [auditGroupHighlightMessage, setAuditGroupHighlightMessage] = useState<string | null>(null);
  const groupEditPanelRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        setLoadError(null);
        await reload();
        const session = await fetchAuthSession();
        const adminUser =
          (session.userType ?? '').trim().toLowerCase() === 'administrator';
        setIsAdministrator(adminUser);
      } catch (e) {
        setLoadError(e instanceof ApiError ? e.message : 'Could not load admin data.');
      }
    })();
  }, [reload]);

  const loadCatAiSettings = useCallback(async () => {
    const data = await apiGet<{
      apiKey?: string;
      apiKeyConfigured?: boolean;
      model?: string;
      baseUrl?: string;
      deployment?: string;
      apiVersion?: string;
      systemPrompt?: string;
      temperature?: number;
      maxTokens?: number;
      maxToolRounds?: number;
    }>('/api/admin/catai-settings');
    setCatAiDraft({
      apiKey: data.apiKey ?? '',
      apiKeyConfigured: data.apiKeyConfigured === true,
      model: data.model ?? 'gpt-4o',
      baseUrl: data.baseUrl ?? '',
      deployment: data.deployment ?? '',
      apiVersion: data.apiVersion ?? '2024-06-01',
      systemPrompt: data.systemPrompt ?? '',
      temperature: typeof data.temperature === 'number' ? data.temperature : 0.2,
      maxTokens: typeof data.maxTokens === 'number' ? data.maxTokens : 1500,
      maxToolRounds: typeof data.maxToolRounds === 'number' ? data.maxToolRounds : 5,
    });
    setCatAiKeyReplace('');
    setCatAiLoaded(true);
  }, []);

  useEffect(() => {
    if (!isAdministrator || tab !== 'catai' || catAiLoaded) {
      return;
    }
    void (async () => {
      try {
        await loadCatAiSettings();
      } catch (err) {
        flash('error', err instanceof ApiError ? err.message : 'Could not load CatAI settings.');
      }
    })();
    // flash is stable enough for this admin page; avoid re-fetch loops
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdministrator, tab, catAiLoaded, loadCatAiSettings]);

  const loadCorrespondenceSettings = useCallback(async () => {
    const data = await apiGet<{
      enabled?: boolean;
      sequence?: string;
      dayOfWeek?: string;
      sendTimeLocal?: string;
      timeZoneId?: string;
      recipientType?: string;
      communicationType?: string;
      ccAddress?: string;
      recipientOptions?: CorrespondenceOption[];
      sequenceOptions?: CorrespondenceOption[];
      communicationOptions?: CorrespondenceOption[];
      dayOfWeekOptions?: string[];
    }>('/api/admin/correspondence-settings');
    setCorrespondenceDraft({
      enabled: data.enabled !== false,
      sequence: data.sequence ?? DEFAULT_CORRESPONDENCE_DRAFT.sequence,
      dayOfWeek: data.dayOfWeek ?? DEFAULT_CORRESPONDENCE_DRAFT.dayOfWeek,
      sendTimeLocal: data.sendTimeLocal ?? DEFAULT_CORRESPONDENCE_DRAFT.sendTimeLocal,
      timeZoneId: data.timeZoneId ?? DEFAULT_CORRESPONDENCE_DRAFT.timeZoneId,
      recipientType: data.recipientType ?? DEFAULT_CORRESPONDENCE_DRAFT.recipientType,
      communicationType: data.communicationType ?? DEFAULT_CORRESPONDENCE_DRAFT.communicationType,
      ccAddress: data.ccAddress ?? DEFAULT_CORRESPONDENCE_DRAFT.ccAddress,
      recipientOptions:
        data.recipientOptions && data.recipientOptions.length > 0
          ? data.recipientOptions
          : DEFAULT_CORRESPONDENCE_DRAFT.recipientOptions,
      sequenceOptions:
        data.sequenceOptions && data.sequenceOptions.length > 0
          ? data.sequenceOptions
          : DEFAULT_CORRESPONDENCE_DRAFT.sequenceOptions,
      communicationOptions:
        data.communicationOptions && data.communicationOptions.length > 0
          ? data.communicationOptions
          : DEFAULT_CORRESPONDENCE_DRAFT.communicationOptions,
      dayOfWeekOptions:
        data.dayOfWeekOptions && data.dayOfWeekOptions.length > 0
          ? data.dayOfWeekOptions
          : DEFAULT_CORRESPONDENCE_DRAFT.dayOfWeekOptions,
    });
    setCorrespondenceLoaded(true);
  }, []);

  useEffect(() => {
    if (tab !== 'correspondence' || correspondenceLoaded) {
      return;
    }
    void (async () => {
      try {
        await loadCorrespondenceSettings();
      } catch (err) {
        flash('error', err instanceof ApiError ? err.message : 'Could not load correspondence settings.');
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, correspondenceLoaded, loadCorrespondenceSettings]);

  const loadDataAudit = useCallback(async () => {
    setDataAuditLoading(true);
    setDataAuditError(null);
    try {
      const report = await apiGet<DataAuditReport>('/api/admin/data-audit');
      setDataAudit(report);
      setExpandedAuditCheckId((prev) => {
        if (prev && report.checks.some((c) => c.checkId === prev && c.count > 0)) {
          return prev;
        }
        const firstFinding = report.checks.find((c) => c.count > 0);
        return firstFinding?.checkId ?? report.checks[0]?.checkId ?? null;
      });
    } catch (e) {
      setDataAudit(null);
      setDataAuditError(e instanceof ApiError ? e.message : 'Could not load data audit.');
    } finally {
      setDataAuditLoading(false);
    }
  }, []);

  const generateDataAuditPdf = useCallback(async () => {
    setDataAuditExporting(true);
    try {
      const exportReport = await apiGet<DataAuditExportReport>('/api/admin/data-audit/export');
      const fileName = await downloadDataAuditReportPdf(exportReport);
      flash('success', `Downloaded ${fileName}.`);
    } catch (e) {
      flash('error', e instanceof ApiError ? e.message : 'Could not generate the Data Audit PDF.');
    } finally {
      setDataAuditExporting(false);
    }
  }, [flash]);

  useEffect(() => {
    if (tab !== 'dataAudit') {
      return;
    }
    if (dataAudit != null || dataAuditLoading) {
      return;
    }
    void loadDataAudit();
  }, [tab, dataAudit, dataAuditLoading, loadDataAudit]);

  // Users tab
  const [selectedUserId, setSelectedUserId] = useState<number | ''>('');
  const [userForm, setUserForm] = useState({
    userID: 0,
    firstName: '',
    lastName: '',
    title: '',
    department: '',
    email: '',
    phone: '',
    company: 'BMS',
    userType: 'Basic' as UserTypeOption,
    address: '',
    addressType: 'Work',
    city: '',
    state: '',
    postalCode: '',
    country: '',
    statusID: 0,
  });
  const [userPhotoRevision, setUserPhotoRevision] = useState(0);
  const [userPhotoBroken, setUserPhotoBroken] = useState(true);
  const userPhotoInputRef = useRef<HTMLInputElement | null>(null);
  const [copyEmail, setCopyEmail] = useState('');
  const [userRemoveDialogOpen, setUserRemoveDialogOpen] = useState(false);
  const [buDeleteBlockedMessage, setBuDeleteBlockedMessage] = useState<string | null>(null);
  const [bcDeleteBlockedMessage, setBcDeleteBlockedMessage] = useState<string | null>(null);
  const [clientDeleteBlockedMessage, setClientDeleteBlockedMessage] = useState<string | null>(null);
  const [ugDeleteBlockedMessage, setUgDeleteBlockedMessage] = useState<string | null>(null);
  const [clientContextMenu, setClientContextMenu] = useState<{
    clientId: number;
    clientName: string;
    x: number;
    y: number;
  } | null>(null);

  const [userBulkWizardOpen, setUserBulkWizardOpen] = useState(false);
  const [userBulkRows, setUserBulkRows] = useState<UserImportRow[]>([]);
  const [userBulkSkippedEmpty, setUserBulkSkippedEmpty] = useState(0);
  const [userBulkVerifyOk, setUserBulkVerifyOk] = useState(false);
  const [userBulkVerifyErrors, setUserBulkVerifyErrors] = useState<string[]>([]);
  const [userBulkRowErrors, setUserBulkRowErrors] = useState<string[]>([]);
  const [userBulkBusy, setUserBulkBusy] = useState(false);
  const [userDirectorySearch, setUserDirectorySearch] = useState('');
  const [userDepartmentFilter, setUserDepartmentFilter] = useState('');
  const [userActiveTab, setUserActiveTab] = useState<'Active' | 'Inactive'>('Active');

  // Roles
  const [selectedRoleId, setSelectedRoleId] = useState<number | ''>('');
  const [roleForm, setRoleForm] = useState({
    userRoleID: 0,
    userRoleName: '',
    roleDescription: '',
    isActive: true,
  });
  const [selectedServiceCategoryId, setSelectedServiceCategoryId] = useState<number | ''>('');
  const [scForm, setScForm] = useState({
    serviceCategoryID: 0,
    categoryName: '',
    serviceCategoryDescription: '',
    isActive: true,
  });
  const [selectedServiceGroupId, setSelectedServiceGroupId] = useState<number | ''>('');
  const [sgForm, setSgForm] = useState({
    serviceGroupID: 0,
    serviceGroupNumber: '',
    serviceGroupName: '',
    serviceGroupDescription: '',
    isActive: true,
  });
  const [sgFormBusy, setSgFormBusy] = useState(false);

  // Groups (UserGroup tab)
  const [groupSearchInput, setGroupSearchInput] = useState('');
  const [groupSearchResults, setGroupSearchResults] = useState<UserGroupListRow[]>([]);
  const [groupSearchNoMatch, setGroupSearchNoMatch] = useState(false);
  const [groupListUseSearchResults, setGroupListUseSearchResults] = useState(false);
  const [selectedUserGroupId, setSelectedUserGroupId] = useState<number | ''>('');
  const [groupEditUgid, setGroupEditUgid] = useState(0);
  const [groupEditNumber, setGroupEditNumber] = useState('');
  const [groupEditName, setGroupEditName] = useState('');
  const [loadedGroupMembers, setLoadedGroupMembers] = useState<AdminGroupMember[]>([]);
  const [pendingGroupMembers, setPendingGroupMembers] = useState<PendingGroupMember[]>([]);
  const [selectedMemberRowKey, setSelectedMemberRowKey] = useState('');
  const [addMemberUserId, setAddMemberUserId] = useState<number | ''>('');
  const [addMemberRoleId, setAddMemberRoleId] = useState<number | ''>('');
  const [newGroupUserFilterDraft, setNewGroupUserFilterDraft] = useState('');
  const [newGroupRoleFilterDraft, setNewGroupRoleFilterDraft] = useState('');
  const [newGroupRoleFilterQuery, setNewGroupRoleFilterQuery] = useState('');
  const [isCreatingGroup, setIsCreatingGroup] = useState(false);
  const [groupPanelBusy, setGroupPanelBusy] = useState(false);
  const prefilledGroupFromQueryRef = useRef(false);

  // Project types
  const [selectedPtId, setSelectedPtId] = useState<number | ''>('');
  const [ptForm, setPtForm] = useState({ projectTypeID: 0, projectTypeCode: '', projectTypeDescription: '' });

  // Business Units
  const [selectedSegId, setSelectedSegId] = useState<number | ''>('');
  const [segForm, setSegForm] = useState({ businessUnitID: 0, businessUnitName: '', businessUnitDescription: '' });

  const [selectedBcId, setSelectedBcId] = useState<number | ''>('');
  const [bcForm, setBcForm] = useState({ businessClassID: 0, businessUnitID: 0, businessClassName: '', businessClassDescription: '' });

  // Client Types (tClientType)
  const [selectedCtId, setSelectedCtId] = useState<number | ''>('');
  const [ctForm, setCtForm] = useState({ clientTypeID: 0, ctName: '', ctDescription: '', isActive: true });

  // Services (tService) - CMS_PhaseI_All: ServiceCategoryID FK + N:N tags via tServiceTag
  const [selectedServiceId, setSelectedServiceId] = useState<number | ''>('');
  const [selectedServiceIds, setSelectedServiceIds] = useState<number[]>([]);
  const [serviceForm, setServiceForm] = useState<ServiceFormState>(() => emptyServiceForm());

  const [serviceBulkWizardOpen, setServiceBulkWizardOpen] = useState(false);
  const [serviceBulkRows, setServiceBulkRows] = useState<ServiceImportRow[]>([]);
  const [serviceBulkSkippedEmpty, setServiceBulkSkippedEmpty] = useState(0);
  const [serviceBulkVerifyOk, setServiceBulkVerifyOk] = useState(false);
  const [serviceBulkVerifyErrors, setServiceBulkVerifyErrors] = useState<string[]>([]);
  const [serviceBulkRowErrors, setServiceBulkRowErrors] = useState<string[]>([]);
  const [serviceBulkBusy, setServiceBulkBusy] = useState(false);
  const [serviceDirectorySearch, setServiceDirectorySearch] = useState('');
  const [serviceGroupFilter, setServiceGroupFilter] = useState<number | ''>('');
  const [serviceCategoryFilter, setServiceCategoryFilter] = useState<number | ''>('');

  // Clients (tClient + tClientRef)
  const [selectedClientId, setSelectedClientId] = useState<number | ''>('');
  const [clientForm, setClientForm] = useState({
    clientID: 0,
    clientName: '',
    clientShortName: '',
    clientDescription: '',
    businessUnitID: 0,
    businessClassID: 0,
    clientTypeID: 0,
    accountExecutiveUserID: 0,
    analyticsLeadUserID: 0,
    clientStatus: 'Active' as ClientStatus,
  });
  const [clientBuFilter, setClientBuFilter] = useState<number | ''>('');
  const [clientBcFilter, setClientBcFilter] = useState<number | ''>('');
  const [clientActiveTab, setClientActiveTab] = useState<'Active' | 'Inactive'>('Active');
  const [clientSearch, setClientSearch] = useState('');

  const [clientBulkWizardOpen, setClientBulkWizardOpen] = useState(false);
  const [clientBulkRows, setClientBulkRows] = useState<ClientImportRow[]>([]);
  const [clientBulkSkippedEmpty, setClientBulkSkippedEmpty] = useState(0);
  const [clientBulkVerifyOk, setClientBulkVerifyOk] = useState(false);
  const [clientBulkVerifyErrors, setClientBulkVerifyErrors] = useState<string[]>([]);
  const [clientBulkRowErrors, setClientBulkRowErrors] = useState<string[]>([]);
  const [clientBulkBusy, setClientBulkBusy] = useState(false);

  useEffect(() => {
    // v3 schema has no tStatus; user/role forms no longer bind to status picklists.
  }, [admin]);

  const handleServerSave = async (e: FormEvent) => {
    e.preventDefault();
    try {
      await apiPostJson('/api/admin/server/save', serverDraft);
      flash('success', 'Server settings were tested, encrypted, and saved.');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const handleServerTest = async () => {
    try {
      const result = await apiPostJson<{ succeeded: boolean; message: string }>(
        '/api/admin/server/test',
        serverDraft,
      );
      flash(result.succeeded ? 'success' : 'error', result.message);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Test failed.');
    }
  };

  const handleCatAiSave = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const saved = await apiPutJson<{
        apiKey?: string;
        apiKeyConfigured?: boolean;
        model?: string;
        baseUrl?: string;
        deployment?: string;
        apiVersion?: string;
        systemPrompt?: string;
        temperature?: number;
        maxTokens?: number;
        maxToolRounds?: number;
      }>('/api/admin/catai-settings', {
        apiKey: catAiKeyReplace.trim() || undefined,
        model: catAiDraft.model,
        baseUrl: catAiDraft.baseUrl,
        deployment: catAiDraft.deployment,
        apiVersion: catAiDraft.apiVersion,
        systemPrompt: catAiDraft.systemPrompt,
        temperature: catAiDraft.temperature,
        maxTokens: catAiDraft.maxTokens,
        maxToolRounds: catAiDraft.maxToolRounds,
      });
      setCatAiDraft({
        apiKey: saved.apiKey ?? '',
        apiKeyConfigured: saved.apiKeyConfigured === true,
        model: saved.model ?? catAiDraft.model,
        baseUrl: saved.baseUrl ?? catAiDraft.baseUrl,
        deployment: saved.deployment ?? catAiDraft.deployment,
        apiVersion: saved.apiVersion ?? catAiDraft.apiVersion,
        systemPrompt: saved.systemPrompt ?? catAiDraft.systemPrompt,
        temperature: typeof saved.temperature === 'number' ? saved.temperature : catAiDraft.temperature,
        maxTokens: typeof saved.maxTokens === 'number' ? saved.maxTokens : catAiDraft.maxTokens,
        maxToolRounds: typeof saved.maxToolRounds === 'number' ? saved.maxToolRounds : catAiDraft.maxToolRounds,
      });
      setCatAiKeyReplace('');
      flash('success', 'CatAI settings were encrypted and saved.');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'CatAI save failed.');
    }
  };

  const handleCatAiTest = async () => {
    try {
      const result = await apiPostJson<{ succeeded: boolean; message: string }>(
        '/api/admin/catai-settings/test',
        {},
      );
      flash(result.succeeded ? 'success' : 'error', result.message);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'CatAI test failed.');
    }
  };

  const handleCorrespondenceSave = async (e: FormEvent) => {
    e.preventDefault();
    setCorrespondenceBusy(true);
    try {
      const saved = await apiPutJson<{
        enabled?: boolean;
        sequence?: string;
        dayOfWeek?: string;
        sendTimeLocal?: string;
        timeZoneId?: string;
        recipientType?: string;
        communicationType?: string;
        ccAddress?: string;
        recipientOptions?: CorrespondenceOption[];
        sequenceOptions?: CorrespondenceOption[];
        communicationOptions?: CorrespondenceOption[];
        dayOfWeekOptions?: string[];
      }>('/api/admin/correspondence-settings', {
        enabled: correspondenceDraft.enabled,
        sequence: correspondenceDraft.sequence,
        dayOfWeek: correspondenceDraft.dayOfWeek,
        sendTimeLocal: correspondenceDraft.sendTimeLocal,
        timeZoneId: correspondenceDraft.timeZoneId,
        recipientType: correspondenceDraft.recipientType,
        communicationType: correspondenceDraft.communicationType,
        ccAddress: correspondenceDraft.ccAddress,
      });
      setCorrespondenceDraft((prev) => ({
        ...prev,
        enabled: saved.enabled !== false,
        sequence: saved.sequence ?? prev.sequence,
        dayOfWeek: saved.dayOfWeek ?? prev.dayOfWeek,
        sendTimeLocal: saved.sendTimeLocal ?? prev.sendTimeLocal,
        timeZoneId: saved.timeZoneId ?? prev.timeZoneId,
        recipientType: saved.recipientType ?? prev.recipientType,
        communicationType: saved.communicationType ?? prev.communicationType,
        ccAddress: saved.ccAddress ?? prev.ccAddress,
        recipientOptions:
          saved.recipientOptions && saved.recipientOptions.length > 0
            ? saved.recipientOptions
            : prev.recipientOptions,
        sequenceOptions:
          saved.sequenceOptions && saved.sequenceOptions.length > 0
            ? saved.sequenceOptions
            : prev.sequenceOptions,
        communicationOptions:
          saved.communicationOptions && saved.communicationOptions.length > 0
            ? saved.communicationOptions
            : prev.communicationOptions,
        dayOfWeekOptions:
          saved.dayOfWeekOptions && saved.dayOfWeekOptions.length > 0
            ? saved.dayOfWeekOptions
            : prev.dayOfWeekOptions,
      }));
      flash('success', 'Correspondence schedule was saved.');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Correspondence save failed.');
    } finally {
      setCorrespondenceBusy(false);
    }
  };

  const clearUserForm = () => {
    setUserForm({
      userID: 0,
      firstName: '',
      lastName: '',
      title: '',
      department: '',
      email: '',
      phone: '',
      company: 'BMS',
      userType: 'Basic',
      address: '',
      addressType: 'Work',
      city: '',
      state: '',
      postalCode: '',
      country: '',
      statusID: 0,
    });
    setUserPhotoRevision((x) => x + 1);
    setUserPhotoBroken(true);
  };

  // Populates the right-hand form from vUser-backed list fields (tUser) plus address lines
  // from tUserAddress. The admin index may embed primaryAddress (OUTER APPLY in the repo);
  // we always re-query GET /api/admin/users/{id}/addresses -> usp_User_ListAddresses so the
  // form matches the database even if the bulk payload omitted or shaped address data differently.
  const applyUserToForm = async (u: AdminUser) => {
    if (!admin) {
      return;
    }

    let addr: AdminUserAddress | null =
      coerceAdminUserAddress(u.primaryAddress) ??
      (u.addresses?.length ? coerceAdminUserAddress(u.addresses[0]) : null);

    if (u.userID > 0) {
      try {
        const raw = await apiGet<unknown>(`/api/admin/users/${u.userID}/addresses`);
        const fetched = parseAddressListPayload(raw);
        const picked = pickPreferredUserAddress(fetched);
        if (picked) {
          addr = picked;
        }
      } catch (e) {
        flash('error', e instanceof ApiError ? e.message : 'Could not load user addresses from the server.');
      }
    }

    setUserForm({
      userID: u.userID,
      firstName: u.firstName ?? '',
      lastName: u.lastName ?? '',
      title: u.title ?? '',
      department: u.department ?? '',
      email: u.email ?? '',
      phone: u.phone ?? '',
      company: u.company ?? 'BMS',
      userType: normalizeUserType(u.userType),
      address: addr?.address1 ?? '',
      addressType: addr?.addressType ?? 'Work',
      city: addr?.city ?? '',
      state: addr?.state ?? '',
      postalCode: addr?.postalCode ?? '',
      country: addr?.country ?? '',
      statusID: 0,
    });
    setUserPhotoRevision((x) => x + 1);
    setUserPhotoBroken(true);
  };

  const handleUserPhotoFileChange = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file || userForm.userID <= 0) {
      return;
    }

    const fd = new FormData();
    fd.append('file', file);
    try {
      await apiPostFormData<{ message: string }>(`/api/admin/users/${userForm.userID}/photo`, fd);
      flash('success', 'Photo uploaded.');
      setUserPhotoRevision((x) => x + 1);
      setUserPhotoBroken(true);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Photo upload failed.');
    }
  };

  const handleRemoveUserPhoto = async () => {
    if (userForm.userID <= 0) {
      return;
    }

    try {
      await apiDelete(`/api/admin/users/${userForm.userID}/photo`);
      flash('success', 'Photo removed.');
      setUserPhotoRevision((x) => x + 1);
      setUserPhotoBroken(true);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not remove photo.');
    }
  };

  const editUserFromSelection = () => {
    if (selectedUserId === '' || !admin) {
      window.alert('Select a user first.');
      return;
    }
    const u = admin.users.find((x) => x.userID === selectedUserId);
    if (!u) {
      return;
    }
    void applyUserToForm(u);
  };

  const submitUser = async (e: FormEvent) => {
    e.preventDefault();
    try {
      const result = await apiPostJson<{ message: string; userId?: number }>('/api/admin/users/save', {
        userID: userForm.userID,
        firstName: userForm.firstName,
        lastName: userForm.lastName,
        title: userForm.title,
        department: userForm.department,
        email: userForm.email,
        phone: userForm.phone,
        company: userForm.company,
        userType: userForm.userType,
      });
      const savedUserId = result.userId ?? userForm.userID;
      setUserForm((f) => ({ ...f, userID: savedUserId }));

      const hasAddress =
        (userForm.address ?? '').trim() !== '' ||
        (userForm.city ?? '').trim() !== '' ||
        (userForm.state ?? '').trim() !== '' ||
        (userForm.postalCode ?? '').trim() !== '' ||
        (userForm.country ?? '').trim() !== '';
      if (hasAddress && savedUserId > 0) {
        try {
          await apiPostJson('/api/admin/users/addresses/save', {
            addressID: 0,
            userID: savedUserId,
            addressType: userForm.addressType || 'Work',
            address1: userForm.address,
            city: userForm.city,
            state: userForm.state,
            postalCode: userForm.postalCode,
            country: userForm.country,
          });
        } catch (addrErr) {
          flash('error', addrErr instanceof ApiError ? addrErr.message : 'Saved user, but address save failed.');
        }
      }

      flash('success', result.message);
      setUserPhotoRevision((x) => x + 1);
      setUserPhotoBroken(true);
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const openRemoveUserDialog = () => {
    if (selectedUserId === '') {
      window.alert('Select a user to remove.');
      return;
    }
    setUserRemoveDialogOpen(true);
  };

  const closeRemoveUserDialog = () => setUserRemoveDialogOpen(false);

  const confirmDeactivateUser = async () => {
    if (selectedUserId === '') {
      return;
    }
    const userID = selectedUserId;
    setUserRemoveDialogOpen(false);
    try {
      await apiPostJson(`/api/admin/users/${userID}/soft-delete`, {});
      flash('success', 'User removed from the active list.');
      if (userForm.userID === userID) {
        clearUserForm();
      }
      setSelectedUserId('');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Deactivate failed.');
    }
  };

  const confirmDeleteUserFromDb = async () => {
    if (selectedUserId === '') {
      return;
    }
    if (
      !window.confirm(
        'Permanently delete this user from the database? This cannot be undone.',
      )
    ) {
      return;
    }
    const userID = selectedUserId;
    setUserRemoveDialogOpen(false);
    try {
      await apiPostJson(`/api/admin/users/${userID}/soft-delete`, {});
      flash('success', 'User deleted from the database.');
      if (userForm.userID === userID) {
        clearUserForm();
      }
      setSelectedUserId('');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Delete failed.');
    }
  };

  const copyUser = async (e?: FormEvent) => {
    e?.preventDefault();
    if (selectedUserId === '') {
      window.alert('Select a user to copy.');
      return;
    }
    try {
      // Copy is not supported in the CMS_PhaseI_All schema; fall back to creating a new user.
      flash('error', 'User Copy is not available in this schema. Create a new user instead.');
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Copy failed.');
    }
  };

  const resetUserBulkWizard = () => {
    setUserBulkWizardOpen(false);
    setUserBulkRows([]);
    setUserBulkSkippedEmpty(0);
    setUserBulkVerifyOk(false);
    setUserBulkVerifyErrors([]);
    setUserBulkRowErrors([]);
    setUserBulkBusy(false);
  };

  const handleUserBulkImportFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) {
      return;
    }
    const formData = new FormData();
    formData.append('file', file);
    setUserBulkBusy(true);
    setUserBulkVerifyOk(false);
    setUserBulkVerifyErrors([]);
    setUserBulkRowErrors([]);
    try {
      const result = await apiPostFormData<UserBulkParseResult>('/api/admin/users/bulk-import/parse', formData);
      setUserBulkRows(result.rows);
      setUserBulkSkippedEmpty(result.skippedEmptyRows);
      setUserBulkWizardOpen(true);
      setUsersSubTab('users');
      setTab('users');
      if (result.rows.length === 0) {
        flash('error', 'No importable rows were found (every data row was missing Email).');
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not read the CSV file.');
    } finally {
      setUserBulkBusy(false);
    }
  };

  const handleUserBulkVerify = async () => {
    if (userBulkRows.length === 0) {
      return;
    }
    setUserBulkBusy(true);
    setUserBulkVerifyOk(false);
    setUserBulkVerifyErrors([]);
    setUserBulkRowErrors([]);
    try {
      const result = await apiPostJson<UserBulkVerifyResult>('/api/admin/users/bulk-import/verify', {
        rows: userBulkRows,
      });
      setUserBulkVerifyErrors(result.errors ?? []);
      setUserBulkVerifyOk(!!result.valid);
      if (result.valid) {
        flash('success', 'Verification passed. You can import these rows via usp_User_CreateWithPrimaryAddress.');
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Verification request failed.');
    } finally {
      setUserBulkBusy(false);
    }
  };

  const handleUserBulkCommit = async () => {
    if (!userBulkVerifyOk || userBulkRows.length === 0) {
      return;
    }
    setUserBulkBusy(true);
    try {
      const result = await apiPostJson<UserBulkCommitResult>('/api/admin/users/bulk-import/commit', {
        rows: userBulkRows,
      });
      const errs = result.rowErrors ?? [];
      setUserBulkRowErrors(errs);
      let msg = `Imported ${result.importedCount} user(s).`;
      if (result.skippedEmptyRows) {
        msg += ` Skipped ${result.skippedEmptyRows} empty row(s).`;
      }
      if (errs.length > 0) {
        msg += ` ${errs.length} row(s) reported errors (see preview).`;
        flash('error', msg);
      } else {
        flash('success', msg);
        resetUserBulkWizard();
      }
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Import failed.');
    } finally {
      setUserBulkBusy(false);
    }
  };

  const clearClientForm = () =>
    setClientForm({
      clientID: 0,
      clientName: '',
      clientShortName: '',
      clientDescription: '',
      businessUnitID: 0,
      businessClassID: 0,
      clientTypeID: 0,
      accountExecutiveUserID: 0,
      analyticsLeadUserID: 0,
      clientStatus: 'Active',
    });

  const applyClientToForm = (c: AdminClient) => {
    setClientForm({
      clientID: c.clientID,
      clientName: c.clientName ?? '',
      clientShortName: c.clientShortName ?? '',
      clientDescription: c.clientDescription ?? '',
      businessUnitID: c.businessUnitID ?? 0,
      businessClassID: c.businessClassID ?? 0,
      clientTypeID: c.clientTypeID ?? 0,
      accountExecutiveUserID: c.accountExecutiveUserID ?? 0,
      analyticsLeadUserID: c.analyticsLeadUserID ?? 0,
      clientStatus: clientStatusOf(c),
    });
  };

  const editClientFromSelection = (preset?: AdminClient) => {
    if (!admin) {
      return;
    }
    const c = preset ?? admin.clients.find((x) => x.clientID === selectedClientId);
    if (!c) {
      window.alert('Select a client first.');
      return;
    }
    applyClientToForm(c);
  };

  const submitClient = async (e: FormEvent) => {
    e.preventDefault();
    if (!clientForm.clientName.trim()) {
      flash('error', 'Client Name is required.');
      return;
    }
    try {
      const result = await apiPostJson<{ clientId: number; message: string }>('/api/admin/clients/save', {
        clientID: clientForm.clientID,
        clientName: clientForm.clientName,
        clientShortName: clientForm.clientShortName || null,
        clientDescription: clientForm.clientDescription || null,
        clientStatus: clientForm.clientStatus,
        accountExecutiveUserID: clientForm.accountExecutiveUserID || null,
        analyticsLeadUserID: clientForm.analyticsLeadUserID || null,
        businessUnitID: clientForm.businessUnitID || null,
        businessClassID: clientForm.businessClassID || null,
        clientTypeID: clientForm.clientTypeID || null,
      });
      flash('success', result.message);
      setClientForm((f) => ({ ...f, clientID: result.clientId }));
      setSelectedClientId(result.clientId);
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deleteClient = async (clientId?: number) => {
    const targetId = clientId ?? (selectedClientId === '' ? null : selectedClientId);
    if (targetId == null) {
      window.alert('Select a client to delete.');
      return;
    }
    setClientContextMenu(null);
    const selected = admin?.clients.find((x) => x.clientID === targetId);
    const name = (selected?.clientName ?? '').trim() || `Client ${targetId}`;
    if (!window.confirm(`Permanently delete "${name}" from the database?\n\nThis removes the client and related client references. This cannot be undone.`)) {
      return;
    }
    try {
      await apiPostJson(`/api/admin/clients/${targetId}/delete`, {});
      flash('success', 'Client deleted.');
      if (clientForm.clientID === targetId) {
        clearClientForm();
      }
      setSelectedClientId('');
      await reload();
    } catch (err) {
      const message = err instanceof ApiError ? err.message : 'Delete failed.';
      if (/active projects/i.test(message) || /cannot be deleted/i.test(message)) {
        setClientDeleteBlockedMessage(
          message ||
            'This client cannot be deleted because it is associated with active projects. Please remove the project before deleting the client.',
        );
        return;
      }
      flash('error', message);
    }
  };

  const resetClientBulkWizard = () => {
    setClientBulkWizardOpen(false);
    setClientBulkRows([]);
    setClientBulkSkippedEmpty(0);
    setClientBulkVerifyOk(false);
    setClientBulkVerifyErrors([]);
    setClientBulkRowErrors([]);
    setClientBulkBusy(false);
  };

  const handleClientBulkImportFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) {
      return;
    }
    const formData = new FormData();
    formData.append('file', file);
    setClientBulkBusy(true);
    setClientBulkVerifyOk(false);
    setClientBulkVerifyErrors([]);
    setClientBulkRowErrors([]);
    try {
      const result = await apiPostFormData<ClientBulkParseResult>('/api/admin/clients/bulk-import/parse', formData);
      setClientBulkRows(result.rows);
      setClientBulkSkippedEmpty(result.skippedEmptyRows);
      setClientBulkWizardOpen(true);
      setTab('clients');
      if (result.rows.length === 0) {
        flash('error', 'No importable rows were found (every data row was missing ClientName).');
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not read the CSV file.');
    } finally {
      setClientBulkBusy(false);
    }
  };

  const handleClientBulkVerify = async () => {
    if (clientBulkRows.length === 0) {
      return;
    }
    setClientBulkBusy(true);
    setClientBulkVerifyOk(false);
    setClientBulkVerifyErrors([]);
    setClientBulkRowErrors([]);
    try {
      const result = await apiPostJson<ClientBulkVerifyResult>('/api/admin/clients/bulk-import/verify', {
        rows: clientBulkRows,
      });
      setClientBulkVerifyErrors(result.errors ?? []);
      setClientBulkVerifyOk(!!result.valid);
      if (result.valid) {
        flash('success', 'Verification passed. You can import these rows into tClient / tClientRef.');
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Verification request failed.');
    } finally {
      setClientBulkBusy(false);
    }
  };

  const handleClientBulkCommit = async () => {
    if (!clientBulkVerifyOk || clientBulkRows.length === 0) {
      return;
    }
    setClientBulkBusy(true);
    try {
      const result = await apiPostJson<ClientBulkCommitResult>('/api/admin/clients/bulk-import/commit', {
        rows: clientBulkRows,
      });
      const errs = result.rowErrors ?? [];
      setClientBulkRowErrors(errs);
      let msg = `Imported ${result.importedCount} client(s).`;
      if (errs.length > 0) {
        msg += ` ${errs.length} row(s) reported errors (see preview).`;
        flash('error', msg);
      } else {
        flash('success', msg);
        resetClientBulkWizard();
      }
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Import failed.');
    } finally {
      setClientBulkBusy(false);
    }
  };

  const clearRoleForm = () =>
    setRoleForm({ userRoleID: 0, userRoleName: '', roleDescription: '', isActive: true });

  const editRoleFromSelection = () => {
    if (selectedRoleId === '' || !admin) {
      window.alert('Select a user role first.');
      return;
    }
    const r = admin.userRoles.find((x) => x.userRoleID === selectedRoleId);
    if (!r) {
      return;
    }
    setRoleForm({
      userRoleID: r.userRoleID,
      userRoleName: r.userRoleName ?? '',
      roleDescription: r.roleDescription ?? '',
      isActive: r.isActive !== false,
    });
  };

  const saveRole = async (e: FormEvent) => {
    e.preventDefault();
    try {
      await apiPostJson('/api/admin/roles/save', {
        userRoleID: roleForm.userRoleID,
        userRoleName: roleForm.userRoleName,
        roleDescription: roleForm.roleDescription,
        isActive: roleForm.isActive,
      });
      flash('success', 'User role saved.');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deleteRole = async (e: FormEvent) => {
    e.preventDefault();
    if (selectedRoleId === '') {
      window.alert('Select a user role to remove.');
      return;
    }
    if (!window.confirm('Remove or deactivate the selected role?')) {
      return;
    }
    try {
      const existingRole = admin?.userRoles.find((r) => r.userRoleID === selectedRoleId);
      if (!existingRole) {
        flash('error', 'Selected user role not found.');
        return;
      }
      await apiPostJson('/api/admin/roles/save', {
        ...existingRole,
        isActive: false,
      });
      flash('success', 'User role deactivated.');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Remove failed.');
    }
  };

  const clearScForm = () =>
    setScForm({ serviceCategoryID: 0, categoryName: '', serviceCategoryDescription: '', isActive: true });

  const editScFromSelection = () => {
    if (selectedServiceCategoryId === '' || !admin) {
      window.alert('Select a service category first.');
      return;
    }
    const c = admin.serviceCategories.find((x) => x.serviceCategoryID === selectedServiceCategoryId);
    if (!c) {
      return;
    }
    setScForm({
      serviceCategoryID: c.serviceCategoryID,
      categoryName: c.categoryName ?? '',
      serviceCategoryDescription: c.serviceCategoryDescription ?? '',
      isActive: c.isActive !== false,
    });
  };

  const saveSc = async (e: FormEvent) => {
    e.preventDefault();
    if (!scForm.categoryName.trim()) {
      window.alert('Enter a category name before saving.');
      return;
    }
    try {
      await apiPostJson('/api/admin/service-categories/save', {
        serviceCategoryID: scForm.serviceCategoryID,
        categoryName: scForm.categoryName.trim(),
        serviceCategoryDescription: scForm.serviceCategoryDescription.trim() || null,
        isActive: scForm.isActive,
      });
      if (scForm.serviceCategoryID > 0) {
        const original = admin?.serviceCategories.find((x) => x.serviceCategoryID === scForm.serviceCategoryID);
        if (original && (original.isActive ?? true) !== scForm.isActive) {
          await apiPostJson(
            `/api/admin/service-categories/${scForm.serviceCategoryID}/active?isActive=${scForm.isActive ? 'true' : 'false'}`,
            {},
          );
        }
      }
      flash('success', 'Service category saved.');
      clearScForm();
      setSelectedServiceCategoryId('');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deleteSc = async (e: FormEvent) => {
    e.preventDefault();
    if (selectedServiceCategoryId === '') {
      window.alert('Select a service category to remove.');
      return;
    }
    if (!window.confirm('Remove or deactivate the selected service category?')) {
      return;
    }
    try {
      await apiPostJson(`/api/admin/service-categories/${selectedServiceCategoryId}/active?isActive=false`, {});
      flash('success', 'Service category deactivated.');
      clearScForm();
      setSelectedServiceCategoryId('');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Remove failed.');
    }
  };

  const nextServiceGroupNumber = () => {
    const nums = (admin?.serviceGroups ?? [])
      .map((g) => Number(String(g.serviceGroupNumber ?? '').trim()))
      .filter((n) => Number.isFinite(n) && n > 0);
    return String((nums.length ? Math.max(...nums) : 0) + 1);
  };

  const clearSgForm = () =>
    setSgForm({
      serviceGroupID: 0,
      serviceGroupNumber: '',
      serviceGroupName: '',
      serviceGroupDescription: '',
      isActive: true,
    });

  const startNewServiceGroup = () => {
    setSelectedServiceGroupId('');
    setSgForm({
      serviceGroupID: 0,
      serviceGroupNumber: nextServiceGroupNumber(),
      serviceGroupName: '',
      serviceGroupDescription: '',
      isActive: true,
    });
  };

  const editSgFromSelection = () => {
    if (selectedServiceGroupId === '' || !admin) {
      window.alert('Select a service group first.');
      return;
    }
    const g = (admin.serviceGroups ?? []).find((x) => x.serviceGroupID === selectedServiceGroupId);
    if (!g) {
      return;
    }
    setSgForm({
      serviceGroupID: g.serviceGroupID,
      serviceGroupNumber: g.serviceGroupNumber ?? '',
      serviceGroupName: g.serviceGroupName ?? '',
      serviceGroupDescription: g.serviceGroupDescription ?? '',
      isActive: g.isActive !== false,
    });
  };

  const saveSg = async (e: FormEvent) => {
    e.preventDefault();
    if (!sgForm.serviceGroupName.trim()) {
      window.alert('Enter a service group name before saving.');
      return;
    }
    setSgFormBusy(true);
    try {
      const result = await apiPostJson<{ serviceGroupID?: number; message?: string }>(
        '/api/admin/service-groups/save',
        {
          serviceGroupID: sgForm.serviceGroupID,
          serviceGroupNumber: sgForm.serviceGroupNumber.trim(),
          serviceGroupName: sgForm.serviceGroupName.trim(),
          serviceGroupDescription: sgForm.serviceGroupDescription.trim() || null,
          isActive: sgForm.isActive,
        },
      );
      flash('success', result.message ?? 'Service group saved.');
      const savedId = result.serviceGroupID ?? sgForm.serviceGroupID;
      clearSgForm();
      setSelectedServiceGroupId(savedId > 0 ? savedId : '');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    } finally {
      setSgFormBusy(false);
    }
  };

  const deleteSg = async (e: FormEvent) => {
    e.preventDefault();
    if (selectedServiceGroupId === '' || !admin) {
      window.alert('Select a service group to remove.');
      return;
    }
    const g = (admin.serviceGroups ?? []).find((x) => x.serviceGroupID === selectedServiceGroupId);
    if (!g) {
      flash('error', 'Selected service group not found.');
      return;
    }
    if (!window.confirm('Remove or deactivate the selected service group?')) {
      return;
    }
    setSgFormBusy(true);
    try {
      await apiPostJson('/api/admin/service-groups/save', {
        serviceGroupID: g.serviceGroupID,
        serviceGroupNumber: g.serviceGroupNumber ?? '',
        serviceGroupName: g.serviceGroupName ?? '',
        serviceGroupDescription: g.serviceGroupDescription ?? null,
        isActive: false,
      });
      flash('success', 'Service group deactivated.');
      clearSgForm();
      setSelectedServiceGroupId('');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Remove failed.');
    } finally {
      setSgFormBusy(false);
    }
  };

  const userPickerLabel = (u: AdminUser) => {
    const base = (u.username?.trim() || u.email?.trim() || u.displayName || `User ${u.userID}`).trim();
    const t = u.title?.trim();
    return t ? `${base} - ${t}` : base;
  };

  // Used by the UserGroup -> "Find user" listbox, which should show only the user's first + last name.
  const userFirstLastLabel = (u: AdminUser) => {
    const fn = (u.firstName ?? '').trim();
    const ln = (u.lastName ?? '').trim();
    const combined = [fn, ln].filter((x) => x !== '').join(' ');
    return combined !== '' ? combined : userPickerLabel(u);
  };

  const rolePickerLabel = (r: AdminRole) =>
    (r.userRoleName?.trim() || `Role ${r.userRoleID}`).trim();

  const distinctUserDepartments = useMemo(() => {
    if (!admin) {
      return [];
    }

    const byLower = new Map<string, string>();
    for (const u of admin.users) {
      const d = (u.department ?? '').trim();
      if (d === '') {
        continue;
      }

      const key = d.toLowerCase();
      if (!byLower.has(key)) {
        byLower.set(key, d);
      }
    }

    return [...byLower.values()].sort((a, b) =>
      a.localeCompare(b, undefined, { sensitivity: 'base' }),
    );
  }, [admin]);

  // Department options for the user form's dropdown. Includes the record's current department
  // even if no other user uses it, so editing never silently drops an existing value.
  const userFormDepartmentOptions = useMemo(() => {
    const current = userForm.department.trim();
    if (current === '' || distinctUserDepartments.some((d) => d.toLowerCase() === current.toLowerCase())) {
      return distinctUserDepartments;
    }
    return [current, ...distinctUserDepartments];
  }, [distinctUserDepartments, userForm.department]);

  // Stable color index per department (by sorted position) shared by chips + row badges.
  const departmentColorIndex = useMemo(() => {
    const map = new Map<string, number>();
    distinctUserDepartments.forEach((d, i) => map.set(d.toLowerCase(), i));
    return map;
  }, [distinctUserDepartments]);

  // Count of users grouped by department, ordered like the filter list, with a trailing
  // "No Department" bucket when applicable.
  const userDepartmentCounts = useMemo(() => {
    if (!admin) {
      return [] as { key: string; label: string; count: number; colorIndex: number }[];
    }
    const counts = new Map<string, number>();
    let noDept = 0;
    for (const u of admin.users) {
      const d = (u.department ?? '').trim();
      if (d === '') {
        noDept += 1;
        continue;
      }
      const k = d.toLowerCase();
      counts.set(k, (counts.get(k) ?? 0) + 1);
    }
    const list = distinctUserDepartments.map((d, i) => ({
      key: d,
      label: d,
      count: counts.get(d.toLowerCase()) ?? 0,
      colorIndex: i,
    }));
    if (noDept > 0) {
      list.push({ key: '__none__', label: 'No Department', count: noDept, colorIndex: -1 });
    }
    return list;
  }, [admin, distinctUserDepartments]);

  const filteredServicesForDirectory = useMemo(() => {
    if (!admin) {
      return [] as CatalogServiceRow[];
    }
    let rows = admin.services;
    if (serviceGroupFilter !== '') {
      rows = rows.filter((s) => (s.serviceGroupID ?? 0) === serviceGroupFilter);
    }
    if (serviceCategoryFilter !== '') {
      rows = rows.filter((s) => (s.serviceCategoryID ?? 0) === serviceCategoryFilter);
    }
    const raw = serviceDirectorySearch.trim().toLowerCase();
    if (!raw) {
      return rows;
    }
    const tokens = raw.split(/\s+/).filter(Boolean);
    return rows.filter((s) => {
      const hay = [
        s.serviceName,
        s.serviceDescription,
        s.categoryName,
        s.serviceCategoryDescription,
        s.serviceGroupName,
        String(s.serviceID),
      ]
        .map((v) => (v ?? '').trim().toLowerCase())
        .join(' ');
      return tokens.every((t) => hay.includes(t));
    });
  }, [admin, serviceDirectorySearch, serviceGroupFilter, serviceCategoryFilter]);

  const serviceFormGroupOptions = useMemo(() => {
    const base = admin?.serviceGroups ?? [];
    if (serviceForm.serviceGroupID <= 0) {
      return base;
    }
    if (base.some((g) => g.serviceGroupID === serviceForm.serviceGroupID)) {
      return base;
    }
    const svc = admin?.services.find((s) => s.serviceID === serviceForm.serviceID);
    const name = (svc?.serviceGroupName ?? '').trim() || `Group ${serviceForm.serviceGroupID}`;
    return [...base, { serviceGroupID: serviceForm.serviceGroupID, serviceGroupName: name }];
  }, [admin, serviceForm.serviceGroupID, serviceForm.serviceID]);

  const serviceFormCategoryOptions = useMemo(() => {
    const base = admin?.serviceCategories ?? [];
    if (serviceForm.serviceCategoryID <= 0) {
      return base;
    }
    if (base.some((c) => c.serviceCategoryID === serviceForm.serviceCategoryID)) {
      return base;
    }
    const label =
      serviceForm.categoryName.trim() ||
      serviceForm.serviceCategoryDescription.trim() ||
      `Category ${serviceForm.serviceCategoryID}`;
    return [
      ...base,
      {
        serviceCategoryID: serviceForm.serviceCategoryID,
        categoryName: serviceForm.categoryName || label,
        functionID: serviceForm.functionID,
        serviceCategoryDescription: serviceForm.serviceCategoryDescription,
      },
    ];
  }, [
    admin,
    serviceForm.serviceCategoryID,
    serviceForm.categoryName,
    serviceForm.functionID,
    serviceForm.serviceCategoryDescription,
  ]);

  const serviceFormTagOptions = useMemo(() => {
    const base = admin?.tags ?? [];
    const missingIds = serviceForm.tagIDs.filter((id) => !base.some((t) => t.tagID === id));
    if (missingIds.length === 0) {
      return base;
    }
    const svc = admin?.services.find((s) => s.serviceID === serviceForm.serviceID);
    const extras = (svc?.tags ?? []).filter((t) => missingIds.includes(t.tagID));
    const stillMissing = missingIds.filter((id) => !extras.some((t) => t.tagID === id));
    const placeholders = stillMissing.map((id) => ({
      tagID: id,
      tagName: `Tag ${id}`,
      tagScope: '',
    }));
    return [...base, ...extras, ...placeholders];
  }, [admin, serviceForm.tagIDs, serviceForm.serviceID]);

  useEffect(() => {
    if (selectedServiceId === '' || !admin) {
      return;
    }
    if (!filteredServicesForDirectory.some((s) => s.serviceID === selectedServiceId)) {
      setSelectedServiceId('');
    }
  }, [filteredServicesForDirectory, selectedServiceId, admin]);

  const allFilteredAdminServicesSelected = useMemo(() => {
    if (filteredServicesForDirectory.length === 0) {
      return false;
    }
    return filteredServicesForDirectory.every((row) => selectedServiceIds.includes(row.serviceID));
  }, [filteredServicesForDirectory, selectedServiceIds]);

  const someFilteredAdminServicesSelected = useMemo(() => {
    if (filteredServicesForDirectory.length === 0) {
      return false;
    }
    const selectedCount = filteredServicesForDirectory.filter((row) =>
      selectedServiceIds.includes(row.serviceID),
    ).length;
    return selectedCount > 0 && selectedCount < filteredServicesForDirectory.length;
  }, [filteredServicesForDirectory, selectedServiceIds]);

  const handleAdminServiceToggle = (serviceID: number) => {
    setSelectedServiceIds((ids) =>
      ids.includes(serviceID) ? ids.filter((x) => x !== serviceID) : [...ids, serviceID],
    );
  };

  const handleToggleAllFilteredAdminServices = () => {
    if (filteredServicesForDirectory.length === 0) {
      return;
    }
    const filteredIds = filteredServicesForDirectory.map((row) => row.serviceID);
    if (allFilteredAdminServicesSelected) {
      setSelectedServiceIds((ids) => ids.filter((id) => !filteredIds.includes(id)));
      return;
    }
    setSelectedServiceIds((ids) => [...new Set([...ids, ...filteredIds])]);
  };

  const handleSelectAllAdminServices = () => {
    if (!admin?.services.length) {
      return;
    }
    setSelectedServiceIds((ids) => [
      ...new Set([...ids, ...admin.services.map((row) => row.serviceID)]),
    ]);
  };

  const handleClearAllAdminServices = () => {
    setSelectedServiceIds([]);
  };

  const filteredUsersForDirectory = useMemo(() => {
    if (!admin) {
      return [];
    }

    let rows = admin.users;
    // Active / Inactive tabs read from soft-delete flag (IsActive inverted as isDeleted).
    rows = rows.filter((u) => (userActiveTab === 'Active' ? u.isDeleted !== true : u.isDeleted === true));

    const deptSel = userDepartmentFilter.trim();
    if (deptSel !== '') {
      const want = deptSel.toLowerCase();
      rows = rows.filter((u) => (u.department ?? '').trim().toLowerCase() === want);
    }

    const raw = userDirectorySearch.trim().toLowerCase();
    if (raw) {
      const tokens = raw.split(/\s+/).filter(Boolean);
      rows = rows.filter((u) => {
        const first = (u.firstName ?? '').trim().toLowerCase();
        const last = (u.lastName ?? '').trim().toLowerCase();
        const combined = `${first} ${last}`.trim();
        const hay = [
          combined,
          first,
          last,
          (u.displayName ?? '').trim().toLowerCase(),
          (u.email ?? '').trim().toLowerCase(),
          String(u.userID),
        ].join(' ');
        return tokens.every((t) => hay.includes(t));
      });
    }

    // Order by department (blank departments last), then by first name.
    return [...rows].sort((a, b) => {
      const da = (a.department ?? '').trim();
      const db = (b.department ?? '').trim();
      if (da.toLowerCase() !== db.toLowerCase()) {
        if (da === '') return 1;
        if (db === '') return -1;
        return da.localeCompare(db, undefined, { sensitivity: 'base' });
      }
      const fa = (a.firstName ?? '').trim();
      const fb = (b.firstName ?? '').trim();
      return fa.localeCompare(fb, undefined, { sensitivity: 'base' });
    });
  }, [admin, userActiveTab, userDepartmentFilter, userDirectorySearch]);

  const userActiveCounts = useMemo(() => {
    const counts = { total: 0, active: 0, inactive: 0 };
    if (!admin) {
      return counts;
    }
    for (const u of admin.users) {
      counts.total += 1;
      if (u.isDeleted === true) {
        counts.inactive += 1;
      } else {
        counts.active += 1;
      }
    }
    return counts;
  }, [admin]);

  useEffect(() => {
    if (selectedUserId === '' || !admin) {
      return;
    }

    if (!filteredUsersForDirectory.some((u) => u.userID === selectedUserId)) {
      setSelectedUserId('');
    }
  }, [filteredUsersForDirectory, selectedUserId, admin]);

  // In this schema dbo.tBusinessClass is global (no BU column); the BU/BC/Type association lives on
  // dbo.tClientRef. So Business Class is NOT scoped to a Business Unit - show every class.
  const businessClassOptions = useMemo(() => admin?.businessClasses ?? [], [admin]);

  const clientStatusOptions = useMemo<ClientStatus[]>(() => {
    const fromDb = (admin?.statuses ?? [])
      .map((s) => (s.statusDescription ?? '').trim())
      .filter((d): d is ClientStatus => (CLIENT_STATUSES as string[]).includes(d));
    return fromDb.length > 0 ? fromDb : CLIENT_STATUSES;
  }, [admin]);

  const clientStatusCounts = useMemo(() => {
    const counts = { total: 0, active: 0, inactive: 0, prospect: 0, activeFlag: 0, inactiveFlag: 0 };
    if (!admin) {
      return counts;
    }
    for (const c of admin.clients) {
      counts.total += 1;
      const status = clientStatusOf(c);
      if (status === 'Active') counts.active += 1;
      else if (status === 'Inactive') counts.inactive += 1;
      else if (status === 'Prospect') counts.prospect += 1;
      // Tab counts read straight from dbo.tClient.IsActive.
      if (c.isActive === false) counts.inactiveFlag += 1;
      else counts.activeFlag += 1;
    }
    return counts;
  }, [admin]);

  const filteredClientsForDirectory = useMemo(() => {
    if (!admin) {
      return [];
    }

    let rows = admin.clients;
    // Active / Inactive tabs read directly from dbo.tClient.IsActive.
    rows = rows.filter((c) => (clientActiveTab === 'Active' ? c.isActive !== false : c.isActive === false));
    if (clientBuFilter !== '') {
      rows = rows.filter((c) => c.businessUnitID === clientBuFilter);
    }
    if (clientBcFilter !== '') {
      rows = rows.filter((c) => c.businessClassID === clientBcFilter);
    }

    const raw = clientSearch.trim().toLowerCase();
    if (!raw) {
      return rows;
    }

    const tokens = raw.split(/\s+/).filter(Boolean);
    return rows.filter((c) => {
      const hay = [
        c.clientName,
        c.clientShortName,
        c.businessUnitName,
        c.businessClassName,
        c.clientTypeName,
        c.accountExecutiveName,
        c.analyticsLeadName,
        String(c.clientID),
      ]
        .map((x) => (x ?? '').toString().toLowerCase())
        .join(' ');
      return tokens.every((t) => hay.includes(t));
    });
  }, [admin, clientActiveTab, clientBuFilter, clientBcFilter, clientSearch]);

  useEffect(() => {
    if (selectedClientId === '' || !admin) {
      return;
    }
    if (!filteredClientsForDirectory.some((c) => c.clientID === selectedClientId)) {
      setSelectedClientId('');
    }
  }, [filteredClientsForDirectory, selectedClientId, admin]);

  const displayedUserGroupRows = useMemo(() => {
    if (groupListUseSearchResults) {
      return groupSearchResults;
    }
    return admin?.userGroupSummaries ?? [];
  }, [groupListUseSearchResults, groupSearchResults, admin?.userGroupSummaries]);

  const filteredBmsUsersForGroup = useMemo(() => {
    if (!admin) {
      return [];
    }
    const q = newGroupUserFilterDraft.trim().toLowerCase();
    return admin.bmsUsers.filter((u) => {
      if (!q) {
        return true;
      }
      const blob = [
        userPickerLabel(u),
        u.firstName,
        u.lastName,
        u.email,
        u.username,
        u.displayName,
        u.primaryAddress?.city,
        u.company,
        String(u.userID),
      ]
        .filter((x) => x !== undefined && x !== null && String(x).trim() !== '')
        .map((x) => String(x).toLowerCase())
        .join(' ');
      return blob.includes(q);
    });
  }, [admin, newGroupUserFilterDraft]);

  const filteredRolesForGroup = useMemo(() => {
    if (!admin) {
      return [];
    }
    const q = newGroupRoleFilterQuery.trim().toLowerCase();
    return admin.userRoles.filter((r) => {
      if (r.isActive === false) {
        return false;
      }
      if (!q) {
        return true;
      }
      const name = (r.userRoleName?.trim() || `Role ${r.userRoleID}`).toLowerCase();
      return name.includes(q) || String(r.userRoleID).includes(q);
    });
  }, [admin, newGroupRoleFilterQuery]);

  useEffect(() => {
    if (addMemberUserId !== '' && !filteredBmsUsersForGroup.some((u) => u.userID === addMemberUserId)) {
      setAddMemberUserId('');
    }
  }, [filteredBmsUsersForGroup, addMemberUserId]);

  useEffect(() => {
    if (addMemberRoleId !== '' && !filteredRolesForGroup.some((r) => r.userRoleID === addMemberRoleId)) {
      setAddMemberRoleId('');
    }
  }, [filteredRolesForGroup, addMemberRoleId]);

  const formatJoinedDate = (d?: string | null) => formatCalendarDateDisplay(d);

  const userGroupListLabel = (g: UserGroupListRow) => {
    const num = g.userGroupNumber?.trim() || '-';
    const name = g.ugName?.trim() || '-';
    const members = `${g.memberCount} member${g.memberCount === 1 ? '' : 's'}`;
    return `#${g.ugid} | ${num} | ${name} | ${members}`;
  };

  /** Preview next group number as max(numeric UserGroupNumber) + 1. */
  const nextUserGroupNumberFromRows = (rows: UserGroupListRow[]): string => {
    let max = 0;
    for (const g of rows) {
      const raw = (g.userGroupNumber ?? '').trim();
      if (!raw) {
        continue;
      }
      const n = Number.parseInt(raw, 10);
      if (Number.isFinite(n) && n > max) {
        max = n;
      }
    }
    return String(max + 1);
  };

  const memberLineLabel = (m: AdminGroupMember) => {
    const u =
      m.username?.trim() ||
      m.email?.trim() ||
      m.userFullName?.trim() ||
      m.displayName?.trim() ||
      `User ${m.userID}`;
    const roleId = m.userRoleID ?? '-';
    const r = m.userRoleName?.trim() || `Role ${roleId}`;
    return `Grp ${m.ugid}  |  User ${m.userID}: ${u}  |  Role ${roleId}: ${r}  |  Joined ${formatJoinedDate(m.joinedDate)}`;
  };

  const pendingMemberLabel = (p: PendingGroupMember) => {
    const grpId = groupEditUgid > 0 ? groupEditUgid : '(new)';
    if (!admin) {
      return `Grp ${grpId}  |  User ${p.userID}  |  Role ${p.userRoleID}  |  Joined - (not saved)`;
    }
    const u = admin.bmsUsers.find((x) => x.userID === p.userID);
    const r = admin.userRoles.find((x) => x.userRoleID === p.userRoleID);
    const un = u ? userPickerLabel(u) : `User ${p.userID}`;
    const rn = r ? rolePickerLabel(r) : `Role ${p.userRoleID}`;
    return `Grp ${grpId}  |  User ${p.userID}: ${un}  |  Role ${p.userRoleID}: ${rn}  |  Joined - (not saved)`;
  };

  const groupMemberComboExists = (userID: number, userRoleID: number) =>
    loadedGroupMembers.some((m) => m.userID === userID && m.userRoleID === userRoleID) ||
    pendingGroupMembers.some((p) => p.userID === userID && p.userRoleID === userRoleID);

  const fetchGroupMembers = async (ugid: number) => {
    const data = await apiGet<{ members: AdminGroupMember[] }>(
      `/api/admin/user-groups/${ugid}/members`,
    );
    setLoadedGroupMembers(data.members);
    setPendingGroupMembers([]);
  };

  const buildGroupMemberSavePayload = () => {
    const map = new Map<string, { userID: number; userRoleID: number; statusID: number }>();
    for (const m of loadedGroupMembers) {
      if (m.userID > 0) {
        const roleId = m.userRoleID ?? 0;
        map.set(`${m.userID}:${roleId}`, { userID: m.userID, userRoleID: roleId, statusID: 1 });
      }
    }
    for (const p of pendingGroupMembers) {
      map.set(`${p.userID}:${p.userRoleID}`, {
        userID: p.userID,
        userRoleID: p.userRoleID,
        statusID: 1,
      });
    }
    return [...map.values()];
  };

  const runGroupSearch = async () => {
    setGroupPanelBusy(true);
    setGroupSearchNoMatch(false);
    try {
      const data = await apiGet<{ groups: UserGroupListRow[] }>(
        `/api/admin/user-groups/search?term=${encodeURIComponent(groupSearchInput.trim())}`,
      );
      setGroupSearchResults(data.groups);
      setGroupSearchNoMatch(data.groups.length === 0);
      setGroupListUseSearchResults(true);
      setSelectedUserGroupId('');
      setGroupEditUgid(0);
      setGroupEditName('');
      setGroupEditNumber('');
      setLoadedGroupMembers([]);
      setPendingGroupMembers([]);
      setSelectedMemberRowKey('');
      setIsCreatingGroup(false);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Search failed.');
    } finally {
      setGroupPanelBusy(false);
    }
  };

  const clearGroupTableSearch = () => {
    setGroupSearchInput('');
    setGroupSearchNoMatch(false);
    setGroupListUseSearchResults(false);
    setGroupSearchResults([]);
  };

  const startNewUserGroup = () => {
    setGroupSearchNoMatch(false);
    setGroupListUseSearchResults(false);
    setGroupSearchResults([]);
    setGroupSearchInput('');
    setNewGroupUserFilterDraft('');
    setNewGroupRoleFilterDraft('');
    setNewGroupRoleFilterQuery('');
    setSelectedUserGroupId('');
    setGroupEditUgid(0);
    setGroupEditName('');
    setLoadedGroupMembers([]);
    setPendingGroupMembers([]);
    setSelectedMemberRowKey('');
    setIsCreatingGroup(true);
    // Default immediately to max+1 from loaded groups, then confirm via API.
    const localNext = nextUserGroupNumberFromRows(admin?.userGroupSummaries ?? []);
    setGroupEditNumber(localNext);
    void (async () => {
      try {
        const data = await apiGet<{ nextUserGroupNumber: string }>('/api/admin/groups/next-number');
        const fromApi = (data.nextUserGroupNumber ?? '').trim();
        if (fromApi) {
          setGroupEditNumber(fromApi);
        }
      } catch {
        // Keep local max+1 preview if the API call fails.
      }
    })();
  };

  const editGroupFromSelection = () => {
    if (selectedUserGroupId === '') {
      window.alert('Select a user group first.');
      return;
    }
    const row = displayedUserGroupRows.find((g) => g.ugid === selectedUserGroupId);
    selectUserGroupByNumber(selectedUserGroupId, row);
  };

  const deleteUserGroup = async () => {
    if (selectedUserGroupId === '') {
      window.alert('Select a user group to delete.');
      return;
    }
    const targetId = selectedUserGroupId;
    const row = displayedUserGroupRows.find((g) => g.ugid === targetId);
    const name = (row?.ugName ?? '').trim() || `User Group ${targetId}`;
    if (
      !window.confirm(
        `Permanently delete "${name}" from the database?\n\nThis removes the group and its member assignments. Users themselves are not deleted. This cannot be undone.`,
      )
    ) {
      return;
    }
    setGroupPanelBusy(true);
    try {
      await apiPostJson(`/api/admin/groups/${targetId}/delete`, {});
      flash('success', 'User group deleted.');
      setSelectedUserGroupId('');
      setGroupEditUgid(0);
      setGroupEditName('');
      setGroupEditNumber('');
      setLoadedGroupMembers([]);
      setPendingGroupMembers([]);
      setSelectedMemberRowKey('');
      setIsCreatingGroup(false);
      setAuditGroupHighlight(null);
      setAuditGroupHighlightMessage(null);
      clearGroupTableSearch();
      await reload();
      if (dataAudit != null) {
        await loadDataAudit();
      }
    } catch (err) {
      const message = err instanceof ApiError ? err.message : 'Delete failed.';
      if (/active projects/i.test(message) || /cannot be deleted/i.test(message)) {
        setUgDeleteBlockedMessage(
          message ||
            'This user group cannot be deleted because it is associated with active projects. Please remove the project before deleting the user group.',
        );
        return;
      }
      flash('error', message);
    } finally {
      setGroupPanelBusy(false);
    }
  };

  const selectUserGroupByNumber = useCallback(
    (n: number, row?: UserGroupListRow) => {
      setSelectedUserGroupId(n);
      setIsCreatingGroup(false);
      const hit =
        row ??
        (groupListUseSearchResults ? groupSearchResults : (admin?.userGroupSummaries ?? [])).find(
          (g) => g.ugid === n,
        );
      if (hit) {
        setGroupEditUgid(hit.ugid);
        setGroupEditName(hit.ugName ?? '');
        setGroupEditNumber(hit.userGroupNumber ?? '');
      } else {
        setGroupEditUgid(n);
        setGroupEditNumber('');
      }
      void (async () => {
        setGroupPanelBusy(true);
        try {
          await fetchGroupMembers(n);
        } catch (err) {
          flash('error', err instanceof ApiError ? err.message : 'Could not load group members.');
        } finally {
          setGroupPanelBusy(false);
        }
      })();
    },
    [admin?.userGroupSummaries, flash, groupListUseSearchResults, groupSearchResults],
  );

  const openUserGroupFromDataAudit = useCallback(
    (check: DataAuditCheck, row: (string | null)[]) => {
      const ugid = auditRowUgid(check, row);
      if (ugid == null) {
        return;
      }
      const hint = auditHighlightForCheck(check.checkId);
      const summary = (admin?.userGroupSummaries ?? []).find((g) => g.ugid === ugid);
      const groupName =
        (summary?.ugName ?? '').trim() ||
        auditRowGroupName(check, row) ||
        `User Group ${ugid}`;
      const filterRow: UserGroupListRow = summary ?? {
        ugid,
        userGroupNumber: summary?.userGroupNumber ?? '',
        ugName: groupName,
        memberCount: summary?.memberCount ?? 0,
      };

      // Keep the UserGroup listbox filtered to this group name (same as using Search).
      setGroupSearchInput(groupName);
      setGroupSearchResults([filterRow]);
      setGroupSearchNoMatch(false);
      setGroupListUseSearchResults(true);

      setUsersSubTab('groups');
      setTab('users');
      selectUserGroupByNumber(ugid, filterRow);
      setAuditGroupHighlight(hint?.highlight ?? 'members');
      setAuditGroupHighlightMessage(hint?.message ?? check.description);
      if (
        hint?.highlight === 'members' &&
        admin?.userRoles?.length &&
        (check.checkId === 'user-groups-missing-account-executive' ||
          check.checkId === 'user-groups-missing-analytics-lead')
      ) {
        const roleMatcher =
          check.checkId === 'user-groups-missing-account-executive'
            ? /account\s*executive/i
            : /analytics\s*lead/i;
        const preferredRole = admin.userRoles.find((r) =>
          roleMatcher.test(`${r.userRoleName ?? ''} ${r.roleDescription ?? ''}`),
        );
        if (preferredRole) {
          setAddMemberRoleId(preferredRole.userRoleID);
          setNewGroupRoleFilterDraft(rolePickerLabel(preferredRole));
          setNewGroupRoleFilterQuery(rolePickerLabel(preferredRole));
        }
      }
      window.setTimeout(() => {
        groupEditPanelRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }, 80);
    },
    [admin?.userGroupSummaries, admin?.userRoles, selectUserGroupByNumber],
  );

  const openUserFromDataAudit = useCallback(
    (check: DataAuditCheck, row: (string | null)[]) => {
      const userId = auditRowUserId(check, row);
      if (userId == null || !admin) {
        return;
      }
      const u = admin.users.find((x) => x.userID === userId);
      if (!u) {
        flash('error', `User ${userId} was not found in the Users list.`);
        return;
      }
      setUserDepartmentFilter('CAT');
      setUserActiveTab(u.isDeleted === true ? 'Inactive' : 'Active');
      setUserDirectorySearch('');
      setSelectedUserId(userId);
      setUsersSubTab('users');
      setTab('users');
      void applyUserToForm(u);
      window.setTimeout(() => {
        document.getElementById(`admin-user-row-${userId}`)?.scrollIntoView({
          behavior: 'smooth',
          block: 'nearest',
        });
      }, 80);
    },
    // applyUserToForm closes over admin/flash; user list is the dependency that matters here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [admin, flash],
  );

  const setAuditClientInactive = useCallback(
    async (clientId: number, clientName: string) => {
      setDataAuditClientMenu(null);
      setDataAuditClientBusy(true);
      try {
        await apiPostJson(`/api/admin/clients/${clientId}/soft-delete`, {});
        flash('success', `"${clientName}" was set to Inactive.`);
        await reload();
        await loadDataAudit();
      } catch (err) {
        flash('error', err instanceof ApiError ? err.message : 'Could not set the client inactive.');
      } finally {
        setDataAuditClientBusy(false);
      }
    },
    [flash, loadDataAudit, reload],
  );

  const deleteAuditClient = useCallback(
    async (clientId: number, clientName: string) => {
      setDataAuditClientMenu(null);
      if (
        !window.confirm(
          `Permanently delete "${clientName}" from the database?\n\nThis removes the client and related client references. This cannot be undone.`,
        )
      ) {
        return;
      }
      setDataAuditClientBusy(true);
      try {
        await apiPostJson(`/api/admin/clients/${clientId}/delete`, {});
        flash('success', `"${clientName}" was deleted.`);
        await reload();
        await loadDataAudit();
      } catch (err) {
        const message = err instanceof ApiError ? err.message : 'Delete failed.';
        if (/active projects/i.test(message) || /cannot be deleted/i.test(message)) {
          setClientDeleteBlockedMessage(
            message ||
              'This client cannot be deleted because it is associated with active projects. Please remove the project before deleting the client.',
          );
          return;
        }
        flash('error', message);
      } finally {
        setDataAuditClientBusy(false);
      }
    },
    [flash, loadDataAudit, reload],
  );

  const openClientFromDataAudit = useCallback(
    (check: DataAuditCheck, row: (string | null)[]) => {
      const clientId = auditRowClientId(check, row);
      if (clientId == null || !admin) {
        return;
      }
      const c = admin.clients.find((x) => x.clientID === clientId);
      if (!c) {
        flash('error', `Client ${clientId} was not found in the Clients list.`);
        return;
      }
      setClientActiveTab(c.isActive === false ? 'Inactive' : 'Active');
      setClientBuFilter('');
      setClientBcFilter('');
      setClientSearch((c.clientName ?? '').trim() || String(clientId));
      setSelectedClientId(clientId);
      applyClientToForm(c);
      setTab('clients');
      window.setTimeout(() => {
        document.getElementById(`admin-client-row-${clientId}`)?.scrollIntoView({
          behavior: 'smooth',
          block: 'nearest',
        });
      }, 80);
    },
    // applyClientToForm closes over clientStatusOf; client list is the dependency that matters here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [admin, flash],
  );

  const openProjectFromDataAudit = useCallback(
    (check: DataAuditCheck, row: (string | null)[]) => {
      const projectId = auditRowProjectId(check, row);
      if (projectId == null) {
        return;
      }
      navigate(`/projects/${projectId}`);
    },
    [navigate],
  );

  // Prefill UserGroup edit panel when arriving from Project Info (selectUserGroup query).
  useEffect(() => {
    if (prefilledGroupFromQueryRef.current) {
      return;
    }
    if (tab !== 'users' || usersSubTab !== 'groups' || !admin) {
      return;
    }
    const raw = searchParams.get('selectUserGroup');
    if (!raw) {
      return;
    }
    const ugid = Number(raw);
    if (!Number.isFinite(ugid) || ugid <= 0) {
      return;
    }
    prefilledGroupFromQueryRef.current = true;
    const row = (admin.userGroupSummaries ?? []).find((g) => g.ugid === ugid);
    selectUserGroupByNumber(ugid, row);
  }, [admin, searchParams, selectUserGroupByNumber, tab, usersSubTab]);

  const addMemberToGroup = () => {
    if (!groupEditName.trim()) {
      window.alert('Enter a group name before adding members.');
      return;
    }
    if (!isCreatingGroup && !groupEditNumber.trim()) {
      window.alert('Enter a group number before adding members.');
      return;
    }
    if (addMemberUserId === '' || addMemberRoleId === '') {
      window.alert('Select a user (by username) and a user role.');
      return;
    }
    const uid = addMemberUserId as number;
    const rid = addMemberRoleId as number;
    if (groupMemberComboExists(uid, rid)) {
      window.alert('That user and role are already listed for this group.');
      return;
    }
    setPendingGroupMembers((prev) => [
      ...prev,
      { tempId: globalThis.crypto?.randomUUID?.() ?? `t-${Date.now()}`, userID: uid, userRoleID: rid },
    ]);
  };

  const saveGroupBatch = async (e: FormEvent) => {
    e.preventDefault();
    if (!groupEditName.trim()) {
      window.alert('Group name is required.');
      return;
    }
    if (!isCreatingGroup && !groupEditNumber.trim()) {
      window.alert('Group number is required.');
      return;
    }
    try {
      setGroupPanelBusy(true);
      const members = buildGroupMemberSavePayload();
      const result = await apiPostJson<{ ugid: number; userGroupNumber?: string; message: string }>('/api/admin/groups/save-batch', {
        ugid: groupEditUgid,
        userGroupNumber: isCreatingGroup ? '' : groupEditNumber.trim(),
        ugName: groupEditName.trim(),
        userRoleID: null,
        statusID: 0,
        members,
      });
      const savedUgid = result.ugid;
      setGroupEditUgid(savedUgid);
      setSelectedUserGroupId(savedUgid);
      setIsCreatingGroup(false);
      if (result.userGroupNumber) {
        setGroupEditNumber(result.userGroupNumber);
      }
      flash('success', result.message || 'Group saved.');
      setPendingGroupMembers([]);
      await fetchGroupMembers(savedUgid);
      await reload();
      const returnTo = safeInternalReturnPath(searchParams.get('returnTo'));
      if (returnTo !== null) {
        navigate(`${returnTo}?focusProjectName=1&selectUserGroup=${savedUgid}`);
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    } finally {
      setGroupPanelBusy(false);
    }
  };

  const removeSelectedGroupMember = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedMemberRowKey) {
      window.alert('Select a member row first.');
      return;
    }
    if (selectedMemberRowKey.startsWith('pending-')) {
      const id = selectedMemberRowKey.slice('pending-'.length);
      setPendingGroupMembers((prev) => prev.filter((p) => p.tempId !== id));
      setSelectedMemberRowKey('');
      return;
    }
    if (selectedMemberRowKey.startsWith('db-')) {
      const compound = selectedMemberRowKey.slice(3); // "ugid:userId"
      const [ugidStr, userIdStr] = compound.includes(':') ? compound.split(':') : [String(groupEditUgid), compound];
      const ugid = Number(ugidStr);
      const userId = Number(userIdStr);
      if (!ugid || !userId) {
        return;
      }
      if (!window.confirm('Remove this member from the group?')) {
        return;
      }
      try {
        setGroupPanelBusy(true);
        await apiDelete(`/api/admin/groups/${ugid}/members/${userId}`);
        flash('success', 'Member removed.');
        if (groupEditUgid > 0) {
          await fetchGroupMembers(groupEditUgid);
        } else {
          setLoadedGroupMembers([]);
        }
        setSelectedMemberRowKey('');
        await reload();
      } catch (err) {
        flash('error', err instanceof ApiError ? err.message : 'Remove failed.');
      } finally {
        setGroupPanelBusy(false);
      }
    }
  };

  const clearPt = () => setPtForm({ projectTypeID: 0, projectTypeCode: '', projectTypeDescription: '' });
  const editPtFromSelection = () => {
    if (selectedPtId === '' || !admin) {
      window.alert('Select a project type first.');
      return;
    }
    const p = admin.projectTypes.find((x) => x.projectTypeID === selectedPtId);
    if (!p) {
      return;
    }
    setPtForm({
      projectTypeID: p.projectTypeID,
      projectTypeCode: p.projectTypeCode ?? '',
      projectTypeDescription: p.projectTypeDescription ?? '',
    });
  };

  const savePt = async (e: FormEvent) => {
    e.preventDefault();
    const code = ptForm.projectTypeCode.trim();
    if (!code) {
      flash('error', 'Project type code is required.');
      return;
    }
    try {
      await apiPostJson('/api/admin/project-types/save', {
        projectTypeID: ptForm.projectTypeID,
        projectTypeCode: code,
        projectTypeDescription: ptForm.projectTypeDescription.trim(),
        isActive: true,
      });
      flash('success', 'Project type saved.');
      clearPt();
      setSelectedPtId('');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deletePt = async (e: FormEvent) => {
    e.preventDefault();
    if (selectedPtId === '') {
      window.alert('Select a project type to remove.');
      return;
    }
    if (!window.confirm('Remove the selected project type?')) {
      return;
    }
    try {
      const existingPt = admin?.projectTypes.find((p) => p.projectTypeID === selectedPtId);
      if (!existingPt) {
        flash('error', 'Selected project type not found.');
        return;
      }
      await apiPostJson('/api/admin/project-types/save', { ...existingPt, isActive: false });
      flash('success', 'Project type deactivated.');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Remove failed.');
    }
  };

  const clearSeg = () => setSegForm({ businessUnitID: 0, businessUnitName: '', businessUnitDescription: '' });
  const editSegFromSelection = () => {
    if (selectedSegId === '' || !admin) {
      window.alert('Select a business unit first.');
      return;
    }
    const s = admin.businessUnits.find((x) => x.businessUnitID === selectedSegId);
    if (!s) {
      return;
    }
    setSegForm({
      businessUnitID: s.businessUnitID,
      businessUnitName: s.businessUnitName ?? '',
      businessUnitDescription: s.businessUnitDescription ?? '',
    });
  };

  const saveSeg = async (e: FormEvent) => {
    e.preventDefault();
    try {
      await apiPostJson('/api/admin/business-units/save', segForm);
      flash('success', 'Business unit saved.');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deleteSeg = async (e: FormEvent) => {
    e.preventDefault();
    if (selectedSegId === '') {
      window.alert('Select a business unit to remove.');
      return;
    }
    const selected = admin?.businessUnits.find((x) => x.businessUnitID === selectedSegId);
    const name = (selected?.businessUnitName ?? '').trim() || `Business Unit ${selectedSegId}`;
    if (!window.confirm(`Permanently delete "${name}" from the database?\n\nThis cannot be undone.`)) {
      return;
    }
    try {
      await apiPostJson(`/api/admin/business-units/${selectedSegId}/delete`, {});
      flash('success', 'Business unit deleted.');
      clearSeg();
      await reload();
    } catch (err) {
      const message = err instanceof ApiError ? err.message : 'Remove failed.';
      if (/active projects/i.test(message) || /cannot be deleted/i.test(message)) {
        setBuDeleteBlockedMessage(
          message ||
            'This business unit cannot be deleted because it is associated with active projects. Please remove the project before deleting the business unit.',
        );
        return;
      }
      flash('error', message);
    }
  };

  const clearBc = () => {
    setSelectedBcId('');
    setBcForm({ businessClassID: 0, businessUnitID: 0, businessClassName: '', businessClassDescription: '' });
  };
  const editBcFromSelection = (preset?: AdminBusinessClass) => {
    if (!admin) {
      return;
    }
    const c = preset ?? admin.businessClasses.find((x) => x.businessClassID === selectedBcId);
    if (!c) {
      window.alert('Select a business class first.');
      return;
    }
    setSelectedBcId(c.businessClassID);
    setBcForm({
      businessClassID: c.businessClassID,
      businessUnitID: c.businessUnitID,
      businessClassName: c.businessClassName ?? '',
      businessClassDescription: c.businessClassDescription ?? '',
    });
  };

  const saveBc = async (e: FormEvent) => {
    e.preventDefault();
    if (!bcForm.businessUnitID || bcForm.businessUnitID <= 0) {
      window.alert('Choose a Business Unit before saving.');
      return;
    }
    if (!bcForm.businessClassName.trim()) {
      window.alert('Enter a Business Class name before saving.');
      return;
    }
    try {
      await apiPostJson('/api/admin/business-classes/save', bcForm);
      flash('success', 'Business class saved.');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deleteBc = async (e: FormEvent) => {
    e.preventDefault();
    if (selectedBcId === '') {
      window.alert('Select a business class to remove.');
      return;
    }
    const selected = admin?.businessClasses.find((x) => x.businessClassID === selectedBcId);
    const name = (selected?.businessClassName ?? '').trim() || `Business Class ${selectedBcId}`;
    if (!window.confirm(`Permanently delete "${name}" from the database?\n\nThis cannot be undone.`)) {
      return;
    }
    try {
      await apiPostJson(`/api/admin/business-classes/${selectedBcId}/delete`, {});
      flash('success', 'Business class deleted.');
      clearBc();
      await reload();
    } catch (err) {
      const message = err instanceof ApiError ? err.message : 'Remove failed.';
      if (/active projects/i.test(message) || /cannot be deleted/i.test(message)) {
        setBcDeleteBlockedMessage(
          message ||
            'This business class cannot be deleted because it is associated with active projects. Please remove the project before deleting the business class.',
        );
        return;
      }
      flash('error', message);
    }
  };

  const clearCt = () => {
    setSelectedCtId('');
    setCtForm({ clientTypeID: 0, ctName: '', ctDescription: '', isActive: true });
  };

  const editCtFromSelection = (preset?: AdminClientType) => {
    if (!admin) {
      return;
    }
    const c = preset ?? admin.clientTypes.find((x) => x.clientTypeID === selectedCtId);
    if (!c) {
      window.alert('Select a client type first.');
      return;
    }
    setSelectedCtId(c.clientTypeID);
    setCtForm({
      clientTypeID: c.clientTypeID,
      ctName: c.ctName ?? '',
      ctDescription: c.ctDescription ?? '',
      isActive: c.isActive ?? true,
    });
  };

  const saveCt = async (e: FormEvent) => {
    e.preventDefault();
    if (!ctForm.ctName.trim()) {
      window.alert('Enter a Client Type name before saving.');
      return;
    }
    try {
      await apiPostJson('/api/admin/client-types/save', {
        clientTypeID: ctForm.clientTypeID,
        ctName: ctForm.ctName.trim(),
        ctDescription: ctForm.ctDescription.trim() || null,
        isActive: ctForm.isActive,
      });
      // The save proc only updates name/description; reconcile active state for existing rows.
      if (ctForm.clientTypeID > 0) {
        const original = admin?.clientTypes.find((x) => x.clientTypeID === ctForm.clientTypeID);
        if (original && (original.isActive ?? true) !== ctForm.isActive) {
          await apiPostJson(
            `/api/admin/client-types/${ctForm.clientTypeID}/active?isActive=${ctForm.isActive ? 'true' : 'false'}`,
            {},
          );
        }
      }
      flash('success', 'Client type saved.');
      clearCt();
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deleteCt = async (e: FormEvent) => {
    e.preventDefault();
    if (selectedCtId === '') {
      window.alert('Select a client type to remove.');
      return;
    }
    if (!window.confirm('Deactivate the selected client type?')) {
      return;
    }
    try {
      await apiPostJson(`/api/admin/client-types/${selectedCtId}/active?isActive=false`, {});
      flash('success', 'Client type deactivated.');
      clearCt();
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Remove failed.');
    }
  };

  const clearServiceForm = () => {
    setServiceForm(emptyServiceForm());
  };

  const applyServiceToForm = (s: CatalogServiceRow) => {
    if (!admin) {
      return;
    }
    const canonical = admin.services.find((x) => x.serviceID === s.serviceID);
    const merged: CatalogServiceRow = canonical ? { ...canonical, ...s } : s;
    setServiceForm(buildServiceFormState(merged, admin));
  };

  const editServiceFromSelection = (preset?: CatalogServiceRow) => {
    if (!admin) {
      return;
    }
    const s = preset ?? admin.services.find((x) => x.serviceID === selectedServiceId);
    if (!s) {
      window.alert('Select a service first.');
      return;
    }
    setSelectedServiceId(s.serviceID);
    applyServiceToForm(s);
  };

  const submitService = async (e: FormEvent) => {
    e.preventDefault();
    if (!admin) {
      return;
    }

    const selectedRow =
      selectedServiceId !== '' ? admin.services.find((s) => s.serviceID === selectedServiceId) : undefined;

    let serviceId = serviceForm.serviceID;
    let serviceName = serviceForm.serviceName.trim();
    let serviceDescription = serviceForm.serviceDescription ?? '';
    let isActive = serviceForm.isActive;

    // Pick-list selection does not load the form; bind the selected row on Save so tServiceRef
    // upsert targets the correct ServiceID (browser required fields would otherwise block submit).
    if (selectedRow) {
      if (serviceId <= 0 || serviceId !== selectedRow.serviceID) {
        serviceId = selectedRow.serviceID;
        if (!serviceName) {
          serviceName = (selectedRow.serviceName ?? '').trim();
        }
        if (!serviceDescription.trim()) {
          serviceDescription = selectedRow.serviceDescription ?? '';
        }
        isActive = serviceIsActive(selectedRow);
      }
    }

    if (!serviceName) {
      flash('error', 'Service name is required. Select a service or use Add Service.');
      return;
    }
    if (!serviceForm.serviceCategoryID || serviceForm.serviceCategoryID <= 0) {
      flash('error', 'Select a service category - required for the tServiceRef link.');
      return;
    }
    if (!serviceForm.serviceGroupID || serviceForm.serviceGroupID <= 0) {
      flash('error', 'Select a service group - required for the tServiceRef link.');
      return;
    }
    const category = (admin?.serviceCategories ?? []).find(
      (c) => c.serviceCategoryID === serviceForm.serviceCategoryID,
    );
    const functionID =
      serviceForm.functionID > 0 ? serviceForm.functionID : (category?.functionID ?? 0);

    try {
      const result = await apiPostJson<{ serviceId: number; message: string }>(
        '/api/admin/services/save',
        {
          serviceID: serviceId,
          serviceCategoryID: serviceForm.serviceCategoryID,
          serviceGroupID: serviceForm.serviceGroupID,
          functionID,
          serviceName,
          serviceDescription: serviceDescription.trim() || null,
          isActive,
        },
      );
      const savedId = result.serviceId ?? serviceId;

      if (savedId > 0) {
        const before = admin?.services.find((s) => s.serviceID === savedId);
        const beforeIds = new Set((before?.tags ?? []).map((t) => t.tagID));
        const afterIds = new Set(serviceForm.tagIDs);
        for (const tid of afterIds) {
          if (!beforeIds.has(tid)) {
            try {
              await apiPostJson(`/api/admin/services/${savedId}/tags/${tid}`, {});
            } catch {
              /* skip; reload will show actual state */
            }
          }
        }
        for (const tid of beforeIds) {
          if (!afterIds.has(tid)) {
            try {
              await apiDelete(`/api/admin/services/${savedId}/tags/${tid}`);
            } catch {
              /* skip; reload will show actual state */
            }
          }
        }
      }

      flash('success', result.message ?? 'Service saved.');
      setServiceForm((f) => ({ ...f, serviceID: savedId }));
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Save failed.');
    }
  };

  const deactivateSelectedService = async () => {
    if (selectedServiceIds.length === 0) {
      window.alert('Select one or more services to deactivate.');
      return;
    }
    const count = selectedServiceIds.length;
    if (
      !window.confirm(
        `Deactivate ${count} selected service${count === 1 ? '' : 's'} (sets IsActive = 0)?`,
      )
    ) {
      return;
    }
    try {
      for (const id of selectedServiceIds) {
        await apiPostJson(`/api/admin/services/${id}/soft-delete`, {});
      }
      flash('success', count === 1 ? 'Service soft-deleted.' : `${count} services soft-deleted.`);
      if (selectedServiceIds.includes(serviceForm.serviceID)) {
        clearServiceForm();
      }
      setSelectedServiceIds([]);
      setSelectedServiceId('');
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Deactivate failed.');
    }
  };

  const resetServiceBulkWizard = () => {
    setServiceBulkWizardOpen(false);
    setServiceBulkRows([]);
    setServiceBulkSkippedEmpty(0);
    setServiceBulkVerifyOk(false);
    setServiceBulkVerifyErrors([]);
    setServiceBulkRowErrors([]);
    setServiceBulkBusy(false);
  };

  const handleServiceBulkImportFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) {
      return;
    }
    const formData = new FormData();
    formData.append('file', file);
    setServiceBulkBusy(true);
    setServiceBulkVerifyOk(false);
    setServiceBulkVerifyErrors([]);
    setServiceBulkRowErrors([]);
    try {
      const result = await apiPostFormData<ServiceBulkParseResult>('/api/admin/services/bulk-import/parse', formData);
      setServiceBulkRows(result.rows);
      setServiceBulkSkippedEmpty(result.skippedEmptyRows);
      setServiceBulkWizardOpen(true);
      setTab('services');
      setServicesSubTab('services');
      if (result.rows.length === 0) {
        flash(
          'error',
          'No importable rows were found (each new row needs ServiceName and ServiceDescription).',
        );
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not read the CSV file.');
    } finally {
      setServiceBulkBusy(false);
    }
  };

  const handleServiceBulkVerify = async () => {
    if (serviceBulkRows.length === 0) {
      return;
    }
    setServiceBulkBusy(true);
    setServiceBulkVerifyOk(false);
    setServiceBulkVerifyErrors([]);
    setServiceBulkRowErrors([]);
    try {
      const result = await apiPostJson<ServiceBulkVerifyResult>('/api/admin/services/bulk-import/verify', {
        rows: serviceBulkRows,
      });
      setServiceBulkVerifyErrors(result.errors ?? []);
      setServiceBulkVerifyOk(!!result.valid);
      if (result.valid) {
        flash('success', 'Verification passed. You can import these rows into tService (and tServiceRef when category/group columns are supplied).');
      }
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Verification request failed.');
    } finally {
      setServiceBulkBusy(false);
    }
  };

  const handleServiceBulkCommit = async () => {
    if (!serviceBulkVerifyOk || serviceBulkRows.length === 0) {
      return;
    }
    setServiceBulkBusy(true);
    try {
      const result = await apiPostJson<ServiceBulkCommitResult>('/api/admin/services/bulk-import/commit', {
        rows: serviceBulkRows,
      });
      const errs = result.rowErrors ?? [];
      setServiceBulkRowErrors(errs);
      let msg = `Imported ${result.importedCount} service row(s).`;
      if (result.skippedEmptyRows) {
        msg += ` Skipped ${result.skippedEmptyRows} empty row(s).`;
      }
      if (errs.length > 0) {
        msg += ` ${errs.length} row(s) reported errors (see preview).`;
        flash('error', msg);
      } else {
        flash('success', msg);
        resetServiceBulkWizard();
      }
      await reload();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Import failed.');
    } finally {
      setServiceBulkBusy(false);
    }
  };

  const formatServiceCell = (v: string | number | boolean | null | undefined) => {
    if (v === null || v === undefined || v === '') {
      return '-';
    }
    return String(v);
  };

  if (loadError) {
    return (
      <section className="card narrow-card">
        <h1>Admin</h1>
        <p>{loadError}</p>
      </section>
    );
  }

  if (!admin) {
    return <p>Loading...</p>;
  }

  const tabBtn = (id: TabId, label: string) => (
    <button
      type="button"
      data-tab={id}
      className={tab === id ? 'active' : ''}
      onClick={() => setTab(id)}
    >
      {label}
    </button>
  );

  return (
    <>
      <section className="page-header">
        <h1>Admin</h1>
        <p>
          Configure SQL connectivity, users, roles, user groups, project types, business units, and catalog
          services (tServices).
        </p>
      </section>

      <section
        className={
          'card admin-card' +
          (tab === 'services' ? ' admin-card--services' : '') +
          (tab === 'users' ? ' admin-card--users' : '')
        }
      >
        <div className="admin-tabs" role="tablist" aria-label="Admin sections">
          {tabBtn('server', 'Server')}
          {isAdministrator ? tabBtn('catai', 'CatAI') : null}
          {tabBtn('businessUnits', 'Business Unit')}
          {tabBtn('businessClasses', 'Business Class')}
          {tabBtn('clientTypes', 'Client Type')}
          {tabBtn('clients', 'Clients')}
          {tabBtn('projectTypes', 'Project Types')}
          {tabBtn('users', 'Users')}
          {tabBtn('correspondence', 'Correspondence')}
          {tabBtn('dataAudit', 'Data Audit')}
          {tabBtn('services', 'Services')}
        </div>

        <div id="admin-tab-server" className={'admin-tab-panel' + (tab === 'server' ? ' active' : '')}>
          <h2>Server</h2>
          <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void handleServerSave(e)}>
            <div className="form-grid">
              <label htmlFor="srv-server">Server</label>
              <select
                id="srv-server"
                value={serverDraft.server}
                onChange={(e) => setServerDraft({ ...serverDraft, server: e.target.value })}
              >
                {admin.serverOptions.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>

              <label htmlFor="srv-db">Database</label>
              <input
                id="srv-db"
                value={serverDraft.database}
                placeholder="e.g. BMS_CMS"
                onChange={(e) => setServerDraft({ ...serverDraft, database: e.target.value })}
                aria-describedby="srv-db-help"
              />

              <label htmlFor="srv-user">Username</label>
              <input
                id="srv-user"
                autoComplete="off"
                value={serverDraft.userName}
                onChange={(e) => setServerDraft({ ...serverDraft, userName: e.target.value })}
              />

              <label htmlFor="srv-pass">Password</label>
              <input
                id="srv-pass"
                type="password"
                autoComplete="new-password"
                placeholder="Leave blank for integrated security"
                value={serverDraft.password}
                onChange={(e) => setServerDraft({ ...serverDraft, password: e.target.value })}
              />

              <label htmlFor="srv-int">Security</label>
              <span>
                <input
                  id="srv-int"
                  type="checkbox"
                  checked={serverDraft.useIntegratedSecurity}
                  onChange={(e) =>
                    setServerDraft({ ...serverDraft, useIntegratedSecurity: e.target.checked })
                  }
                />{' '}
                Use Windows Integrated Security
              </span>
            </div>
            <div className="button-row">
              <button type="submit" className="primary-button">
                Test and Save
              </button>
              <button type="button" className="secondary-button" onClick={() => void handleServerTest()}>
                Test Connection Only
              </button>
            </div>
            <p id="srv-db-help" className="field-help">
              Default target is <strong>vmus-sql-099</strong> / <strong>BMS_CMS</strong>. Server is chosen from the
              approved list; database is typed exactly as stored on SQL Server.
              <br />
              <strong>Windows Integrated Security</strong> (recommended) connects as the <em>web app process
              identity</em> — the IIS application-pool account for CMS-Dev1, or your Windows user when running
              locally under Kestrel — <em>not</em> the person signed into the browser. That account needs a SQL
              login on <code>vmus-sql-099</code> with rights to <code>BMS_CMS</code> (typically the{' '}
              <code>app_CMS_Execute</code> role).
              <br />
              To use a SQL login instead, uncheck Integrated Security and enter Username and Password. Settings are
              encrypted under <code>App_Data</code> after a successful <strong>Test and Save</strong>.
            </p>
          </form>
        </div>

        {isAdministrator ? (
          <div id="admin-tab-catai" className={'admin-tab-panel' + (tab === 'catai' ? ' active' : '')}>
            <h2>CatAI</h2>
            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void handleCatAiSave(e)}>
              <div className="form-grid">
                <label htmlFor="catai-base">Base URL</label>
                <input
                  id="catai-base"
                  value={catAiDraft.baseUrl}
                  onChange={(e) => setCatAiDraft({ ...catAiDraft, baseUrl: e.target.value })}
                  placeholder="https://....openai.azure.com"
                />

                <label htmlFor="catai-deployment">Deployment</label>
                <input
                  id="catai-deployment"
                  value={catAiDraft.deployment}
                  onChange={(e) => setCatAiDraft({ ...catAiDraft, deployment: e.target.value })}
                />

                <label htmlFor="catai-model">Model</label>
                <input
                  id="catai-model"
                  value={catAiDraft.model}
                  onChange={(e) => setCatAiDraft({ ...catAiDraft, model: e.target.value })}
                />

                <label htmlFor="catai-apiversion">API version</label>
                <input
                  id="catai-apiversion"
                  value={catAiDraft.apiVersion}
                  onChange={(e) => setCatAiDraft({ ...catAiDraft, apiVersion: e.target.value })}
                />

                <label htmlFor="catai-temp">Temperature</label>
                <input
                  id="catai-temp"
                  type="number"
                  min={0}
                  max={2}
                  step={0.1}
                  value={catAiDraft.temperature}
                  onChange={(e) =>
                    setCatAiDraft({ ...catAiDraft, temperature: Number(e.target.value) || 0 })
                  }
                />

                <label htmlFor="catai-maxtokens">Max tokens</label>
                <input
                  id="catai-maxtokens"
                  type="number"
                  min={64}
                  max={8000}
                  value={catAiDraft.maxTokens}
                  onChange={(e) =>
                    setCatAiDraft({ ...catAiDraft, maxTokens: Number(e.target.value) || 1500 })
                  }
                />

                <label htmlFor="catai-maxrounds">Max tool rounds</label>
                <input
                  id="catai-maxrounds"
                  type="number"
                  min={1}
                  max={10}
                  value={catAiDraft.maxToolRounds}
                  onChange={(e) =>
                    setCatAiDraft({ ...catAiDraft, maxToolRounds: Number(e.target.value) || 5 })
                  }
                />

                <label htmlFor="catai-key">API key</label>
                <div>
                  <input
                    id="catai-key"
                    type="password"
                    autoComplete="new-password"
                    placeholder={
                      catAiDraft.apiKeyConfigured
                        ? 'Configured (enter a new key to replace)'
                        : 'Enter API key'
                    }
                    value={catAiKeyReplace}
                    onChange={(e) => setCatAiKeyReplace(e.target.value)}
                  />
                  {catAiDraft.apiKeyConfigured ? (
                    <p className="field-help">Current key (masked): {catAiDraft.apiKey || '••••'}</p>
                  ) : (
                    <p className="field-help">No API key configured yet.</p>
                  )}
                </div>

                <label htmlFor="catai-prompt">System prompt</label>
                <textarea
                  id="catai-prompt"
                  rows={6}
                  value={catAiDraft.systemPrompt}
                  onChange={(e) => setCatAiDraft({ ...catAiDraft, systemPrompt: e.target.value })}
                />
              </div>
              <div className="button-row">
                <button type="submit" className="primary-button">
                  Save CatAI settings
                </button>
                <button type="button" className="secondary-button" onClick={() => void handleCatAiTest()}>
                  Test connection
                </button>
              </div>
              <p className="field-help">
                Settings are stored encrypted under App_Data and override appsettings without rewriting the config
                file on disk. Leave the API key blank when saving to keep the existing key.
              </p>
            </form>
          </div>
        ) : null}

        <div
          id="admin-tab-correspondence"
          className={'admin-tab-panel' + (tab === 'correspondence' ? ' active' : '')}
        >
          <h2>Correspondence</h2>
          <p className="field-help">
            Configure when CMS sends scheduled communications and who receives them. The report content for each
            communication type will be defined next; this tab stores the schedule and recipient options.
          </p>
          <form
            className="admin-edit-card project-panel-tab-card"
            onSubmit={(e) => void handleCorrespondenceSave(e)}
          >
            <div className="form-grid">
              <label htmlFor="corr-enabled">Enabled</label>
              <span>
                <input
                  id="corr-enabled"
                  type="checkbox"
                  checked={correspondenceDraft.enabled}
                  onChange={(e) =>
                    setCorrespondenceDraft({ ...correspondenceDraft, enabled: e.target.checked })
                  }
                />{' '}
                Send this correspondence on the schedule below
              </span>

              <label htmlFor="corr-sequence">Sequence</label>
              <select
                id="corr-sequence"
                value={correspondenceDraft.sequence}
                onChange={(e) =>
                  setCorrespondenceDraft({ ...correspondenceDraft, sequence: e.target.value })
                }
              >
                {correspondenceDraft.sequenceOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="corr-day">Day</label>
              <select
                id="corr-day"
                value={correspondenceDraft.dayOfWeek}
                onChange={(e) =>
                  setCorrespondenceDraft({ ...correspondenceDraft, dayOfWeek: e.target.value })
                }
              >
                {correspondenceDraft.dayOfWeekOptions.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>

              <label htmlFor="corr-time">Send time</label>
              <input
                id="corr-time"
                type="time"
                value={correspondenceDraft.sendTimeLocal}
                onChange={(e) =>
                  setCorrespondenceDraft({ ...correspondenceDraft, sendTimeLocal: e.target.value })
                }
                aria-describedby="corr-time-help"
              />

              <label htmlFor="corr-tz">Time zone</label>
              <select
                id="corr-tz"
                value={correspondenceDraft.timeZoneId}
                onChange={(e) =>
                  setCorrespondenceDraft({ ...correspondenceDraft, timeZoneId: e.target.value })
                }
              >
                <option value="Eastern Standard Time">Eastern (EDT / EST)</option>
                <option value="Central Standard Time">Central (CDT / CST)</option>
                <option value="Mountain Standard Time">Mountain (MDT / MST)</option>
                <option value="Pacific Standard Time">Pacific (PDT / PST)</option>
                <option value="UTC">UTC</option>
              </select>

              <label htmlFor="corr-recipient">Addressed to</label>
              <select
                id="corr-recipient"
                value={correspondenceDraft.recipientType}
                onChange={(e) =>
                  setCorrespondenceDraft({ ...correspondenceDraft, recipientType: e.target.value })
                }
              >
                {correspondenceDraft.recipientOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="corr-comm">What to send</label>
              <select
                id="corr-comm"
                value={correspondenceDraft.communicationType}
                onChange={(e) =>
                  setCorrespondenceDraft({ ...correspondenceDraft, communicationType: e.target.value })
                }
              >
                {correspondenceDraft.communicationOptions.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.text}
                  </option>
                ))}
              </select>

              <label htmlFor="corr-cc">CC</label>
              <input
                id="corr-cc"
                type="email"
                value={correspondenceDraft.ccAddress}
                onChange={(e) =>
                  setCorrespondenceDraft({ ...correspondenceDraft, ccAddress: e.target.value })
                }
                placeholder="Scott.Christian@bmsgroup.com"
              />
            </div>
            <div className="button-row">
              <button type="submit" className="primary-button" disabled={correspondenceBusy}>
                {correspondenceBusy ? 'Saving…' : 'Save schedule'}
              </button>
            </div>
            <p id="corr-time-help" className="field-help">
              Default schedule: every <strong>Monday</strong> at <strong>7:30 AM Eastern</strong>, weekly, addressed to{' '}
              <strong>Each Cat Member</strong>, with you on CC. The email body will cover{' '}
              <strong>Online</strong> assigned projects only (tasks, completion %, time tracking) and will be added in a
              follow-up change; saving here only stores the schedule and options under <code>App_Data</code>.
            </p>
          </form>
        </div>

        <div id="admin-tab-users" className={'admin-tab-panel' + (tab === 'users' ? ' active' : '')}>
          <h2>Users</h2>
          <p className="field-help">
            Manage users, user roles, and user groups from one place.
          </p>
          <div className="admin-tabs admin-users-subtabs" role="tablist" aria-label="Users sections">
            <button
              type="button"
              role="tab"
              id="admin-users-tab-directory"
              aria-selected={usersSubTab === 'users'}
              aria-controls="admin-users-panel-directory"
              className={usersSubTab === 'users' ? 'active' : ''}
              onClick={() => setUsersSubTab('users')}
            >
              Add / Edit Users
            </button>
            <button
              type="button"
              role="tab"
              id="admin-users-tab-roles"
              aria-selected={usersSubTab === 'roles'}
              aria-controls="admin-users-panel-roles"
              className={usersSubTab === 'roles' ? 'active' : ''}
              onClick={() => setUsersSubTab('roles')}
            >
              User Role
            </button>
            <button
              type="button"
              role="tab"
              id="admin-users-tab-groups"
              aria-selected={usersSubTab === 'groups'}
              aria-controls="admin-users-panel-groups"
              className={usersSubTab === 'groups' ? 'active' : ''}
              onClick={() => setUsersSubTab('groups')}
            >
              User Group
            </button>
          </div>

          <div
            id="admin-users-panel-directory"
            role="tabpanel"
            aria-labelledby="admin-users-tab-directory"
            hidden={usersSubTab !== 'users'}
            className={'admin-tab-panel admin-users-subpanel' + (usersSubTab === 'users' ? ' active' : '')}
          >
          <div className="page-header-top admin-users-toolbar">
            <h3 className="admin-subheading">
              <span className="admin-users-count-num">{userActiveCounts.total}</span> Users
              <span className="admin-client-count-chips">
                <span className="admin-client-count-chip admin-client-count-chip--active">
                  Active <strong>{userActiveCounts.active}</strong>
                </span>
                <span className="admin-client-count-chip admin-client-count-chip--inactive">
                  Inactive <strong>{userActiveCounts.inactive}</strong>
                </span>
              </span>
              {userDepartmentCounts.length > 0 ? (
                <span className="admin-dept-count-chips">
                  {userDepartmentCounts.map((d) => {
                    const color = deptColorByIndex(d.colorIndex);
                    return (
                      <span
                        key={d.key}
                        className="admin-dept-count-chip"
                        style={{ backgroundColor: color.bg, color: color.fg }}
                        title={`${d.label}: ${d.count} user${d.count === 1 ? '' : 's'}`}
                      >
                        {d.label} <strong>{d.count}</strong>
                      </span>
                    );
                  })}
                </span>
              ) : null}
            </h3>
            <div className="page-header-actions">
              <input
                id="admin-user-bulk-csv"
                type="file"
                accept=".csv,text/csv"
                className="visually-hidden"
                disabled={userBulkBusy}
                onChange={(ev) => void handleUserBulkImportFile(ev)}
              />
              <label
                htmlFor="admin-user-bulk-csv"
                className={
                  'secondary-button bulk-import-label' +
                  (userBulkBusy || userBulkWizardOpen ? ' disabled-label' : '')
                }
              >
                Bulk import
              </label>
            </div>
          </div>
          <div className="admin-layout admin-users-layout">
            <div className="admin-users-left">
              <div className="admin-users-dept-row">
                <label htmlFor="admin-user-dept-filter">Department</label>
                <select
                  id="admin-user-dept-filter"
                  className="admin-users-dept-select"
                  value={userDepartmentFilter}
                  onChange={(e) => setUserDepartmentFilter(e.target.value)}
                  aria-label="Filter users by department"
                >
                  <option value="">All departments</option>
                  {distinctUserDepartments.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                </select>
              </div>
              <div className="admin-users-filter-row admin-search-row">
                <label htmlFor="admin-user-directory-search" className="sr-only">
                  Search users by name
                </label>
                <input
                  id="admin-user-directory-search"
                  type="search"
                  className="admin-search-input"
                  placeholder="Search by first name, last name, or email..."
                  value={userDirectorySearch}
                  onChange={(e) => setUserDirectorySearch(e.target.value)}
                  aria-controls="admin-user-pick-list"
                  autoComplete="off"
                />
              </div>
              <div className="admin-client-tabs" role="tablist" aria-label="User status">
                {(['Active', 'Inactive'] as const).map((statusTab) => (
                  <button
                    key={statusTab}
                    type="button"
                    role="tab"
                    aria-selected={userActiveTab === statusTab}
                    className={'admin-client-tab' + (userActiveTab === statusTab ? ' selected' : '')}
                    onClick={() => setUserActiveTab(statusTab)}
                  >
                    {statusTab}
                    <span className="admin-client-tab-count">
                      {statusTab === 'Active' ? userActiveCounts.active : userActiveCounts.inactive}
                    </span>
                  </button>
                ))}
              </div>
              <div
                id="admin-user-pick-list"
                className="admin-user-pick-list"
                role="listbox"
                aria-label={`${userActiveTab} users`}
                aria-activedescendant={selectedUserId === '' ? undefined : `admin-user-row-${selectedUserId}`}
              >
                {filteredUsersForDirectory.length === 0 ? (
                  <div className="admin-user-pick-empty" role="status">
                    {admin.users.length === 0
                      ? 'No users loaded.'
                      : `No ${userActiveTab.toLowerCase()} users match your filters.`}
                  </div>
                ) : (
                  filteredUsersForDirectory.map((u) => {
                    const first = (u.firstName ?? '').trim();
                    const last = (u.lastName ?? '').trim();
                    const fullName = [first, last].filter((x) => x !== '').join(' ') || '-';
                    const titleLine = (u.title ?? '').trim();
                    const emailLine = (u.email ?? '').trim() || (u.displayName ?? '').trim();
                    const phone = (u.phone ?? '').trim();
                    const isInactive = u.isDeleted === true;
                    const dept = (u.department ?? '').trim();
                    const deptColor = deptColorByIndex(dept ? departmentColorIndex.get(dept.toLowerCase()) ?? -1 : -1);
                    return (
                      <button
                        key={u.userID}
                        id={`admin-user-row-${u.userID}`}
                        type="button"
                        role="option"
                        aria-selected={selectedUserId === u.userID}
                        className={'admin-user-pick-row' + (selectedUserId === u.userID ? ' selected' : '')}
                        onClick={() => setSelectedUserId(u.userID)}
                        onDoubleClick={() => {
                          setSelectedUserId(u.userID);
                          void applyUserToForm(u);
                        }}
                      >
                        <span className="admin-user-pick-name">
                          <span className="admin-user-pick-name-text">{fullName}</span>
                          <span className="admin-user-pick-badges">
                            {isInactive ? (
                              <span
                                className="admin-user-pick-status admin-user-pick-status--inactive"
                                title="Inactive user"
                              >
                                Inactive
                              </span>
                            ) : null}
                            {dept ? (
                              <span
                                className="admin-user-pick-dept"
                                style={{ backgroundColor: deptColor.bg, color: deptColor.fg }}
                                title={`Department: ${dept}`}
                              >
                                {dept}
                              </span>
                            ) : null}
                          </span>
                        </span>
                        <span className="admin-user-pick-title">{titleLine || '\u00a0'}</span>
                        <span className="admin-user-pick-meta">{emailLine || '\u00a0'}</span>
                        <span className="admin-user-pick-meta">{phone || '\u00a0'}</span>
                      </button>
                    );
                  })
                )}
              </div>
            </div>

            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void submitUser(e)}>
              <input
                ref={userPhotoInputRef}
                type="file"
                accept="image/jpeg,image/png,image/gif,image/webp"
                className="visually-hidden"
                aria-hidden="true"
                tabIndex={-1}
                onChange={(ev) => void handleUserPhotoFileChange(ev)}
              />
              <div className="admin-user-photo-row">
                <div className="admin-user-photo-thumb-wrap">
                  {userForm.userID > 0 ? (
                    <>
                      <img
                        key={`${userForm.userID}-${userPhotoRevision}`}
                        className={
                          'admin-user-photo-thumb' + (userPhotoBroken ? ' admin-user-photo-thumb--hidden' : '')
                        }
                        alt=""
                        src={`/api/admin/users/${userForm.userID}/photo?r=${userPhotoRevision}`}
                        onError={() => setUserPhotoBroken(true)}
                        onLoad={() => setUserPhotoBroken(false)}
                      />
                      {userPhotoBroken ? (
                        <div className="admin-user-photo-placeholder">No photo</div>
                      ) : null}
                    </>
                  ) : (
                    <div className="admin-user-photo-placeholder">Save user first</div>
                  )}
                </div>
                <div className="admin-user-photo-actions">
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={userForm.userID <= 0}
                    onClick={() => userPhotoInputRef.current?.click()}
                  >
                    Upload picture
                  </button>
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={userForm.userID <= 0}
                    onClick={() => void handleRemoveUserPhoto()}
                  >
                    Remove picture
                  </button>
                  <p className="field-help admin-user-photo-help">
                    JPEG, PNG, GIF, or WebP. Maximum size 5 MB. Stored on the application server under{' '}
                    <code>App_Data/UserPhotos</code>.
                  </p>
                </div>
              </div>
              <div className="form-grid compact-grid">
                <label>First Name</label>
                <input
                  maxLength={100}
                  value={userForm.firstName}
                  onChange={(e) => setUserForm({ ...userForm, firstName: e.target.value })}
                />
                <label>Last Name</label>
                <input
                  maxLength={100}
                  value={userForm.lastName}
                  onChange={(e) => setUserForm({ ...userForm, lastName: e.target.value })}
                />
                <label>Title</label>
                <input
                  maxLength={100}
                  value={userForm.title}
                  onChange={(e) => setUserForm({ ...userForm, title: e.target.value })}
                />
                <label>Department</label>
                <select
                  value={userForm.department}
                  onChange={(e) => setUserForm({ ...userForm, department: e.target.value })}
                  aria-label="Department"
                >
                  <option value="">- Select Department -</option>
                  {userFormDepartmentOptions.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                </select>
                <label>Email</label>
                <input
                  maxLength={100}
                  value={userForm.email}
                  onChange={(e) => setUserForm({ ...userForm, email: e.target.value })}
                />
                <label>Phone</label>
                <input
                  maxLength={20}
                  value={userForm.phone}
                  onChange={(e) => setUserForm({ ...userForm, phone: e.target.value })}
                />
                <label>Company</label>
                <input
                  maxLength={50}
                  value={userForm.company}
                  onChange={(e) => setUserForm({ ...userForm, company: e.target.value })}
                />
                <label>User type</label>
                <select
                  value={userForm.userType}
                  onChange={(e) =>
                    setUserForm({ ...userForm, userType: normalizeUserType(e.target.value) })
                  }
                  aria-label="User type (Basic, External, Administrator, Manager, or Analyst)"
                >
                  <option value="Basic">Basic</option>
                  <option value="External">External</option>
                  <option value="Administrator">Administrator</option>
                  <option value="Manager">Manager</option>
                  <option value="Analyst">Analyst</option>
                </select>
                <label>Address type</label>
                <select
                  value={userForm.addressType}
                  onChange={(e) => setUserForm({ ...userForm, addressType: e.target.value })}
                  aria-label="Address type"
                >
                  <option value="Work">Work</option>
                  <option value="Home">Home</option>
                  <option value="Mailing">Mailing</option>
                  <option value="Other">Other</option>
                </select>
                <label>Address</label>
                <input
                  maxLength={100}
                  value={userForm.address}
                  onChange={(e) => setUserForm({ ...userForm, address: e.target.value })}
                />
                <label>City</label>
                <input
                  maxLength={25}
                  value={userForm.city}
                  onChange={(e) => setUserForm({ ...userForm, city: e.target.value })}
                />
                <label>State</label>
                <select
                  value={userStateSelectValue(userForm.state)}
                  onChange={(e) => setUserForm({ ...userForm, state: e.target.value })}
                  aria-label="State (US postal code)"
                >
                  <option value="">-</option>
                  {US_STATE_CODES.map((code) => (
                    <option key={code} value={code}>
                      {code}
                    </option>
                  ))}
                  {(() => {
                    const raw = userForm.state.trim();
                    if (!raw) {
                      return null;
                    }
                    const upper = raw.toUpperCase();
                    if (US_STATE_CODE_SET.has(upper)) {
                      return null;
                    }
                    return (
                      <option key="__legacy_state" value={userForm.state}>
                        {userForm.state}
                      </option>
                    );
                  })()}
                </select>
                <label>Postal code</label>
                <input
                  maxLength={25}
                  value={userForm.postalCode}
                  onChange={(e) => setUserForm({ ...userForm, postalCode: e.target.value })}
                  autoComplete="postal-code"
                />
                <label>Country</label>
                <input
                  maxLength={25}
                  value={userForm.country}
                  onChange={(e) => setUserForm({ ...userForm, country: e.target.value })}
                />
              </div>
              <div className="button-row wrap-buttons admin-user-form-actions">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearUserForm}>
                  Cancel
                </button>
                <span className="admin-user-action-divider" aria-hidden="true" />
                <button type="button" className="secondary-button" onClick={clearUserForm}>
                  Add User
                </button>
                <button type="button" className="secondary-button" onClick={editUserFromSelection}>
                  Edit User
                </button>
                <button type="button" className="danger-button" onClick={openRemoveUserDialog}>
                  Remove User
                </button>
                <span className="admin-user-copy-inline">
                  <input
                    maxLength={100}
                    value={copyEmail}
                    onChange={(e) => setCopyEmail(e.target.value)}
                    placeholder="New email for copied user"
                    aria-label="New email for copied user"
                  />
                  <button type="button" className="secondary-button" onClick={() => void copyUser()}>
                    Copy User
                  </button>
                </span>
              </div>
            </form>
          </div>
          </div>

          <div
            id="admin-users-panel-roles"
            role="tabpanel"
            aria-labelledby="admin-users-tab-roles"
            hidden={usersSubTab !== 'roles'}
            className={'admin-tab-panel admin-users-subpanel' + (usersSubTab === 'roles' ? ' active' : '')}
          >
          <h3 className="admin-subheading">User Role</h3>
          <div className="admin-layout">
            <div>
              <select
                className="cms-listbox"
                size={25}
                value={selectedRoleId === '' ? '' : String(selectedRoleId)}
                onChange={(e) => setSelectedRoleId(e.target.value ? Number(e.target.value) : '')}
                onDoubleClick={editRoleFromSelection}
              >
                {admin.userRoles.map((r) => (
                  <option key={r.userRoleID} value={r.userRoleID}>
                    {r.userRoleName ?? ''}
                  </option>
                ))}
              </select>
              <div className="button-row wrap-buttons">
                <button type="button" className="secondary-button" onClick={clearRoleForm}>
                  Add
                </button>
                <button type="button" className="secondary-button" onClick={editRoleFromSelection}>
                  Edit
                </button>
                <button type="button" className="danger-button" onClick={(e) => void deleteRole(e)}>
                  Remove
                </button>
              </div>
            </div>
            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void saveRole(e)}>
              <div className="form-grid compact-grid">
                <label>Role Name</label>
                <input
                  maxLength={100}
                  value={roleForm.userRoleName}
                  onChange={(e) => setRoleForm({ ...roleForm, userRoleName: e.target.value })}
                />
                <label>Role Description</label>
                <input
                  maxLength={255}
                  value={roleForm.roleDescription}
                  onChange={(e) => setRoleForm({ ...roleForm, roleDescription: e.target.value })}
                />
                <label>Is Active</label>
                <select
                  value={roleForm.isActive ? 'true' : 'false'}
                  onChange={(e) => setRoleForm({ ...roleForm, isActive: e.target.value === 'true' })}
                  aria-label="Is Active"
                >
                  <option value="true">True</option>
                  <option value="false">False</option>
                </select>
              </div>
              <div className="button-row">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearRoleForm}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
          </div>

          <div
            id="admin-users-panel-groups"
            role="tabpanel"
            aria-labelledby="admin-users-tab-groups"
            hidden={usersSubTab !== 'groups'}
            className={'admin-tab-panel admin-users-subpanel' + (usersSubTab === 'groups' ? ' active' : '')}
          >
          <h3 className="admin-subheading">User Group</h3>
          <div className="admin-usergroup-toolbar">
            <div className="admin-search-row">
              <input
                type="search"
                className="admin-search-input"
                placeholder="Search groups by name or number..."
                value={groupSearchInput}
                onChange={(e) => setGroupSearchInput(e.target.value)}
                disabled={groupPanelBusy}
              />
              <button
                type="button"
                className="secondary-button"
                disabled={groupPanelBusy}
                onClick={() => void runGroupSearch()}
              >
                Search
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={groupPanelBusy}
                onClick={clearGroupTableSearch}
              >
                Clear filter
              </button>
            </div>
          </div>

          {groupSearchNoMatch ? (
            <p className="field-help admin-warning" role="status">
              No groups matched your search. Try different keywords or clear the filter and search again.
            </p>
          ) : null}

          <div className="admin-layout">
            <div>
              <div className="admin-usergroup-table-wrap">
                <table
                  id="lstbxUserGroup"
                  className="admin-usergroup-table admin-usergroup-list-table"
                  aria-label="User groups"
                >
                  <caption className="sr-only">User groups (UGID | Number | Name | Members)</caption>
                  <thead>
                    <tr>
                      <th scope="col">UGID</th>
                      <th scope="col">Group #</th>
                      <th scope="col">Name</th>
                      <th scope="col">Members</th>
                    </tr>
                  </thead>
                  <tbody>
                    {displayedUserGroupRows.length === 0 ? (
                      <tr>
                        <td colSpan={4}>
                          {groupListUseSearchResults
                            ? 'No groups in this search result.'
                            : 'No group summaries loaded yet. Use Search or reload the page.'}
                        </td>
                      </tr>
                    ) : (
                      displayedUserGroupRows.map((g) => {
                        const num = g.userGroupNumber?.trim() || '-';
                        const name = g.ugName?.trim() || '-';
                        const members = `${g.memberCount} member${g.memberCount === 1 ? '' : 's'}`;
                        return (
                          <tr
                            key={g.ugid}
                            className={selectedUserGroupId === g.ugid ? 'selected' : ''}
                            onClick={() => !groupPanelBusy && setSelectedUserGroupId(g.ugid)}
                            onDoubleClick={() => {
                              if (!groupPanelBusy) {
                                selectUserGroupByNumber(g.ugid, g);
                              }
                            }}
                            title={userGroupListLabel(g)}
                          >
                            <td className="admin-usergroup-col-id">{g.ugid}</td>
                            <td className="admin-usergroup-col-num">
                              <span className="admin-usergroup-pipe-sep" aria-hidden="true">
                                |
                              </span>
                              {num}
                            </td>
                            <td className="admin-usergroup-col-name">
                              <span className="admin-usergroup-pipe-sep" aria-hidden="true">
                                |
                              </span>
                              {name}
                            </td>
                            <td className="admin-usergroup-col-members">
                              <span className="admin-usergroup-pipe-sep" aria-hidden="true">
                                |
                              </span>
                              {members}
                            </td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                </table>
              </div>
              <div className="button-row wrap-buttons">
                <button
                  type="button"
                  className="secondary-button"
                  disabled={groupPanelBusy}
                  onClick={startNewUserGroup}
                >
                  Add
                </button>
                <button
                  type="button"
                  className="secondary-button"
                  disabled={groupPanelBusy}
                  onClick={editGroupFromSelection}
                >
                  Edit
                </button>
                <button
                  type="button"
                  className="danger-button"
                  disabled={groupPanelBusy || selectedUserGroupId === ''}
                  onClick={() => void deleteUserGroup()}
                >
                  Delete
                </button>
              </div>
            </div>

            <div
              id="lstbxNewUserGroup"
              ref={groupEditPanelRef}
              className={
                'admin-edit-card project-panel-tab-card admin-usergroup-new-panel' +
                (auditGroupHighlight ? ' admin-usergroup-new-panel--from-audit' : '')
              }
            >
              {auditGroupHighlightMessage ? (
                <div className="admin-audit-jump-banner" role="status">
                  <div>
                    <strong>From Data Audit</strong>
                    <p>{auditGroupHighlightMessage}</p>
                  </div>
                  <button
                    type="button"
                    className="secondary-button"
                    onClick={() => {
                      setAuditGroupHighlight(null);
                      setAuditGroupHighlightMessage(null);
                    }}
                  >
                    Dismiss
                  </button>
                </div>
              ) : null}
              <p className="field-help">
                {isCreatingGroup
                  ? 'New group: enter a name, pick users and roles, then save. Group number defaults to the next available (max + 1).'
                  : groupEditUgid > 0
                    ? 'Editing selected group. Update details, manage members, then save.'
                    : 'Select a group and choose Edit, or choose Add to create a new group.'}
              </p>
              <form onSubmit={(e) => void saveGroupBatch(e)}>
                <div
                  className={
                    'admin-audit-target' +
                    (auditGroupHighlight === 'project-link' || auditGroupHighlight === 'client-link'
                      ? ' admin-audit-target--active'
                      : '')
                  }
                >
                  <h3 className="admin-subheading">Group details</h3>
                  <div className="form-grid compact-grid">
                    <label htmlFor="ug-group-name">User group name</label>
                    <input
                      id="ug-group-name"
                      maxLength={50}
                      placeholder="Required"
                      value={groupEditName}
                      onChange={(e) => setGroupEditName(e.target.value)}
                      disabled={groupPanelBusy}
                    />
                    <label htmlFor="ug-group-num">Group number</label>
                    <input
                      id="ug-group-num"
                      maxLength={50}
                      placeholder={isCreatingGroup ? 'Next number' : 'Required'}
                      value={groupEditNumber}
                      onChange={(e) => setGroupEditNumber(e.target.value)}
                      disabled={groupPanelBusy}
                      readOnly={isCreatingGroup}
                      title={
                        isCreatingGroup
                          ? 'Auto-assigned as max existing group number + 1'
                          : undefined
                      }
                    />
                  </div>
                </div>

                <div
                  className={
                    'admin-audit-target' +
                    (auditGroupHighlight === 'members' ? ' admin-audit-target--active' : '')
                  }
                >
                <h3 className="admin-subheading">Add member</h3>
                <div className="admin-usergroup-member-block">
                  <label htmlFor="ug-filter-user">Find user (name or city)</label>
                  <input
                    id="ug-filter-user"
                    type="search"
                    className="admin-usergroup-filter-input admin-usergroup-filter-input--full"
                    placeholder="Search by name, email, city, company..."
                    value={newGroupUserFilterDraft}
                    onChange={(e) => setNewGroupUserFilterDraft(e.target.value)}
                    disabled={groupPanelBusy}
                    autoComplete="off"
                    aria-label="Search users by name or city"
                  />
                  <label htmlFor="ug-add-user">Users</label>
                  <select
                    id="ug-add-user"
                    className="cms-listbox admin-usergroup-pick-list"
                    size={10}
                    value={addMemberUserId === '' ? '' : String(addMemberUserId)}
                    onChange={(e) => setAddMemberUserId(e.target.value ? Number(e.target.value) : '')}
                    disabled={groupPanelBusy}
                  >
                    <option value="">- Select user -</option>
                    {filteredBmsUsersForGroup.map((u) => (
                      <option key={u.userID} value={u.userID}>
                        {userFirstLastLabel(u)}
                      </option>
                    ))}
                  </select>
                  <p className="field-help admin-usergroup-list-hint" role="status">
                    {filteredBmsUsersForGroup.length} user
                    {filteredBmsUsersForGroup.length === 1 ? '' : 's'} shown
                    {newGroupUserFilterDraft.trim() ? ` (filtered from ${admin?.bmsUsers.length ?? 0} total)` : ''}.
                  </p>
                </div>

                <div className="admin-usergroup-member-block">
                  <label htmlFor="ug-add-role">User roles</label>
                  <select
                    id="ug-add-role"
                    className="cms-listbox admin-usergroup-pick-list"
                    size={8}
                    value={addMemberRoleId === '' ? '' : String(addMemberRoleId)}
                    onChange={(e) => setAddMemberRoleId(e.target.value ? Number(e.target.value) : '')}
                    disabled={groupPanelBusy}
                  >
                    <option value="">- Select role -</option>
                    {filteredRolesForGroup.map((r) => (
                      <option key={r.userRoleID} value={r.userRoleID}>
                        {rolePickerLabel(r)}
                      </option>
                    ))}
                  </select>
                  <div className="admin-usergroup-inline-search">
                    <input
                      id="ug-filter-role"
                      type="search"
                      className="admin-usergroup-filter-input"
                      placeholder="Type to filter, then Search"
                      value={newGroupRoleFilterDraft}
                      onChange={(e) => setNewGroupRoleFilterDraft(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                          e.preventDefault();
                          setNewGroupRoleFilterQuery(newGroupRoleFilterDraft.trim());
                        }
                      }}
                      disabled={groupPanelBusy}
                      aria-label="Search user roles"
                    />
                    <button
                      type="button"
                      className="secondary-button"
                      disabled={groupPanelBusy}
                      onClick={() => setNewGroupRoleFilterQuery(newGroupRoleFilterDraft.trim())}
                    >
                      Search
                    </button>
                  </div>
                </div>

                <div className="button-row wrap-buttons">
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={groupPanelBusy}
                    onClick={addMemberToGroup}
                  >
                    Add to group
                  </button>
                </div>

                <h3 className="admin-subheading">Group members</h3>
                <select
                  id="lstbxUserGroupMembers"
                  className="cms-listbox"
                  size={10}
                  value={selectedMemberRowKey}
                  onChange={(e) => setSelectedMemberRowKey(e.target.value)}
                  disabled={groupPanelBusy}
                  aria-label="Members of selected user group"
                >
                  {loadedGroupMembers.length === 0 && pendingGroupMembers.length === 0 ? (
                    <option value="">
                      {isCreatingGroup || groupEditUgid > 0
                        ? '- No members yet -'
                        : '- Select a group and choose Edit -'}
                    </option>
                  ) : null}
                  {loadedGroupMembers.map((m) => (
                    <option key={`db-${m.ugid}:${m.userID}`} value={`db-${m.ugid}:${m.userID}`}>
                      {memberLineLabel(m)}
                    </option>
                  ))}
                  {pendingGroupMembers.map((p) => (
                    <option key={p.tempId} value={`pending-${p.tempId}`}>
                      {pendingMemberLabel(p)}
                    </option>
                  ))}
                </select>
                <div className="button-row wrap-buttons">
                  <button
                    type="button"
                    className="danger-button"
                    disabled={groupPanelBusy}
                    onClick={(e) => void removeSelectedGroupMember(e)}
                  >
                    Remove selected member
                  </button>
                </div>
                </div>

                <div className="button-row wrap-buttons">
                  <button type="submit" className="primary-button" disabled={groupPanelBusy}>
                    Save group
                  </button>
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={groupPanelBusy}
                    onClick={startNewUserGroup}
                  >
                    Cancel
                  </button>
                </div>
              </form>
            </div>
          </div>
          </div>
        </div>

        <div id="admin-tab-projectTypes" className={'admin-tab-panel' + (tab === 'projectTypes' ? ' active' : '')}>
          <h2>Project Types</h2>
          <div className="admin-layout">
            <div>
              <select
                className="cms-listbox"
                size={25}
                value={selectedPtId === '' ? '' : String(selectedPtId)}
                onChange={(e) => setSelectedPtId(e.target.value ? Number(e.target.value) : '')}
                onDoubleClick={editPtFromSelection}
              >
                {admin.projectTypes.map((p) => (
                  <option key={p.projectTypeID} value={p.projectTypeID}>
                    {p.displayName}
                  </option>
                ))}
              </select>
              <div className="button-row wrap-buttons">
                <button type="button" className="secondary-button" onClick={clearPt}>
                  Add
                </button>
                <button type="button" className="secondary-button" onClick={editPtFromSelection}>
                  Edit
                </button>
                <button type="button" className="danger-button" onClick={(e) => void deletePt(e)}>
                  Remove
                </button>
              </div>
            </div>
            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void savePt(e)}>
              <div className="form-grid compact-grid">
                <label>Code</label>
                <input
                  maxLength={20}
                  required
                  value={ptForm.projectTypeCode}
                  onChange={(e) => setPtForm({ ...ptForm, projectTypeCode: e.target.value })}
                />
                <label>Description</label>
                <input
                  maxLength={255}
                  value={ptForm.projectTypeDescription}
                  onChange={(e) => setPtForm({ ...ptForm, projectTypeDescription: e.target.value })}
                />
              </div>
              <div className="button-row">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearPt}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>

        <div id="admin-tab-business-units" className={'admin-tab-panel' + (tab === 'businessUnits' ? ' active' : '')}>
          <h2>Business Unit</h2>
          <div className="admin-layout">
            <div>
              <select
                className="cms-listbox"
                size={25}
                value={selectedSegId === '' ? '' : String(selectedSegId)}
                onChange={(e) => setSelectedSegId(e.target.value ? Number(e.target.value) : '')}
                onDoubleClick={editSegFromSelection}
              >
                {admin.businessUnits.map((s) => (
                  <option key={s.businessUnitID} value={s.businessUnitID}>
                    {s.businessUnitName}
                  </option>
                ))}
              </select>
              <div className="button-row wrap-buttons">
                <button type="button" className="secondary-button" onClick={clearSeg}>
                  Add
                </button>
                <button type="button" className="secondary-button" onClick={editSegFromSelection}>
                  Edit
                </button>
                <button type="button" className="danger-button" onClick={(e) => void deleteSeg(e)}>
                  Remove
                </button>
              </div>
            </div>
            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void saveSeg(e)}>
              <div className="form-grid compact-grid">
                <label>Name</label>
                <input
                  maxLength={100}
                  value={segForm.businessUnitName}
                  onChange={(e) => setSegForm({ ...segForm, businessUnitName: e.target.value })}
                />
                <label>Description</label>
                <textarea
                  maxLength={1000}
                  value={segForm.businessUnitDescription}
                  onChange={(e) => setSegForm({ ...segForm, businessUnitDescription: e.target.value })}
                />
              </div>
              <div className="button-row">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearSeg}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>

        <div id="admin-tab-business-classes" className={'admin-tab-panel' + (tab === 'businessClasses' ? ' active' : '')}>
          <h2>Business Class</h2>
          <div className="admin-layout">
            <div>
              <div className="admin-usergroup-table-wrap admin-businessclass-table-wrap">
                <table id="lstbxBusinessClass" className="admin-usergroup-table admin-businessclass-table">
                  <caption className="sr-only">All rows from dbo.tBusinessClass</caption>
                  <thead>
                    <tr>
                      <th scope="col">BusinessClassID</th>
                      <th scope="col">BC Name</th>
                      <th scope="col">BC Description</th>
                    </tr>
                  </thead>
                  <tbody>
                    {admin.businessClasses.length === 0 ? (
                      <tr>
                        <td colSpan={3} className="admin-businessclass-empty">
                          No business classes loaded.
                        </td>
                      </tr>
                    ) : (
                      admin.businessClasses.map((c) => {
                        const desc = (c.businessClassDescription ?? '').trim();
                        return (
                          <tr
                            key={c.businessClassID}
                            className={selectedBcId === c.businessClassID ? 'selected' : ''}
                            onClick={() => setSelectedBcId(c.businessClassID)}
                            onDoubleClick={() => editBcFromSelection(c)}
                          >
                            <td className="admin-businessclass-num">{c.businessClassID}</td>
                            <td>{(c.businessClassName ?? '').trim() || '-'}</td>
                            <td className="admin-businessclass-desc" title={desc || undefined}>
                              {desc || '-'}
                            </td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                </table>
              </div>
              <div className="button-row wrap-buttons">
                <button type="button" className="secondary-button" onClick={clearBc}>
                  Add
                </button>
                <button type="button" className="secondary-button" onClick={() => editBcFromSelection()}>
                  Edit
                </button>
                <button type="button" className="danger-button" onClick={(e) => void deleteBc(e)}>
                  Remove
                </button>
              </div>
            </div>
            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void saveBc(e)}>
              <div className="form-grid compact-grid">
                <label htmlFor="bc-business-unit">Business Unit</label>
                <select
                  id="bc-business-unit"
                  className="cms-listbox admin-bc-bu-pick"
                  size={Math.min(12, Math.max(3, admin.businessUnits.length + 1))}
                  value={bcForm.businessUnitID > 0 ? String(bcForm.businessUnitID) : ''}
                  onChange={(e) =>
                    setBcForm((prev) => ({
                      ...prev,
                      businessUnitID: e.target.value ? Number(e.target.value) : 0,
                    }))
                  }
                  onMouseDown={(e) => e.stopPropagation()}
                  onClick={(e) => e.stopPropagation()}
                  aria-label="Owning Business Unit"
                >
                  <option value="">- Select Business Unit -</option>
                  {admin.businessUnits.map((u) => (
                    <option key={u.businessUnitID} value={String(u.businessUnitID)}>
                      {u.businessUnitName?.trim() || `BU ${u.businessUnitID}`}
                    </option>
                  ))}
                </select>
                <label htmlFor="bc-name">Name</label>
                <input
                  id="bc-name"
                  maxLength={100}
                  value={bcForm.businessClassName}
                  onChange={(e) => setBcForm((prev) => ({ ...prev, businessClassName: e.target.value }))}
                />
                <label htmlFor="bc-description">Description</label>
                <textarea
                  id="bc-description"
                  maxLength={1000}
                  value={bcForm.businessClassDescription}
                  onChange={(e) => setBcForm((prev) => ({ ...prev, businessClassDescription: e.target.value }))}
                />
              </div>
              <div className="button-row">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearBc}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>

        <div id="admin-tab-client-types" className={'admin-tab-panel' + (tab === 'clientTypes' ? ' active' : '')}>
          <h2>Client Type</h2>
          <div className="admin-layout">
            <div>
              <div className="admin-usergroup-table-wrap admin-businessclass-table-wrap">
                <table id="lstbxClientType" className="admin-usergroup-table admin-businessclass-table">
                  <caption className="sr-only">All rows from dbo.tClientType</caption>
                  <thead>
                    <tr>
                      <th scope="col">ClientTypeID</th>
                      <th scope="col">CT Name</th>
                      <th scope="col">CT Description</th>
                      <th scope="col">Is Active</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(admin.clientTypes ?? []).length === 0 ? (
                      <tr>
                        <td colSpan={4} className="admin-businessclass-empty">
                          No client types loaded.
                        </td>
                      </tr>
                    ) : (
                      (admin.clientTypes ?? []).map((c) => {
                        const desc = (c.ctDescription ?? '').trim();
                        return (
                          <tr
                            key={c.clientTypeID}
                            className={selectedCtId === c.clientTypeID ? 'selected' : ''}
                            onClick={() => setSelectedCtId(c.clientTypeID)}
                            onDoubleClick={() => editCtFromSelection(c)}
                          >
                            <td className="admin-businessclass-num">{c.clientTypeID}</td>
                            <td>{(c.ctName ?? '').trim() || '-'}</td>
                            <td className="admin-businessclass-desc" title={desc || undefined}>
                              {desc || '-'}
                            </td>
                            <td>{(c.isActive ?? true) ? 'True' : 'False'}</td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                </table>
              </div>
              <div className="button-row wrap-buttons">
                <button type="button" className="secondary-button" onClick={clearCt}>
                  Add
                </button>
                <button type="button" className="secondary-button" onClick={() => editCtFromSelection()}>
                  Edit
                </button>
                <button type="button" className="danger-button" onClick={(e) => void deleteCt(e)}>
                  Remove
                </button>
              </div>
            </div>
            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void saveCt(e)}>
              <div className="form-grid compact-grid">
                <label htmlFor="ct-name">Name</label>
                <input
                  id="ct-name"
                  maxLength={100}
                  value={ctForm.ctName}
                  onChange={(e) => setCtForm((prev) => ({ ...prev, ctName: e.target.value }))}
                />
                <label htmlFor="ct-description">Description</label>
                <textarea
                  id="ct-description"
                  maxLength={255}
                  value={ctForm.ctDescription}
                  onChange={(e) => setCtForm((prev) => ({ ...prev, ctDescription: e.target.value }))}
                />
                <label htmlFor="ct-is-active">Is Active</label>
                <select
                  id="ct-is-active"
                  className="admin-users-dept-select"
                  value={ctForm.isActive ? 'true' : 'false'}
                  onChange={(e) => setCtForm((prev) => ({ ...prev, isActive: e.target.value === 'true' }))}
                >
                  <option value="true">True</option>
                  <option value="false">False</option>
                </select>
              </div>
              <div className="button-row">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearCt}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>

        <div id="admin-tab-services" className={'admin-tab-panel' + (tab === 'services' ? ' active' : '')}>
          <h2>Services</h2>
          <p className="field-help">
            Manage service groups, service categories, and catalog services from one place.
          </p>
          <div className="admin-tabs admin-services-subtabs" role="tablist" aria-label="Services sections">
            <button
              type="button"
              role="tab"
              id="admin-services-tab-groups"
              aria-selected={servicesSubTab === 'groups'}
              aria-controls="admin-services-panel-groups"
              className={servicesSubTab === 'groups' ? 'active' : ''}
              onClick={() => setServicesSubTab('groups')}
            >
              Service Groups
            </button>
            <button
              type="button"
              role="tab"
              id="admin-services-tab-categories"
              aria-selected={servicesSubTab === 'categories'}
              aria-controls="admin-services-panel-categories"
              className={servicesSubTab === 'categories' ? 'active' : ''}
              onClick={() => setServicesSubTab('categories')}
            >
              Service Category
            </button>
            <button
              type="button"
              role="tab"
              id="admin-services-tab-catalog"
              aria-selected={servicesSubTab === 'services'}
              aria-controls="admin-services-panel-catalog"
              className={servicesSubTab === 'services' ? 'active' : ''}
              onClick={() => setServicesSubTab('services')}
            >
              Services
            </button>
          </div>

          <div
            id="admin-services-panel-groups"
            role="tabpanel"
            aria-labelledby="admin-services-tab-groups"
            hidden={servicesSubTab !== 'groups'}
            className={'admin-tab-panel admin-services-subpanel' + (servicesSubTab === 'groups' ? ' active' : '')}
          >
            <h3 className="admin-subheading">Service Groups</h3>
            <p className="field-help">
              Service groups from <code>dbo.tServiceGroup</code>. Groups organize catalog services on{' '}
              <code>tServiceRef</code> rows.
            </p>
            <div className="admin-layout">
              <div>
                <select
                  className="cms-listbox"
                  size={25}
                  value={selectedServiceGroupId === '' ? '' : String(selectedServiceGroupId)}
                  onChange={(e) => setSelectedServiceGroupId(e.target.value ? Number(e.target.value) : '')}
                  onDoubleClick={editSgFromSelection}
                  aria-label="Service groups"
                >
                  {[...(admin.serviceGroups ?? [])]
                    .sort((a, b) =>
                      (a.serviceGroupName ?? '').localeCompare(b.serviceGroupName ?? '', undefined, {
                        sensitivity: 'base',
                      }),
                    )
                    .map((g) => (
                      <option key={g.serviceGroupID} value={g.serviceGroupID}>
                        {g.isActive === false
                          ? `${(g.serviceGroupName ?? '').trim() || g.serviceGroupID} (inactive)`
                          : (g.serviceGroupName ?? '').trim() || String(g.serviceGroupID)}
                      </option>
                    ))}
                </select>
                <div className="button-row wrap-buttons">
                  <button type="button" className="secondary-button" onClick={startNewServiceGroup}>
                    Add
                  </button>
                  <button type="button" className="secondary-button" onClick={editSgFromSelection}>
                    Edit
                  </button>
                  <button type="button" className="danger-button" onClick={(e) => void deleteSg(e)}>
                    Remove
                  </button>
                </div>
              </div>
              <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void saveSg(e)}>
                <div className="form-grid compact-grid">
                  <label htmlFor="sg-number">Group Number</label>
                  <input
                    id="sg-number"
                    maxLength={50}
                    value={sgForm.serviceGroupNumber}
                    onChange={(e) => setSgForm({ ...sgForm, serviceGroupNumber: e.target.value })}
                    placeholder="Auto if blank"
                  />
                  <label htmlFor="sg-name">Group Name</label>
                  <input
                    id="sg-name"
                    maxLength={100}
                    value={sgForm.serviceGroupName}
                    onChange={(e) => setSgForm({ ...sgForm, serviceGroupName: e.target.value })}
                    required
                  />
                  <label htmlFor="sg-desc">Description</label>
                  <input
                    id="sg-desc"
                    maxLength={255}
                    value={sgForm.serviceGroupDescription}
                    onChange={(e) => setSgForm({ ...sgForm, serviceGroupDescription: e.target.value })}
                  />
                  <label htmlFor="sg-active">Is Active</label>
                  <select
                    id="sg-active"
                    value={sgForm.isActive ? 'true' : 'false'}
                    onChange={(e) => setSgForm({ ...sgForm, isActive: e.target.value === 'true' })}
                    aria-label="Is Active"
                  >
                    <option value="true">True</option>
                    <option value="false">False</option>
                  </select>
                </div>
                <div className="button-row">
                  <button type="submit" className="primary-button" disabled={sgFormBusy}>
                    {sgFormBusy ? 'Saving…' : 'Save'}
                  </button>
                  <button type="button" className="secondary-button" onClick={clearSgForm} disabled={sgFormBusy}>
                    Cancel
                  </button>
                </div>
              </form>
            </div>
          </div>

          <div
            id="admin-services-panel-categories"
            role="tabpanel"
            aria-labelledby="admin-services-tab-categories"
            hidden={servicesSubTab !== 'categories'}
            className={
              'admin-tab-panel admin-services-subpanel' +
              (servicesSubTab === 'categories' ? ' active' : '')
            }
          >
          <h3 className="admin-subheading">Service Category</h3>
          <div className="admin-layout">
            <div>
              <select
                className="cms-listbox"
                size={25}
                value={selectedServiceCategoryId === '' ? '' : String(selectedServiceCategoryId)}
                onChange={(e) => setSelectedServiceCategoryId(e.target.value ? Number(e.target.value) : '')}
                onDoubleClick={editScFromSelection}
              >
                {(admin.serviceCategories ?? []).map((c) => (
                  <option key={c.serviceCategoryID} value={c.serviceCategoryID}>
                    {c.categoryName ?? ''}
                  </option>
                ))}
              </select>
              <div className="button-row wrap-buttons">
                <button type="button" className="secondary-button" onClick={clearScForm}>
                  Add
                </button>
                <button type="button" className="secondary-button" onClick={editScFromSelection}>
                  Edit
                </button>
                <button type="button" className="danger-button" onClick={(e) => void deleteSc(e)}>
                  Remove
                </button>
              </div>
            </div>
            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void saveSc(e)}>
              <div className="form-grid compact-grid">
                <label>Category Name</label>
                <input
                  maxLength={100}
                  value={scForm.categoryName}
                  onChange={(e) => setScForm({ ...scForm, categoryName: e.target.value })}
                />
                <label>Service Category Description</label>
                <input
                  maxLength={100}
                  value={scForm.serviceCategoryDescription}
                  onChange={(e) => setScForm({ ...scForm, serviceCategoryDescription: e.target.value })}
                />
                <label>Is Active</label>
                <select
                  value={scForm.isActive ? 'true' : 'false'}
                  onChange={(e) => setScForm({ ...scForm, isActive: e.target.value === 'true' })}
                  aria-label="Is Active"
                >
                  <option value="true">True</option>
                  <option value="false">False</option>
                </select>
              </div>
              <div className="button-row">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearScForm}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
          </div>

          <div
            id="admin-services-panel-catalog"
            role="tabpanel"
            aria-labelledby="admin-services-tab-catalog"
            hidden={servicesSubTab !== 'services'}
            className={
              'admin-tab-panel admin-services-subpanel' + (servicesSubTab === 'services' ? ' active' : '')
            }
          >
          <div className="page-header-top admin-users-toolbar">
            <h3 className="admin-subheading">Services catalog</h3>
            <div className="page-header-actions">
              <input
                id="admin-service-bulk-csv"
                type="file"
                accept=".csv,text/csv"
                className="visually-hidden"
                disabled={serviceBulkBusy}
                onChange={(ev) => void handleServiceBulkImportFile(ev)}
              />
              <label
                htmlFor="admin-service-bulk-csv"
                className={
                  'secondary-button bulk-import-label' +
                  (serviceBulkBusy || serviceBulkWizardOpen ? ' disabled-label' : '')
                }
              >
                Bulk import
              </label>
            </div>
          </div>

          <div className="admin-layout admin-users-layout">
            <div className="admin-users-left">
              <div className="admin-users-dept-row">
                <label htmlFor="svc-group-filter">Service Group</label>
                <select
                  id="svc-group-filter"
                  className="admin-users-dept-select"
                  value={serviceGroupFilter === '' ? '' : String(serviceGroupFilter)}
                  onChange={(e) =>
                    setServiceGroupFilter(e.target.value === '' ? '' : Number(e.target.value))
                  }
                  aria-label="Filter services by service group"
                >
                  <option value="">All groups</option>
                  {(admin?.serviceGroups ?? []).map((g) => (
                    <option key={g.serviceGroupID} value={String(g.serviceGroupID)}>
                      {g.serviceGroupName}
                    </option>
                  ))}
                </select>
              </div>
              <div className="admin-users-dept-row">
                <label htmlFor="svc-category-filter">Service Category</label>
                <select
                  id="svc-category-filter"
                  className="admin-users-dept-select"
                  value={serviceCategoryFilter === '' ? '' : String(serviceCategoryFilter)}
                  onChange={(e) =>
                    setServiceCategoryFilter(e.target.value === '' ? '' : Number(e.target.value))
                  }
                  aria-label="Filter services by service category"
                >
                  <option value="">All categories</option>
                  {(admin?.serviceCategories ?? []).map((c) => (
                    <option key={c.serviceCategoryID} value={String(c.serviceCategoryID)}>
                      {c.serviceCategoryDescription
                        ? `${c.categoryName} (${c.serviceCategoryDescription})`
                        : c.categoryName}
                    </option>
                  ))}
                </select>
              </div>
              <div className="admin-user-directory-search">
                <label htmlFor="svc-directory-search" className="sr-only">
                  Search services
                </label>
                <input
                  id="svc-directory-search"
                  type="search"
                  className="admin-user-search-input"
                  placeholder="Search by name, description, category, or group..."
                  value={serviceDirectorySearch}
                  onChange={(e) => setServiceDirectorySearch(e.target.value)}
                  aria-controls="admin-service-pick-list"
                  autoComplete="off"
                />
              </div>
              <div className="page-header-actions project-services-select-actions">
                <button
                  type="button"
                  className="secondary-button"
                  disabled={!(admin?.services ?? []).length}
                  onClick={handleSelectAllAdminServices}
                >
                  Select all services
                </button>
                <button
                  type="button"
                  className="secondary-button"
                  disabled={selectedServiceIds.length === 0}
                  onClick={handleClearAllAdminServices}
                >
                  Clear all
                </button>
              </div>
              {filteredServicesForDirectory.length > 0 ? (
                <div className="project-services-select-all">
                  <label className="project-services-select-all-label">
                    <input
                      type="checkbox"
                      checked={allFilteredAdminServicesSelected}
                      ref={(el) => {
                        if (el) {
                          el.indeterminate = someFilteredAdminServicesSelected;
                        }
                      }}
                      onChange={handleToggleAllFilteredAdminServices}
                      aria-label="Select or clear all services shown in the list"
                    />
                    Select all shown
                  </label>
                  <span className="project-services-select-all-count">
                    {selectedServiceIds.length} selected
                  </span>
                </div>
              ) : null}
              <div
                id="admin-service-pick-list"
                className="admin-user-pick-list"
                role="listbox"
                aria-label="Services"
                aria-activedescendant={
                  selectedServiceId === '' ? undefined : `admin-service-row-${selectedServiceId}`
                }
              >
                {filteredServicesForDirectory.length === 0 ? (
                  <div className="admin-user-pick-empty" role="status">
                    {(admin.services ?? []).length === 0
                      ? 'No services loaded.'
                      : 'No services match your filters.'}
                  </div>
                ) : (
                  filteredServicesForDirectory.map((row) => {
                    const groupName = (row.serviceGroupName ?? '').trim();
                    const desc = (row.serviceDescription ?? '').trim();
                    const serviceName = formatServiceCell(row.serviceName);
                    const isChecked = selectedServiceIds.includes(row.serviceID);
                    return (
                      <div key={row.serviceID} className="admin-service-pick-item">
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => handleAdminServiceToggle(row.serviceID)}
                          aria-label={`Select service ${serviceName}`}
                        />
                        <button
                          id={`admin-service-row-${row.serviceID}`}
                          type="button"
                          role="option"
                          aria-selected={selectedServiceId === row.serviceID}
                          className={
                            'admin-user-pick-row' + (selectedServiceId === row.serviceID ? ' selected' : '')
                          }
                          onClick={() => setSelectedServiceId(row.serviceID)}
                          onDoubleClick={(e) => {
                            e.preventDefault();
                            editServiceFromSelection(row);
                          }}
                        >
                          <span className="admin-user-pick-name">
                            <span className="admin-user-pick-name-text">{serviceName}</span>
                            <span className="admin-user-pick-badges">
                              {!serviceIsActive(row) ? (
                                <span
                                  className="admin-user-pick-status admin-user-pick-status--inactive"
                                  title="Inactive service"
                                >
                                  Inactive
                                </span>
                              ) : null}
                            </span>
                          </span>
                          <span className="admin-user-pick-title admin-service-pick-desc">
                            {desc || '\u00a0'}
                          </span>
                          <span className="admin-user-pick-meta">
                            {groupName ? `Service Group: ${groupName}` : 'Service Group: -'}
                          </span>
                          <span className="admin-user-pick-meta">
                            {serviceName !== '-' ? `Service Name: ${serviceName}` : 'Service Name: -'}
                          </span>
                        </button>
                      </div>
                    );
                  })
                )}
              </div>
            </div>

            <form className="admin-edit-card project-panel-tab-card" noValidate onSubmit={(e) => void submitService(e)}>
              <p className="field-help admin-services-form-lead">
                Tick services for bulk actions (e.g. deactivate), or click a row to bind Save to that
                service. Use <strong>Select all services</strong> or <strong>Select all shown</strong> to
                select multiple at once. Choose group and category, then Save (or use Edit Service to
                change name/description). Save writes <code>tService</code> and upserts the linked{' '}
                <code>tServiceRef</code> row.
              </p>
              <div className="form-grid compact-grid">
                <label htmlFor="svc-id">ServiceID</label>
                <input
                  id="svc-id"
                  readOnly
                  value={serviceForm.serviceID || ''}
                  placeholder="(new)"
                  aria-label="ServiceID (identity; assigned on insert)"
                />
                <label htmlFor="svc-group">Service Group</label>
                <select
                  id="svc-group"
                  key={`svc-group-${serviceForm.serviceID}-${serviceForm.serviceGroupID}`}
                  value={serviceForm.serviceGroupID > 0 ? String(serviceForm.serviceGroupID) : ''}
                  onChange={(e) =>
                    setServiceForm((f) => ({
                      ...f,
                      serviceGroupID: e.target.value ? Number(e.target.value) : 0,
                    }))
                  }
                >
                  <option value="">-- Select group --</option>
                  {serviceFormGroupOptions.map((g) => (
                    <option key={g.serviceGroupID} value={String(g.serviceGroupID)}>
                      {g.serviceGroupName}
                    </option>
                  ))}
                </select>
                <label htmlFor="svc-cat">Service Category</label>
                <select
                  id="svc-cat"
                  key={`svc-cat-${serviceForm.serviceID}-${serviceForm.serviceCategoryID}`}
                  value={serviceForm.serviceCategoryID > 0 ? String(serviceForm.serviceCategoryID) : ''}
                  onChange={(e) => {
                    const id = Number(e.target.value);
                    const cat = serviceFormCategoryOptions.find((c) => c.serviceCategoryID === id);
                    setServiceForm((f) => ({
                      ...f,
                      serviceCategoryID: id,
                      functionID: cat?.functionID ?? 0,
                      categoryName: cat?.categoryName ?? '',
                      serviceCategoryDescription: cat?.serviceCategoryDescription ?? '',
                    }));
                  }}
                >
                  <option value="">-- Select category --</option>
                  {serviceFormCategoryOptions.map((c) => (
                    <option key={c.serviceCategoryID} value={String(c.serviceCategoryID)}>
                      {c.serviceCategoryDescription
                        ? `${c.categoryName} (${c.serviceCategoryDescription})`
                        : c.categoryName}
                    </option>
                  ))}
                </select>
                <label htmlFor="svc-name">Service Name</label>
                <input
                  id="svc-name"
                  maxLength={200}
                  value={serviceForm.serviceName}
                  onChange={(e) => setServiceForm((f) => ({ ...f, serviceName: e.target.value }))}
                />
                <label htmlFor="svc-desc">Service Description</label>
                <textarea
                  id="svc-desc"
                  maxLength={1000}
                  rows={3}
                  value={serviceForm.serviceDescription}
                  onChange={(e) => setServiceForm((f) => ({ ...f, serviceDescription: e.target.value }))}
                />
                <label htmlFor="svc-tags">Tags</label>
                <select
                  id="svc-tags"
                  multiple
                  size={6}
                  value={serviceForm.tagIDs.map(String)}
                  onChange={(e) => {
                    const selected = Array.from(e.target.selectedOptions).map((o) => Number(o.value));
                    setServiceForm((f) => ({ ...f, tagIDs: selected }));
                  }}
                  aria-describedby="svc-tags-help"
                >
                  {(serviceFormTagOptions).map((t) => (
                    <option key={t.tagID} value={String(t.tagID)}>
                      {t.tagName}
                      {t.tagScope ? ` [${t.tagScope}]` : ''}
                    </option>
                  ))}
                </select>
                <span id="svc-tags-help" className="field-help">
                  Hold Ctrl/Cmd to toggle multiple tags.
                </span>
                <label htmlFor="svc-active">IsActive</label>
                <span>
                  <input
                    id="svc-active"
                    type="checkbox"
                    checked={serviceForm.isActive}
                    onChange={(e) => setServiceForm((f) => ({ ...f, isActive: e.target.checked }))}
                  />{' '}
                  Active
                </span>
                <label htmlFor="svc-created">CreatedDate</label>
                <input
                  id="svc-created"
                  readOnly
                  value={serviceForm.createdDateDisplay}
                  placeholder="(after save)"
                  aria-label="CreatedDate from database"
                />
              </div>
              <div className="button-row wrap-buttons admin-user-form-actions">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearServiceForm}>
                  Cancel
                </button>
                <span className="admin-user-action-divider" aria-hidden="true" />
                <button type="button" className="secondary-button" onClick={clearServiceForm}>
                  Add Service
                </button>
                <button type="button" className="secondary-button" onClick={() => editServiceFromSelection()}>
                  Edit Service
                </button>
                <button
                  type="button"
                  className="danger-button"
                  disabled={selectedServiceIds.length === 0}
                  onClick={() => void deactivateSelectedService()}
                >
                  Deactivate selected
                </button>
              </div>
            </form>
          </div>
          </div>
        </div>

        <div id="admin-tab-data-audit" className={'admin-tab-panel' + (tab === 'dataAudit' ? ' active' : '')}>
          <h2>Data Audit</h2>
          <p className="field-help">
            Client → Project → User Group → User linkage checks, plus CAT user login counts. Run the report to refresh
            counts and drill into findings.
          </p>
          <div className="button-row wrap-buttons">
            <button
              type="button"
              className="primary-button"
              disabled={dataAuditLoading || dataAuditExporting}
              onClick={() => void loadDataAudit()}
            >
              {dataAuditLoading ? 'Running…' : dataAudit ? 'Refresh report' : 'Run report'}
            </button>
            <button
              type="button"
              className="secondary-button"
              disabled={dataAuditLoading || dataAuditExporting}
              onClick={() => void generateDataAuditPdf()}
              title="Download a PDF with section summaries and findings by Analytics Lead"
            >
              {dataAuditExporting ? 'Generating…' : 'Generate report'}
            </button>
          </div>
          {dataAuditError ? (
            <div className="alert alert-error" role="alert">
              {dataAuditError}
            </div>
          ) : null}
          {dataAudit ? (
            <div className="admin-data-audit">
              <p className="field-help" role="status">
                Generated {new Date(dataAudit.generatedAtUtc).toLocaleString()} ·{' '}
                {dataAudit.checks.reduce((sum, c) => sum + c.count, 0)} total finding
                {dataAudit.checks.reduce((sum, c) => sum + c.count, 0) === 1 ? '' : 's'}
              </p>
              <div className="admin-data-audit-groups">
                {groupDataAuditChecks(dataAudit.checks).map((group) => (
                  <section key={group.key} className="admin-data-audit-group" aria-label={group.label}>
                    <h3 className="admin-data-audit-group__title">{group.label}</h3>
                    <div className="admin-data-audit-summary">
                      {group.checks.map((check) => (
                        <button
                          key={check.checkId}
                          type="button"
                          className={
                            'admin-data-audit-card' +
                            (check.count > 0 ? ' admin-data-audit-card--findings' : ' admin-data-audit-card--ok') +
                            (expandedAuditCheckId === check.checkId ? ' selected' : '')
                          }
                          onClick={() => setExpandedAuditCheckId(check.checkId)}
                          title={check.description}
                        >
                          <span className="admin-data-audit-card__header">
                            <span className="admin-data-audit-card__count">{check.count}</span>
                            <span
                              className="admin-data-audit-card__status"
                              aria-label={check.count > 0 ? 'Needs attention' : 'All clear'}
                            >
                              {check.count > 0 ? '!' : '✓'}
                            </span>
                          </span>
                          <span className="admin-data-audit-card__title">{check.title}</span>
                        </button>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
              {(() => {
                const selected =
                  dataAudit.checks.find((c) => c.checkId === expandedAuditCheckId) ?? dataAudit.checks[0];
                if (!selected) {
                  return null;
                }
                return (
                  <div className="admin-data-audit-detail">
                    <h3 className="admin-subheading">{selected.title}</h3>
                    <p className="field-help">{selected.description}</p>
                    {selected.rows.length === 0 ? (
                      <p className="field-help" role="status">
                        {selected.checkId === 'user-logins-activity'
                          ? 'No CAT users available for login tracking.'
                          : 'No findings for this check.'}
                      </p>
                    ) : (
                      <>
                        {selected.checkId === 'user-logins-activity' ? (
                          <p className="field-help">
                            Card count is total successful launches across CAT users. Each row shows that user&apos;s
                            login total and last sign-in (UTC).
                          </p>
                        ) : selected.checkId === 'user-groups-cat-users-without-membership' ? (
                          <p className="field-help">
                            Double-click a row to open that user on the Users tab (CAT filter applied).
                          </p>
                        ) : isClientsMissingProjectCheck(selected) ? (
                          <p className="field-help">
                            Right-click a row to set the client Inactive or delete the client. Double-click a row to
                            open that client on the Clients tab.
                          </p>
                        ) : selected.checkId.startsWith('clients-') ? (
                          <p className="field-help">
                            Double-click a row to open that client on the Clients tab with the form filled in.
                          </p>
                        ) : selected.checkId.startsWith('projects-') ? (
                          <p className="field-help">
                            Double-click a row to open that project with the project info filled in.
                          </p>
                        ) : (
                          <p className="field-help">
                            Double-click a row with a user group (UGID) to open that group on the Users → User Group tab.
                          </p>
                        )}
                        <div className="admin-usergroup-table-wrap admin-data-audit-table-wrap">
                          <table
                            className="admin-usergroup-table admin-data-audit-table"
                            aria-label={selected.title}
                          >
                            <thead>
                              <tr>
                                {selected.columns.map((col) => (
                                  <th key={col} scope="col">
                                    {col}
                                  </th>
                                ))}
                              </tr>
                            </thead>
                            <tbody>
                              {selected.rows.map((row, ri) => {
                                const ugid = auditRowUgid(selected, row);
                                const userId = auditRowUserId(selected, row);
                                const clientId = auditRowClientId(selected, row);
                                const projectId = auditRowProjectId(selected, row);
                                const canOpenProject =
                                  selected.checkId.startsWith('projects-') && projectId != null;
                                const canOpenGroup = !canOpenProject && ugid != null;
                                const canOpenUser =
                                  selected.checkId === 'user-groups-cat-users-without-membership' &&
                                  userId != null;
                                const canOpenClient =
                                  selected.checkId.startsWith('clients-') && clientId != null;
                                const canActOnMissingProject =
                                  isClientsMissingProjectCheck(selected) && clientId != null;
                                const canJump =
                                  canOpenProject || canOpenGroup || canOpenUser || canOpenClient;
                                return (
                                  <tr
                                    key={`${selected.checkId}-${ri}`}
                                    className={
                                      canJump
                                        ? 'admin-data-audit-row--jump' +
                                          (canActOnMissingProject ? ' admin-data-audit-row--menu' : '')
                                        : canActOnMissingProject
                                          ? 'admin-data-audit-row--menu'
                                          : undefined
                                    }
                                    title={
                                      canActOnMissingProject
                                        ? `Right-click for client actions. Double-click to open client ${clientId} on the Clients tab`
                                        : canOpenProject
                                        ? `Double-click to open project ${projectId}`
                                        : canOpenClient
                                          ? `Double-click to open client ${clientId} on the Clients tab`
                                          : canOpenUser
                                            ? `Double-click to open user ${userId} on the Users tab`
                                            : canOpenGroup
                                              ? `Double-click to open user group ${ugid} on the Users → User Group tab`
                                              : undefined
                                    }
                                    onContextMenu={
                                      canActOnMissingProject
                                        ? (e) => {
                                            e.preventDefault();
                                            if (clientId == null) {
                                              return;
                                            }
                                            const menuWidth = 240;
                                            const menuHeight = 96;
                                            setDataAuditClientMenu({
                                              clientId,
                                              clientName: auditRowClientLabel(selected, row),
                                              x: Math.max(8, Math.min(e.clientX, window.innerWidth - menuWidth)),
                                              y: Math.max(8, Math.min(e.clientY, window.innerHeight - menuHeight)),
                                            });
                                          }
                                        : undefined
                                    }
                                    onDoubleClick={() => {
                                      if (canOpenProject) {
                                        openProjectFromDataAudit(selected, row);
                                      } else if (canOpenClient) {
                                        openClientFromDataAudit(selected, row);
                                      } else if (canOpenUser) {
                                        openUserFromDataAudit(selected, row);
                                      } else if (canOpenGroup) {
                                        openUserGroupFromDataAudit(selected, row);
                                      }
                                    }}
                                  >
                                    {row.map((cell, ci) => (
                                      <td key={`${selected.checkId}-${ri}-${ci}`}>{cell?.trim() || '—'}</td>
                                    ))}
                                  </tr>
                                );
                              })}
                            </tbody>
                          </table>
                        </div>
                      </>
                    )}
                  </div>
                );
              })()}
            </div>
          ) : null}
        </div>

        <div id="admin-tab-clients" className={'admin-tab-panel' + (tab === 'clients' ? ' active' : '')}>
          <div className="page-header-top admin-users-toolbar">
            <h2>
              <span className="admin-users-count-num">{clientStatusCounts.total}</span> Clients
              <span className="admin-client-count-chips">
                <span className="admin-client-count-chip admin-client-count-chip--active">
                  Active <strong>{clientStatusCounts.active}</strong>
                </span>
                <span className="admin-client-count-chip admin-client-count-chip--inactive">
                  Inactive <strong>{clientStatusCounts.inactive}</strong>
                </span>
                <span className="admin-client-count-chip admin-client-count-chip--prospect">
                  Prospect <strong>{clientStatusCounts.prospect}</strong>
                </span>
              </span>
            </h2>
            <div className="page-header-actions">
              <input
                id="admin-client-bulk-csv"
                type="file"
                accept=".csv,text/csv"
                className="visually-hidden"
                disabled={clientBulkBusy}
                onChange={(ev) => void handleClientBulkImportFile(ev)}
              />
              <label
                htmlFor="admin-client-bulk-csv"
                className={
                  'secondary-button bulk-import-label' +
                  (clientBulkBusy || clientBulkWizardOpen ? ' disabled-label' : '')
                }
              >
                Bulk import
              </label>
            </div>
          </div>
          <div className="admin-layout admin-users-layout">
            <div className="admin-users-left">
              <div className="admin-users-dept-row">
                <label htmlFor="admin-client-bu-filter">Business Unit</label>
                <select
                  id="admin-client-bu-filter"
                  className="admin-users-dept-select"
                  value={clientBuFilter === '' ? '' : String(clientBuFilter)}
                  onChange={(e) => setClientBuFilter(e.target.value ? Number(e.target.value) : '')}
                  aria-label="Filter clients by business unit"
                >
                  <option value="">All business units</option>
                  {admin.businessUnits.map((u) => (
                    <option key={u.businessUnitID} value={String(u.businessUnitID)}>
                      {u.businessUnitName?.trim() || `BU ${u.businessUnitID}`}
                    </option>
                  ))}
                </select>
              </div>
              <div className="admin-users-dept-row">
                <label htmlFor="admin-client-bc-filter">Business Class</label>
                <select
                  id="admin-client-bc-filter"
                  className="admin-users-dept-select"
                  value={clientBcFilter === '' ? '' : String(clientBcFilter)}
                  onChange={(e) => setClientBcFilter(e.target.value ? Number(e.target.value) : '')}
                  aria-label="Filter clients by business class"
                >
                  <option value="">All business classes</option>
                  {businessClassOptions.map((bc) => (
                    <option key={bc.businessClassID} value={String(bc.businessClassID)}>
                      {bc.businessClassDescription?.trim() || bc.businessClassName?.trim() || `BC ${bc.businessClassID}`}
                    </option>
                  ))}
                </select>
              </div>
              <div className="admin-users-filter-row admin-search-row">
                <label htmlFor="admin-client-search" className="sr-only">
                  Search clients
                </label>
                <input
                  id="admin-client-search"
                  type="search"
                  className="admin-search-input"
                  placeholder="Search by name, short name, AE, analytics lead..."
                  value={clientSearch}
                  onChange={(e) => setClientSearch(e.target.value)}
                  aria-controls="admin-client-pick-list"
                  autoComplete="off"
                />
              </div>
              <div className="admin-client-tabs" role="tablist" aria-label="Client status">
                {(['Active', 'Inactive'] as const).map((tab) => (
                  <button
                    key={tab}
                    type="button"
                    role="tab"
                    aria-selected={clientActiveTab === tab}
                    className={'admin-client-tab' + (clientActiveTab === tab ? ' selected' : '')}
                    onClick={() => setClientActiveTab(tab)}
                  >
                    {tab}
                    <span className="admin-client-tab-count">
                      {tab === 'Active' ? clientStatusCounts.activeFlag : clientStatusCounts.inactiveFlag}
                    </span>
                  </button>
                ))}
              </div>
              <div
                id="admin-client-pick-list"
                className="admin-user-pick-list"
                role="listbox"
                aria-label={`${clientActiveTab} clients`}
                aria-activedescendant={selectedClientId === '' ? undefined : `admin-client-row-${selectedClientId}`}
              >
                {filteredClientsForDirectory.length === 0 ? (
                  <div className="admin-user-pick-empty" role="status">
                    {admin.clients.length === 0
                      ? 'No clients loaded.'
                      : `No ${clientActiveTab.toLowerCase()} clients match your filters.`}
                  </div>
                ) : (
                  filteredClientsForDirectory.map((c) => {
                    const name = (c.clientName ?? '').trim() || `Client ${c.clientID}`;
                    const tag = clientRefTag(c);
                    const classLine = [c.businessUnitName, (c.businessClassName ?? '').trim()]
                      .map((x) => (x ?? '').trim())
                      .filter((x) => x !== '')
                      .join(' * ');
                    const peopleLine = [
                      c.accountExecutiveName ? `AE: ${c.accountExecutiveName}` : '',
                      c.analyticsLeadName ? `AL: ${c.analyticsLeadName}` : '',
                    ]
                      .filter((x) => x !== '')
                      .join('   ');
                    return (
                      <button
                        key={c.clientID}
                        id={`admin-client-row-${c.clientID}`}
                        type="button"
                        role="option"
                        aria-selected={selectedClientId === c.clientID}
                        className={'admin-user-pick-row' + (selectedClientId === c.clientID ? ' selected' : '')}
                        onClick={() => {
                          setClientContextMenu(null);
                          setSelectedClientId(c.clientID);
                        }}
                        onDoubleClick={() => {
                          setClientContextMenu(null);
                          setSelectedClientId(c.clientID);
                          editClientFromSelection(c);
                        }}
                        onContextMenu={(e) => {
                          e.preventDefault();
                          setSelectedClientId(c.clientID);
                          setClientContextMenu({
                            clientId: c.clientID,
                            clientName: name,
                            x: e.clientX,
                            y: e.clientY,
                          });
                        }}
                      >
                        <span className="admin-user-pick-name">
                          <span className="admin-user-pick-name-text">{name}</span>
                          <span
                            className={`admin-user-pick-status admin-user-pick-status--${tag.kind}`}
                            title={
                              tag.kind === 'needupdate'
                                ? 'No classification row (dbo.tClientRef) yet - needs update'
                                : `${tag.label} (from tClientRef to tStatus)`
                            }
                          >
                            {tag.label}
                          </span>
                        </span>
                        <span className="admin-user-pick-title">
                          {(c.clientShortName ?? '').trim() || '\u00a0'}
                        </span>
                        <span className="admin-user-pick-meta">{classLine || '\u00a0'}</span>
                        <span className="admin-user-pick-meta">{peopleLine || '\u00a0'}</span>
                      </button>
                    );
                  })
                )}
              </div>
            </div>

            <form className="admin-edit-card project-panel-tab-card" onSubmit={(e) => void submitClient(e)}>
              <div className="form-grid compact-grid">
                <label htmlFor="client-name">Client Name</label>
                <input
                  id="client-name"
                  maxLength={150}
                  value={clientForm.clientName}
                  onChange={(e) => setClientForm({ ...clientForm, clientName: e.target.value })}
                />

                <label htmlFor="client-short-name">Short Name</label>
                <input
                  id="client-short-name"
                  maxLength={50}
                  value={clientForm.clientShortName}
                  onChange={(e) => setClientForm({ ...clientForm, clientShortName: e.target.value })}
                />

                <label htmlFor="client-description">Description</label>
                <textarea
                  id="client-description"
                  maxLength={500}
                  value={clientForm.clientDescription}
                  onChange={(e) => setClientForm({ ...clientForm, clientDescription: e.target.value })}
                />

                <label htmlFor="client-bu">Business Unit</label>
                <select
                  id="client-bu"
                  value={clientForm.businessUnitID > 0 ? String(clientForm.businessUnitID) : ''}
                  onChange={(e) =>
                    setClientForm({ ...clientForm, businessUnitID: e.target.value ? Number(e.target.value) : 0 })
                  }
                >
                  <option value="">- Select Business Unit -</option>
                  {admin.businessUnits.map((u) => (
                    <option key={u.businessUnitID} value={String(u.businessUnitID)}>
                      {u.businessUnitName?.trim() || `BU ${u.businessUnitID}`}
                    </option>
                  ))}
                </select>

                <label htmlFor="client-bc">Business Class</label>
                <select
                  id="client-bc"
                  value={clientForm.businessClassID > 0 ? String(clientForm.businessClassID) : ''}
                  onChange={(e) =>
                    setClientForm({ ...clientForm, businessClassID: e.target.value ? Number(e.target.value) : 0 })
                  }
                >
                  <option value="">- Select Business Class -</option>
                  {businessClassOptions.map((bc) => (
                    <option key={bc.businessClassID} value={String(bc.businessClassID)}>
                      {bc.businessClassDescription?.trim() || bc.businessClassName?.trim() || `BC ${bc.businessClassID}`}
                    </option>
                  ))}
                </select>

                <label htmlFor="client-type">Client Type</label>
                <select
                  id="client-type"
                  value={clientForm.clientTypeID > 0 ? String(clientForm.clientTypeID) : ''}
                  onChange={(e) =>
                    setClientForm({ ...clientForm, clientTypeID: e.target.value ? Number(e.target.value) : 0 })
                  }
                >
                  <option value="">- Select Client Type -</option>
                  {(admin.clientTypes ?? []).map((ct) => (
                    <option key={ct.clientTypeID} value={String(ct.clientTypeID)}>
                      {ct.ctDescription?.trim() || ct.ctName?.trim() || `Type ${ct.clientTypeID}`}
                    </option>
                  ))}
                </select>

                <label htmlFor="client-ae">Account Executive</label>
                <UserCombo
                  id="client-ae"
                  users={admin.bmsUsers}
                  value={clientForm.accountExecutiveUserID}
                  onChange={(uid) => setClientForm({ ...clientForm, accountExecutiveUserID: uid })}
                />

                <label htmlFor="client-al">Analytics Lead</label>
                <UserCombo
                  id="client-al"
                  users={admin.bmsUsers}
                  value={clientForm.analyticsLeadUserID}
                  onChange={(uid) => setClientForm({ ...clientForm, analyticsLeadUserID: uid })}
                />

                <label htmlFor="client-status">Status</label>
                <select
                  id="client-status"
                  value={clientForm.clientStatus}
                  onChange={(e) =>
                    setClientForm({ ...clientForm, clientStatus: e.target.value as ClientStatus })
                  }
                >
                  {clientStatusOptions.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </div>
              <p className="field-help">
                Business Unit, Business Class, and Client Type are stored on dbo.tClientRef. You can set any of them
                individually - whatever is left blank is saved as NULL and can be filled in later. The client status is
                also written to that row so a client can carry a different status per business unit. Account Executive
                and Analytics Lead are stored on dbo.tClient.
              </p>
              <div className="button-row wrap-buttons admin-user-form-actions">
                <button type="submit" className="primary-button">
                  Save
                </button>
                <button type="button" className="secondary-button" onClick={clearClientForm}>
                  Cancel
                </button>
                <span className="admin-user-action-divider" aria-hidden="true" />
                <button type="button" className="secondary-button" onClick={clearClientForm}>
                  Add Client
                </button>
                <button type="button" className="secondary-button" onClick={() => editClientFromSelection()}>
                  Edit Client
                </button>
                <button type="button" className="danger-button" onClick={() => void deleteClient()}>
                  Delete Client
                </button>
              </div>
            </form>
          </div>
        </div>
      </section>

      {buDeleteBlockedMessage ? (
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="bu-delete-blocked-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="bu-delete-blocked-title">Cannot delete business unit</h2>
            <p className="field-help" role="alert">
              {buDeleteBlockedMessage}
            </p>
            <div className="button-row bulk-import-actions">
              <button type="button" className="primary-button" onClick={() => setBuDeleteBlockedMessage(null)}>
                OK
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {bcDeleteBlockedMessage ? (
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="bc-delete-blocked-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="bc-delete-blocked-title">Cannot delete business class</h2>
            <p className="field-help" role="alert">
              {bcDeleteBlockedMessage}
            </p>
            <div className="button-row bulk-import-actions">
              <button type="button" className="primary-button" onClick={() => setBcDeleteBlockedMessage(null)}>
                OK
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {clientDeleteBlockedMessage ? (
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="client-delete-blocked-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="client-delete-blocked-title">Cannot delete client</h2>
            <p className="field-help" role="alert">
              {clientDeleteBlockedMessage}
            </p>
            <div className="button-row bulk-import-actions">
              <button type="button" className="primary-button" onClick={() => setClientDeleteBlockedMessage(null)}>
                OK
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {ugDeleteBlockedMessage ? (
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="ug-delete-blocked-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="ug-delete-blocked-title">Cannot delete user group</h2>
            <p className="field-help" role="alert">
              {ugDeleteBlockedMessage}
            </p>
            <div className="button-row bulk-import-actions">
              <button type="button" className="primary-button" onClick={() => setUgDeleteBlockedMessage(null)}>
                OK
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {dataAuditClientMenu ? (
        <>
          <button
            type="button"
            className="admin-context-menu-backdrop"
            aria-label="Close client actions"
            onClick={() => setDataAuditClientMenu(null)}
          />
          <div
            className="admin-context-menu admin-context-menu--blue"
            role="menu"
            style={{ top: dataAuditClientMenu.y, left: dataAuditClientMenu.x }}
          >
            <button
              type="button"
              role="menuitem"
              className="admin-context-menu__item"
              disabled={dataAuditClientBusy}
              onClick={() =>
                void setAuditClientInactive(dataAuditClientMenu.clientId, dataAuditClientMenu.clientName)
              }
            >
              Set Client Status Inactive
            </button>
            <button
              type="button"
              role="menuitem"
              className="admin-context-menu__item"
              disabled={dataAuditClientBusy}
              onClick={() => void deleteAuditClient(dataAuditClientMenu.clientId, dataAuditClientMenu.clientName)}
            >
              Delete Client
            </button>
          </div>
        </>
      ) : null}

      {clientContextMenu ? (
        <>
          <button
            type="button"
            className="admin-context-menu-backdrop"
            aria-label="Close client menu"
            onClick={() => setClientContextMenu(null)}
          />
          <div
            className="admin-context-menu"
            role="menu"
            style={{ top: clientContextMenu.y, left: clientContextMenu.x }}
          >
            <button
              type="button"
              role="menuitem"
              className="admin-context-menu__item admin-context-menu__item--danger"
              onClick={() => void deleteClient(clientContextMenu.clientId)}
            >
              Delete Client
            </button>
          </div>
        </>
      ) : null}

      {userRemoveDialogOpen ? (
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="user-remove-choice-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="user-remove-choice-title">Remove user</h2>
            <p className="field-help">
              <strong>Deactivate</strong> keeps the user record and uses the existing deactivate procedure (same as
              before). <strong>Delete from database</strong> runs <code>dbo.usp_User_Delete</code> and permanently removes
              the user per your database implementation.
            </p>
            <div className="button-row bulk-import-actions">
              <button type="button" className="secondary-button" onClick={closeRemoveUserDialog}>
                Cancel
              </button>
              <button type="button" className="secondary-button" onClick={() => void confirmDeactivateUser()}>
                Deactivate
              </button>
              <button type="button" className="danger-button" onClick={() => void confirmDeleteUserFromDb()}>
                Delete from database
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {userBulkWizardOpen ? (
        <div className="bulk-import-overlay" role="dialog" aria-modal="true" aria-labelledby="user-bulk-import-title">
          <div className="bulk-import-panel card">
            <h2 id="user-bulk-import-title">User bulk import preview</h2>
            <p className="field-help">
              CSV columns: Email (required, up to 100 characters), FirstName, LastName, Title, Department (each up to 100
              characters), Phone, Company, Address (or Address1), City, State, PostalCode, Country, AddressType. Default Company when omitted is BMS.
              New users default to Is Active = true. Import runs atomically per row via dbo.usp_User_CreateWithPrimaryAddress (tUser, tAddress, tUserAddressRef).
              {userBulkSkippedEmpty > 0
                ? ` ${userBulkSkippedEmpty} CSV row(s) were skipped because Email was empty.`
                : ''}
            </p>

            {userBulkVerifyOk ? (
              <div className="bulk-verify-pass" role="status">
                <span className="bulk-verify-check" aria-hidden="true">
                  OK
                </span>
                <span>Verification passed - you can select Import to insert rows via dbo.usp_User_CreateWithPrimaryAddress.</span>
              </div>
            ) : null}

            {userBulkVerifyErrors.length > 0 ? (
              <div className="bulk-verify-fail" role="alert">
                <strong>Verification failed</strong>
                <ul className="bulk-verify-error-list">
                  {userBulkVerifyErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            ) : null}

            {userBulkRowErrors.length > 0 ? (
              <div className="bulk-verify-fail" role="alert">
                <strong>Import row messages</strong>
                <ul className="bulk-verify-error-list">
                  {userBulkRowErrors.map((err, i) => (
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
                    <th>Email</th>
                    <th>FirstName</th>
                    <th>LastName</th>
                    <th>Department</th>
                    <th>Title</th>
                    <th>Phone</th>
                    <th>Company</th>
                    <th>UserType</th>
                    <th>Address1</th>
                    <th>City</th>
                    <th>State</th>
                    <th>PostalCode</th>
                    <th>Country</th>
                    <th>Is Active</th>
                  </tr>
                </thead>
                <tbody>
                  {userBulkRows.length === 0 ? (
                    <tr>
                      <td colSpan={15}>
                        No rows to display. Ensure the CSV has a header row and at least one row with an Email.
                      </td>
                    </tr>
                  ) : (
                    userBulkRows.map((r) => (
                      <tr key={r.lineNumber}>
                        <td>{r.lineNumber}</td>
                        <td>{r.email}</td>
                        <td>{r.firstName}</td>
                        <td>{r.lastName}</td>
                        <td>{r.department}</td>
                        <td>{r.title}</td>
                        <td>{r.phone}</td>
                        <td>{r.company}</td>
                        <td>{r.userType}</td>
                        <td>{r.address1}</td>
                        <td>{r.city}</td>
                        <td>{r.state}</td>
                        <td>{r.postalCode}</td>
                        <td>{r.country}</td>
                        <td>{r.isActive ? 'true' : 'false'}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            <div className="button-row bulk-import-actions">
              <button type="button" className="secondary-button" disabled={userBulkBusy} onClick={resetUserBulkWizard}>
                Cancel
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={userBulkBusy || userBulkRows.length === 0}
                onClick={() => void handleUserBulkVerify()}
              >
                Verify
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={userBulkBusy || !userBulkVerifyOk || userBulkRows.length === 0}
                onClick={() => void handleUserBulkCommit()}
              >
                Import
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {serviceBulkWizardOpen ? (
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="service-bulk-import-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="service-bulk-import-title">Service bulk import preview</h2>
            <p className="field-help">
              Required columns: <strong>ServiceName</strong>, <strong>ServiceDescription</strong>. Optional{' '}
              <strong>ServiceCategory</strong> (or <strong>CategoryName</strong>), <strong>ServiceCategoryDescription</strong>,{' '}
              <strong>ServiceGroupName</strong> - when supplied, a <code>tServiceRef</code> row links the service to{' '}
              <code>tServiceCategory</code> and/or <code>tServiceGroup</code>. Also optional: <strong>ServiceID</strong>{' '}
              (0 or omit = insert), <strong>Tags</strong>, <strong>StatusID</strong>.
              {serviceBulkSkippedEmpty > 0
                ? ` ${serviceBulkSkippedEmpty} CSV row(s) were skipped because ServiceName or ServiceDescription was empty on a new row.`
                : ''}
            </p>

            {serviceBulkVerifyOk ? (
              <div className="bulk-verify-pass" role="status">
                <span className="bulk-verify-check" aria-hidden="true">
                  OK
                </span>
                <span>Verification passed - you can import rows into tService (tServiceRef when category/group are present).</span>
              </div>
            ) : null}

            {serviceBulkVerifyErrors.length > 0 ? (
              <div className="bulk-verify-fail" role="alert">
                <strong>Verification failed</strong>
                <ul className="bulk-verify-error-list">
                  {serviceBulkVerifyErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            ) : null}

            {serviceBulkRowErrors.length > 0 ? (
              <div className="bulk-verify-fail" role="alert">
                <strong>Import row messages</strong>
                <ul className="bulk-verify-error-list">
                  {serviceBulkRowErrors.map((err, i) => (
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
                    <th>ServiceID</th>
                    <th>ServiceName</th>
                    <th>ServiceDescription</th>
                    <th>ServiceCategory</th>
                    <th>ServiceCategoryDescription</th>
                    <th>ServiceGroupName</th>
                    <th>Tags</th>
                  </tr>
                </thead>
                <tbody>
                  {serviceBulkRows.length === 0 ? (
                    <tr>
                      <td colSpan={8}>
                        No rows to display. Ensure the CSV header includes ServiceName and ServiceDescription.
                      </td>
                    </tr>
                  ) : (
                    serviceBulkRows.map((r) => (
                      <tr key={r.lineNumber}>
                        <td>{r.lineNumber}</td>
                        <td>{r.serviceID}</td>
                        <td>{r.serviceName}</td>
                        <td>{r.serviceDescription}</td>
                        <td>{r.categoryName}</td>
                        <td>{r.serviceCategoryDescription}</td>
                        <td>{r.serviceGroupName}</td>
                        <td>{r.tags}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            <div className="button-row bulk-import-actions">
              <button type="button" className="secondary-button" disabled={serviceBulkBusy} onClick={resetServiceBulkWizard}>
                Cancel
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={serviceBulkBusy || serviceBulkRows.length === 0}
                onClick={() => void handleServiceBulkVerify()}
              >
                Verify
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={serviceBulkBusy || !serviceBulkVerifyOk || serviceBulkRows.length === 0}
                onClick={() => void handleServiceBulkCommit()}
              >
                Import
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {clientBulkWizardOpen ? (
        <div
          className="bulk-import-overlay"
          role="dialog"
          aria-modal="true"
          aria-labelledby="client-bulk-import-title"
        >
          <div className="bulk-import-panel card">
            <h2 id="client-bulk-import-title">Client bulk import preview</h2>
            <p className="field-help">
              CSV columns: <strong>ClientName</strong> (required); optional <strong>ClientShortName</strong>,{' '}
              <strong>ClientDescription</strong>, <strong>IsActive</strong> (true/false, 1/0, Y/N; default true),{' '}
              <strong>AccountExecutiveEmail</strong>, <strong>AnalyticsLeadEmail</strong> (matched to a user email),
              and <strong>BusinessUnit</strong>, <strong>BusinessClass</strong>, <strong>ClientType</strong> (matched
              by name; provide all three together to classify the client).
              {clientBulkSkippedEmpty > 0
                ? ` ${clientBulkSkippedEmpty} CSV row(s) were skipped because ClientName was empty.`
                : ''}
            </p>

            {clientBulkVerifyOk ? (
              <div className="bulk-verify-pass" role="status">
                <span className="bulk-verify-check" aria-hidden="true">
                  OK
                </span>
                <span>Verification passed - you can select Import to load rows into tClient / tClientRef.</span>
              </div>
            ) : null}

            {clientBulkVerifyErrors.length > 0 ? (
              <div className="bulk-verify-fail" role="alert">
                <strong>Verification failed</strong>
                <ul className="bulk-verify-error-list">
                  {clientBulkVerifyErrors.map((err, i) => (
                    <li key={i}>{err}</li>
                  ))}
                </ul>
              </div>
            ) : null}

            {clientBulkRowErrors.length > 0 ? (
              <div className="bulk-verify-fail" role="alert">
                <strong>Import row messages</strong>
                <ul className="bulk-verify-error-list">
                  {clientBulkRowErrors.map((err, i) => (
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
                    <th>ShortName</th>
                    <th>Description</th>
                    <th>Account Executive</th>
                    <th>Analytics Lead</th>
                    <th>Business Unit</th>
                    <th>Business Class</th>
                    <th>Client Type</th>
                    <th>Is Active</th>
                  </tr>
                </thead>
                <tbody>
                  {clientBulkRows.length === 0 ? (
                    <tr>
                      <td colSpan={10}>
                        No rows to display. Ensure the CSV has a header row and at least one row with a ClientName.
                      </td>
                    </tr>
                  ) : (
                    clientBulkRows.map((r) => (
                      <tr key={r.lineNumber}>
                        <td>{r.lineNumber}</td>
                        <td>{r.clientName}</td>
                        <td>{r.clientShortName}</td>
                        <td>{r.clientDescription}</td>
                        <td>{r.accountExecutiveEmail}</td>
                        <td>{r.analyticsLeadEmail}</td>
                        <td>{r.businessUnit}</td>
                        <td>{r.businessClass}</td>
                        <td>{r.clientType}</td>
                        <td>{r.isActive ? 'true' : 'false'}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            <div className="button-row bulk-import-actions">
              <button type="button" className="secondary-button" disabled={clientBulkBusy} onClick={resetClientBulkWizard}>
                Cancel
              </button>
              <button
                type="button"
                className="secondary-button"
                disabled={clientBulkBusy || clientBulkRows.length === 0}
                onClick={() => void handleClientBulkVerify()}
              >
                Verify
              </button>
              <button
                type="button"
                className="primary-button"
                disabled={clientBulkBusy || !clientBulkVerifyOk || clientBulkRows.length === 0}
                onClick={() => void handleClientBulkCommit()}
              >
                Import
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
