import type { BarChartItem } from '../components/DashboardBarChart';

/** Seed bars so business-unit charts always show US / UK / LatAM / Global. */
const DEFAULT_BUSINESS_UNIT_BARS: BarChartItem[] = [
  { label: 'US', value: 0, colorKey: 'US' },
  { label: 'UK', value: 0, colorKey: 'UK' },
  { label: 'LatAM', value: 0, colorKey: 'LA' },
  { label: 'Global', value: 0, colorKey: 'Global' },
];

const BU_SORT_ORDER = ['US', 'UK', 'LA', 'LATAM', 'GLOBAL'];

function buDisplayLabel(name: string): string {
  const bu = name.trim();
  if (bu.toUpperCase() === 'LA') {
    return 'LatAM';
  }
  return bu || '(Unassigned)';
}

function buSortKey(colorKey: string | null | undefined): number {
  const k = (colorKey ?? '').trim().toUpperCase();
  const idx = BU_SORT_ORDER.indexOf(k === 'LA' ? 'LATAM' : k);
  return idx >= 0 ? idx : BU_SORT_ORDER.length;
}

/** Accept API camelCase or PascalCase count fields. */
export function coerceCount(row: Record<string, unknown>): number {
  const raw = row.clientCount ?? row.count ?? row.ClientCount ?? row.Count;
  const n = typeof raw === 'number' ? raw : Number(raw);
  return Number.isFinite(n) ? Math.max(0, Math.trunc(n)) : 0;
}

export function businessUnitRowsToChartItems(rows: unknown): BarChartItem[] {
  const list = Array.isArray(rows) ? rows : [];
  const byKey = new Map<string, BarChartItem>();

  for (const seed of DEFAULT_BUSINESS_UNIT_BARS) {
    const key = (seed.colorKey ?? seed.label).trim().toUpperCase();
    byKey.set(key, { ...seed });
  }

  for (const row of list) {
    if (row == null || typeof row !== 'object') {
      continue;
    }
    const r = row as Record<string, unknown>;
    const rawName = String(r.businessUnitName ?? r.BusinessUnitName ?? '').trim();
    const key = rawName.toUpperCase() || 'UNASSIGNED';
    byKey.set(key, {
      label: buDisplayLabel(rawName),
      value: coerceCount(r),
      colorKey: rawName || null,
    });
  }

  return [...byKey.values()].sort(
    (a, b) => buSortKey(a.colorKey) - buSortKey(b.colorKey) || a.label.localeCompare(b.label),
  );
}

export function namedRowsToChartItems(rows: unknown): BarChartItem[] {
  const list = Array.isArray(rows) ? rows : [];
  return list.map((row) => {
    const r = row as Record<string, unknown>;
    const label = String(r.label ?? r.Label ?? '').trim() || '(Unspecified)';
    const colorKey = String(r.colorKey ?? r.ColorKey ?? '').trim() || null;
    return {
      label,
      value: coerceCount(r),
      colorKey,
    };
  });
}

export function ensureNamedChartItems(items: BarChartItem[]): BarChartItem[] {
  if (items.length > 0) {
    return items;
  }
  return [{ label: '(No data)', value: 0, colorKey: null }];
}
