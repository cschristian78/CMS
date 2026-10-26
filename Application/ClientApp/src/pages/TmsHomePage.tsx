import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import type { FlashFn } from '../App';
import { ApiError, apiGet, apiPostJson } from '../api';
import { fetchAuthSession } from '../authSession';
import { setCatAppMode } from '../catAppMode';
import { formatCalendarDateDisplay, toInputDateValue } from '../utils/dateInput';

type CatUser = {
  userID: number;
  userFullName: string;
  email?: string | null;
};

type TaskRow = {
  calendarItemID: number;
  itemType?: string | null;
  title?: string | null;
  sortOrder?: number;
};

type TimeEntry = {
  calendarItemID: number;
  workDate: string;
  hours: number;
  entryMode?: string;
};

type OtherEntry = {
  category: string;
  workDate: string;
  hours: number;
  entryMode?: string;
};

type ProjectBlock = {
  projectID: number;
  projectName: string;
  clientName?: string | null;
  workStatus?: string;
  isOnline?: boolean;
  tasks: TaskRow[];
  entries: TimeEntry[];
};

type WeekResponse = {
  weekStartDate: string;
  weekEndDate: string;
  userID: number;
  projects: ProjectBlock[];
  otherEntries: OtherEntry[];
  otherCategories: string[];
};

type HoursSummary = {
  userID: number;
  thisWeekHours: number;
  lastWeekHours: number;
  monthHours: number;
  ytdHours: number;
};

type TmsTimeTab = 'client' | 'nonClient';

const DAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const;
const DEFAULT_OTHER = [
  'Meetings',
  'Holidays',
  'PTO',
  'Travel',
  'Training',
  'CSI',
  'Moodys MAT',
  'Verisk MAT',
  'Peril Advisory',
  'Administrative',
] as const;

function startOfWeekMonday(d: Date): Date {
  const date = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const day = date.getDay();
  const diff = (day + 6) % 7;
  date.setDate(date.getDate() - diff);
  return date;
}

function addDays(d: Date, n: number): Date {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  x.setDate(x.getDate() + n);
  return x;
}

