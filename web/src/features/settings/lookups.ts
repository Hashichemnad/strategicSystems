import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { pickText } from '@/lib/format';
import type { SelectOption } from '@/ui';

/** Values of a Settings list (picklist) for dropdowns; any signed-in user may read them. */
export function useLookupValues(type: string) {
  return useQuery({
    queryKey: ['settings', 'lookups', type],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/settings/lookups/{type}/values', { params: { path: { type } }, signal })),
    staleTime: 5 * 60_000,
  });
}

/**
 * Dropdown options of a list: active values in their configured order, plus the record's current value when it is no
 * longer active (or a legacy free-text value), so editing a record never silently drops its value.
 */
export function useLookupOptions(
  type: string,
  current: string | null | undefined,
  emptyLabel: string,
): SelectOption[] {
  const { i18n } = useTranslation();
  const values = useLookupValues(type).data ?? [];
  const options = values
    .filter((value) => value.active || value.code === current)
    .map((value) => ({
      value: value.code,
      label: pickText(i18n.language, value.labelAr, value.labelEn) || value.code,
    }));
  if (current && !options.some((option) => option.value === current))
    options.push({ value: current, label: current });
  return [{ value: '-', label: emptyLabel }, ...options];
}

/** Codes of the features switched on in Settings → Features. */
export function useFeatureEnabled(code: string): boolean | undefined {
  const enabled = useQuery({
    queryKey: ['settings', 'features', 'enabled'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/settings/features/enabled', { signal })),
    staleTime: 60_000,
  });
  return enabled.data?.includes(code);
}
