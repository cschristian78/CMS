/** Prefer BusinessClassDescription for UI; fall back to BusinessClassName (short code). */
export function formatBusinessClassDisplay(
  businessClassName?: string | null,
  businessClassDescription?: string | null,
): string {
  const desc = (businessClassDescription ?? '').trim();
  if (desc) {
    return desc;
  }
  return (businessClassName ?? '').trim();
}
