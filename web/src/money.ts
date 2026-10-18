// Money arrives as a decimal string ("6100.5000") and is DISPLAYED from that string, never
// through a JavaScript number, so what is shown is exactly what is stored.

/** "1234567.5000" -> "1,234,567.50"; keeps every non-zero digit beyond two places. */
export function formatMoney(value: string): string {
  const negative = value.startsWith('-');
  const [whole, fraction = ''] = (negative ? value.slice(1) : value).split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  const trimmed = fraction.replace(/0+$/, '').padEnd(2, '0');
  return `${negative ? '−' : ''}${grouped}.${trimmed}`;
}

/**
 * For PLOTTING only. A chart needs a coordinate, and a double is precise to about 15
 * significant digits - ample for a pixel, never used for a figure a person reads.
 */
export function toPlotValue(value: string | null): number | null {
  return value === null ? null : Number(value);
}

/** Today in the user's local calendar, as the API's date format (yyyy-mm-dd). */
export function localToday(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/**
 * A MOMENT from the API (UTC) in the viewer's own time zone, the zone named (slice-02.md "Dates
 * and time", Kyle 2026-10-18: the data layer is UTC; the browser shows the viewer's zone).
 * Never for a calendar date - those are shown as the strings they are, because new Date() would
 * read "2026-10-15" as UTC midnight: the 14th anywhere in the Americas.
 */
export function formatMoment(iso: string): string {
  // The parts spelled out: dateStyle/timeStyle cannot be combined with timeZoneName - the
  // browser throws, and with no error boundary that unmounted the whole page (found by the E2E).
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', timeZoneName: 'short',
  });
}
