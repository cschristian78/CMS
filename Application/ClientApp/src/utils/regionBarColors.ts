/** Business-unit bars stay inside the interface palette. */
export const REGION_BAR_COLORS = {
  US: '#48086f',
  UK: '#2f2c31',
  LatAM: '#7f7f7f',
} as const;

const DEFAULT_BAR_COLOR = '#000000';

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
  if (key === 'TIMELINE') {
    return '#48086f';
  }
  return DEFAULT_BAR_COLOR;
}
