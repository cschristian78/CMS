import { forwardRef, useCallback, useEffect, useImperativeHandle, useMemo, useState } from 'react';
import type { FlashFn } from '../App';
import { ApiError, apiGet, apiPostJson } from '../api';
import { formatCalendarDateDisplay, toInputDateValue } from '../utils/dateInput';

export interface TimeTrackerTask {
  calendarItemID: number;
  itemType?: string | null;
  title: string;
  sortOrder?: number;
}

export interface ProjectTimeTrackerHandle {
  save: () => Promise<void>;
  reload: () => Promise<void>;
}

interface TimeEntryDto {
  timeEntryID?: number;
  calendarItemID: number;
  workDate: string;
  hours: number;
  entryMode?: string;
  notes?: string | null;
}

interface CatUserDto {
  userID: number;
  userFullName: string;
  email?: string | null;
}

interface TimeWeekResponse {
  weekStartDate: string;
  weekEndDate: string;
  userID?: number | null;
  catUsers?: CatUserDto[];
  tasks: {
    calendarItemID: number;
    itemType?: string | null;
    title?: string | null;
    sortOrder?: number;
  }[];
  entries: TimeEntryDto[];
}

const DAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] as const;

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

type CellKey = string;

function cellKey(calendarItemID: number, ymd: string): CellKey {
  return `${calendarItemID}|${ymd}`;
}

const ProjectTimeTrackerTab = forwardRef<
  ProjectTimeTrackerHandle,
  {
    projectId: number;
    isActive: boolean;
    flash: FlashFn;
    fallbackTasks: TimeTrackerTask[];
    /** Prefer this CAT user when opening from TM (or other deep links). */
    initialUserId?: number | null;
  }
