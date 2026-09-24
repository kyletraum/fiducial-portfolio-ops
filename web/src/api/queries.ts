// TanStack Query over the generated client. Every request and response type comes from
// schema.d.ts, so a contract change fails `tsc` here rather than at runtime.
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import type { components } from './schema';

export type Account = components['schemas']['AccountResponse'];
export type NetWorthPoint = components['schemas']['NetWorthPoint'];
export type NetWorthResponse = components['schemas']['NetWorthResponse'];
export type CreateAccountRequest = components['schemas']['CreateAccountRequest'];
export type RecordBalanceRequest = components['schemas']['RecordBalanceRequest'];

/** A refused request, carrying the API's RFC 9457 field errors when it sent them. */
export class ApiError extends Error {
  readonly status: number;
  readonly fields: Record<string, string[]>;
  constructor(status: number, title: string, fields: Record<string, string[]> = {}) {
    super(title);
    this.status = status;
    this.fields = fields;
  }
}

function fail(response: Response, error: unknown): never {
  const problem = (error ?? {}) as { title?: string; errors?: Record<string, string[]> };
  throw new ApiError(response.status, problem.title ?? `Request failed (HTTP ${response.status}).`, problem.errors);
}

const keys = {
  accounts: ['accounts'] as const,
  netWorth: (from: string, to: string) => ['net-worth', from, to] as const,
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

export function useRecordBalance() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async ({ accountId, body }: { accountId: string; body: RecordBalanceRequest }) => {
      const { data, error, response } = await api.POST('/api/v1/accounts/{id}/balances', {
        params: { path: { id: accountId } },
        body,
      });
      return data ?? fail(response, error);
    },
    onSuccess: invalidate,
  });
}
