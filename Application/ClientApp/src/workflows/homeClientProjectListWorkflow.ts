/**
 * Home page workflow: all requests under /api/home (summary + materialized list + filter options).
 */

export const HOME_CLIENT_PROJECT_LIST_OPTIONS_URL = '/api/home/client-project-list/options';

const HOME_LIST_FILTERS_STORAGE_KEY = 'cms.home.listFilters.v2';

export type HomeListFilters = {
  businessUnit: string;
  businessClass: string;
  status: string;
  online: string;
  client: string;
  projectType: string;
};

const DEFAULT_HOME_LIST_FILTERS: HomeListFilters = {
  businessUnit: '',
  businessClass: '',
  status: 'Active',
  online: 'Online',
  client: '',
  projectType: '',
};

function asFilterString(value: unknown, fallback: string): string {
  return typeof value === 'string' ? value : fallback;
}

/** Restores Main list filters after navigating away (same browser tab). */
export function loadHomeListFilters(): HomeListFilters {
  try {
    const raw = sessionStorage.getItem(HOME_LIST_FILTERS_STORAGE_KEY);
    if (!raw) {
      return { ...DEFAULT_HOME_LIST_FILTERS };
    }
    const parsed = JSON.parse(raw) as Partial<HomeListFilters>;
    return {
      businessUnit: asFilterString(parsed.businessUnit, DEFAULT_HOME_LIST_FILTERS.businessUnit),
      businessClass: asFilterString(parsed.businessClass, DEFAULT_HOME_LIST_FILTERS.businessClass),
      status: asFilterString(parsed.status, DEFAULT_HOME_LIST_FILTERS.status),
      online: asFilterString(parsed.online, DEFAULT_HOME_LIST_FILTERS.online),
      client: asFilterString(parsed.client, DEFAULT_HOME_LIST_FILTERS.client),
      projectType: asFilterString(parsed.projectType, DEFAULT_HOME_LIST_FILTERS.projectType),
    };
  } catch {
    return { ...DEFAULT_HOME_LIST_FILTERS };
  }
}

export function saveHomeListFilters(filters: HomeListFilters): void {
  try {
    sessionStorage.setItem(HOME_LIST_FILTERS_STORAGE_KEY, JSON.stringify(filters));
  } catch {
    // Ignore quota / private-mode failures; filters still work for the current visit.
  }
}

function appendFilterParams(
  p: URLSearchParams,
  businessUnit: string,
  businessClass: string,
  status: string,
  onlineStatus: string,
  client: string,
  projectType: string,
): void {
  const bu = businessUnit.trim();
  const bc = businessClass.trim();
  const stat = status.trim();
  const online = onlineStatus.trim();
  const cli = client.trim();
  const pt = projectType.trim();
  if (bu) {
    p.set('businessUnitName', bu);
  }
  if (bc) {
    p.set('businessClassName', bc);
  }
  if (stat) {
    p.set('statusName', stat);
  }
  if (online) {
    p.set('onlineStatus', online);
  }
  if (cli) {
    p.set('clientName', cli);
  }
  if (pt) {
    p.set('projectTypeName', pt);
  }
}

export function buildHomeClientProjectListUrl(
  refresh: boolean,
  businessUnit: string,
  businessClass: string,
  status: string,
  client: string,
  projectType: string = '',
  onlineStatus: string = '',
): string {
  const p = new URLSearchParams();
  p.set('refresh', refresh ? 'true' : 'false');
  appendFilterParams(p, businessUnit, businessClass, status, onlineStatus, client, projectType);
  return `/api/home/client-project-list?${p.toString()}`;
}

export function buildHomeSummaryUrl(
  businessUnit: string,
  businessClass: string,
  status: string,
  client: string,
  projectType: string = '',
  onlineStatus: string = '',
): string {
  const p = new URLSearchParams();
  appendFilterParams(p, businessUnit, businessClass, status, onlineStatus, client, projectType);
  const qs = p.toString();
  return qs ? `/api/home/summary?${qs}` : '/api/home/summary';
}
