// Step 1 shell: proves the page reaches the API through a relative path (Spike B).
// Replaced by the account list and net-worth chart in step 4.
import { useEffect, useState } from 'react';

type Health = { state: 'checking' } | { state: 'ok' } | { state: 'error'; detail: string };

export default function App() {
  const [health, setHealth] = useState<Health>({ state: 'checking' });

  useEffect(() => {
    fetch('/api/health')
      .then(async r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        const body = await r.json();
        setHealth(body.status === 'ok' ? { state: 'ok' } : { state: 'error', detail: JSON.stringify(body) });
      })
      .catch(e => setHealth({ state: 'error', detail: String(e) }));
  }, []);

  return (
    <main>
      <h1>Portfolio</h1>
      <p role="status" data-testid="api-health">
        API:{' '}
        {health.state === 'checking' && 'checking…'}
        {health.state === 'ok' && 'reachable'}
        {health.state === 'error' && `unreachable (${health.detail})`}
      </p>
    </main>
  );
}
