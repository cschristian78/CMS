import { toApiDateValue } from './utils/dateInput';

/** Helpers for Edit Project per-tab dirty detection and change summaries. */

export type ProjectTabId =
  | 'client'
  | 'project'
  | 'projectData'
  | 'services'
  | 'calendar'
  | 'time'
  | 'revenue'
  | 'notes';

export const PROJECT_TAB_LABELS: Record<ProjectTabId, string> = {
  client: 'Client Info',
  project: 'Project Info',
  projectData: 'Project Data',
  services: 'Services',
  calendar: 'Calendar',
  time: 'Time tracker',
  revenue: 'Revenue history',
  notes: 'Notes',
};

export interface ClientSlice {
  clientBusinessUnitID: number | null | '';
  clientBusinessClassID: number | null | '';
  clientTypeID: number | null | '';
  clientStatusID: number | null | '';
}

export interface ProjectSlice {
  projectNumber: string;
  projectName: string;
  ugid: number;
  projectTypeID: number;
  renewalDate: string | null;
  dueDate: string | null;
  statusID: number;
  revenueAmount: number | null;
  revenueEffectiveDate: string | null;
  revenueChangeReason: string | null;
}

export function pickClientSlice(m: {
  clientBusinessUnitID?: number | null;
  clientBusinessClassID?: number | null;
  clientTypeID?: number | null;
  clientStatusID?: number | null;
}): ClientSlice {
  return {
    clientBusinessUnitID: m.clientBusinessUnitID ?? '',
    clientBusinessClassID: m.clientBusinessClassID ?? '',
    clientTypeID: m.clientTypeID ?? '',
    clientStatusID: m.clientStatusID ?? '',
  };
}

export function pickProjectSlice(m: {
  projectNumber: string;
  projectName: string;
  ugid: number;
  projectTypeID: number;
  renewalDate?: string | null;
  dueDate?: string | null;
  statusID: number;
  revenueAmount?: number | null;
  revenueEffectiveDate?: string | null;
  revenueChangeReason?: string | null;
}): ProjectSlice {
  return {
    projectNumber: m.projectNumber ?? '',
    projectName: m.projectName ?? '',
    ugid: m.ugid ?? 0,
    projectTypeID: m.projectTypeID ?? 0,
    renewalDate: toApiDateValue(m.renewalDate),
    dueDate: toApiDateValue(m.dueDate),
    statusID: m.statusID ?? 0,
    revenueAmount: m.revenueAmount ?? null,
    revenueEffectiveDate: toApiDateValue(m.revenueEffectiveDate),
    revenueChangeReason: (m.revenueChangeReason ?? '').trim() || null,
  };
}

/** Normalize date strings before POST /api/projects so ASP.NET can bind DateTime? fields. */
export function sanitizeProjectFormForApi<
  T extends {
    renewalDate?: string | null;
    dueDate?: string | null;
    revenueEffectiveDate?: string | null;
    revenueChangeReason?: string | null;
  },
>(model: T): T {
  return {
    ...model,
    renewalDate: toApiDateValue(model.renewalDate),
    dueDate: toApiDateValue(model.dueDate),
    revenueEffectiveDate: toApiDateValue(model.revenueEffectiveDate),
    revenueChangeReason: (model.revenueChangeReason ?? '').trim() || null,
  };
}

function normId(v: number | null | '' | undefined): number {
  if (v === '' || v === null || v === undefined) {
    return 0;
  }
  return Number(v) || 0;
}

export function clientSliceEqual(a: ClientSlice, b: ClientSlice): boolean {
  return (
    normId(a.clientBusinessUnitID) === normId(b.clientBusinessUnitID) &&
    normId(a.clientBusinessClassID) === normId(b.clientBusinessClassID) &&
    normId(a.clientTypeID) === normId(b.clientTypeID) &&
    normId(a.clientStatusID) === normId(b.clientStatusID)
  );
}

export function projectSliceEqual(a: ProjectSlice, b: ProjectSlice): boolean {
  return (
    (a.projectNumber ?? '') === (b.projectNumber ?? '') &&
    (a.projectName ?? '') === (b.projectName ?? '') &&
    (a.ugid ?? 0) === (b.ugid ?? 0) &&
    (a.projectTypeID ?? 0) === (b.projectTypeID ?? 0) &&
    (a.renewalDate ?? '') === (b.renewalDate ?? '') &&
    (a.dueDate ?? '') === (b.dueDate ?? '') &&
    (a.statusID ?? 0) === (b.statusID ?? 0) &&
    Number(a.revenueAmount ?? 0) === Number(b.revenueAmount ?? 0) &&
    (a.revenueEffectiveDate ?? '') === (b.revenueEffectiveDate ?? '') &&
    (a.revenueChangeReason ?? '') === (b.revenueChangeReason ?? '')
  );
}

export function serviceIdsEqual(a: number[], b: number[]): boolean {
  const aa = [...a].sort((x, y) => x - y);
  const bb = [...b].sort((x, y) => x - y);
  return aa.length === bb.length && aa.every((v, i) => v === bb[i]);
}

export function describeClientChanges(base: ClientSlice, curr: ClientSlice): string[] {
  const changes: string[] = [];
  if (normId(base.clientBusinessUnitID) !== normId(curr.clientBusinessUnitID)) {
    changes.push('Business Unit');
  }
  if (normId(base.clientBusinessClassID) !== normId(curr.clientBusinessClassID)) {
    changes.push('Business Class');
  }
  if (normId(base.clientTypeID) !== normId(curr.clientTypeID)) {
    changes.push('Client Type');
  }
  if (normId(base.clientStatusID) !== normId(curr.clientStatusID)) {
    changes.push('Client Status');
  }
  return changes;
}

export function describeProjectChanges(base: ProjectSlice, curr: ProjectSlice): string[] {
  const changes: string[] = [];
  if ((base.projectNumber ?? '') !== (curr.projectNumber ?? '')) {
    changes.push('Project Number');
  }
  if ((base.projectName ?? '') !== (curr.projectName ?? '')) {
    changes.push('Project Name');
  }
  if ((base.ugid ?? 0) !== (curr.ugid ?? 0)) {
    changes.push('User Group');
  }
  if ((base.projectTypeID ?? 0) !== (curr.projectTypeID ?? 0)) {
    changes.push('Project Type');
  }
  if ((base.renewalDate ?? '') !== (curr.renewalDate ?? '')) {
    changes.push('Renewal Date');
  }
  if ((base.dueDate ?? '') !== (curr.dueDate ?? '')) {
    changes.push('Due Date');
  }
  if ((base.statusID ?? 0) !== (curr.statusID ?? 0)) {
    changes.push('Project Status');
  }
  if (
    Number(base.revenueAmount ?? 0) !== Number(curr.revenueAmount ?? 0) ||
    (base.revenueEffectiveDate ?? '') !== (curr.revenueEffectiveDate ?? '') ||
    (base.revenueChangeReason ?? '') !== (curr.revenueChangeReason ?? '')
  ) {
    changes.push('New revenue entry');
  }
  return changes;
}
