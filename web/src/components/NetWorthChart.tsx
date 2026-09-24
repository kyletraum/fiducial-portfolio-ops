// The net-worth chart. Three rules from slice-01 step 4, all of them Constitution II:
//   - an UNVERIFIED day (netWorth null) is a GAP, never a zero: connectNulls={false};
//   - a PARTIAL day (some accounts unverified) is visibly marked, not silently summed;
//   - the tooltip keeps null entries (filterNull={false}), so hovering a gap says "unverified".
// Status is carried by labelled bands and by the table below, never by colour alone.
import {
  CartesianGrid, Line, LineChart, ReferenceArea, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import type { NetWorthPoint } from '../api/queries';
import { formatMoney, toPlotValue } from '../money';

type Band = { status: 'partial' | 'unverified'; from: string; to: string };

/** Contiguous runs of non-verified days, so each gets one labelled band. */
function bands(points: NetWorthPoint[]): Band[] {
  const out: Band[] = [];
  points.forEach((p, i) => {
    if (p.status === 'verified') return;
    const last = out[out.length - 1];
    // Extend the current band only if it ended on the immediately preceding point.
    if (last && last.status === p.status && points[i - 1]?.asOfDate === last.to) last.to = p.asOfDate;
    else out.push({ status: p.status, from: p.asOfDate, to: p.asOfDate });
  });
  return out;
}

/**
 * A dot only where a segment starts or ends. With no dots at all, a lone value between two
 * unverified days has zero length and draws NOTHING - the first balance entered after a gap
 * would be invisible, which is exactly the moment DoD 2 checks.
 */
function SegmentEnd({ cx, cy, index, data }: {
  cx?: number; cy?: number; index?: number; data: { plot: number | null }[];
}) {
  const i = index ?? -1;
  const here = data[i]?.plot ?? null;
  const isEnd = here !== null && ((data[i - 1]?.plot ?? null) === null || (data[i + 1]?.plot ?? null) === null);
  if (!isEnd || cx === undefined || cy === undefined) return <g />;
  return <circle cx={cx} cy={cy} r={4} fill="var(--line)" stroke="var(--bg)" strokeWidth={1.5} />;
}

export function NetWorthChart({ points, tableId }: { points: NetWorthPoint[]; tableId: string }) {
  const data = points.map(p => ({ ...p, plot: toPlotValue(p.netWorth) }));
  const byDate = new Map(points.map(p => [p.asOfDate, p]));

  return (
    <figure className="chart" aria-describedby={tableId}>
      <figcaption>
        Net worth by day. A gap means no account was verified that day; a shaded band marks
        days where only some were. The table below lists every point.
      </figcaption>
      <ResponsiveContainer width="100%" height={320}>
        <LineChart data={data} margin={{ top: 24, right: 16, bottom: 8, left: 16 }} accessibilityLayer
          title="Net worth by day">
          <CartesianGrid stroke="var(--grid)" strokeDasharray="3 3" />
          <XAxis dataKey="asOfDate" tick={{ fill: 'var(--muted)' }} minTickGap={32} />
          <YAxis tick={{ fill: 'var(--muted)' }} width={88}
            tickFormatter={(v: number) => v.toLocaleString(undefined, { maximumFractionDigits: 0 })} />
          {bands(points).map(b => (
            <ReferenceArea key={`${b.status}-${b.from}`} x1={b.from} x2={b.to} ifOverflow="extendDomain"
              fill={b.status === 'unverified' ? 'var(--band-unverified)' : 'var(--band-partial)'}
              label={{ value: b.status, position: 'insideTop', fill: 'var(--muted)', fontSize: 12 }} />
          ))}
          <Tooltip
            filterNull={false}
            contentStyle={{ background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)' }}
            labelFormatter={label => String(label)}
            formatter={(_value, _name, item) => {
              const p = byDate.get((item.payload as NetWorthPoint).asOfDate)!;
              const coverage = `${p.accountsVerified} of ${p.accountsInWindow} accounts verified`;
              return p.netWorth === null
                ? [`unverified (${coverage})`, 'Net worth']
                : [`${formatMoney(p.netWorth)} USD, ${p.status} (${coverage})`, 'Net worth'];
            }}
          />
          <Line type="stepAfter" dataKey="plot" name="Net worth" stroke="var(--line)" strokeWidth={2}
            dot={props => <SegmentEnd key={`dot-${props.index}`} {...props} data={data} />}
            connectNulls={false} isAnimationActive={false} />
        </LineChart>
      </ResponsiveContainer>
    </figure>
  );
}
