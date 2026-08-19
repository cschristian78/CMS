import { normalizeClientProjectListModel, type ClientProjectListModel } from '../clientProjectList';
import { toInputDateValue } from '../utils/dateInput';
import { REVENUE_TIER_ORDER } from './dashboardColors';

export type DashCell = string | number | boolean | null;

export interface DashboardInactiveProject {
  projectID: number;
  projectNumber?: string | null;
  projectName?: string | null;
  clientName?: string | null;
  createdDate?: string | null;
  isOnline?: boolean;
}

export interface DashboardDataset {
  columns: string[];
  rows: DashCell[][];
  roleColumns: string[];
  serviceColumns: string[];
  inactiveProjects: DashboardInactiveProject[];
}

export interface DashColIndex {
  projectId: number;
  clientId: number;
  clientName: number;
  projectNumber: number;
  projectName: number;
  businessUnit: number;
  businessClass: number;
  statusDescription: number;
  projectStatus: number;
  ptDescription: number;
  isOnline: number;
  revenueTier: number;
  latestRevenue: number;
  latestRevenueDate: number;
  createdDate: number;
  renewalDate: number;
  dueDate: number;
  userGroupName: number;
  totalHours: number;
}

export type DateField = 'latestRevenueDate' | 'createdDate' | 'renewalDate' | 'dueDate';

export interface GlobalDashFilters {
  businessUnit: string;
  businessClass: string;
  projectStatus: string;
  /** Client StatusDescription; default Active to match the home tiles. */
  statusDescription: string;
  dateFrom: string;
  dateTo: string;
}

export interface NamedMetric {
  label: string;
  count: number;
  revenue: number;
  hours: number;
  avgRevenue: number;
  extra?: number;
}

export type AgingBucket = 'Overdue' | 'Due this month' | 'Due this quarter' | 'Later' | 'No date';

export interface AgingRow {
  row: DashCell[];
  clientName: string;
  projectNumber: string;
  projectName: string;
  projectStatus: string;
  renewalBucket: AgingBucket;
  dueBucket: AgingBucket;
  renewalYmd: string;
  dueYmd: string;
}

function compactName(name: string): string {
  return name.replace(/[\s_\-.]/g, '').toLowerCase();
}

export function findDashColumn(columns: string[], ...logicalNames: string[]): number {
  if (!columns?.length) {
    return -1;
  }
  const normalized = columns.map((c) => compactName(c));
  for (const logical of logicalNames) {
    const want = compactName(logical);
    const exact = normalized.findIndex((n) => n === want);
    if (exact >= 0) {
      return exact;
    }
  }
  return -1;
}

export function buildColIndex(columns: string[]): DashColIndex {
  const isOnline =
    findDashColumn(columns, 'ProjectOnline') >= 0
      ? findDashColumn(columns, 'ProjectOnline')
      : findDashColumn(columns, 'IsOnline');
  return {
    projectId: findDashColumn(columns, 'ProjectID'),
    clientId: findDashColumn(columns, 'ClientID'),
    clientName: findDashColumn(columns, 'ClientName'),
    projectNumber: findDashColumn(columns, 'ProjectNumber'),
    projectName: findDashColumn(columns, 'ProjectName'),
    businessUnit: findDashColumn(columns, 'BusinessUnitName', 'SegmentName'),
    businessClass: findDashColumn(columns, 'BusinessClassName'),
    statusDescription: findDashColumn(columns, 'StatusDescription'),
    projectStatus: findDashColumn(columns, 'ProjectStatus'),
    ptDescription: findDashColumn(columns, 'PTDescription'),
    isOnline,
    revenueTier: findDashColumn(columns, 'RevenueTier'),
    latestRevenue: findDashColumn(columns, 'LatestRevenue', 'RevenueAmount'),
    latestRevenueDate: findDashColumn(columns, 'LatestRevenueEffectiveDate'),
    createdDate: findDashColumn(columns, 'CreatedDate'),
    renewalDate: findDashColumn(columns, 'RenewalDate'),
    dueDate: findDashColumn(columns, 'DueDate'),
    userGroupName: findDashColumn(columns, 'UserGroupName', 'UGName'),
    totalHours: findDashColumn(columns, 'TotalHours'),
  };
}

