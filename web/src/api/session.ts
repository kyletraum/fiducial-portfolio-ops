// The session, as the page sees it (slice-03.md step 4): the CSRF token, and one signed-out state.
//
// Every API call goes through apiFetch - the typed client's and the upload's - so the token and the
// 401 handling live in one place, not in each section.
import { useSyncExternalStore } from 'react';

/** The antiforgery filter's title. The page keys on it, never the status: layer 2 also answers 403. */
const CSRF_REFUSED = 'CSRF token missing or invalid';

const UNSAFE = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/** Held in module memory, never in web storage (security.md "Sessions"). Bound to the signed-in user. */
let token: string | null = null;

let signedOut = false;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach(l => l());

/** True once a request has come back 401: the session ended while the page was open. */
export function useSignedOut() {
  return useSyncExternalStore(
    l => { listeners.add(l); return () => listeners.delete(l); },
    () => signedOut,
  );
}

export function markSignedOut() {
  token = null;
  if (!signedOut) { signedOut = true; emit(); }
}

/** After a sign-in: the next unsafe request fetches a token for the new session first (C3-22). */
export function markSignedIn() {
  token = null;
  if (signedOut) { signedOut = false; emit(); }
}

async function fetchToken(): Promise<string | null> {
  const response = await fetch('/api/v1/auth/csrf-token');
  if (!response.ok) return null;
  token = ((await response.json()) as { token: string }).token;
  return token;
}

async function titleOf(response: Response): Promise<string | undefined> {
  try { return ((await response.clone().json()) as { title?: string }).title; } catch { return undefined; }
}

/**
 * fetch, for the API. Unsafe requests carry the token. A 403 from the CSRF filter (a stale token: a
 * redeploy, or a session since replaced) refetches it and retries ONCE. A 401 anywhere but the
 * authentication routes means the session is over.
 */
export async function apiFetch(input: Request): Promise<Response> {
  const unsafe = UNSAFE.has(input.method);
  const second = unsafe ? input.clone() : null;
  if (unsafe) input.headers.set('X-CSRF-TOKEN', token ?? (await fetchToken()) ?? '');

  let response = await fetch(input);
  if (second && response.status === 403 && (await titleOf(response)) === CSRF_REFUSED) {
    token = null;
    const fresh = await fetchToken();
    if (fresh) {
      second.headers.set('X-CSRF-TOKEN', fresh);
      response = await fetch(second);
    }
  }
  if (response.status === 401 && !new URL(input.url, location.href).pathname.startsWith('/api/v1/auth/')) markSignedOut();
  return response;
}
