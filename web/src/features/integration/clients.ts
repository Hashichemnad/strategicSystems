import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type IntegrationClient = components['schemas']['ClientDto'];
export type ClientInput = components['schemas']['ClientInput'];
export type CreatedClient = components['schemas']['CreatedClientDto'];
export type ClientCredential = components['schemas']['CredentialDto'];
export type ClientRole = components['schemas']['ServiceAccountRoleOption'];
export type WebhookSubscription = components['schemas']['WebhookSubscriptionDto'];
export type WebhookSubscriptionSecret = components['schemas']['WebhookSubscriptionSecretDto'];
export type WebhookDelivery = components['schemas']['WebhookDeliveryDto'];
export type WebhookEvent = components['schemas']['WebhookEventDto'];
export type CatalogueSystem = components['schemas']['CatalogueSystemDto'];
export type ExportSchedule = components['schemas']['ExportScheduleDto'];
export type Portal = components['schemas']['PortalDto'];

/** Permissions of Settings → Integrations (ADR 0191, 0194, 0195). */
export const CLIENT_MANAGE = 'integration.client.manage';
export const WEBHOOK_MANAGE = 'integration.webhook.manage';
export const PORTAL_VIEW = 'integration.portal.view';

export const clientKeys = {
  all: ['integration', 'clients'] as const,
  one: (id: string) => ['integration', 'clients', id] as const,
  part: (id: string, part: string) => ['integration', 'clients', id, part] as const,
  catalogue: ['integration', 'catalogue'] as const,
  deliveries: (clientId: string | undefined, status: string | undefined, page: number) =>
    ['integration', 'deliveries', clientId ?? '*', status ?? '*', page] as const,
  portal: ['integration', 'portal'] as const,
  events: ['integration', 'events'] as const,
};

export function useIntegrationClients() {
  return useQuery({
    queryKey: clientKeys.all,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/integration/clients', { signal })),
  });
}

export function useIntegrationClient(id: string) {
  return useQuery({
    queryKey: clientKeys.one(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/integration/clients/{id}', { params: { path: { id } }, signal })),
  });
}

export function useIntegrationCatalogue() {
  return useQuery({
    queryKey: clientKeys.catalogue,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/integration/catalogue', { signal })),
  });
}

export function useClientCredentials(id: string) {
  return useQuery({
    queryKey: clientKeys.part(id, 'credentials'),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/clients/{id}/credentials', { params: { path: { id } }, signal }),
      ),
  });
}

export function useClientRoles(id: string) {
  return useQuery({
    queryKey: clientKeys.part(id, 'roles'),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/integration/clients/{id}/roles', { params: { path: { id } }, signal })),
  });
}

export function useClientUsage(id: string, page: number) {
  return useQuery({
    queryKey: [...clientKeys.part(id, 'usage'), page],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/clients/{id}/usage', {
          params: { path: { id }, query: { page, pageSize: 25 } },
          signal,
        }),
      ),
  });
}

export function useWebhookSubscriptions(clientId: string) {
  return useQuery({
    queryKey: clientKeys.part(clientId, 'webhooks'),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/clients/{clientId}/webhooks', {
          params: { path: { clientId } },
          signal,
        }),
      ),
  });
}

export function useExportSchedules(clientId: string) {
  return useQuery({
    queryKey: clientKeys.part(clientId, 'exports'),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/clients/{clientId}/exports', {
          params: { path: { clientId } },
          signal,
        }),
      ),
  });
}

export function useWebhookDeliveries(clientId: string | undefined, status: string | undefined, page: number) {
  return useQuery({
    queryKey: clientKeys.deliveries(clientId, status, page),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/webhooks/deliveries', {
          params: { query: { clientId, status, page, pageSize: 25 } },
          signal,
        }),
      ),
  });
}

export function useWebhookEvents(enabled = true) {
  return useQuery({
    queryKey: clientKeys.events,
    enabled,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/integration/webhooks/events', { signal })),
  });
}

export function useDeveloperPortal() {
  return useQuery({
    queryKey: clientKeys.portal,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/integration/portal', { signal })),
  });
}

/** The OData metadata document of a system as the viewer would read it (XML). */
export function usePortalMetadata(system: string | undefined) {
  return useQuery({
    queryKey: ['integration', 'portal', 'metadata', system],
    enabled: !!system,
    queryFn: async ({ signal }) => {
      const { data, error, response } = await api.GET('/api/v1/integration/portal/metadata/{system}', {
        params: { path: { system: system ?? '' } },
        parseAs: 'text',
        signal,
      });
      if (error !== undefined || !response.ok) throw new Error(String(response.status));
      return data;
    },
  });
}

export function useReplayDeliveries() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (deliveryIds: string[]) =>
      unwrap(await api.POST('/api/v1/integration/webhooks/deliveries/replay', { body: { deliveryIds } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['integration', 'deliveries'] }),
  });
}
