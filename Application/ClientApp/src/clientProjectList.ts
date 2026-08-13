/** Response shape for dbo.vProject_Current-backed grid endpoints (CMS_PhaseI_All replaces dbo.tClientProjectList). */
export interface ClientProjectListModel {
  columns: string[];
  rows: (string | number | boolean | null)[][];
}

export type ClientProjectListSortDirection = 'asc' | 'desc';

export type ClientProjectListRow = (string | number | boolean | null)[];

function normalizeColumnKey(name: string): string {
  return name.replace(/[\s_\-.]/g, '').toLowerCase();
}

function materializeRow(row: unknown, columns: string[]): ClientProjectListRow {
  if (Array.isArray(row)) {
    const copy = [...row] as ClientProjectListRow;
    while (copy.length < columns.length) {
      copy.push(null);
    }
    if (copy.length > columns.length) {
      copy.length = columns.length;
    }
    return copy;
  }

  if (row !== null && typeof row === 'object') {
    const record = row as Record<string, unknown>;
    return columns.map((col) => {
      let val = record[col];
      if (val === undefined) {
        const camel = col.charAt(0).toLowerCase() + col.slice(1);
        val = record[camel];
      }
      if (val === undefined || val === null) {
        return null;
      }
      if (typeof val === 'string' || typeof val === 'number' || typeof val === 'boolean') {
        return val;
      }
      return String(val);
    });
  }

  return columns.map(() => null);
}

/**
 * Ensures each row has exactly one cell per header column so the React table never skips trailing columns.
 */
export function normalizeClientProjectListModel(data: ClientProjectListModel): ClientProjectListModel {
  const columns = Array.isArray(data.columns) ? data.columns : [];
  const rows = (Array.isArray(data.rows) ? data.rows : []).map((row) => materializeRow(row, columns));
  return { columns, rows };
}

function inferSortKind(columnName: string): 'date' | 'number' | 'text' {
  const key = normalizeColumnKey(columnName);
  if (key.includes('date')) {
    return 'date';
  }
  if (
    key.includes('revenue') ||
    key.includes('amount') ||
    key.includes('hours') ||
    key.endsWith('id') ||
    key.includes('count')
  ) {
    return 'number';
  }
  return 'text';
}

function parseSortableTimestamp(value: string): number | null {
  const text = value.trim();
  if (!text) {
    return null;
  }

  if (/^\d{4}-\d{2}-\d{2}/.test(text)) {
    const ms = Date.parse(text);
    return Number.isNaN(ms) ? null : ms;
  }

  const usMatch = text.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})/);
  if (usMatch) {
    const [, month, day, year] = usMatch;
    const ms = Date.parse(`${year}-${month.padStart(2, '0')}-${day.padStart(2, '0')}`);
    return Number.isNaN(ms) ? null : ms;
  }

  const ms = Date.parse(text);
  return Number.isNaN(ms) ? null : ms;
}

function parseSortableNumber(value: string | number | boolean): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  if (typeof value === 'boolean') {
    return value ? 1 : 0;
  }
  const text = String(value).trim();
  if (!text) {
    return null;
  }
  const normalized = text.replace(/[$,\s]/g, '');
  const n = Number(normalized);
  return Number.isFinite(n) ? n : null;
}

export function compareClientProjectListCellValues(
  a: string | number | boolean | null | undefined,
  b: string | number | boolean | null | undefined,
  sortKind: 'date' | 'number' | 'text' = 'text',
): number {
  const aEmpty = a === null || a === undefined || a === '';
  const bEmpty = b === null || b === undefined || b === '';
  if (aEmpty && bEmpty) {
    return 0;
  }
  if (aEmpty) {
    return 1;
  }
  if (bEmpty) {
    return -1;
  }

  if (sortKind === 'date') {
    const aDate = parseSortableTimestamp(String(a));
    const bDate = parseSortableTimestamp(String(b));
    if (aDate !== null && bDate !== null) {
      return aDate - bDate;
    }
  }

  if (sortKind === 'number' || sortKind === 'text') {
    const aNum = parseSortableNumber(a);
    const bNum = parseSortableNumber(b);
    if (aNum !== null && bNum !== null) {
      return aNum - bNum;
    }
  }

  return String(a).trim().localeCompare(String(b).trim(), undefined, {
    sensitivity: 'base',
    numeric: true,
  });
}

export function sortClientProjectListRows(
  rows: ClientProjectListRow[],
  columnIndex: number,
  columnName: string,
  direction: ClientProjectListSortDirection,
): ClientProjectListRow[] {
  if (columnIndex < 0 || rows.length === 0) {
    return rows;
  }

  const sortKind = inferSortKind(columnName);
  const indexed = rows.map((row, originalIndex) => ({ row, originalIndex }));
  indexed.sort((left, right) => {
    const cmp = compareClientProjectListCellValues(
      left.row[columnIndex],
      right.row[columnIndex],
      sortKind,
    );
    if (cmp !== 0) {
      return direction === 'asc' ? cmp : -cmp;
    }
    return left.originalIndex - right.originalIndex;
  });
  return indexed.map((entry) => entry.row);
}

export function clientProjectListSortIndicator(
  columnIndex: number,
  sortColumnIndex: number | null,
  sortDirection: ClientProjectListSortDirection,
): string {
  if (sortColumnIndex !== columnIndex) {
    return ' ↕';
  }
  return sortDirection === 'asc' ? ' ↑' : ' ↓';
}
