// The typed API client. Types come from src/api/schema.d.ts, generated from the committed
// OpenAPI document by `npm run gen:api` - never hand-edited.
// baseUrl is empty on purpose: the page calls a relative /api in every environment (Spike B).
// Every call goes through apiFetch: the CSRF token, and the one signed-out state (slice 03 step 4).
import createClient from 'openapi-fetch';
import type { paths } from './schema';
import { apiFetch } from './session';

export const api = createClient<paths>({ baseUrl: '', fetch: apiFetch });
