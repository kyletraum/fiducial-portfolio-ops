// Spike B: the page calls a RELATIVE /api path and reports what it got, plus
// which environment variables client code can see at all.
import { useEffect, useState } from 'react';

export default function App() {
  const [result, setResult] = useState('pending');
  useEffect(() => {
    fetch('/api/health')
      .then(async r => setResult(`${r.status} ${await r.text()}`))
      .catch(e => setResult(`error ${e}`));
  }, []);
  return (
    <main>
      <p id="origin">origin: {window.location.origin}</p>
      <p id="result">api: {result}</p>
      <p id="env">import.meta.env keys: {Object.keys(import.meta.env).sort().join(',')}</p>
    </main>
  );
}
