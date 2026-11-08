// TanStack Query over the generated client. Every request and response type comes from
// schema.d.ts, so a contract change fails `tsc` here rather than at runtime.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import { apiFetch } from './session';
import type { components } from './schema';

export type Account = components['schemas']['AccountResponse'];
export type NetWorthPoint = components['schemas']['NetWorthPoint'];
export type NetWorthResponse = components['schemas']['NetWorthResponse'];
export type CreateAccountRequest = components['schemas']['CreateAccountRequest'];
export type RecordBalanceRequest = components['schemas']['RecordBalanceRequest'];
export type Balance = components['schemas']['BalanceResponse'];
export type Source = components['schemas']['SourceResponse'];
export type BindSourceRequest = components['schemas']['BindSourceRequest'];
export type ImportRun = components['schemas']['ImportRunResponse'];
export type ReportRun = components['schemas']['ReportRunResponse'];
export type SavedReport = components['schemas']['SavedReportResponse'];
export type CreateSavedReport = components['schemas']['CreateSavedReportRequest'];
export type ReportParameterType = components['schemas']['ReportParameterType'];

/** A refused request, carrying the API's RFC 9457 field errors when it sent them. */
export class ApiError extends Error {
  readonly status: number;
  readonly fields: Record<string, string[]>;
  /** The problem's detail: for a refused report, PostgreSQL's own message. */
  readonly detail?: string;
  /** The SQLSTATE of a report the database refused (slice 04). */
  readonly sqlState?: string;
  constructor(status: number, title: string, fields: Record<string, string[]> = {}, detail?: string, sqlState?: string) {
    super(title);
    this.status = status;
    this.fields = fields;
    this.detail = detail;
    this.sqlState = sqlState;
  }
}

function fail(response: Response, error: unknown): never {
  // A 401 is the session ending (step 4): the sign-in dialog says so once, sections say nothing,
  // and a refused write keeps its input and says why. It is never replayed.
  if (response.status === 401)
    throw new ApiError(401, 'You were signed out before this was saved. Sign in, then send it again.');
  const problem = (error ?? {}) as { title?: string; detail?: string; sqlState?: string; errors?: Record<string, string[]> };
  throw new ApiError(response.status, problem.title ?? `Request failed (HTTP ${response.status}).`, problem.errors, problem.detail, problem.sqlState);
}

/** A section shows no alert of its own for this: the sign-in dialog is the one message. */
export const isSignedOut = (error: unknown) => error instanceof ApiError && error.status === 401;

const keys = {
  accounts: ['accounts'] as const,
  netWorth: (from: string, to: string) => ['net-worth', from, to] as const,
  sources: (accountId: string) => ['sources', accountId] as const,
  imports: ['imports'] as const,
  reportSchema: ['report-schema'] as const,
  savedReports: ['saved-reports'] as const,
};

export function useAccounts() {
  return useQuery({
    queryKey: keys.accounts,
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/accounts');
      return data ?? fail(response, error);
    },
  });
}

export function useNetWorth(from: string, to: string) {
  return useQuery({
    queryKey: keys.netWorth(from, to),
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/net-worth', { params: { query: { from, to } } });
      return data ?? fail(response, error);
    },
  });
}

export function useHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/system/health');
      return data ?? fail(response, error);
    },
  });
}

/** A write changes the account list AND the series, so both are refetched. */
function useInvalidateAll() {
  const client = useQueryClient();
  return () => Promise.all([
    client.invalidateQueries({ queryKey: keys.accounts }),
    client.invalidateQueries({ queryKey: ['net-worth'] }),
  ]);
}

export function useCreateAccount() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async (body: CreateAccountRequest) => {
      const { data, error, response } = await api.POST('/api/v1/accounts', { body });
      return data ?? fail(response, error);
    },
    onSuccess: invalidate,
  });
}

/**
 * 201 is a new or restated balance; 200 is the same amount already recorded, and nothing was
 * written (POST /balances is idempotent since slice 02). The form says which.
 */
export function useRecordBalance() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async ({ accountId, body }: { accountId: string; body: RecordBalanceRequest }) => {
      const { data, error, response } = await api.POST('/api/v1/accounts/{id}/balances', {
        params: { path: { id: accountId } },
        body,
      });
      return data ? { balance: data, unchanged: response.status === 200 } : fail(response, error);
    },
    onSuccess: invalidate,
  });
}