export function cellText(row: DashCell[], index: number): string {
  if (index < 0 || index >= row.length) {
    return '';
  }
  const raw = row[index];
  if (raw === null || raw === undefined || raw === '') {
    return '';
  }
  if (typeof raw === 'boolean') {
    return raw ? 'true' : 'false';
  }
  return String(raw).trim();
}

export function cellNumber(row: DashCell[], index: number): number {
  if (index < 0 || index >= row.length) {
    return 0;
  }
  const raw = row[index];
  if (typeof raw === 'number' && Number.isFinite(raw)) {
    return raw;
  }
  if (typeof raw === 'boolean') {
    return raw ? 1 : 0;
  }
  if (raw === null || raw === undefined || raw === '') {
    return 0;
  }
  const n = Number(String(raw).replace(/[$,\s]/g, ''));
  return Number.isFinite(n) ? n : 0;
}

export function cellBool(row: DashCell[], index: number): boolean {
  if (index < 0 || index >= row.length) {
    return false;
  }
  const raw = row[index];
  if (typeof raw === 'boolean') {
    return raw;
  }
  if (typeof raw === 'number') {
    return raw !== 0;
  }
  const t = String(raw ?? '').trim().toLowerCase();
  return t === '1' || t === 'true' || t === 'yes' || t === 'online' || t === 'y';
}

export function cellYmd(row: DashCell[], index: number): string {
  if (index < 0 || index >= row.length) {
    return '';
  }
  return toInputDateValue(row[index] as string | Date | null | undefined);
}

export function blankLabel(value: string, fallback = '(Unassigned)'): string {
  return value.trim() || fallback;
}

export function parsePositiveId(raw: DashCell): number | null {
  if (typeof raw === 'number' && Number.isFinite(raw) && raw > 0) {
    return Math.trunc(raw);
  }
  if (typeof raw === 'string' && raw.trim()) {
    const n = Number(raw.trim());
    if (Number.isFinite(n) && n > 0) {
      return Math.trunc(n);
    }
  }
  return null;
}

export function projectIdOf(row: DashCell[], col: DashColIndex): number | null {
  return col.projectId >= 0 ? parsePositiveId(row[col.projectId]) : null;
}

export function clientKey(row: DashCell[], col: DashColIndex): string {
  const cid = col.clientId >= 0 ? parsePositiveId(row[col.clientId]) : null;
  if (cid) {
    return `id:${cid}`;
  }
  const name = cellText(row, col.clientName);
  return name ? `name:${name}` : '';
}

export function summarizePortfolio(rows: DashCell[][], col: DashColIndex): {
  projects: number;
  clients: number;
  revenue: number;
  hours: number;
} {
  const projects = new Set<number>();
  const clients = new Set<string>();
  const revenueByProject = new Map<number, number>();
  let hours = 0;
  rows.forEach((row, i) => {
    const pid = projectIdOf(row, col) ?? -(i + 1);
    projects.add(pid);
    const key = clientKey(row, col);
    if (key) {
      clients.add(key);
    }
    const rev = cellNumber(row, col.latestRevenue);
    const prev = revenueByProject.get(pid) ?? 0;
    if (rev > prev) {
      revenueByProject.set(pid, rev);
    }
    hours += cellNumber(row, col.totalHours);
  });
  let revenue = 0;
  revenueByProject.forEach((v) => {
    revenue += v;
  });
  return { projects: projects.size, clients: clients.size, revenue, hours };
}

export function normalizeDataset(raw: unknown): DashboardDataset {
  const data = (raw ?? {}) as Record<string, unknown>;
  const model = normalizeClientProjectListModel({
    columns: Array.isArray(data.columns) ? (data.columns as string[]) : [],
    rows: Array.isArray(data.rows) ? (data.rows as ClientProjectListModel['rows']) : [],
  });
  const roleColumns = Array.isArray(data.roleColumns)
    ? (data.roleColumns as string[]).map((s) => String(s))
    : [];
  const serviceColumns = Array.isArray(data.serviceColumns)
    ? (data.serviceColumns as string[]).map((s) => String(s))
    : [];
  const inactiveRaw = Array.isArray(data.inactiveProjects) ? data.inactiveProjects : [];
  const inactiveProjects: DashboardInactiveProject[] = inactiveRaw
    .map((item) => {
      if (item === null || typeof item !== 'object') {
        return null;
      }
      const o = item as Record<string, unknown>;
      const idRaw = o.projectID ?? o.ProjectID;
      const id = typeof idRaw === 'number' ? idRaw : Number(idRaw);
      if (!Number.isFinite(id) || id <= 0) {
        return null;
      }
      const onlineRaw = o.isOnline ?? o.IsOnline;
      return {
        projectID: Math.trunc(id),
        projectNumber: o.projectNumber != null ? String(o.projectNumber) : o.ProjectNumber != null ? String(o.ProjectNumber) : '',
        projectName: o.projectName != null ? String(o.projectName) : o.ProjectName != null ? String(o.ProjectName) : '',
        clientName: o.clientName != null ? String(o.clientName) : o.ClientName != null ? String(o.ClientName) : '',
        createdDate: o.createdDate != null ? String(o.createdDate) : o.CreatedDate != null ? String(o.CreatedDate) : '',
        isOnline: onlineRaw === true || onlineRaw === 1 || String(onlineRaw).toLowerCase() === 'true',
      };
    })
    .filter((x): x is DashboardInactiveProject => x !== null);

  return {
    columns: model.columns,
    rows: model.rows,
    roleColumns,
    serviceColumns,
    inactiveProjects,
  };
}

