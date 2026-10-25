// The two passkey ceremonies, as the page runs them (slice-03.md step 4). The options are Identity's
// own WebAuthn JSON, passed straight to the browser's parse functions, and the browser's toJSON()
// goes straight back: no base64url by hand (committee BRW F5).
import { ApiError } from './queries';

/** Chrome 129, Firefox 119, Safari 18.4 and later (C3-52). Elsewhere the page says it cannot sign in. */
export const passkeysAvailable =
  typeof PublicKeyCredential !== 'undefined'
  && 'parseRequestOptionsFromJSON' in PublicKeyCredential
  && 'parseCreationOptionsFromJSON' in PublicKeyCredential;

/** Passkeys work only when the app is reached as localhost (D8; the RP ID is pinned to it). */
export const onLocalhost = () => location.hostname === 'localhost';

async function post(path: string, body?: unknown): Promise<unknown> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body ?? {}),
  });
  const json: unknown = await response.json().catch(() => ({}));
  if (!response.ok) {
    const problem = json as { title?: string };
    let message = problem.title ?? `Request failed (HTTP ${response.status}).`;
    // DoD 5 / C3-19: a static time, worked out once, never a countdown in a live region.
    const retryAfter = Number(response.headers.get('Retry-After'));
    if (response.status === 429 && retryAfter > 0) {
      const at = new Date(Date.now() + retryAfter * 1000).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      message += ` You can try again after ${at}.`;
    }
    throw new ApiError(response.status, message);
  }
  return json;
}

export async function signInWithPasskey(signal: AbortSignal): Promise<string> {
  const options = await post('/api/v1/auth/sign-in/options');
  const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(options as PublicKeyCredentialRequestOptionsJSON);
  const credential = await navigator.credentials.get({ publicKey, signal }) as PublicKeyCredential;
  const session = await post('/api/v1/auth/sign-in', credential.toJSON()) as { userName: string };
  return session.userName;
}

export async function enrolWithPasskey(setupCode: string, userName: string, signal: AbortSignal): Promise<string> {
  const options = await post('/api/v1/auth/enrolment/options', { setupCode, userName });
  const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(options as PublicKeyCredentialCreationOptionsJSON);
  const credential = await navigator.credentials.create({ publicKey, signal }) as PublicKeyCredential;
  const session = await post('/api/v1/auth/enrolment', { setupCode, credential: credential.toJSON() }) as { userName: string };
  return session.userName;
}

/**
 * What to tell the person after a ceremony fails (C3-18). Null for our own abort. The browser reports
 * a cancel and a timeout with the same NotAllowedError, so the page does not pretend to tell them apart.
 */
export function ceremonyMessage(error: unknown): string | null {
  if (error instanceof DOMException) {
    switch (error.name) {
      case 'AbortError': return null;
      case 'NotAllowedError': return 'The passkey request was cancelled or timed out. Nothing has changed. Try again.';
      case 'SecurityError': return `Passkeys do not work at this address. Open the app as http://localhost:${location.port}.`;
      case 'InvalidStateError': return 'This device already holds a passkey for this app.';
    }
  }
  return error instanceof Error ? error.message : 'Something went wrong. Nothing has changed. Try again.';
}