export function useSources(accountId: string) {
  return useQuery({
    queryKey: keys.sources(accountId),
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/accounts/{id}/sources', { params: { path: { id: accountId } } });
      return data ?? fail(response, error);
    },
  });
}

export function useBindSource() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ accountId, body }: { accountId: string; body: BindSourceRequest }) => {
      const { data, error, response } = await api.POST('/api/v1/accounts/{id}/sources', {
        params: { path: { id: accountId } },
        body,
      });
      return data ?? fail(response, error);
    },
    onSuccess: (_, { accountId }) => client.invalidateQueries({ queryKey: keys.sources(accountId) }),
  });
}

export function useImports() {
  return useQuery({
    queryKey: keys.imports,
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/imports', { params: { query: { limit: 50 } } });
      return data ?? fail(response, error);
    },
  });
}

/** Spike F: the API refuses more; the page says so before sending, because through the dev proxy a refusal arrives as 502. */
export const MAX_UPLOAD_BYTES = 1024 * 1024;

/** The Content-Type the API reads the file as, from its extension; null if it is neither format. */
export function importTypeFor(fileName: string): 'text/csv' | 'application/x-ofx' | null {
  const ext = fileName.toLowerCase().split('.').pop();
  return ext === 'csv' ? 'text/csv' : ext === 'ofx' ? 'application/x-ofx' : null;
}

/**
 * The upload. Plain fetch, not the typed client: openapi-typescript types a binary body as
 * `string`, so the client cannot take a File without a cast (slice-02.md step 3). The File IS the
 * body, with the type set from its extension - never file.type, which Windows reports as
 * application/vnd.ms-excel for a .csv - and never a FormData (Spike F). The response is typed.
 */
export function useImportFile() {
  const invalidate = useInvalidateAll();
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (file: File): Promise<ImportRun> => {
      const type = importTypeFor(file.name);
      if (!type) throw new ApiError(415, 'Choose a .csv or .ofx file.');
      if (file.size > MAX_UPLOAD_BYTES) throw new ApiError(413, `The file is larger than 1 MiB (${file.size.toLocaleString()} bytes).`);
      const response = await apiFetch(new Request(`/api/v1/imports?fileName=${encodeURIComponent(file.name)}`, {
        method: 'POST',
        headers: { 'Content-Type': type },
        body: file,
      }));
      const body: unknown = await response.json().catch(() => ({}));
      if (response.status === 201) return body as ImportRun;
      return fail(response, body);
    },
    onSuccess: () => Promise.all([invalidate(), client.invalidateQueries({ queryKey: keys.imports })]),
  });
}

// Slice 04 step 5: reports. A run is a mutation, not a query: it must never retry or refetch by itself.

export function useRunReport() {
  return useMutation({
    mutationFn: async (sql: string) => {
      const { data, error, response } = await api.POST('/api/v1/reports/run', { body: { sql } });
      return data ?? fail(response, error);
    },
  });
}

export function useRunSavedReport(id: string) {
  return useMutation({
    mutationFn: async (parameters: Record<string, string>) => {
      const { data, error, response } = await api.POST('/api/v1/reports/saved/{id}/run', {
        params: { path: { id } },
        body: { parameters },
      });
      return data ?? fail(response, error);
    },
  });
}

/** Fetched only while the documentation is open, and again each time it opens: it is live. */
export function useReportSchema(enabled: boolean) {
  return useQuery({
    queryKey: keys.reportSchema,
    enabled,
    staleTime: 0,
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/reports/schema');
      return data ?? fail(response, error);
    },
  });
}

export function useSavedReports() {
  return useQuery({
    queryKey: keys.savedReports,
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/reports/saved');
      return data ?? fail(response, error);
    },
  });
}

export function useSaveReport() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateSavedReport) => {
      const { data, error, response } = await api.POST('/api/v1/reports/saved', { body });
      return data ?? fail(response, error);
    },
    onSuccess: () => client.invalidateQueries({ queryKey: keys.savedReports }),
  });
}

export function useDeleteSavedReport() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/reports/saved/{id}', { params: { path: { id } } });
      if (!response.ok) fail(response, error);
    },
    onSuccess: () => client.invalidateQueries({ queryKey: keys.savedReports }),
  });
}
