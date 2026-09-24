// M-24: the chart's tabular equivalent. Same points, exact figures (the decimal strings the
// API sent, not the chart's plotted doubles). Scrollable by keyboard: the region is focusable.
import type { NetWorthPoint } from '../api/queries';
import { formatMoney } from '../money';

export function NetWorthTable({ id, points }: { id: string; points: NetWorthPoint[] }) {
  const newestFirst = [...points].reverse();
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-labelledby={`${id}-caption`}>
      <table id={id}>
        <caption id={`${id}-caption`}>Net worth by day, newest first ({points.length} days)</caption>
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col" className="num">Net worth (USD)</th>
            <th scope="col">Status</th>
            <th scope="col" className="num">Verified</th>
            <th scope="col" className="num">Carried</th>
            <th scope="col" className="num">Oldest value (days)</th>
          </tr>
        </thead>
        <tbody>
          {newestFirst.map(p => (
            <tr key={p.asOfDate}>
              <th scope="row">{p.asOfDate}</th>
              <td className="num">{p.netWorth === null ? <span className="muted">unverified</span> : formatMoney(p.netWorth)}</td>
              <td>{p.status}</td>
              <td className="num">{p.accountsVerified} of {p.accountsInWindow}</td>
              <td className="num">{p.accountsCarried}</td>
              <td className="num">{p.maxStalenessDays ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
