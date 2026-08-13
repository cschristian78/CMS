/**
 * Calendar-date helpers for <input type="date"> and SQL date-only fields.
 * Parsing with `new Date('yyyy-MM-dd')` treats the value as UTC midnight, which
 * displays as the previous day in US timezones (e.g. 2026-01-01 → 2025-12-31).
 */

function formatYmd(year: number, month: number, day: number): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${year}-${pad(month)}-${pad(day)}`;
}

/** Value for <input type="date"> — always the intended calendar day as yyyy-MM-dd. */
export function toInputDateValue(value: string | Date | null | undefined): string {
  if (value == null || value === '') {
    return '';
  }

  if (value instanceof Date) {
    if (Number.isNaN(value.getTime())) {
      return '';
    }
    return formatYmd(value.getUTCFullYear(), value.getUTCMonth() + 1, value.getUTCDate());
  }

  const s = String(value).trim();
  const ymd = s.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (ymd) {
    return `${ymd[1]}-${ymd[2]}-${ymd[3]}`;
  }

  const us = s.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})/);
  if (us) {
    return formatYmd(Number(us[3]), Number(us[1]), Number(us[2]));
  }

  const d = new Date(s);
  if (Number.isNaN(d.getTime())) {
    return '';
  }

  return formatYmd(d.getUTCFullYear(), d.getUTCMonth() + 1, d.getUTCDate());
}

/** Display a calendar date as MM/DD/YYYY (no time component). */
export function formatCalendarDateDisplay(value: string | null | undefined): string {
  const iso = toInputDateValue(value);
  if (!iso) {
    return '—';
  }
  const [y, m, day] = iso.split('-');
  if (!y || !m || !day) {
    return '—';
  }
  return `${m}/${day}/${y}`;
}
