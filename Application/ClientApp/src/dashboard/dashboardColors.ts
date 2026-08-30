/** Consistent palette for ProjectStatus and RevenueTier across dashboard tabs. */

export const PROJECT_STATUS_COLORS: Record<string, string> = {
  active: '#30d880',
  inactive: '#5a7080',
};

export const REVENUE_TIER_ORDER = [
  'Unknown',
  'Less Than $250K',
  'Less Than $500K',
  'Less Than $1.0M',
  'Less Than $2.5M',
  'Less Than $5.0M',
  'Less Than $10.0M',
  'Needs Reviewed',
];

export const REVENUE_TIER_COLORS: Record<string, string> = {
  unknown: '#5a7080',
  lessthan250k: '#6b7280',
  lessthan500k: '#3b82f6',
  lessthan10m: '#8b5cf6',
  lessthan25m: '#20d4e8',
  lessthan50m: '#f59e0b',
  lessthan100m: '#f08700',
  needsreviewed: '#ef4444',
};

const SERIES_FALLBACK = [
  '#20d4e8',
  '#f08700',
  '#30d880',
  '#3b82f6',
  '#8b5cf6',
  '#f59e0b',
  '#ef4444',
  '#14b8a6',
];

function compactKey(value: string): string {
  return value.replace(/[\s_\-.$]/g, '').toLowerCase();
}

export function projectStatusColor(status: string): string {
  const k = compactKey(status);
  if (k.includes('inactive') || k === 'offline' || k === '0') {
    return PROJECT_STATUS_COLORS.inactive;
  }
  if (k.includes('active') || k === 'online' || k === '1') {
    return PROJECT_STATUS_COLORS.active;
  }
  return SERIES_FALLBACK[0];
}

export function revenueTierColor(tier: string): string {
  const k = compactKey(tier);
  if (k === 'unknown' || k === '') {
    return REVENUE_TIER_COLORS.unknown;
  }
  if (k.includes('250k')) {
    return REVENUE_TIER_COLORS.lessthan250k;
  }
  if (k.includes('500k')) {
    return REVENUE_TIER_COLORS.lessthan500k;
  }
  if (k.includes('10m') && !k.includes('100m')) {
    return REVENUE_TIER_COLORS.lessthan10m;
  }
  if (k.includes('25m')) {
    return REVENUE_TIER_COLORS.lessthan25m;
  }
  if (k.includes('50m')) {
    return REVENUE_TIER_COLORS.lessthan50m;
  }
  if (k.includes('100m') || k.includes('10.0m')) {
    return REVENUE_TIER_COLORS.lessthan100m;
  }
  if (k.includes('needsreviewed') || k.includes('needsreview')) {
    return REVENUE_TIER_COLORS.needsreviewed;
  }
  // "$1.0M" compact is "10m" after stripping dots — also match "1.0m" leftover
  if (k.includes('1.0m') || k === 'lessthan1m' || k === 'lessthan10m') {
    return REVENUE_TIER_COLORS.lessthan10m;
  }
  return SERIES_FALLBACK[2];
}

export function seriesColor(index: number, label?: string): string {
  if (label) {
    const status = projectStatusColor(label);
    if (compactKey(label) === 'active' || compactKey(label) === 'inactive') {
      return status;
    }
    const tier = revenueTierColor(label);
    if (REVENUE_TIER_ORDER.some((t) => compactKey(t) === compactKey(label))) {
      return tier;
    }
  }
  return SERIES_FALLBACK[Math.abs(index) % SERIES_FALLBACK.length];
}

export function agingBucketColor(bucket: string): string {
  const k = compactKey(bucket);
  if (k === 'overdue') {
    return '#ef4444';
  }
  if (k === 'duethismonth') {
    return '#f59e0b';
  }
  if (k === 'duethisquarter') {
    return '#f08700';
  }
  if (k === 'later') {
    return '#20d4e8';
  }
  return '#5a7080';
}

export function formatUsd(n: number): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    maximumFractionDigits: 0,
  }).format(n);
}

export function formatNumber(n: number, fractionDigits = 0): string {
  return new Intl.NumberFormat('en-US', {
    maximumFractionDigits: fractionDigits,
    minimumFractionDigits: 0,
  }).format(n);
}

/** USD amount divided by 1,000 (chart labels; pair with a "$000" key). */
export function formatUsdThousands(n: number): string {
  if (!Number.isFinite(n)) {
    return '$0';
  }
  return `$${formatNumber(Math.round(n / 1000))}`;
}

export function formatPercent(value: number, total: number, fractionDigits = 1): string {
  if (total <= 0) {
    return '—';
  }
  return `${formatNumber((value / total) * 100, fractionDigits)}%`;
}

export function formatTimelineBarUsd(value: number): string {
  return formatUsdThousands(value);
}
