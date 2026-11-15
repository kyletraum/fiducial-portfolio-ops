// slice-04.md step 5: write and run SQL over the reporting views, read the views' documentation,
// and keep reports. A plain <textarea>: no code-editor bundle, so slice 03's CSP is untouched, and
// Tab leaves the field, which a tab-to-indent editor would not (TST-11, WCAG 2.1.2).
// Every value is rendered as text by React. Nothing here interprets the SQL.
import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { flushSync } from 'react-dom';
import {
  ApiError,
  isSignedOut,
  useDeleteSavedReport,
  useReportSchema,
  useRunReport,
  useRunSavedReport,
  useSaveReport,
  useSavedReports,
  type ReportParameterType,
  type ReportRun,
  type SavedReport,
} from '../api/queries';

const NUMERIC_TYPES = new Set(['smallint', 'integer', 'bigint', 'numeric', 'real', 'double precision', 'money']);
const PARAMETER_TYPES: ReportParameterType[] = ['date', 'text', 'numeric'];

function rows(n: number) {
  return `${n.toLocaleString()} ${n === 1 ? 'row' : 'rows'}`;
}

/** The one sentence that says how the run ended. A cut-off result always says so (FR-6.3). */
function summarise(result: ReportRun): string {
  const n = result.rows.length;
  switch (result.cutOffBy) {
    case 'rows': return `First ${rows(n)} shown, result cut off at the row limit.`;
    case 'bytes': return n === 0
      ? 'No rows shown: the first row is over the size limit, result cut off.'
      : `First ${rows(n)} shown, result cut off at the size limit.`;
    case 'deadline': return n === 0
      ? 'Timed out before any rows came back.'
      : `Timed out: first ${rows(n)} shown, result cut off.`;
    default: return result.columns.length === 0 ? 'The statement ran and returned no rows.' : `${rows(n)}.`;
  }
}

/** UTC from the API; shown in the viewer's zone, to the second (slice-02's rule). */
function formatTimestamp(value: string): ReactNode {
  const moment = new Date(value);
  if (Number.isNaN(moment.getTime())) return value; // infinity
  return (
    <time dateTime={value}>
      {moment.toLocaleString(undefined, {
        year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit', timeZoneName: 'short',
      })}
    </time>
  );
}

function Cell({ value, type }: { value: unknown; type: string }): ReactNode {
  if (value === null) return <span className="report-null">null</span>;
  if (type === 'timestamp with time zone' && typeof value === 'string') return formatTimestamp(value);
  return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

/** Takes focus when it appears, so the keyboard lands on the outcome of what was just run. */
function useFocusOnMount<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  useEffect(() => {
    ref.current?.focus();
  }, []);
  return ref;
}

