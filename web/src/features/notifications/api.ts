import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type MyNotifications = components['schemas']['MyNotificationsDto'];
export type Notification = components['schemas']['NotificationDto'];
export type Preferences = components['schemas']['NotificationPreferencesDto'];
export type PreferencesInput = components['schemas']['NotificationPreferencesInput'];
export type Template = components['schemas']['TemplateDto'];
export type TemplateInput = components['schemas']['TemplateInput'];

export const notificationKeys = {
  mine: (page: number, unread: boolean) => ['notifications', 'mine', page, unread] as const,
  preferences: ['notifications', 'preferences'] as const,
  templates: ['notifications', 'templates'] as const,
  deliveries: (page: number) => ['notifications', 'deliveries', page] as const,
};

export function useMyNotifications(page = 1, unread = false) {
  return useQuery({
    queryKey: notificationKeys.mine(page, unread),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/notifications/mine', {
          params: { query: { page, pageSize: 20, unread } },
          signal,
        }),
      ),
    refetchInterval: 120_000, // fallback when the live connection is down
  });
}

export function useMarkRead() {
  const queryClient = useQueryClient();
  return useMutation({
    /** An item id, or null for "all". */
    mutationFn: async (id: string | null) => {
      if (id === null) unwrap(await api.POST('/api/v1/notifications/mine/read-all'));
      else unwrap(await api.POST('/api/v1/notifications/mine/{id}/read', { params: { path: { id } } }));
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications', 'mine'] }),
  });
}

/**
 * Live counter over SignalR (/hubs/notifications): the server pushes when a new item arrives; the list is then refreshed.
 * The client library is loaded on demand so the shell stays small.
 */
export function useLiveNotifications(enabled: boolean) {
  const queryClient = useQueryClient();
  useEffect(() => {
    if (!enabled) return;
    const state: { stopped: boolean; connection?: import('@microsoft/signalr').HubConnection } = {
      stopped: false,
    };
    void (async () => {
      const signalR = await import('@microsoft/signalr');
      if (state.stopped) return;
      const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/notifications', { withCredentials: true })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.None)
        .build();
      connection.on(
        'notification',
        () => void queryClient.invalidateQueries({ queryKey: ['notifications', 'mine'] }),
      );
      state.connection = connection;
      await connection.start().catch(() => undefined);
    })();
    return () => {
      state.stopped = true;
      void state.connection?.stop();
    };
  }, [enabled, queryClient]);
}

export function usePreferences() {
  return useQuery({
    queryKey: notificationKeys.preferences,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/notifications/preferences', { signal })),
  });
}

export function useSavePreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: PreferencesInput) =>
      unwrap(await api.PUT('/api/v1/notifications/preferences', { body })),
    onSuccess: (data) => queryClient.setQueryData(notificationKeys.preferences, data),
  });
}

export function useTemplates() {
  return useQuery({
    queryKey: notificationKeys.templates,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/notifications/templates', { signal })),
  });
}

export function useSaveTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ key, ...body }: TemplateInput & { key: string }) =>
      unwrap(await api.PUT('/api/v1/notifications/templates/{key}', { params: { path: { key } }, body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationKeys.templates }),
  });
}

export function useResetTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ key, version }: { key: string; version: number }) =>
      unwrap(
        await api.POST('/api/v1/notifications/templates/{key}/reset', {
          params: { path: { key } },
          body: { version },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationKeys.templates }),
  });
}

export function useDeliveries(page: number) {
  return useQuery({
    queryKey: notificationKeys.deliveries(page),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/notifications/deliveries', {
          params: { query: { page, pageSize: 25 } },
          signal,
        }),
      ),
  });
}
