/** US Red, UK Blue, LatAM (LA) Green — matches dbo.tBusinessUnit seed codes. */
export const REGION_BAR_COLORS = {
  US: '#c41e3a',
  UK: '#1e4a8c',
  LatAM: '#2d8a3e',
} as const;

const DEFAULT_BAR_COLOR = '#5a6470';

/** Resolve bar fill from business unit code/name (US, UK, LA / LatAM). */
export function regionBarColor(colorKey?: string | null): string {
  const key = (colorKey ?? '').trim().toUpperCase();
  if (key === 'US' || key.startsWith('US ')) {
    return REGION_BAR_COLORS.US;
  }
  if (key === 'UK' || key.startsWith('UK ')) {
    return REGION_BAR_COLORS.UK;
  }
  if (key === 'LA' || key === 'LATAM' || key.includes('LATIN')) {
    return REGION_BAR_COLORS.LatAM;
  }
  return DEFAULT_BAR_COLOR;
}