export function distinctValues(rows: DashCell[][], index: number): string[] {
  if (index < 0) {
    return [];
  }
  const set = new Set<string>();
  for (const row of rows) {
    const v = cellText(row, index);
    if (v) {
      set.add(v);
    }
  }
  return [...set].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }));
}

export function splitPeople(cell: string): string[] {
  return cell
    .split(';')
    .map((p) => p.trim())
    .filter((p) => p.length > 0 && p.toLowerCase() !== 'n/a');
}

export function isBlankRoleValue(value: string): boolean {
  const t = value.trim().toLowerCase();
  return t === '' || t === 'n/a' || t === 'na' || t === 'null';
}

export function isServiceActive(value: string): boolean {
  const t = value.trim().toLowerCase();
  return t !== '' && t !== 'n/a' && t !== 'na' && t !== '0' && t !== 'false' && t !== 'inactive';
}

function ymdInRange(ymd: string, from: string, to: string): boolean {
  if (!from && !to) {
    return true;
  }
  if (!ymd) {
    return false;
  }
  if (from && ymd < from) {
    return false;
  }
  if (to && ymd > to) {
    return false;
  }
  return true;
}

export function applyGlobalFilters(
  rows: DashCell[][],
  col: DashColIndex,
  filters: GlobalDashFilters,
  dateField: DateField,
): DashCell[][] {
  const bu = filters.businessUnit.trim();
  const bc = filters.businessClass.trim();
  const status = filters.projectStatus.trim();
  const clientStatus = filters.statusDescription.trim();
  const from = filters.dateFrom.trim();
  const to = filters.dateTo.trim();
  const dateIndex =
    dateField === 'latestRevenueDate'
      ? col.latestRevenueDate
      : dateField === 'createdDate'
        ? col.createdDate
        : dateField === 'renewalDate'
          ? col.renewalDate
          : col.dueDate;

  return rows.filter((row) => {
    if (bu && cellText(row, col.businessUnit) !== bu) {
      return false;
    }
    if (bc && cellText(row, col.businessClass) !== bc) {
      return false;
    }
    if (status && cellText(row, col.projectStatus) !== status) {
      return false;
    }
    if (clientStatus && cellText(row, col.statusDescription) !== clientStatus) {
      return false;
    }
    if (from || to) {
      return ymdInRange(cellYmd(row, dateIndex), from, to);
    }
    return true;
  });
}

export function groupMetrics(
  rows: DashCell[][],
  col: DashColIndex,
  labelIndex: number,
  blank = '(Unassigned)',
): NamedMetric[] {
  const map = new Map<string, { ids: Set<number>; clientIds: Set<number>; revenue: number; hours: number }>();
  rows.forEach((row, ri) => {
    const label = blankLabel(cellText(row, labelIndex), blank);
    let bucket = map.get(label);
    if (!bucket) {
      bucket = { ids: new Set(), clientIds: new Set(), revenue: 0, hours: 0 };
      map.set(label, bucket);
    }
    const pid = projectIdOf(row, col) ?? -(ri + 1);
    bucket.ids.add(pid);
    const cid = col.clientId >= 0 ? parsePositiveId(row[col.clientId]) : null;
    if (cid) {
      bucket.clientIds.add(cid);
    } else {
      const name = cellText(row, col.clientName);
      if (name) {
        bucket.clientIds.add(name.length + 1_000_000_000);
      }
    }
    bucket.revenue += cellNumber(row, col.latestRevenue);
    bucket.hours += cellNumber(row, col.totalHours);
  });

  return [...map.entries()]
    .map(([label, b]) => ({
      label,
      count: b.ids.size,
      extra: b.clientIds.size,
      revenue: b.revenue,
      hours: b.hours,
      avgRevenue: b.ids.size > 0 ? b.revenue / b.ids.size : 0,
    }))
    .sort((a, b) => b.revenue - a.revenue || b.count - a.count || a.label.localeCompare(b.label));
}

