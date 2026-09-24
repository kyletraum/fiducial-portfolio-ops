// Step 1 shell: proves the page reaches the API through a relative path (Spike B).
// Replaced by the account list and net-worth chart in step 4.
import { useEffect, useState } from 'react';
import { api } from './api/client';

type Health = { state: 'checking' } | { state: 'ok' } | { state: 'error'; detail: string };

export default function App() {
  const [health, setHealth] = useState<Health>({ state: 'checking' });

  useEffect(() => {
    api.GET('/api/v1/system/health')
      .then(({ data, response }) => {
        if (!response.ok || !data) throw new Error(`HTTP ${response.status}`);
        setHealth(data.status === 'ok' ? { state: 'ok' } : { state: 'error', detail: data.status });
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
