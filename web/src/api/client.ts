// The typed API client. Types come from src/api/schema.d.ts, generated from the committed
// OpenAPI document by `npm run gen:api` - never hand-edited.
// baseUrl is empty on purpose: the page calls a relative /api in every environment (Spike B).
import createClient from 'openapi-fetch';
import type { paths } from './schema';

export const api = createClient<paths>({ baseUrl: '' });