export function onlineLabel(row: DashCell[], col: DashColIndex): string {
  return cellBool(row, col.isOnline) ? 'Online' : 'Offline';
}

export function monthKey(ymd: string): string {
  return ymd.length >= 7 ? ymd.slice(0, 7) : '';
}

export function formatMonthLabel(key: string): string {
  const m = key.trim().match(/^(\d{4})-(\d{2})$/);
  if (!m) {
    return key;
  }
  const names = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const idx = Number(m[2]) - 1;
  return `${names[idx] ?? m[2]} ${m[1]}`;
}

export function trendByMonth(
  rows: DashCell[][],
  col: DashColIndex,
  dateIndex: number,
  mode: 'revenue' | 'count',
): NamedMetric[] {
  const map = new Map<string, { ids: Set<number>; revenue: number }>();
  rows.forEach((row, ri) => {
    const key = monthKey(cellYmd(row, dateIndex));
    if (!key) {
      return;
    }
    let bucket = map.get(key);
    if (!bucket) {
      bucket = { ids: new Set(), revenue: 0 };
      map.set(key, bucket);
    }
    bucket.ids.add(projectIdOf(row, col) ?? -(ri + 1));
    bucket.revenue += cellNumber(row, col.latestRevenue);
  });
  return [...map.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([label, b]) => ({
      label,
      count: b.ids.size,
      extra: b.ids.size,
      revenue: b.revenue,
      hours: 0,
      avgRevenue: b.ids.size > 0 ? b.revenue / b.ids.size : 0,
    }))
    .map((x) => (mode === 'count' ? x : x));
}

export function sortTiers(metrics: NamedMetric[]): NamedMetric[] {
  const order = new Map(REVENUE_TIER_ORDER.map((t, i) => [compactName(t), i]));
  return [...metrics].sort((a, b) => {
    const ai = order.get(compactName(a.label)) ?? 99;
    const bi = order.get(compactName(b.label)) ?? 99;
    return ai - bi || a.label.localeCompare(b.label);
  });
}

function calendarParts(ymd: string): { y: number; m: number; d: number } | null {
  const m = ymd.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!m) {
    return null;
  }
  return { y: Number(m[1]), m: Number(m[2]), d: Number(m[3]) };
}

export function agingBucket(ymd: string, todayYmd: string): AgingBucket {
  if (!ymd) {
    return 'No date';
  }
  if (ymd < todayYmd) {
    return 'Overdue';
  }
  const date = calendarParts(ymd);
  const today = calendarParts(todayYmd);
  if (!date || !today) {
    return 'Later';
  }
  if (date.y === today.y && date.m === today.m) {
    return 'Due this month';
  }
  const dateQ = Math.floor((date.m - 1) / 3);
  const todayQ = Math.floor((today.m - 1) / 3);
  if (date.y === today.y && dateQ === todayQ) {
    return 'Due this quarter';
  }
  return 'Later';
}

export function todayYmd(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

export function buildAgingRows(rows: DashCell[][], col: DashColIndex): AgingRow[] {
  const today = todayYmd();
  return rows.map((row) => {
    const renewalYmd = cellYmd(row, col.renewalDate);
    const dueYmd = cellYmd(row, col.dueDate);
    return {
      row,
      clientName: blankLabel(cellText(row, col.clientName), '(No client)'),
      projectNumber: cellText(row, col.projectNumber),
      projectName: cellText(row, col.projectName),
      projectStatus: blankLabel(cellText(row, col.projectStatus), 'Active'),
      renewalBucket: agingBucket(renewalYmd, today),
      dueBucket: agingBucket(dueYmd, today),
      renewalYmd,
      dueYmd,
    };
  });
}

export function displayServiceName(columnName: string): string {
  return columnName.replace(/_/g, ' ');
}
