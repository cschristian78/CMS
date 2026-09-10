/**
 * Home page workflow: all requests under /api/home (summary + materialized list + filter options).
 */

export const HOME_CLIENT_PROJECT_LIST_OPTIONS_URL = '/api/home/client-project-list/options';

const HOME_LIST_FILTERS_STORAGE_KEY = 'cms.home.listFilters.v3';

export type HomeListFilters = {
  businessUnit: string;
  businessClass: string;
  status: string;
  online: string;
  client: string;
  projectType: string;
  analyticsLeads: string[];
};

/** Default Main list view: all clients, Online projects only (other filters cleared). */
export const DEFAULT_HOME_LIST_FILTERS: HomeListFilters = {
  businessUnit: '',
  businessClass: '',
  status: '',
  online: 'Online',
  client: '',
  projectType: '',
  analyticsLeads: [],
};

export function createDefaultHomeListFilters(): HomeListFilters {
  return { ...DEFAULT_HOME_LIST_FILTERS, analyticsLeads: [] };
}

function asFilterString(value: unknown, fallback: string): string {
  return typeof value === 'string' ? value : fallback;
}

function asFilterStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  const names = value
    .filter((item): item is string => typeof item === 'string')
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
  return [...new Set(names)];
}

/** Restores Main list filters after navigating away (same browser tab). */
export function loadHomeListFilters(): HomeListFilters {
  try {
    const raw = sessionStorage.getItem(HOME_LIST_FILTERS_STORAGE_KEY);
    if (!raw) {
      return createDefaultHomeListFilters();
    }
    const parsed = JSON.parse(raw) as Partial<HomeListFilters>;
    return {
      businessUnit: asFilterString(parsed.businessUnit, DEFAULT_HOME_LIST_FILTERS.businessUnit),
      businessClass: asFilterString(parsed.businessClass, DEFAULT_HOME_LIST_FILTERS.businessClass),
      status: asFilterString(parsed.status, DEFAULT_HOME_LIST_FILTERS.status),
      online: asFilterString(parsed.online, DEFAULT_HOME_LIST_FILTERS.online),
      client: asFilterString(parsed.client, DEFAULT_HOME_LIST_FILTERS.client),
      projectType: asFilterString(parsed.projectType, DEFAULT_HOME_LIST_FILTERS.projectType),
      analyticsLeads: asFilterStringArray(parsed.analyticsLeads),
    };
  } catch {
    return createDefaultHomeListFilters();
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
  analyticsLeads: string[] = [],
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
  for (const name of analyticsLeads) {
    const trimmed = name.trim();
    if (trimmed) {
      p.append('analyticsLeadName', trimmed);
    }
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
  analyticsLeads: string[] = [],
): string {
  const p = new URLSearchParams();
  p.set('refresh', refresh ? 'true' : 'false');
  appendFilterParams(p, businessUnit, businessClass, status, onlineStatus, client, projectType, analyticsLeads);
  return `/api/home/client-project-list?${p.toString()}`;
}

export function buildHomeSummaryUrl(
  businessUnit: string,
  businessClass: string,
  status: string,
  client: string,
  projectType: string = '',
  onlineStatus: string = '',
  analyticsLeads: string[] = [],
): string {
  const p = new URLSearchParams();
  appendFilterParams(p, businessUnit, businessClass, status, onlineStatus, client, projectType, analyticsLeads);
  const qs = p.toString();
  return qs ? `/api/home/summary?${qs}` : '/api/home/summary';
}
