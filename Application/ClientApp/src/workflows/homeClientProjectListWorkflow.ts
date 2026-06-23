/**
 * Home page workflow: all requests under /api/home (summary + materialized list + filter options).
 */

export const HOME_CLIENT_PROJECT_LIST_OPTIONS_URL = '/api/home/client-project-list/options';

function appendFilterParams(
  p: URLSearchParams,
  businessUnit: string,
  businessClass: string,
  status: string,
  client: string,
  projectType: string,
): void {
  const bu = businessUnit.trim();
  const bc = businessClass.trim();
  const stat = status.trim();
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
): string {
  const p = new URLSearchParams();
  p.set('refresh', refresh ? 'true' : 'false');
  appendFilterParams(p, businessUnit, businessClass, status, client, projectType);
  return `/api/home/client-project-list?${p.toString()}`;
}

export function buildHomeSummaryUrl(
  businessUnit: string,
  businessClass: string,
  status: string,
  client: string,
  projectType: string = '',
): string {
  const p = new URLSearchParams();
  appendFilterParams(p, businessUnit, businessClass, status, client, projectType);
  const qs = p.toString();
  return qs ? `/api/home/summary?${qs}` : '/api/home/summary';
}
