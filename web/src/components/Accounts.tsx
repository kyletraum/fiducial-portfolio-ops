import { useId, useState, type FormEvent, type ReactNode } from 'react';
import {
  ApiError, isSignedOut, useAccounts, useBindSource, useCreateAccount, useRecordBalance, useSources, type Account,
} from '../api/queries';
import { formatMoney, localToday } from '../money';

export function AccountsTable({ accounts }: { accounts: Account[] }) {
  if (accounts.length === 0) return <p>No accounts yet. Add one below.</p>;
  return (
    <table>
      <caption>Each account and its newest balance</caption>
      <thead>
        <tr>
          <th scope="col">Account</th>
          <th scope="col">Institution</th>
          <th scope="col">Opened</th>
          <th scope="col" className="num">Latest balance</th>
          <th scope="col">As of</th>
          <th scope="col">Import identifiers</th>
        </tr>
      </thead>
      <tbody>
        {accounts.map(a => (
          <tr key={a.id}>
            <th scope="row">{a.displayName}</th>
            <td>{a.institutionName}</td>
            <td>{a.openedOn}</td>
            <td className="num">
              {a.latestBalance === null ? <span className="muted">none</span> : `${formatMoney(a.latestBalance)} ${a.currency}`}
            </td>
            <td>{a.latestAsOfDate ?? '—'}</td>
            <td><SourceList accountId={a.id} /></td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

const FORMATS = { 'import:csv': 'CSV', 'import:ofx': 'OFX' } as const;

/** The identifiers a file may use for this account. Text from the API is rendered as text (FR-1.5). */
function SourceList({ accountId }: { accountId: string }) {
  const sources = useSources(accountId);
  if (!sources.data) return <span className="muted">…</span>;
  if (sources.data.length === 0) return <span className="muted">none</span>;
  return (
    <ul className="plain">
      {sources.data.map(s => (
        <li key={`${s.sourceSystem}:${s.sourceId}`}>
          <code>{s.sourceId}</code> <span className="muted">({FORMATS[s.sourceSystem as keyof typeof FORMATS] ?? s.sourceSystem})</span>
        </li>
      ))}
    </ul>
  );
}

/** A labelled input with its API error, linked by aria-describedby so it is read out. */
function Field({ label, error, children }: {
  label: string; error?: string[]; children: (props: { id: string; 'aria-describedby'?: string; 'aria-invalid'?: boolean }) => ReactNode;
}) {
  const id = useId();
  const errorId = `${id}-error`;
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {children({ id, 'aria-describedby': error ? errorId : undefined, 'aria-invalid': error ? true : undefined })}
      {error && <p id={errorId} className="field-error">{error.join(' ')}</p>}
    </div>
  );
}

function FormError({ error }: { error: Error | null }) {
  if (!error) return null;
  const fieldsOnly = error instanceof ApiError && Object.keys(error.fields).length > 0;
  return <p role="alert" className="form-error">{fieldsOnly ? 'Please correct the fields marked below.' : error.message}</p>;
}

const fieldErrors = (error: Error | null) => (error instanceof ApiError ? error.fields : {});

export function AddAccountForm() {
  const create = useCreateAccount();
  const [institutionName, setInstitution] = useState('');
  const [displayName, setDisplay] = useState('');
  const [openedOn, setOpenedOn] = useState(localToday());
  const errors = fieldErrors(create.error);

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate({ institutionName, displayName, openedOn }, {
      onSuccess: () => { setInstitution(''); setDisplay(''); },
    });
  }

  return (
    <form onSubmit={submit} aria-labelledby="add-account-heading" noValidate>
      <h3 id="add-account-heading">Add an account</h3>
      <FormError error={create.error} />
      <Field label="Institution" error={errors.institutionName}>
        {p => <input {...p} value={institutionName} onChange={e => setInstitution(e.target.value)} required maxLength={200} />}
      </Field>
      <Field label="Account name" error={errors.displayName}>
        {p => <input {...p} value={displayName} onChange={e => setDisplay(e.target.value)} required maxLength={200} />}
      </Field>
      <Field label="Opened on" error={errors.openedOn}>
        {p => <input {...p} type="date" value={openedOn} onChange={e => setOpenedOn(e.target.value)} required />}
      </Field>
      <button type="submit" disabled={create.isPending}>{create.isPending ? 'Adding…' : 'Add account'}</button>
      {create.isSuccess && <p role="status">Added {create.data.displayName}.</p>}
    </form>
  );
}

export function RecordBalanceForm({ accounts }: { accounts: Account[] }) {
  const record = useRecordBalance();
  const [accountId, setAccountId] = useState('');
  const [asOfDate, setAsOfDate] = useState(localToday());
  const [balance, setBalance] = useState('');
  const errors = fieldErrors(record.error);
  const chosen = accountId || accounts[0]?.id || '';

  if (accounts.length === 0) return null;

  function submit(e: FormEvent) {
    e.preventDefault();
    record.mutate({ accountId: chosen, body: { asOfDate, balance: balance.trim() } }, {
      onSuccess: () => setBalance(''),
    });
  }

  return (
    <form onSubmit={submit} aria-labelledby="record-balance-heading" noValidate>
      <h3 id="record-balance-heading">Record a balance</h3>
      <p className="hint">Entering a balance for a date that already has one replaces it; the old value is kept as history.</p>
      <FormError error={record.error} />
      <Field label="Account">
        {p => (
          <select {...p} value={chosen} onChange={e => setAccountId(e.target.value)}>
            {accounts.map(a => <option key={a.id} value={a.id}>{a.displayName} ({a.institutionName})</option>)}
          </select>
        )}
      </Field>
      <Field label="As of" error={errors.asOfDate}>
        {p => <input {...p} type="date" value={asOfDate} onChange={e => setAsOfDate(e.target.value)} required />}
      </Field>
      <Field label="Balance (USD)" error={errors.balance}>
        {p => <input {...p} inputMode="decimal" placeholder="1234.56" value={balance} onChange={e => setBalance(e.target.value)} required />}
      </Field>
      <button type="submit" disabled={record.isPending}>{record.isPending ? 'Saving…' : 'Record balance'}</button>
      {record.isSuccess && (
        <p role="status">
          {record.data.unchanged
            ? <>That balance, {formatMoney(record.data.balance.balance)} {record.data.balance.currency} for {record.data.balance.asOfDate}, was already recorded; nothing changed.</>
            : <>Recorded {formatMoney(record.data.balance.balance)} {record.data.balance.currency} for {record.data.balance.asOfDate}
                {record.data.balance.supersedes ? ', replacing the earlier value' : ''}.</>}
        </p>
      )}
    </form>
  );
}

/**
 * slice-02.md step 4: tell the importer which account a file's identifier means. A binding is
 * never guessed by the importer and never moved once made - the API refuses a re-bind (409).
 */
export function BindSourceForm({ accounts }: { accounts: Account[] }) {
  const bind = useBindSource();
  const [accountId, setAccountId] = useState('');
  const [sourceSystem, setSourceSystem] = useState<keyof typeof FORMATS>('import:csv');
  const [sourceId, setSourceId] = useState('');
  const errors = fieldErrors(bind.error);
  const chosen = accountId || accounts[0]?.id || '';

  if (accounts.length === 0) return null;

  function submit(e: FormEvent) {
    e.preventDefault();
    bind.mutate({ accountId: chosen, body: { sourceSystem, sourceId } }, { onSuccess: () => setSourceId('') });
  }

  return (
    <form onSubmit={submit} aria-labelledby="bind-source-heading" noValidate>
      <h3 id="bind-source-heading">Link an import identifier</h3>
      <p className="hint">
        The identifier a file uses for this account: the CSV <code>account</code> column, or the OFX account id.
        A link cannot be moved once made.
      </p>
      <FormError error={bind.error} />
      <Field label="Account">
        {p => (
          <select {...p} value={chosen} onChange={e => setAccountId(e.target.value)}>
            {accounts.map(a => <option key={a.id} value={a.id}>{a.displayName} ({a.institutionName})</option>)}
          </select>
        )}
      </Field>
      <Field label="File format" error={errors.sourceSystem}>
        {p => (
          <select {...p} value={sourceSystem} onChange={e => setSourceSystem(e.target.value as keyof typeof FORMATS)}>
            {Object.entries(FORMATS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        )}
      </Field>
      <Field label="Identifier in the file" error={errors.sourceId}>
        {p => <input {...p} value={sourceId} onChange={e => setSourceId(e.target.value)} required maxLength={64} autoComplete="off" />}
      </Field>
      <button type="submit" disabled={bind.isPending}>{bind.isPending ? 'Linking…' : 'Link identifier'}</button>
      {bind.isSuccess && <p role="status">Linked <code>{bind.data.sourceId}</code>.</p>}
    </form>
  );
}

export function AccountsSection() {
  const accounts = useAccounts();
  return (
    <section aria-labelledby="accounts-heading">
      <h2 id="accounts-heading">Accounts</h2>
      {accounts.isPending && <p>Loading accounts…</p>}
      {accounts.isError && !isSignedOut(accounts.error) && <p role="alert" className="form-error">Could not load accounts: {accounts.error.message}</p>}
      {accounts.data && <AccountsTable accounts={accounts.data} />}
      <div className="forms">
        {accounts.data && <RecordBalanceForm accounts={accounts.data} />}
        <AddAccountForm />
        {accounts.data && <BindSourceForm accounts={accounts.data} />}
      </div>
    </section>
  );
}