function ResultView({ result, label }: { result: ReportRun; label: string }) {
  const summary = useFocusOnMount<HTMLParagraphElement>();
  const captionId = useId();
  return (
    <div className="report-result">
      <p ref={summary} role="status" tabIndex={-1} className={result.cutOff ? 'report-cut-off' : undefined}>{summarise(result)}</p>
      {result.columns.length > 0 && (
        <div className="table-scroll" tabIndex={0} role="region" aria-labelledby={captionId}>
          <table>
            <caption id={captionId}>{label}</caption>
            <thead>
              <tr>{result.columns.map((c, i) => <th key={i} scope="col" className={NUMERIC_TYPES.has(c.type) ? 'num' : undefined}>{c.name}</th>)}</tr>
            </thead>
            <tbody>
              {result.rows.map((row, r) => (
                <tr key={r}>
                  {row.map((value, c) => (
                    <td key={c} className={NUMERIC_TYPES.has(result.columns[c].type) ? 'num' : undefined}>
                      <Cell value={value} type={result.columns[c].type} />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/** A refusal: the database's own words and its code, or the API's. Takes focus, as a result does. */
function Problem({ error, listFields = true }: { error: Error; listFields?: boolean }) {
  const alert = useFocusOnMount<HTMLDivElement>();
  const problem = error instanceof ApiError ? error : null;
  const fields = listFields ? Object.values(problem?.fields ?? {}).flat() : [];
  return (
    <div ref={alert} role="alert" tabIndex={-1} className="report-problem">
      <p className="form-error">{error.message}</p>
      {problem?.detail && <p>{problem.detail}</p>}
      {problem?.sqlState && <p className="hint">SQLSTATE <code>{problem.sqlState}</code></p>}
      {fields.length > 0 && <ul className="problems">{fields.map((f, i) => <li key={i}>{f}</li>)}</ul>}
    </div>
  );
}

function SaveForm({ sql }: { sql: string }) {
  const save = useSaveReport();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [parameters, setParameters] = useState<{ key: number; name: string; type: ReportParameterType }[]>([]);
  const [nextKey, setNextKey] = useState(0);
  const [focusRow, setFocusRow] = useState<number | null>(null);
  const addButton = useRef<HTMLButtonElement>(null);
  const baseId = useId();

  function add() {
    setParameters([...parameters, { key: nextKey, name: '', type: 'text' }]);
    setFocusRow(nextKey);
    setNextKey(nextKey + 1);
  }

  function remove(key: number) {
    setParameters(parameters.filter(p => p.key !== key));
    addButton.current?.focus(); // the removed row's button is about to go
  }

  function submit(e: FormEvent) {
    e.preventDefault();
    if (save.isPending) return;
    save.mutate(
      { name, description: description.trim() || null, sql, parameters: parameters.map(p => ({ name: p.name, type: p.type })) },
      { onSuccess: () => { setName(''); setDescription(''); setParameters([]); } },
    );
  }

  return (
    <form onSubmit={submit} aria-labelledby={`${baseId}-heading`} noValidate>
      <h3 id={`${baseId}-heading`}>Save this query</h3>
      <p className="hint">Saves the SQL in the box above. Write its parameters as <code>$1</code>, <code>$2</code>, … in the order listed here.</p>
      <div className="field">
        <label htmlFor={`${baseId}-name`}>Report name</label>
        <input id={`${baseId}-name`} value={name} onChange={e => setName(e.target.value)} required maxLength={200} />
      </div>
      <div className="field">
        <label htmlFor={`${baseId}-description`}>Description (optional)</label>
        <input id={`${baseId}-description`} value={description} onChange={e => setDescription(e.target.value)} maxLength={2000} />
      </div>
      {parameters.map((p, i) => (
        <fieldset key={p.key} className="report-parameter">
          <legend>Parameter ${i + 1}</legend>
          <div className="field">
            <label htmlFor={`${baseId}-p${p.key}-name`}>Name of ${i + 1}</label>
            <input
              id={`${baseId}-p${p.key}-name`}
              ref={el => { if (el && focusRow === p.key) { el.focus(); setFocusRow(null); } }}
              value={p.name}
              onChange={e => setParameters(parameters.map(x => x.key === p.key ? { ...x, name: e.target.value } : x))}
            />
          </div>
          <div className="field">
            <label htmlFor={`${baseId}-p${p.key}-type`}>Type of ${i + 1}</label>
            <select
              id={`${baseId}-p${p.key}-type`}
              value={p.type}
              onChange={e => setParameters(parameters.map(x => x.key === p.key ? { ...x, type: e.target.value as ReportParameterType } : x))}
            >
              {PARAMETER_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
          <button type="button" onClick={() => remove(p.key)}>Remove ${i + 1}</button>
        </fieldset>
      ))}
      <p className="report-actions">
        <button type="button" ref={addButton} onClick={add}>Add a parameter</button>
        <button type="submit" aria-disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save report'}</button>
      </p>
      {save.error && !isSignedOut(save.error) && <Problem error={save.error} />}
      {save.error && isSignedOut(save.error) && <p role="alert" className="form-error">{save.error.message}</p>}
      {save.isSuccess && <p role="status">Saved {save.data.name}.</p>}
    </form>
  );
}

function ParameterInput({ report, parameter, value, error, onChange }: {
  report: SavedReport; parameter: SavedReport['parameters'][number]; value: string; error?: string[]; onChange: (v: string) => void;
}) {
  const id = `${useId()}-${report.id}`;
  return (
    <div className="field">
      <label htmlFor={id}>{parameter.name} ({parameter.type})</label>
      <input
        id={id}
        type={parameter.type === 'date' ? 'date' : 'text'}
        inputMode={parameter.type === 'numeric' ? 'decimal' : undefined}
        value={value}
        onChange={e => onChange(e.target.value)}
        aria-describedby={error ? `${id}-error` : undefined}
        aria-invalid={error ? true : undefined}
      />
      {error && <p id={`${id}-error`} className="field-error">{error.join(' ')}</p>}
    </div>
  );
}

function SavedReportItem({ report, onDeleted }: { report: SavedReport; onDeleted: (name: string) => void }) {
  const run = useRunSavedReport(report.id);
  const remove = useDeleteSavedReport();
  const [values, setValues] = useState<Record<string, string>>({});
  const [confirming, setConfirming] = useState(false);
  const deleteButton = useRef<HTMLButtonElement>(null);
  const keepButton = useRef<HTMLButtonElement>(null);
  const headingId = useId();
  const fields = run.error instanceof ApiError ? run.error.fields : {};

  useEffect(() => {
    if (confirming) keepButton.current?.focus();
  }, [confirming]);

  function submit(e: FormEvent) {
    e.preventDefault();
    if (run.isPending) return;
    // A blank date or number is sent as absent, and the API says "Required." against it. An empty
    // text is a value. NULL is not offered: blank must never quietly match nothing.
    const parameters: Record<string, string> = {};
    for (const p of report.parameters) {
      const value = values[p.name] ?? '';
      if (p.type === 'text' || value !== '') parameters[p.name] = value;
    }
    run.mutate(parameters);
  }

  function keep() {
    flushSync(() => setConfirming(false)); // the Delete button must exist before it can take focus
    deleteButton.current?.focus();
  }

  return (
    <li className="report-saved">
      <form onSubmit={submit} aria-labelledby={headingId} noValidate>
        <h4 id={headingId}>{report.name}</h4>
        {report.description && <p>{report.description}</p>}
        <details>
          <summary>Show the SQL<span className="visually-hidden"> of {report.name}</span></summary>
          <pre className="report-sql"><code>{report.sql}</code></pre>
        </details>
        {report.parameters.map(p => (
          <ParameterInput
            key={p.name}
            report={report}
            parameter={p}
            value={values[p.name] ?? ''}
            error={fields[p.name]}
            onChange={v => setValues({ ...values, [p.name]: v })}
          />
        ))}
        <p className="report-actions">
          <button type="submit" aria-disabled={run.isPending}>
            {run.isPending ? 'Running…' : 'Run'}<span className="visually-hidden"> {report.name}</span>
          </button>
          {!confirming && (
            <button type="button" ref={deleteButton} onClick={() => setConfirming(true)}>
              Delete<span className="visually-hidden"> {report.name}</span>
            </button>
          )}
          {confirming && (
            <>
              <span>Delete this report? It cannot be brought back.</span>
              <button type="button" ref={keepButton} onClick={keep}>Keep it</button>
              <button type="button" onClick={() => remove.mutate(report.id, { onSuccess: () => onDeleted(report.name) })}>Yes, delete</button>
            </>
          )}
        </p>
        {remove.error && <p role="alert" className="form-error">{remove.error.message}</p>}
        {run.error && <Problem error={run.error} listFields={false} />}
        {run.data && <ResultView result={run.data} label={`Result of ${report.name}`} />}
      </form>
    </li>
  );
}

function SavedReports() {
  const saved = useSavedReports();
  const [deleted, setDeleted] = useState<string | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  function onDeleted(name: string) {
    setDeleted(name);
    heading.current?.focus(); // the item, and the button that deleted it, have gone
  }

  return (
    <div className="report-block">
      <h3 ref={heading} tabIndex={-1}>Saved reports</h3>
      <div role="status">{deleted && <p>Deleted {deleted}.</p>}</div>
      {saved.isPending && <p>Loading saved reports…</p>}
      {saved.isError && !isSignedOut(saved.error) && <p role="alert" className="form-error">Could not load saved reports: {saved.error.message}</p>}
      {saved.data?.length === 0 && <p>No saved reports yet. Write a query above and save it.</p>}
      {saved.data && saved.data.length > 0 && (
        <ul className="plain">{saved.data.map(r => <SavedReportItem key={r.id} report={r} onDeleted={onDeleted} />)}</ul>
      )}
    </div>
  );
}

/** Read live from the database's own COMMENTs each time it is opened (FR-6.6), never a copy here. */
function SchemaDocs() {
  const [open, setOpen] = useState(false);
  const schema = useReportSchema(open);
  return (
    <details className="report-block" onToggle={e => setOpen(e.currentTarget.open)}>
      <summary>The views you can query</summary>
      {schema.isPending && open && <p>Loading the views…</p>}
      {schema.isError && !isSignedOut(schema.error) && <p role="alert" className="form-error">Could not load the views: {schema.error.message}</p>}
      {schema.data?.views.map(view => (
        <div key={view.name} className="report-view">
          <h4><code>reporting.{view.name}</code></h4>
          {view.description && <p>{view.description}</p>}
          <dl>
            {view.columns.map(c => (
              <div key={c.name}>
                <dt><code>{c.name}</code> <span className="hint">{c.type}</span></dt>
                <dd>{c.description}</dd>
              </div>
            ))}
          </dl>
        </div>
      ))}
    </details>
  );
}

export function ReportsSection() {
  const run = useRunReport();
  const [sql, setSql] = useState('');
  const baseId = useId();

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!run.isPending) run.mutate(sql);
  }

  return (
    <section aria-labelledby="reports-heading">
      <h2 id="reports-heading">Reports</h2>
      <div className="forms">
        <form onSubmit={submit} aria-labelledby={`${baseId}-heading`} noValidate>
          <h3 id={`${baseId}-heading`}>Run a query</h3>
          <p id={`${baseId}-hint`} className="hint">
            One SQL statement over the views listed below. It runs read-only, with a time limit and limits on
            rows and size; a result that is cut off says so.
          </p>
          <div className="field">
            <label htmlFor={`${baseId}-sql`}>SQL</label>
            <textarea
              id={`${baseId}-sql`}
              rows={6}
              spellCheck={false}
              autoComplete="off"
              aria-describedby={`${baseId}-hint`}
              value={sql}
              onChange={e => setSql(e.target.value)}
            />
          </div>
          {/* Never disabled while running: a disabled button would drop keyboard focus. */}
          <button type="submit" aria-disabled={run.isPending}>{run.isPending ? 'Running…' : 'Run query'}</button>
        </form>
        <SaveForm sql={sql} />
      </div>
      {/* Below both forms, at full width: a result can be wide. */}
      {run.error && <Problem error={run.error} />}
      {run.data && <ResultView result={run.data} label="Query result" />}
      <SchemaDocs />
      <SavedReports />
    </section>
  );
}
