// Slice 03 step 4: who may see the portfolio. The gate chooses set-up, sign-in or the app from the
// anonymous state check, and an expired session opens sign-in as a dialog OVER the page, so nothing
// on it is lost (C3-17). Sign-out is a full navigation (D15).
import { createContext, useContext, useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';
import { ceremonyMessage, enrolWithPasskey, onLocalhost, passkeysAvailable, signInWithPasskey } from '../api/passkey';
import { markSignedIn, useSignedOut } from '../api/session';

/** Set just before sign-out navigates away; read once by the sign-in view (D15). */
const SIGNED_OUT_FLAG = 'portfolio:signed-out';

const LIFETIME = 'You stay signed in for 7 days after you last use the app, or until you sign out.';

const Session = createContext<{ userName: string; signOut: () => Promise<void> } | null>(null);

export function AuthGate({ children }: { children: ReactNode }) {
  const client = useQueryClient();
  const state = useQuery({
    queryKey: ['auth-state'],
    queryFn: async () => (await api.GET('/api/v1/auth/state')).data ?? null,
  });
  const session = useQuery({
    queryKey: ['auth-session'],
    queryFn: async () => (await api.GET('/api/v1/auth/session')).data ?? null,
    enabled: state.data?.enrolled === true,
    retry: false,
    staleTime: Infinity,
  });
  const [userName, setUserName] = useState<string | null>(null);
  const [acted, setActed] = useState(false); // focus moves on a view change the person caused, not on first load
  // On the gate, not the dialog: the dialog unmounts the moment the session is back, and a live
  // region that leaves with it announces nothing.
  const [announcement, setAnnouncement] = useState('');
  const signedOut = useSignedOut();
  const name = userName ?? session.data?.userName ?? null;

  if (state.isPending || (state.data?.enrolled && session.isPending)) return <p>Loading…</p>;
  if (!state.data) return <p role="alert" className="form-error">The app cannot be reached. Reload the page to try again.</p>;

  const signedIn = (who: string) => {
    markSignedIn();
    setActed(true);
    setUserName(who);
    void client.invalidateQueries();
  };

  if (!state.data.enrolled && !name)
    return <SetUpView environment={state.data.environment} onDone={signedIn} />;
  if (!name)
    return <SignInView environment={state.data.environment} focusHeading={acted} onSignedIn={signedIn} />;

  async function signOut() {
    await api.POST('/api/v1/auth/sign-out');
    try { sessionStorage.setItem(SIGNED_OUT_FLAG, '1'); } catch { /* storage refused: the view simply omits the line */ }
    location.assign('/');
  }

  return (
    <Session.Provider value={{ userName: name, signOut }}>
      <View title="Portfolio" focusHeading={acted}>{children}</View>
      <p role="status" className="visually-hidden">{announcement}</p>
      {signedOut && <SignInDialog onSignedIn={who => {
        // C3-17: the token first (it belongs to the new session), then the data, then focus back.
        markSignedIn();
        void client.invalidateQueries();
        setUserName(who);
        setAnnouncement('Signed in again.');
      }} />}
    </Session.Provider>
  );
}

/** The header's account line: who is signed in, and the way out. */
export function AccountBar() {
  const session = useContext(Session);
  if (!session) return null;
  return (
    <p className="account-bar">
      Signed in as <strong>{session.userName}</strong>{' '}
      <button type="button" onClick={() => void session.signOut()}>Sign out</button>
    </p>
  );
}

/** Sets the document title, and focuses the view's h1 when the person caused the change (C3-37). */
function View({ title, focusHeading, children }: { title: string; focusHeading: boolean; children: ReactNode }) {
  useEffect(() => {
    document.title = title === 'Portfolio' ? 'Portfolio' : `${title} — Portfolio`;
    if (focusHeading) document.querySelector<HTMLElement>('h1')?.focus();
  }, [title, focusHeading]);
  return <>{children}</>;
}

function EnvironmentLabel({ environment }: { environment: string }) {
  // environments.md: the environment is shown before authentication.
  return <p className="hint">Environment: <strong>{environment}</strong>. Invented data only.</p>;
}

function SignInView({ environment, focusHeading, onSignedIn }: {
  environment: string; focusHeading: boolean; onSignedIn: (userName: string) => void;
}) {
  const [justSignedOut] = useState(() => {
    try {
      const flag = sessionStorage.getItem(SIGNED_OUT_FLAG) === '1';
      sessionStorage.removeItem(SIGNED_OUT_FLAG);
      return flag;
    } catch { return false; }
  });
  return (
    <View title="Sign in" focusHeading={focusHeading || justSignedOut}>
      <main className="auth">
        <h1 tabIndex={-1}>Sign in</h1>
        {justSignedOut && <p role="status">You are signed out.</p>}
        <EnvironmentLabel environment={environment} />
        <p className="hint">{LIFETIME}</p>
        <PasskeyButton label="Sign in with a passkey" run={signInWithPasskey} onDone={onSignedIn} />
      </main>
    </View>
  );
}

/** An expired session, over the page: sign in again without losing anything on it (C3-17). */
function SignInDialog({ onSignedIn }: { onSignedIn: (userName: string) => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  const [open, setOpen] = useState(true);
  const returnTo = useRef<HTMLElement | null>(null);

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog || !open || dialog.open) return;
    returnTo.current = document.activeElement as HTMLElement | null;
    dialog.showModal(); // the rest of the page is inert, and focus moves in (G-30)
  }, [open]);

  return (
    <>
      {!open && <p className="account-bar"><button type="button" onClick={() => setOpen(true)}>Sign in</button></p>}
      <dialog ref={ref} aria-labelledby={titleId} aria-describedby={descriptionId} onClose={() => setOpen(false)}>
        <h2 id={titleId}>Signed out</h2>
        <p id={descriptionId}>Your session ended. Sign in to carry on; nothing on this page has been lost.</p>
        <PasskeyButton label="Sign in with a passkey" run={signInWithPasskey} onDone={who => {
          ref.current?.close();
          onSignedIn(who);
          (returnTo.current?.isConnected ? returnTo.current : document.querySelector<HTMLElement>('h1'))?.focus();
        }} />
      </dialog>
    </>
  );
}

