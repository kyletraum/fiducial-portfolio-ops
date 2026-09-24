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