function toYmd(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function parseHours(raw: string): number {
  const n = Number(String(raw).trim().replace(',', '.'));
  if (!Number.isFinite(n) || n < 0) {
    return 0;
  }
  return Math.min(24, Math.round(n * 100) / 100);
}

function formatHours(n: number): string {
  if (!n) {
    return '';
  }
  return String(Math.round(n * 100) / 100);
}

function formatSummaryHours(n: number): string {
  const value = Number.isFinite(n) ? Math.round(n * 100) / 100 : 0;
  return value.toLocaleString(undefined, {
    minimumFractionDigits: value % 1 === 0 ? 0 : 1,
    maximumFractionDigits: 2,
  });
}

function normalizeHoursSummary(raw: Record<string, unknown> | null | undefined): HoursSummary | null {
  if (!raw) {
    return null;
  }
  const userID = Number(raw.userID ?? raw.UserID ?? 0);
  if (!Number.isFinite(userID) || userID <= 0) {
    return null;
  }
  return {
    userID,
    thisWeekHours: Number(raw.thisWeekHours ?? raw.ThisWeekHours ?? 0) || 0,
    lastWeekHours: Number(raw.lastWeekHours ?? raw.LastWeekHours ?? 0) || 0,
    monthHours: Number(raw.monthHours ?? raw.MonthHours ?? 0) || 0,
    ytdHours: Number(raw.ytdHours ?? raw.YtdHours ?? 0) || 0,
  };
}

function normalizeCatUser(raw: Record<string, unknown>): CatUser | null {
  const userID = Number(raw.userID ?? raw.UserID ?? 0);
  if (!Number.isFinite(userID) || userID <= 0) {
    return null;
  }
  return {
    userID,
    userFullName: String(raw.userFullName ?? raw.UserFullName ?? '').trim() || `User ${userID}`,
    email: (raw.email ?? raw.Email ?? null) as string | null,
  };
}

type CellMap = Record<string, string>;

function projectCellKey(projectId: number, calendarItemID: number, ymd: string): string {
  return `p:${projectId}|${calendarItemID}|${ymd}`;
}

function otherCellKey(category: string, ymd: string): string {
  return `o:${category}|${ymd}`;
}

function projectDisplayTitle(project: Pick<ProjectBlock, 'clientName' | 'projectName'>): string {
  const client = (project.clientName ?? '').trim();
  return client ? `${client} — ${project.projectName}` : project.projectName;
}

function projectWorkStatus(project: Pick<ProjectBlock, 'workStatus'>): string {
  const text = (project.workStatus ?? '').trim().toLowerCase();
  if (text === 'planned') {
    return 'Planned';
  }
  if (text === 'in progress' || text === 'inprogress' || text === 'online') {
    return 'In Progress';
  }
  if (text === 'closed') {
    return 'Closed';
  }
  return 'Planned';
}

function compareProjectsAlpha(a: ProjectBlock, b: ProjectBlock): number {
  return projectDisplayTitle(a).localeCompare(projectDisplayTitle(b), undefined, { sensitivity: 'base' });
}

export default function TmsHomePage({ flash }: { flash: FlashFn }) {
  const [catUsers, setCatUsers] = useState<CatUser[]>([]);
  const [selectedUserId, setSelectedUserId] = useState<number | ''>('');
  const [weekStart, setWeekStart] = useState(() => startOfWeekMonday(new Date()));
  const [projects, setProjects] = useState<ProjectBlock[]>([]);
  const [otherCategories, setOtherCategories] = useState<string[]>([...DEFAULT_OTHER]);
  const [projectCells, setProjectCells] = useState<CellMap>({});
  const [otherCells, setOtherCells] = useState<CellMap>({});
  const [visibleOtherCategories, setVisibleOtherCategories] = useState<string[]>([]);
  const [loadingUsers, setLoadingUsers] = useState(true);
  const [loadingWeek, setLoadingWeek] = useState(false);
  const [saving, setSaving] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [addCategory, setAddCategory] = useState<string>(DEFAULT_OTHER[0]);
  const [activeTab, setActiveTab] = useState<TmsTimeTab>('client');
  const [collapsedProjectIds, setCollapsedProjectIds] = useState<Set<number>>(() => new Set());
  const [canSelectAnyUser, setCanSelectAnyUser] = useState(false);
  const [hoursSummary, setHoursSummary] = useState<HoursSummary | null>(null);
  const [loadingSummary, setLoadingSummary] = useState(false);

  const weekDays = useMemo(
    () => DAY_LABELS.map((label, i) => ({ label, date: addDays(weekStart, i), ymd: toYmd(addDays(weekStart, i)) })),
    [weekStart],
  );

  const selectedUser = useMemo(
    () => catUsers.find((u) => u.userID === selectedUserId) ?? null,
    [catUsers, selectedUserId],
  );

  const projectsByStatus = useMemo(() => {
    const statusOf = (p: ProjectBlock) => projectWorkStatus(p);
    return {
      inProgress: projects.filter((p) => statusOf(p) === 'In Progress').sort(compareProjectsAlpha),
      planned: projects.filter((p) => statusOf(p) === 'Planned').sort(compareProjectsAlpha),
      closed: projects.filter((p) => statusOf(p) === 'Closed').sort(compareProjectsAlpha),
    };
  }, [projects]);

  const availableAddCategories = useMemo(
    () => otherCategories.filter((c) => !visibleOtherCategories.includes(c)),
    [otherCategories, visibleOtherCategories],
  );

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoadingUsers(true);
      try {
        const [session, raw] = await Promise.all([
          fetchAuthSession(),
          apiGet<unknown>('/api/tms/cat-users'),
        ]);
        const rows = Array.isArray(raw) ? raw : [];
        if (cancelled) {
          return;
        }
        const allUsers = rows
          .map((row) => normalizeCatUser(row as Record<string, unknown>))
          .filter((x): x is CatUser => x !== null)
          .sort((a, b) => a.userFullName.localeCompare(b.userFullName));
        const sessionUserId = session.authenticated ? Number(session.userID ?? 0) : 0;
        const sessionEmail = (session.email ?? '').trim().toLowerCase();
        const sessionName = (session.fullName ?? '').trim().toLowerCase();
        const isAdmin =
          session.authenticated &&
          (session.userType ?? '').trim().toLowerCase() === 'administrator';

        const matched =
          (sessionUserId > 0 ? allUsers.find((u) => u.userID === sessionUserId) : undefined) ??
          (sessionEmail
            ? allUsers.find((u) => (u.email ?? '').trim().toLowerCase() === sessionEmail)
            : undefined) ??
          (sessionName
            ? allUsers.find((u) => u.userFullName.trim().toLowerCase() === sessionName)
            : undefined);

        if (isAdmin) {
          if (allUsers.length === 0) {
            setCatUsers([]);
            setSelectedUserId('');
            setCanSelectAnyUser(false);
            flash('error', 'No active CAT users are available for time review.');
            return;
          }
          setCanSelectAnyUser(true);
          setCatUsers(allUsers);
          setSelectedUserId(matched?.userID ?? allUsers[0].userID);
          return;
        }

        setCanSelectAnyUser(false);
        if (!matched) {
          setCatUsers([]);
          setSelectedUserId('');
          flash(
            'error',
            'Your signed-in account is not in the CAT department list, so time tracking is unavailable.',
          );
          return;
        }

        setCatUsers([matched]);
        setSelectedUserId(matched.userID);
      } catch (err) {
        if (!cancelled) {
          flash('error', err instanceof ApiError ? err.message : 'Could not load CAT users.');
          setCatUsers([]);
          setSelectedUserId('');
          setCanSelectAnyUser(false);
        }
      } finally {
        if (!cancelled) {
          setLoadingUsers(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [flash]);

  const loadHoursSummary = useCallback(async () => {
    if (selectedUserId === '' || selectedUserId <= 0) {
      setHoursSummary(null);
      return;
    }

    setLoadingSummary(true);
    try {
      const data = await apiGet<Record<string, unknown>>(
        `/api/tms/hours-summary?userId=${encodeURIComponent(String(selectedUserId))}`,
      );
      setHoursSummary(normalizeHoursSummary(data));
    } catch {
      setHoursSummary(null);
    } finally {
      setLoadingSummary(false);
    }
  }, [selectedUserId]);

  const loadWeek = useCallback(async () => {
    if (selectedUserId === '' || selectedUserId <= 0) {
      setProjects([]);
      setProjectCells({});
      setOtherCells({});
      setVisibleOtherCategories([]);
      setCollapsedProjectIds(new Set());
      setLoadError(null);
      return;
    }

    setLoadingWeek(true);
    setLoadError(null);
    try {
      const data = await apiGet<WeekResponse>(
        `/api/tms/week?userId=${encodeURIComponent(String(selectedUserId))}&weekStart=${encodeURIComponent(toYmd(weekStart))}`,
      );

      const nextProjects = (data.projects ?? [])
        .map((p) => ({
          projectID: Number(p.projectID),
          projectName: (p.projectName ?? '').trim() || `Project ${p.projectID}`,
          clientName: p.clientName ?? null,
          workStatus: String(
            (p as ProjectBlock).workStatus ?? (p as { WorkStatus?: string }).WorkStatus ?? '',
          ),
          isOnline: Boolean(
            (p as ProjectBlock).isOnline ?? (p as { IsOnline?: boolean }).IsOnline,
          ),
          tasks: [...(p.tasks ?? [])]
            .filter((t) => Number(t.calendarItemID) > 0)
            .map((t) => ({
              calendarItemID: Number(t.calendarItemID),
              itemType: t.itemType ?? 'Task',
              title: (t.title ?? '').trim() || `Task ${t.calendarItemID}`,
              sortOrder: t.sortOrder ?? 0,
            }))
            .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || (a.title ?? '').localeCompare(b.title ?? '')),
          entries: p.entries ?? [],
        }))
        .sort((a, b) => {
          const rank = (status: string) => (status === 'In Progress' ? 0 : status === 'Planned' ? 1 : 2);
          const statusDelta = rank(projectWorkStatus(a)) - rank(projectWorkStatus(b));
          if (statusDelta !== 0) {
            return statusDelta;
          }
          return compareProjectsAlpha(a, b);
        });
      setProjects(nextProjects);
      setCollapsedProjectIds(new Set(nextProjects.map((p) => p.projectID)));

      const nextProjectCells: CellMap = {};
      for (const project of nextProjects) {
        for (const entry of project.entries ?? []) {
          const ymd = toInputDateValue(entry.workDate);
          const calendarItemID = Number(entry.calendarItemID);
          if (!ymd || calendarItemID <= 0) {
            continue;
          }
          nextProjectCells[projectCellKey(project.projectID, calendarItemID, ymd)] = formatHours(
            Number(entry.hours) || 0,
          );
        }
      }
      setProjectCells(nextProjectCells);

      const categories =
        Array.isArray(data.otherCategories) && data.otherCategories.length > 0
          ? data.otherCategories
          : [...DEFAULT_OTHER];
      setOtherCategories(categories);

      const nextOtherCells: CellMap = {};
      const usedCategories = new Set<string>();
      for (const entry of data.otherEntries ?? []) {
        const category = String(entry.category ?? '').trim();
        const ymd = toInputDateValue(entry.workDate);
        if (!category || !ymd) {
          continue;
        }
        usedCategories.add(category);
        nextOtherCells[otherCellKey(category, ymd)] = formatHours(Number(entry.hours) || 0);
      }
      setOtherCells(nextOtherCells);
      setVisibleOtherCategories(categories.filter((c) => usedCategories.has(c)));
      setAddCategory(categories.find((c) => !usedCategories.has(c)) ?? categories[0] ?? DEFAULT_OTHER[0]);
    } catch (err) {
      setLoadError(err instanceof ApiError ? err.message : 'Could not load time week.');
      setProjects([]);
      setProjectCells({});
      setOtherCells({});
      setVisibleOtherCategories([]);
    } finally {
      setLoadingWeek(false);
    }
  }, [selectedUserId, weekStart]);

  useEffect(() => {
    void loadWeek();
  }, [loadWeek]);

  useEffect(() => {
    void loadHoursSummary();
  }, [loadHoursSummary]);

  const setProjectHours = (projectId: number, calendarItemID: number, ymd: string, hours: string) => {
    setProjectCells((prev) => ({
      ...prev,
      [projectCellKey(projectId, calendarItemID, ymd)]: hours,
    }));
  };

  const setOtherHours = (category: string, ymd: string, hours: string) => {
    setOtherCells((prev) => ({
      ...prev,
      [otherCellKey(category, ymd)]: hours,
    }));
  };

  const projectRowTotal = (projectId: number, calendarItemID: number) =>
    weekDays.reduce(
      (sum, d) => sum + parseHours(projectCells[projectCellKey(projectId, calendarItemID, d.ymd)] ?? ''),
      0,
    );

  const projectDayTotal = (project: ProjectBlock, ymd: string) =>
    project.tasks.reduce(
      (sum, t) => sum + parseHours(projectCells[projectCellKey(project.projectID, t.calendarItemID, ymd)] ?? ''),
      0,
    );

  const projectWeekTotal = (project: ProjectBlock) =>
    project.tasks.reduce((sum, t) => sum + projectRowTotal(project.projectID, t.calendarItemID), 0);

  const isProjectCollapsed = (projectId: number) => collapsedProjectIds.has(projectId);

  const toggleProjectCollapsed = (projectId: number) => {
    setCollapsedProjectIds((prev) => {
      const next = new Set(prev);
      if (next.has(projectId)) {
        next.delete(projectId);
      } else {
        next.add(projectId);
      }
      return next;
    });
  };

  const expandAllProjects = () => setCollapsedProjectIds(new Set());

  const collapseAllProjects = () => {
    setCollapsedProjectIds(new Set(projects.map((p) => p.projectID)));
  };

  const otherRowTotal = (category: string) =>
    weekDays.reduce((sum, d) => sum + parseHours(otherCells[otherCellKey(category, d.ymd)] ?? ''), 0);

  const handleAddOtherCategory = () => {
    if (!addCategory || visibleOtherCategories.includes(addCategory)) {
      return;
    }
    const nextVisible = [...visibleOtherCategories, addCategory];
    setVisibleOtherCategories(nextVisible);
    setAddCategory(otherCategories.find((c) => !nextVisible.includes(c)) ?? '');
  };

  const handleSave = async () => {
    if (selectedUserId === '' || selectedUserId <= 0) {
      flash('error', 'Select a CAT department user first.');
      return;
    }

    setSaving(true);
    try {
      const projectPayload = projects.map((project) => {
        const entries: TimeEntry[] = [];
        for (const task of project.tasks) {
          for (const day of weekDays) {
            const key = projectCellKey(project.projectID, task.calendarItemID, day.ymd);
            const raw = projectCells[key];
            if (raw === undefined) {
              continue;
            }
            entries.push({
              calendarItemID: task.calendarItemID,
              workDate: day.ymd,
              hours: parseHours(raw),
              entryMode: 'Exact',
            });
          }
        }
        return { projectID: project.projectID, entries };
      });

      const otherEntries: OtherEntry[] = [];
      for (const category of visibleOtherCategories) {
        for (const day of weekDays) {
          const key = otherCellKey(category, day.ymd);
          const raw = otherCells[key];
          if (raw === undefined) {
            continue;
          }
          otherEntries.push({
            category,
            workDate: day.ymd,
            hours: parseHours(raw),
            entryMode: 'Exact',
          });
        }
      }

      const result = await apiPostJson<{ message?: string }>('/api/tms/week', {
        weekStartDate: toYmd(weekStart),
        userID: selectedUserId,
        projects: projectPayload,
        otherEntries,
      });
      flash('success', result.message ?? 'Time week saved.');
      await Promise.all([loadWeek(), loadHoursSummary()]);
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not save time week.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="tms-home tms-home--tracker">
      <section className="page-header client-workflow-header">
        <div className="page-header-top">
          <h1>
            <span className="logo-accent">T</span>
            <span className="logo-rest">M</span>
            <span className="tms-home-title-rest"> Time Management</span>
          </h1>
          <div className="page-header-actions">
            <Link className="secondary-button" to="/tms/dashboard" onClick={() => setCatAppMode('tm')}>
              Workload dashboard
            </Link>
            <Link className="secondary-button" to="/" onClick={() => setCatAppMode('hub')}>
              Back to home
            </Link>
          </div>
        </div>
        <p>
          {canSelectAnyUser
            ? 'As an Administrator, you can select any CAT user to review or enter weekly time. Save applies to both tabs for the selected week.'
            : 'Time entry is locked to your signed-in CAT account. Enter weekly hours under Client Time (projects) or Non-Client Time (meetings, PTO, and other categories). Save applies to both tabs for the selected week.'}
        </p>
      </section>

      <section className="card admin-card tms-tracker-card">
        <div className="form-grid tms-user-week-bar">
          <label htmlFor="tms-cat-user">CAT user</label>
          {canSelectAnyUser ? (
            <select
              id="tms-cat-user"
              value={selectedUserId === '' ? '' : String(selectedUserId)}
              disabled={loadingUsers || saving}
              onChange={(e) => {
                const next = Number(e.target.value);
                setSelectedUserId(Number.isFinite(next) && next > 0 ? next : '');
              }}
            >
              {loadingUsers ? <option value="">Loading CAT users…</option> : null}
              {!loadingUsers && catUsers.length === 0 ? (
                <option value="">No CAT users available</option>
              ) : null}
              {catUsers.map((u) => (
                <option key={u.userID} value={u.userID}>
                  {u.userFullName}
                </option>
              ))}
            </select>
          ) : (
            <input
              id="tms-cat-user"
              type="text"
              readOnly
              aria-readonly="true"
              value={
                loadingUsers
                  ? 'Loading signed-in user…'
                  : selectedUser?.userFullName ?? 'Signed-in CAT user unavailable'
              }
            />
          )}

          <span className="field-label">Week</span>
          <div className="page-header-actions project-services-select-actions project-time-week-nav">
            <button
              type="button"
              className="secondary-button"
              disabled={loadingWeek || saving || selectedUserId === ''}
              onClick={() => setWeekStart((prev) => addDays(prev, -7))}
            >
              Previous week
            </button>
            <button
              type="button"
              className="secondary-button"
              disabled={loadingWeek || saving || selectedUserId === ''}
              onClick={() => setWeekStart(startOfWeekMonday(new Date()))}
            >
              This week
            </button>
            <button
              type="button"
              className="secondary-button"
              disabled={loadingWeek || saving || selectedUserId === ''}
              onClick={() => setWeekStart((prev) => addDays(prev, 7))}
            >
              Next week
            </button>
            <span className="project-time-week-range" role="status">
              {formatCalendarDateDisplay(toYmd(weekStart))} –{' '}
              {formatCalendarDateDisplay(toYmd(addDays(weekStart, 6)))}
              {selectedUser ? ` · ${selectedUser.userFullName}` : ''}
            </span>
            <button
              type="button"
              className="primary-button"
              disabled={loadingWeek || saving || selectedUserId === ''}
              onClick={() => void handleSave()}
            >
              {saving ? 'Saving…' : 'Save time'}
            </button>
          </div>
        </div>

        {selectedUserId === '' && !loadingUsers ? (
          <p className="field-help" role="status">
            Sign in with a CAT department account to load your projects and weekly time grid.
          </p>
        ) : null}

        {loadError ? <div className="validation-summary">{loadError}</div> : null}
        {loadingWeek ? <p className="field-help">Loading week…</p> : null}

        {selectedUserId !== '' ? (
          <>
            <div className="tms-hours-summary" aria-live="polite">
              <div className="tms-hours-summary-card">
                <span className="tms-hours-summary-label">This week</span>
                <strong className="tms-hours-summary-value">
                  {loadingSummary && !hoursSummary ? '…' : formatSummaryHours(hoursSummary?.thisWeekHours ?? 0)}
                  <span className="tms-hours-summary-unit">h</span>
                </strong>
              </div>
              <div className="tms-hours-summary-card">
                <span className="tms-hours-summary-label">Last week</span>
                <strong className="tms-hours-summary-value">
                  {loadingSummary && !hoursSummary ? '…' : formatSummaryHours(hoursSummary?.lastWeekHours ?? 0)}
                  <span className="tms-hours-summary-unit">h</span>
                </strong>
              </div>
              <div className="tms-hours-summary-card">
                <span className="tms-hours-summary-label">This month</span>
                <strong className="tms-hours-summary-value">
                  {loadingSummary && !hoursSummary ? '…' : formatSummaryHours(hoursSummary?.monthHours ?? 0)}
                  <span className="tms-hours-summary-unit">h</span>
                </strong>
              </div>
              <div className="tms-hours-summary-card">
                <span className="tms-hours-summary-label">YTD</span>
                <strong className="tms-hours-summary-value">
                  {loadingSummary && !hoursSummary ? '…' : formatSummaryHours(hoursSummary?.ytdHours ?? 0)}
                  <span className="tms-hours-summary-unit">h</span>
                </strong>
              </div>
            </div>

            <div className="admin-tabs tms-time-tabs" role="tablist" aria-label="Time entry views">
              <button
                type="button"
                role="tab"
                id="tms-tab-client"
                aria-selected={activeTab === 'client'}
                aria-controls="tms-panel-client"
                className={activeTab === 'client' ? 'active' : ''}
                onClick={() => setActiveTab('client')}
              >
                Client Time
              </button>
              <button
                type="button"
                role="tab"
                id="tms-tab-non-client"
                aria-selected={activeTab === 'nonClient'}
                aria-controls="tms-panel-non-client"
                className={activeTab === 'nonClient' ? 'active' : ''}
                onClick={() => setActiveTab('nonClient')}
              >
                Non-Client Time
              </button>
            </div>

            <div
              id="tms-panel-client"
              role="tabpanel"
              aria-labelledby="tms-tab-client"
              hidden={activeTab !== 'client'}
              className={'admin-tab-panel' + (activeTab === 'client' ? ' active' : '')}
            >
              {!loadingWeek && projects.length === 0 ? (
                <p className="field-help" role="status">
                  No projects found for this user via user-group membership.
                </p>
              ) : null}

              {!loadingWeek && projects.length > 0 ? (
                <div className="page-header-actions tms-project-collapse-actions">
                  <button type="button" className="secondary-button" onClick={expandAllProjects}>
                    Expand all
                  </button>
                  <button type="button" className="secondary-button" onClick={collapseAllProjects}>
                    Collapse all
                  </button>
                </div>
              ) : null}

              {!loadingWeek &&
                (
                  [
                    { key: 'in-progress', title: 'In Progress', green: true, items: projectsByStatus.inProgress },
                    { key: 'planned', title: 'Planned', green: false, items: projectsByStatus.planned },
                    { key: 'closed', title: 'Closed', green: false, items: projectsByStatus.closed },
                  ] as const
                ).map((section) =>
                  section.items.length === 0 ? null : (
                    <div key={section.key} className="tms-project-status-section">
                      <h2 className="tms-project-status-heading">
                        <span
                          className={
                            'home-online-dot' +
                            (section.green ? ' home-online-dot--on' : ' home-online-dot--off')
                          }
                          aria-hidden="true"
                        />
                        <span>{section.title}</span>
                        <span className="tms-project-status-count">{section.items.length}</span>
                      </h2>

                      {section.items.map((project) => {
                        const collapsed = isProjectCollapsed(project.projectID);
                        const title = projectDisplayTitle(project);
                        const bodyId = `tms-project-body-${project.projectID}`;
                        return (
                          <div
                            key={project.projectID}
                            className={
                              'tms-project-block' + (collapsed ? ' tms-project-block--collapsed' : '')
                            }
                          >
                            <div className="tms-project-block-header">
                              <button
                                type="button"
                                className="tms-project-collapse-toggle"
                                aria-expanded={!collapsed}
                                aria-controls={bodyId}
                                onClick={() => toggleProjectCollapsed(project.projectID)}
                              >
                                <span className="tms-project-collapse-chevron" aria-hidden="true">
                                  {collapsed ? '▸' : '▾'}
                                </span>
                                <span
                                  className={
                                    'home-online-dot' +
                                    (projectWorkStatus(project) === 'In Progress' ?
                                      ' home-online-dot--on'
                                    : ' home-online-dot--off')
                                  }
                                  title={projectWorkStatus(project)}
                                  aria-label={projectWorkStatus(project)}
                                />
                                <span className="tms-project-block-title">{title}</span>
                                <span className="tms-project-collapse-meta">
                                  {project.tasks.length} service{project.tasks.length === 1 ? '' : 's'}
                                  {' · '}
                                  {formatHours(projectWeekTotal(project)) || '0'}h week
                                </span>
                              </button>
                              <Link
                                className="secondary-button"
                                to={`/projects/${project.projectID}?tab=time&userId=${encodeURIComponent(String(selectedUserId))}`}
                              >
                                Open project
                              </Link>
                            </div>

                            <div id={bodyId} hidden={collapsed}>
                              {project.tasks.length === 0 ? (
                                <p className="field-help" role="status">
                                  No calendar services / tasks on this project yet.
                                </p>
                              ) : (
                                <div className="project-tab-table-wrap project-time-table-wrap">
                                  <table className="dashboard-data-table dashboard-data-table--full home-client-project-table project-time-table">
                                    <thead>
                                      <tr>
                                        <th>Service / task</th>
                                        {weekDays.map((d) => (
                                          <th key={d.ymd}>
                                            <div className="project-time-day-head">
                                              <span>{d.label}</span>
                                              <span className="project-time-day-date">
                                                {formatCalendarDateDisplay(d.ymd)}
                                              </span>
                                            </div>
                                          </th>
                                        ))}
                                        <th>Week</th>
                                      </tr>
                                    </thead>
                                    <tbody>
                                      {project.tasks.map((task) => (
                                        <tr key={task.calendarItemID}>
                                          <td>
                                            <div className="project-time-task-title">{task.title}</div>
                                            <div className="project-time-task-type">
                                              {task.itemType ?? 'Task'}
                                            </div>
                                          </td>
                                          {weekDays.map((d) => {
                                            const key = projectCellKey(
                                              project.projectID,
                                              task.calendarItemID,
                                              d.ymd,
                                            );
                                            return (
                                              <td key={d.ymd}>
                                                <input
                                                  className="project-time-hours-input"
                                                  inputMode="decimal"
                                                  placeholder="0"
                                                  value={projectCells[key] ?? ''}
                                                  disabled={saving}
                                                  onChange={(e) =>
                                                    setProjectHours(
                                                      project.projectID,
                                                      task.calendarItemID,
                                                      d.ymd,
                                                      e.target.value,
                                                    )
                                                  }
                                                  aria-label={`${project.projectName} ${task.title} ${d.label}`}
                                                />
                                              </td>
                                            );
                                          })}
                                          <td className="project-time-total">
                                            {formatHours(
                                              projectRowTotal(project.projectID, task.calendarItemID),
                                            ) || '0'}
                                          </td>
                                        </tr>
                                      ))}
                                      <tr className="project-time-day-totals">
                                        <td>Day total</td>
                                        {weekDays.map((d) => (
                                          <td key={d.ymd} className="project-time-total">
                                            {formatHours(projectDayTotal(project, d.ymd)) || '0'}
                                          </td>
                                        ))}
                                        <td className="project-time-total">
                                          {formatHours(projectWeekTotal(project)) || '0'}
                                        </td>
                                      </tr>
                                    </tbody>
                                  </table>
                                </div>
                              )}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  ),
                )}
            </div>

            <div
              id="tms-panel-non-client"
              role="tabpanel"
              aria-labelledby="tms-tab-non-client"
              hidden={activeTab !== 'nonClient'}
              className={'admin-tab-panel' + (activeTab === 'nonClient' ? ' active' : '')}
            >
              {!loadingWeek ? (
                <div className="tms-other-time-block">
                  <div className="tms-project-block-header">
                    <h2 className="tms-project-block-title">Add time</h2>
                  </div>
                  <p className="field-help">
                    Track non-client time (Meetings, Holidays, PTO, Travel, Training, CSI, Moodys MAT, Verisk MAT,
                    Peril Advisory, Administrative). Add a category, then enter hours for each day of the week.
                  </p>

                  {availableAddCategories.length > 0 ? (
                    <div className="page-header-actions project-services-select-actions tms-add-time-row">
                      <label htmlFor="tms-add-category" className="tms-add-category-label">
                        Category
                      </label>
                      <select
                        id="tms-add-category"
                        value={addCategory}
                        disabled={saving}
                        onChange={(e) => setAddCategory(e.target.value)}
                      >
                        {availableAddCategories.map((category) => (
                          <option key={category} value={category}>
                            {category}
                          </option>
                        ))}
                      </select>
                      <button
                        type="button"
                        className="secondary-button"
                        disabled={saving || !addCategory}
                        onClick={handleAddOtherCategory}
                      >
                        Add time row
                      </button>
                    </div>
                  ) : null}

                  {visibleOtherCategories.length === 0 ? (
                    <p className="field-help" role="status">
                      No other-time categories added for this week yet.
                    </p>
                  ) : (
                    <div className="project-tab-table-wrap project-time-table-wrap">
                      <table className="dashboard-data-table dashboard-data-table--full home-client-project-table project-time-table">
                        <thead>
                          <tr>
                            <th>Category</th>
                            {weekDays.map((d) => (
                              <th key={d.ymd}>
                                <div className="project-time-day-head">
                                  <span>{d.label}</span>
                                  <span className="project-time-day-date">
                                    {formatCalendarDateDisplay(d.ymd)}
                                  </span>
                                </div>
                              </th>
                            ))}
                            <th>Week</th>
                          </tr>
                        </thead>
                        <tbody>
                          {visibleOtherCategories.map((category) => (
                            <tr key={category}>
                              <td>
                                <div className="project-time-task-title">{category}</div>
                                <div className="project-time-task-type">Other time</div>
                              </td>
                              {weekDays.map((d) => {
                                const key = otherCellKey(category, d.ymd);
                                return (
                                  <td key={d.ymd}>
                                    <input
                                      className="project-time-hours-input"
                                      inputMode="decimal"
                                      placeholder="0"
                                      value={otherCells[key] ?? ''}
                                      disabled={saving}
                                      onChange={(e) => setOtherHours(category, d.ymd, e.target.value)}
                                      aria-label={`${category} ${d.label}`}
                                    />
                                  </td>
                                );
                              })}
                              <td className="project-time-total">
                                {formatHours(otherRowTotal(category)) || '0'}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              ) : null}
            </div>
          </>
        ) : null}
      </section>
    </div>
  );
}
