import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type McpSettings = components['schemas']['McpSettings'];
export type McpSettingsDocument = components['schemas']['McpSettingsDto'];
export type McpClient = components['schemas']['McpClientDto'];

const keys = { settings: ['mcp', 'settings'] as const, clients: ['mcp', 'clients'] as const };

export function useMcpSettings() {
  return useQuery({
    queryKey: keys.settings,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/mcp/settings', { signal })),
  });
}

export function useSaveMcpSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { settings: McpSettings; version: number }) =>
      unwrap(await api.PUT('/api/v1/mcp/settings', { body })),
    onSuccess: (saved) => queryClient.setQueryData(keys.settings, saved),
  });
}

export function useMcpClients() {
  return useQuery({
    queryKey: keys.clients,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/mcp/clients', { signal })),
  });
}

export function useRevokeMcpClient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (clientId: string) =>
      unwrap(await api.POST('/api/v1/mcp/clients/{clientId}/revoke', { params: { path: { clientId } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.clients }),
  });
}

export function useMcpConsent(clientId: string, redirectUri: string) {
  return useQuery({
    queryKey: ['mcp', 'consent', clientId, redirectUri],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/mcp/consent', { params: { query: { clientId, redirectUri } }, signal })),
    retry: false,
  });
}

export function useGrantMcpConsent() {
  return useMutation({
    mutationFn: async (body: { clientId: string; redirectUri: string; codeChallenge: string }) =>
      unwrap(await api.POST('/api/v1/mcp/consent', { body })),
  });
}