>(function ProjectTimeTrackerTab(
  { projectId, isActive, flash, fallbackTasks, initialUserId = null },
  ref,
) {
  const [weekStart, setWeekStart] = useState(() => startOfWeekMonday(new Date()));
  const [tasks, setTasks] = useState<TimeTrackerTask[]>([]);
  const [cells, setCells] = useState<Record<CellKey, { hours: string; entryMode: 'Exact' | 'Allocated' }>>({});
  const [catUsers, setCatUsers] = useState<CatUserDto[]>([]);
  const [activeUserId, setActiveUserId] = useState<number | null>(() =>
    initialUserId != null && initialUserId > 0 ? initialUserId : null,
  );
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [blockDayIndex, setBlockDayIndex] = useState(0);
  const [blockHours, setBlockHours] = useState('');
  const [blockTaskIds, setBlockTaskIds] = useState<number[]>([]);

  const weekDays = useMemo(
    () => DAY_LABELS.map((label, i) => ({ label, date: addDays(weekStart, i), ymd: toYmd(addDays(weekStart, i)) })),
    [weekStart],
  );

  const displayTasks = useMemo(() => {
    if (tasks.length > 0) {
      return tasks;
    }
    return fallbackTasks.filter((t) => t.calendarItemID > 0 && t.title.trim().length > 0);
  }, [tasks, fallbackTasks]);

  const showUserTabs = catUsers.length > 1;

  const activeUserLabel = useMemo(() => {
    if (activeUserId === null) {
      return '';
    }
    const match = catUsers.find((u) => u.userID === activeUserId);
    return (match?.userFullName ?? '').trim() || `User ${activeUserId}`;
  }, [activeUserId, catUsers]);

  const loadWeek = useCallback(async () => {
    setLoading(true);
    setLoadError(null);
    try {
      const weekStartYmd = toYmd(weekStart);
      const userQuery =
        activeUserId !== null && activeUserId > 0
          ? `&userId=${encodeURIComponent(String(activeUserId))}`
          : '';
      const data = await apiGet<TimeWeekResponse>(
        `/api/projects/${projectId}/time?weekStart=${encodeURIComponent(weekStartYmd)}${userQuery}`,
      );

      const nextCatUsers = Array.isArray(data.catUsers) ? data.catUsers : [];
      setCatUsers(nextCatUsers);

      const resolvedUserId =
        data.userID && data.userID > 0
          ? data.userID
          : nextCatUsers.length === 1
            ? nextCatUsers[0].userID
            : activeUserId ?? (initialUserId != null && initialUserId > 0 ? initialUserId : null);
      if (resolvedUserId && resolvedUserId > 0) {
        setActiveUserId(resolvedUserId);
      }

      const nextTasks = (data.tasks ?? [])
        .filter((t) => t.calendarItemID > 0)
        .map((t) => ({
          calendarItemID: t.calendarItemID,
          itemType: t.itemType ?? 'Task',
          title: (t.title ?? '').trim() || `Task ${t.calendarItemID}`,
          sortOrder: t.sortOrder ?? 0,
        }))
        .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.title.localeCompare(b.title));

      setTasks(nextTasks);

      const nextCells: Record<CellKey, { hours: string; entryMode: 'Exact' | 'Allocated' }> = {};
      for (const e of data.entries ?? []) {
        const ymd = toInputDateValue(e.workDate);
        if (!ymd || e.calendarItemID <= 0) {
          continue;
        }
        nextCells[cellKey(e.calendarItemID, ymd)] = {
          hours: formatHours(Number(e.hours) || 0),
          entryMode: e.entryMode === 'Allocated' ? 'Allocated' : 'Exact',
        };
      }
      setCells(nextCells);
      setBlockTaskIds(nextTasks.map((t) => t.calendarItemID));
    } catch (err) {
      const message = err instanceof ApiError ? err.message : 'Could not load time tracker.';
      setLoadError(message);
      setTasks([]);
      setCells({});
    } finally {
      setLoading(false);
    }
  }, [projectId, weekStart, activeUserId, initialUserId]);

  useEffect(() => {
    if (initialUserId != null && initialUserId > 0) {
      setActiveUserId(initialUserId);
    }
  }, [projectId, initialUserId]);

  useEffect(() => {
    if (!isActive || projectId <= 0) {
      return;
    }
    void loadWeek();
  }, [isActive, projectId, loadWeek]);

  const setCellHours = (calendarItemID: number, ymd: string, hours: string) => {
    const key = cellKey(calendarItemID, ymd);
    setCells((prev) => ({
      ...prev,
      [key]: {
        hours,
        entryMode: 'Exact',
      },
    }));
  };

  const rowTotal = (calendarItemID: number) =>
    weekDays.reduce((sum, d) => sum + parseHours(cells[cellKey(calendarItemID, d.ymd)]?.hours ?? ''), 0);

  const dayTotal = (ymd: string) =>
    displayTasks.reduce((sum, t) => sum + parseHours(cells[cellKey(t.calendarItemID, ymd)]?.hours ?? ''), 0);

  const weekTotal = displayTasks.reduce((sum, t) => sum + rowTotal(t.calendarItemID), 0);

  const handleSave = async () => {
    if (activeUserId === null || activeUserId <= 0) {
      flash('error', 'Select a team member to save time for.');
      return;
    }

    setSaving(true);
    try {
      const entries: TimeEntryDto[] = [];
      for (const task of displayTasks) {
        for (const day of weekDays) {
          const cell = cells[cellKey(task.calendarItemID, day.ymd)];
          const hours = parseHours(cell?.hours ?? '');
          if (hours <= 0 && !cell) {
            continue;
          }
          entries.push({
            calendarItemID: task.calendarItemID,
            workDate: day.ymd,
            hours,
            entryMode: cell?.entryMode ?? 'Exact',
          });
        }
      }

      for (const [key, cell] of Object.entries(cells)) {
        const [idStr, ymd] = key.split('|');
        const calendarItemID = Number(idStr);
        if (!calendarItemID || !ymd) {
          continue;
        }
        if (entries.some((e) => e.calendarItemID === calendarItemID && e.workDate === ymd)) {
          continue;
        }
        entries.push({
          calendarItemID,
          workDate: ymd,
          hours: parseHours(cell.hours),
          entryMode: cell.entryMode,
        });
      }

      const result = await apiPostJson<{ message: string; savedCount: number }>(`/api/projects/${projectId}/time`, {
        weekStartDate: toYmd(weekStart),
        userID: activeUserId,
        entries,
      });
      flash(
        'success',
        result.message ??
          `Saved ${result.savedCount ?? 0} time entry cell(s) for ${activeUserLabel || 'selected user'}.`,
      );
      await loadWeek();
    } catch (err) {
      flash('error', err instanceof ApiError ? err.message : 'Could not save time entries.');
    } finally {
      setSaving(false);
    }
  };

  useImperativeHandle(
    ref,
    () => ({
      save: handleSave,
      reload: loadWeek,
    }),
    [handleSave, loadWeek],
  );

  const toggleBlockTask = (id: number) => {
    setBlockTaskIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  };

  const distributeBlock = () => {
    const hours = parseHours(blockHours);
    if (hours <= 0) {
      flash('error', 'Enter a block of hours greater than zero.');
      return;
    }
    if (blockTaskIds.length === 0) {
      flash('error', 'Select at least one service / task to allocate.');
      return;
    }
    const day = weekDays[blockDayIndex];
    if (!day) {
      return;
    }

    const each = Math.round((hours / blockTaskIds.length) * 100) / 100;
    let assigned = 0;
    setCells((prev) => {
      const next = { ...prev };
      blockTaskIds.forEach((id, index) => {
        const value =
          index === blockTaskIds.length - 1 ? Math.round((hours - assigned) * 100) / 100 : each;
        assigned += index === blockTaskIds.length - 1 ? 0 : each;
        next[cellKey(id, day.ymd)] = {
          hours: formatHours(value),
          entryMode: 'Allocated',
        };
      });
      return next;
    });
    flash(
      'success',
      `Allocated ${hours}h across ${blockTaskIds.length} task(s) on ${DAY_LABELS[blockDayIndex]} ${formatCalendarDateDisplay(day.ymd)}.`,
    );
  };

  const switchUser = (userId: number) => {
    if (userId === activeUserId) {
      return;
    }
    setCells({});
    setActiveUserId(userId);
  };

  if (!isActive) {
    return null;
  }

  return (
    <div className="admin-edit-card project-panel-tab-card">
      <p className="field-help">
        Track daily hours against calendar services / tasks, one week at a time (Monday–Sunday). When multiple user
        group members are in the <strong>CAT</strong> department, use the person tabs below — each person&apos;s hours are
        stored separately in <code>dbo.tProjectTimeEntry</code> by <code>UserID</code>.
      </p>

      {showUserTabs ? (
        <div className="project-time-user-tabs" role="tablist" aria-label="CAT department members">
          {catUsers.map((user) => (
            <button
              key={user.userID}
              type="button"
              role="tab"
              className={activeUserId === user.userID ? 'active' : ''}
              aria-selected={activeUserId === user.userID}
              onClick={() => switchUser(user.userID)}
              disabled={loading || saving}
            >
              {(user.userFullName ?? '').trim() || `User ${user.userID}`}
            </button>
          ))}
        </div>
      ) : catUsers.length === 1 ? (
        <p className="field-help" role="status">
          Tracking time for <strong>{catUsers[0].userFullName}</strong> (CAT department).
        </p>
      ) : null}

      <div className="page-header-actions project-services-select-actions project-time-week-nav">
        <button
          type="button"
          className="secondary-button"
          onClick={() => setWeekStart((prev) => addDays(prev, -7))}
          disabled={loading || saving}
        >
          Previous week
        </button>
        <button
          type="button"
          className="secondary-button"
          onClick={() => setWeekStart(startOfWeekMonday(new Date()))}
          disabled={loading || saving}
        >
          This week
        </button>
        <button
          type="button"
          className="secondary-button"
          onClick={() => setWeekStart((prev) => addDays(prev, 7))}
          disabled={loading || saving}
        >
          Next week
        </button>
        <span className="project-time-week-range" role="status">
          {formatCalendarDateDisplay(toYmd(weekStart))} - {formatCalendarDateDisplay(toYmd(addDays(weekStart, 6)))}
          {activeUserLabel ? ` · ${activeUserLabel}` : ''}
        </span>
        <button type="button" className="primary-button" onClick={() => void handleSave()} disabled={loading || saving}>
          {saving ? 'Saving...' : 'Save time'}
        </button>
      </div>

      {loadError ? <div className="validation-summary">{loadError}</div> : null}
      {loading ? <p className="field-help">Loading week...</p> : null}

      {!loading && displayTasks.length === 0 ? (
        <p className="field-help" role="status">
          No calendar services / tasks are saved for this project yet. Add items on the <strong>Calendar</strong> tab
          (or use Add to Project on Services), then Save Project before tracking time.
        </p>
      ) : null}

      {displayTasks.length > 0 ? (
        <>
          <div className="project-tab-table-wrap project-time-table-wrap">
            <table className="dashboard-data-table dashboard-data-table--full home-client-project-table project-time-table">
              <thead>
                <tr>
                  <th>Service / task</th>
                  {weekDays.map((d) => (
                    <th key={d.ymd}>
                      <div className="project-time-day-head">
                        <span>{d.label}</span>
                        <span className="project-time-day-date">{formatCalendarDateDisplay(d.ymd)}</span>
                      </div>
                    </th>
                  ))}
                  <th>Week</th>
                </tr>
              </thead>
              <tbody>
                {displayTasks.map((task) => (
                  <tr key={task.calendarItemID}>
                    <td>
                      <div className="project-time-task-title">{task.title}</div>
                      <div className="project-time-task-type">{task.itemType ?? 'Task'}</div>
                    </td>
                    {weekDays.map((d) => {
                      const key = cellKey(task.calendarItemID, d.ymd);
                      const cell = cells[key];
                      return (
                        <td key={d.ymd}>
                          <input
                            className={
                              'project-time-hours-input' +
                              (cell?.entryMode === 'Allocated' ? ' project-time-hours-input--allocated' : '')
                            }
                            inputMode="decimal"
                            placeholder="0"
                            value={cell?.hours ?? ''}
                            onChange={(e) => setCellHours(task.calendarItemID, d.ymd, e.target.value)}
                            aria-label={`${task.title} ${d.label}`}
                            title={cell?.entryMode === 'Allocated' ? 'Allocated from block' : 'Exact time'}
                          />
                        </td>
                      );
                    })}
                    <td className="project-time-total">{formatHours(rowTotal(task.calendarItemID)) || '0'}</td>
                  </tr>
                ))}
                <tr className="project-time-day-totals">
                  <td>Day total</td>
                  {weekDays.map((d) => (
                    <td key={d.ymd} className="project-time-total">
                      {formatHours(dayTotal(d.ymd)) || '0'}
                    </td>
                  ))}
                  <td className="project-time-total">{formatHours(weekTotal) || '0'}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <div className="project-time-block-panel">
            <h3 className="project-time-block-title">Allocate block of time</h3>
            <p className="field-help">
              When you tracked a total for the day but not exact splits, enter the block hours, choose the day and
              tasks, then distribute evenly (marked as Allocated).
            </p>
            <div className="form-grid project-time-block-grid">
              <label htmlFor="time-block-day">Day</label>
              <select
                id="time-block-day"
                value={blockDayIndex}
                onChange={(e) => setBlockDayIndex(Number(e.target.value))}
              >
                {weekDays.map((d, i) => (
                  <option key={d.ymd} value={i}>
                    {d.label} {formatCalendarDateDisplay(d.ymd)}
                  </option>
                ))}
              </select>

              <label htmlFor="time-block-hours">Hours</label>
              <input
                id="time-block-hours"
                inputMode="decimal"
                value={blockHours}
                onChange={(e) => setBlockHours(e.target.value)}
                placeholder="e.g. 6.5"
              />

              <span className="field-label">Tasks</span>
              <div className="project-time-block-tasks">
                <div className="page-header-actions project-services-select-actions">
                  <button
                    type="button"
                    className="secondary-button"
                    onClick={() => setBlockTaskIds(displayTasks.map((t) => t.calendarItemID))}
                  >
                    Select all
                  </button>
                  <button type="button" className="secondary-button" onClick={() => setBlockTaskIds([])}>
                    Clear
                  </button>
                </div>
                {displayTasks.map((task) => (
                  <label key={task.calendarItemID} className="project-time-block-task">
                    <input
                      type="checkbox"
                      checked={blockTaskIds.includes(task.calendarItemID)}
                      onChange={() => toggleBlockTask(task.calendarItemID)}
                    />
                    <span>{task.title}</span>
                  </label>
                ))}
              </div>

              <span className="field-label" aria-hidden="true" />
              <div>
                <button type="button" className="secondary-button" onClick={distributeBlock}>
                  Distribute evenly
                </button>
              </div>
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
});

export default ProjectTimeTrackerTab;
