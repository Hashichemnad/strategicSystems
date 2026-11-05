import { Link } from '@tanstack/react-router';
import { LayoutDashboard } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { Button, useToast } from '@/ui';
import type { ExportMenuProps, PeriodValue } from '@/ui/dashboard';

/** Level 3 system dashboard codes of the module dashboards (docs/design/reporting.md §3.3, §5). */
export type SystemDashboardCode =
  | 'portfolio.overview'
  | 'kpi.scorecard'
  | 'risk.profile'
  | 'teams.overview'
  | 'tasks.performance'
  | 'docreg.library'
  | 'ip.register'
  | 'bench.plan'
  | 'cap.closure'
  | 'auditmgmt.closure';

/** Downloads the reporting export of a dashboard (PDF / Excel) for a period (null = the dashboard's default). */
export async function downloadDashboardExport(
  key: string,
  format: 'pdf' | 'xlsx',
  period: PeriodValue | null,
  language: string,
  unit?: string,
): Promise<void> {
  const { data: blob } = await api.GET('/api/v1/reporting/dashboards/{key}/export', {
    params: {
      path: { key },
      query: {
        format,
        ...(period ? { kind: period.kind, from: period.from, to: period.to } : {}),
        ...(unit ? { unit } : {}),
        lang: language,
      },
    },
    parseAs: 'blob',
  });
  if (!blob) throw new Error('export failed');
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${key}.${format}`;
  a.click();
  URL.revokeObjectURL(url);
}

/**
 * The Level 2 → Level 3 bridge of a module dashboard: a "System dashboard" link to `/dashboards/<code>` with the same period, and the
 * reporting export menu (PDF / Excel) — each shown only with the reporting permission.
 */
export function useSystemDashboard(
  code: SystemDashboardCode,
  period: PeriodValue | null,
  unit?: string,
): { action: ReactNode; exports: Omit<ExportMenuProps, 'testId'> | undefined } {
  const { t, i18n } = useTranslation('charts');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const canRead = useCan('reporting.dashboard.read');
  const canExport = useCan('reporting.report.export');
  const action = canRead ? (
    <Button variant="outline" size="sm" asChild>
      <Link
        to="/dashboards/$key"
        params={{ key: code }}
        search={
          (period
            ? { kind: period.kind, from: period.from, to: period.to, compare: period.compare, unit }
            : { unit }) as never
        }
        data-testid="system-dashboard-link"
      >
        <LayoutDashboard aria-hidden />
        {t('systemDashboard')}
      </Link>
    </Button>
  ) : null;
  const exports = canExport
    ? {
        onPdf: () => downloadDashboardExport(code, 'pdf', period, i18n.language, unit),
        onExcel: () => downloadDashboardExport(code, 'xlsx', period, i18n.language, unit),
        onError: (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      }
    : undefined;
  return { action, exports };
}
