import { useState } from 'react';
import { useHealth, useNetWorth } from './api/queries';
import { AccountsSection } from './components/Accounts';
import { NetWorthChart } from './components/NetWorthChart';
import { NetWorthTable } from './components/NetWorthTable';
import { addDays, localToday } from './money';

const RANGES = [
  { label: '3 months', days: 91 },
  { label: '6 months', days: 182 },
  { label: '1 year', days: 365 },
] as const;

function NetWorthSection() {
  const [days, setDays] = useState<number>(182);
  const to = localToday();
  const from = addDays(to, -days);
  const series = useNetWorth(from, to);
  const points = series.data?.points ?? [];
  const anyValue = points.some(p => p.netWorth !== null);

  return (
    <section aria-labelledby="net-worth-heading">
      <h2 id="net-worth-heading">Net worth</h2>
      <fieldset className="ranges">
        <legend>Range</legend>
        {RANGES.map(r => (
          <label key={r.days}>
            <input type="radio" name="range" value={r.days} checked={days === r.days} onChange={() => setDays(r.days)} />
            {r.label}
          </label>
        ))}
      </fieldset>
      {series.isPending && <p>Loading net worth…</p>}
      {series.isError && <p role="alert" className="form-error">Could not load net worth: {series.error.message}</p>}
      {series.data && (
        <>
          {anyValue
            ? <NetWorthChart points={points} tableId="net-worth-table" />
            : <p>No verified balance in this range yet. Record one below and it will appear here.</p>}
          <p className="hint">
            A balance is carried forward for up to {series.data.stalenessDays} days; after that the account
            counts as unverified until a new balance is recorded.
          </p>
          <details>
            <summary>Show as a table</summary>
            <NetWorthTable id="net-worth-table" points={points} />
          </details>
        </>
      )}
    </section>
  );
}

export default function App() {
  const health = useHealth();
  return (
    <>
      <header>
        <h1>Portfolio</h1>
        <p className="hint">Invented data only. Nothing here is a real account.</p>
      </header>
      <main>
        <NetWorthSection />
        <AccountsSection />
      </main>
      <footer>
        <p role="status" data-testid="api-health">
          API: {health.isPending ? 'checking…' : health.isSuccess ? 'reachable' : `unreachable (${health.error?.message})`}
        </p>
      </footer>
    </>
  );
}
