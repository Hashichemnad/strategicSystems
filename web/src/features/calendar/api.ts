import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type BusinessHours = components['schemas']['BusinessHoursDto'];
export type Day = components['schemas']['DayDto'];
export type Holiday = components['schemas']['HolidayDto'];
export type HolidayInput = components['schemas']['HolidayInput'];
export type UnitAssignment = components['schemas']['UnitAssignmentDto'];
export type HolidayImportResult = components['schemas']['HolidayImportDto'];

export const calendarKeys = {
  all: ['calendar'] as const,
  hours: ['calendar', 'business-hours'] as const,
  units: ['calendar', 'units'] as const,
  holidays: (year: number) => ['calendar', 'holidays', year] as const,
};

export function useBusinessHours() {
  return useQuery({
    queryKey: calendarKeys.hours,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/calendar/business-hours', { signal })),
  });
}

export function useUnitAssignments() {
  return useQuery({
    queryKey: calendarKeys.units,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/calendar/units', { signal })),
  });
}

export function useHolidays(year: number) {
  return useQuery({
    queryKey: calendarKeys.holidays(year),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/calendar/holidays', { params: { query: { year } }, signal })),
  });
}

function useInvalidate() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: calendarKeys.all });
}

export function useSaveBusinessHours() {
  const onSuccess = useInvalidate();
  return useMutation({
    mutationFn: async ({
      id,
      ...body
    }: {
      id?: string;
      nameAr: string | null;
      nameEn: string | null;
      timeZone: string | null;
      days: Day[];
      version: number;
    }) =>
      id
        ? unwrap(await api.PUT('/api/v1/calendar/business-hours/{id}', { params: { path: { id } }, body }))
        : unwrap(await api.POST('/api/v1/calendar/business-hours', { body })),
    onSuccess,
  });
}

export function useBusinessHoursAction() {
  const onSuccess = useInvalidate();
  return useMutation({
    mutationFn: async ({
      record,
      action,
    }: {
      record: BusinessHours;
      action: 'make-default' | 'activate' | 'deactivate';
    }) => {
      const path =
        action === 'make-default'
          ? '/api/v1/calendar/business-hours/{id}/make-default'
          : action === 'activate'
            ? '/api/v1/calendar/business-hours/{id}/activate'
            : '/api/v1/calendar/business-hours/{id}/deactivate';
      return unwrap(
        await api.POST(path, { params: { path: { id: record.id } }, body: { version: record.version } }),
      );
    },
    onSuccess,
  });
}

export function useSetUnitBusinessHours() {
  const onSuccess = useInvalidate();
  return useMutation({
    mutationFn: async ({ unitId, businessHoursId }: { unitId: string; businessHoursId: string | null }) =>
      unwrap(
        await api.PUT('/api/v1/calendar/units/{unitId}', {
          params: { path: { unitId } },
          body: { businessHoursId },
        }),
      ),
    onSuccess,
  });
}

export function useCalculate() {
  return useMutation({
    mutationFn: async ({ id, start, hours }: { id: string; start: string; hours: number }) =>
      unwrap(
        await api.POST('/api/v1/calendar/business-hours/{id}/calculate', {
          params: { path: { id } },
          body: { start, hours },
        }),
      ),
  });
}

export function useSaveHoliday() {
  const onSuccess = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, ...body }: HolidayInput & { id?: string }) =>
      id
        ? unwrap(await api.PUT('/api/v1/calendar/holidays/{id}', { params: { path: { id } }, body }))
        : unwrap(await api.POST('/api/v1/calendar/holidays', { body })),
    onSuccess,
  });
}

export function useRemoveHoliday() {
  const onSuccess = useInvalidate();
  return useMutation({
    mutationFn: async (holiday: Holiday) =>
      unwrap(
        await api.POST('/api/v1/calendar/holidays/{id}/remove', {
          params: { path: { id: holiday.id } },
          body: { version: holiday.version },
        }),
      ),
    onSuccess,
  });
}

/** Multipart body for the typed client (the client middleware still adds antiforgery, correlation and channel). */
function fileForm(file: File) {
  const form = new FormData();
  form.append('file', file);
  return { body: form as unknown as { file: string }, bodySerializer: (body: unknown) => body as FormData };
}

export async function importHolidays(year: number, file: File, mode: 'validate' | 'commit') {
  return unwrap(
    await api.POST('/api/v1/calendar/holidays/import', {
      params: { query: { year, mode } },
      ...fileForm(file),
    }),
  );
}

export async function downloadImportReport(year: number, file: File) {
  const { data: blob } = await api.POST('/api/v1/calendar/holidays/import/report', {
    params: { query: { year } },
    ...fileForm(file),
    parseAs: 'blob',
  });
  if (!blob) return;
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `holidays-${year}-report.xlsx`;
  link.click();
  URL.revokeObjectURL(url);
}

/** ISO weekday (1 = Monday … 7 = Sunday) in the user's language. */
export function weekdayName(day: number, language: string): string {
  // 2 November 2026 is a Monday.
  return new Intl.DateTimeFormat(language === 'ar' ? 'ar-AE' : 'en-GB', {
    weekday: 'long',
    timeZone: 'UTC',
  }).format(new Date(Date.UTC(2026, 10, 1 + day)));
}