const SETUP_STEPS = 3;

function SetUpView({ environment, onDone }: { environment: string; onDone: (userName: string) => void }) {
  const [step, setStep] = useState(1);
  const [setupCode, setSetupCode] = useState('');
  const [userName, setName] = useState('');
  const [enrolled, setEnrolled] = useState<string | null>(null);
  const codeId = useId();
  const nameId = useId();
  const titles = ['Enter the setup code', 'Choose a user name', 'Add a passkey'];
  const heading = enrolled ? 'Set-up complete' : `Step ${step} of ${SETUP_STEPS}: ${titles[step - 1]}`;

  useEffect(() => {
    document.title = 'Set up — Portfolio';
    if (step > 1 || enrolled) document.querySelector<HTMLElement>('h1')?.focus(); // C3-53
  }, [step, enrolled]);

  function next(e: FormEvent) { e.preventDefault(); setStep(s => s + 1); }

  return (
    <main className="auth">
      <h1 tabIndex={-1}>{heading}</h1>
      <EnvironmentLabel environment={environment} />
      {enrolled ? (
        <>
          <p role="status">Set-up complete. You are signed in as {enrolled}.</p>
          <button type="button" onClick={() => onDone(enrolled)}>Open the portfolio</button>
        </>
      ) : step === 1 ? (
        <form onSubmit={next}>
          <p className="hint">The setup code is in the deployment's environment file, or the AppHost's parameters under <code>aspire run</code>.</p>
          <div className="field">
            <label htmlFor={codeId}>Setup code</label>
            <input id={codeId} value={setupCode} onChange={e => setSetupCode(e.target.value)}
              autoComplete="one-time-code" spellCheck={false} required />
          </div>
          <button type="submit">Next</button>
        </form>
      ) : step === 2 ? (
        <form onSubmit={next}>
          <div className="field">
            <label htmlFor={nameId}>User name</label>
            <input id={nameId} value={userName} onChange={e => setName(e.target.value)} autoComplete="username" required />
          </div>
          <button type="button" onClick={() => setStep(1)}>Back</button>{' '}
          <button type="submit">Next</button>
        </form>
      ) : (
        <>
          <p className="hint">{LIFETIME}</p>
          <PasskeyButton label="Create a passkey" run={signal => enrolWithPasskey(setupCode, userName, signal)} onDone={setEnrolled} />
          <p><button type="button" onClick={() => setStep(1)}>Change the setup code or user name</button></p>
        </>
      )}
    </main>
  );
}

/**
 * The ceremony's button (C3-18). It never starts on page load or focus. While the browser has control
 * it stays enabled but aria-disabled, so focus is not dropped, and a second press is ignored. Each
 * outcome has its own text, and focus comes back to the button.
 */
function PasskeyButton({ label, run, onDone }: {
  label: string; run: (signal: AbortSignal) => Promise<string>; onDone: (userName: string) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const hintId = useId();
  const button = useRef<HTMLButtonElement>(null);
  const running = useRef<AbortController | null>(null);
  useEffect(() => () => running.current?.abort(), []);

  if (!passkeysAvailable)
    return <p role="alert" className="form-error">This browser cannot use passkeys, so it cannot sign in here. Use a recent Chrome, Edge, Firefox or Safari.</p>;
  const elsewhere = !onLocalhost();

  async function press() {
    if (busy || elsewhere) return;
    const controller = new AbortController();
    running.current = controller;
    setBusy(true);
    setError(null);
    try {
      onDone(await run(controller.signal));
    } catch (e) {
      setError(ceremonyMessage(e));
      button.current?.focus();
    } finally {
      setBusy(false);
      running.current = null;
    }
  }

  return (
    <div className="passkey">
      <p id={hintId} className="hint">
        {elsewhere
          ? <>Passkeys do not work at this address. Open the app as <code>http://localhost:{location.port}</code>.</>
          : 'Your browser will ask you to unlock with your device or a security key. You have up to 10 minutes.'}
      </p>
      <button ref={button} type="button" onClick={() => void press()} aria-disabled={busy || elsewhere} aria-describedby={hintId}>
        {label}
      </button>
      <p role="status">{busy ? 'Waiting for your passkey…' : ''}</p>
      {error && <p role="alert" className="form-error">{error}</p>}
    </div>
  );
}
