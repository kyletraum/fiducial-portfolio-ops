// slice-02.md step 4: upload a balance file, see what it did, see what every import did.
import { useId, useRef, useState, type FormEvent } from 'react';
import { ApiError, MAX_UPLOAD_BYTES, importTypeFor, useImportFile, useImports, type ImportRun } from '../api/queries';
import { formatMoment } from '../money';

const FORMAT_LABEL: Record<string, string> = { 'import:csv': 'CSV', 'import:ofx': 'OFX' };

function plural(n: number, one: string, many = `${one}s`) {
  return `${n.toLocaleString()} ${n === 1 ? one : many}`;
}

/** What a run did, in one sentence - the text announced when an upload finishes. */
function summarise(run: ImportRun): string {
  if (run.status === 'failed')
    return `Nothing was imported from ${run.fileName}: ${plural(run.errors.length, 'problem')} found. Fix the file and upload it again.`;
  const parts = [`${run.created.toLocaleString()} new`, `${run.restated.toLocaleString()} restated`, `${run.unchanged.toLocaleString()} unchanged`];
  return `Imported ${run.fileName}: ${parts.join(', ')}.`;
}

function ProblemList({ run }: { run: ImportRun }) {
  return (
    <ul className="problems">
      {/* File text arrives quoted and escaped by the API, and React renders it as text (FR-1.5). */}
      {run.errors.map((e, i) => <li key={i}><strong>{e.location}</strong>: {e.reason}</li>)}
    </ul>
  );
}

export function ImportForm() {
  const upload = useImportFile();
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const hintId = useId();

  function choose(f: File | null) {
    setFile(f);
    upload.reset();
    // Checked here, before anything is sent: through the dev proxy an over-limit upload comes
    // back as 502, which would say nothing about size (Spike F).
    setProblem(
      f === null ? null
        : importTypeFor(f.name) === null ? `${f.name} is not a .csv or .ofx file.`
        : f.size > MAX_UPLOAD_BYTES ? `${f.name} is ${f.size.toLocaleString()} bytes; files up to 1 MiB are accepted.`
        : null);
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!file) { setProblem('Choose a file first.'); return; }
    if (problem) return;
    upload.mutate(file, {
      onSuccess: run => {
        if (run.status === 'succeeded') {
          setFile(null);
          if (input.current) input.current.value = '';
        }
      },
    });
  }

  const run = upload.data;
  const refusal = upload.error instanceof ApiError ? upload.error.message : upload.error?.message;
  return (
    <form onSubmit={submit} aria-labelledby="import-heading" noValidate>
      <h3 id="import-heading">Import a balance file</h3>
      <p id={hintId} className="hint">
        A CSV with the columns <code>account,as_of_date,balance</code>, or an OFX statement. Up to 1 MiB.
        Every identifier in it must be linked to an account first. One problem in the file and nothing is imported.
      </p>
      <div className="field">
        <label htmlFor={`${hintId}-file`}>File</label>
        <input
          id={`${hintId}-file`}
          ref={input}
          type="file"
          accept=".csv,.ofx"
          aria-describedby={hintId}
          aria-invalid={problem ? true : undefined}
          onChange={e => choose(e.target.files?.[0] ?? null)}
        />
      </div>
      <button type="submit" disabled={upload.isPending}>{upload.isPending ? 'Importing…' : 'Import file'}</button>
      {/* One live region for every outcome, so a screen reader hears how the upload ended. */}
      <div aria-live="polite" className="import-result">
        {problem && <p className="form-error">{problem}</p>}
        {refusal && <p className="form-error">{refusal}</p>}
        {run && (
          <>
            <p className={run.status === 'failed' ? 'form-error' : undefined}>{summarise(run)}</p>
            {run.errors.length > 0 && <ProblemList run={run} />}
          </>
        )}
      </div>
    </form>
  );
}

export function ImportHistory() {
  const runs = useImports();
  if (runs.isPending) return <p>Loading imports…</p>;
  if (runs.isError) return <p role="alert" className="form-error">Could not load imports: {runs.error.message}</p>;
  if (runs.data.length === 0) return <p>No imports yet.</p>;
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-labelledby="import-history-caption">
      <table>
        <caption id="import-history-caption">Every import, newest first, times in your time zone</caption>
        <thead>
          <tr>
            <th scope="col">Started</th>
            <th scope="col">File</th>
            <th scope="col">Format</th>
            <th scope="col">Outcome</th>
            <th scope="col" className="num">Rows</th>
            <th scope="col" className="num">New</th>
            <th scope="col" className="num">Restated</th>
            <th scope="col" className="num">Unchanged</th>
          </tr>
        </thead>
        <tbody>
          {runs.data.map(r => (
            <tr key={r.id}>
              <th scope="row"><time dateTime={r.startedAt}>{formatMoment(r.startedAt)}</time></th>
              <td>{r.fileName}</td>
              <td>{FORMAT_LABEL[r.sourceSystem] ?? r.sourceSystem}</td>
              <td>
                {r.status === 'succeeded' ? 'imported' : (
                  <details>
                    <summary>refused: {plural(r.errors.length, 'problem')}</summary>
                    <ProblemList run={r} />
                  </details>
                )}
              </td>
              <td className="num">{r.rowsRead.toLocaleString()}</td>
              <td className="num">{r.created.toLocaleString()}</td>
              <td className="num">{r.restated.toLocaleString()}</td>
              <td className="num">{r.unchanged.toLocaleString()}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function ImportsSection() {
  return (
    <section aria-labelledby="imports-heading">
      <h2 id="imports-heading">Imports</h2>
      <div className="forms">
        <ImportForm />
      </div>
      <ImportHistory />
    </section>
  );
}
