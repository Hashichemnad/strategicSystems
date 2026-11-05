import { RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { useLevels } from '@/features/org/api';
import { formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import { useRefreshStatus, useReportingSettings, useRequestRefresh, useSaveReportingSettings, type ReportingSettings } from './api';

/** Settings → Reporting (docs/design/reporting.md §9, ADR 0146): department level, fiscal year, refresh, delivery, scorecard limits. */
export function ReportingSettingsPage() {
  const { t, i18n } = useTranslation('reporting');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const doc = useReportingSettings();
  const save = useSaveReportingSettings();
  const levels = useLevels();
  const canRefresh = useCan('reporting.aggregate.refresh');
  const status = useRefreshStatus(canRefresh);
  const refresh = useRequestRefresh();
  const [value, setValue] = useState<ReportingSettings>();
  useEffect(() => {
    if (doc.data?.value) setValue(doc.data.value);
  }, [doc.data]);
  if (doc.isPending || !value) return <Skeleton className="h-96" />;
  const set = (patch: Partial<ReportingSettings>) => setValue({ ...value, ...patch });
  const months = Array.from({ length: 12 }, (_, i) => ({
    value: String(i + 1),
    label: new Intl.DateTimeFormat(i18n.language, { month: 'long' }).format(new Date(2026, i, 1)),
  }));
  return (
    <div className="flex flex-col gap-6" data-testid="reporting-settings">
      <PageHeader
        title={t('settings.title')}
        description={t('settings.description')}
        actions={
          <Button
            data-testid="reporting-settings-save"
            disabled={save.isPending}
            onClick={() =>
              save.mutate(
                { settings: value, version: doc.data?.version ?? 0 },
                { onSuccess: () => toast({ title: t('settings.saved') }), onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) },
              )
            }
          >
            {t('settings.save')}
          </Button>
        }
      />
      {save.isError && <StatusBanner tone="danger" title={errorMessage(tc, save.error)} />}
      <section className="grid gap-4 rounded-lg border bg-card p-4 md:grid-cols-3">
        <FormField label={t('settings.departmentLevel')} hint={t('settings.departmentLevelHint')}>
          <Select
            value={value.departmentLevelCode}
            onValueChange={(v) => set({ departmentLevelCode: v })}
            options={(levels.data ?? []).map((l) => ({ value: l.code, label: pickText(i18n.language, l.nameAr, l.nameEn) || l.code }))}
          />
        </FormField>
        <FormField label={t('settings.fiscalStart')}>
          <Select value={String(value.fiscalStartMonth)} onValueChange={(v) => set({ fiscalStartMonth: Number(v) })} options={months} />
        </FormField>
        <FormField label={t('settings.refreshMinutes')} hint={t('settings.refreshHint')}>
          <Input type="number" min={1} max={1440} value={value.refreshMinutes} onChange={(e) => set({ refreshMinutes: Number(e.target.value) })} />
        </FormField>
        <FormField label={t('settings.exportRowLimit')}>
          <Input type="number" min={100} max={500000} value={value.exportRowLimit} onChange={(e) => set({ exportRowLimit: Number(e.target.value) })} />
        </FormField>
        <FormField label={t('settings.attachmentLimit')}>
          <Input type="number" min={1} max={50} value={value.attachmentLimitMb} onChange={(e) => set({ attachmentLimitMb: Number(e.target.value) })} />
        </FormField>
        <FormField label={t('settings.retention')}>
          <Input type="number" min={1} max={365} value={value.deliveryRetentionDays} onChange={(e) => set({ deliveryRetentionDays: Number(e.target.value) })} />
        </FormField>
        <Switch
          checked={value.deliveryMode === 'attachment'}
          onCheckedChange={(checked) => set({ deliveryMode: checked ? 'attachment' : 'link' })}
          label={t('settings.attachments')}
          description={t('settings.attachmentsHint')}
        />
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">{t('settings.scorecard')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.scorecardHint')}</p>
        <div className="overflow-x-auto">
          <Table data-testid="scorecard-limits">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('settings.column')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.metric')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.green')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.amber')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.direction')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {value.scorecard.map((column, index) => {
                const update = (patch: Partial<typeof column>) =>
                  set({ scorecard: value.scorecard.map((c, i) => (i === index ? { ...c, ...patch } : c)) });
                return (
                  <TableRow key={column.code}>
                    <TableCell>{pickText(i18n.language, column.labelAr, column.labelEn)}</TableCell>
                    <TableCell className="font-mono text-xs">{column.metric}</TableCell>
                    <TableCell>
                      <Input
                        aria-label={`${t('settings.green')} ${column.labelEn}`}
                        type="number"
                        step="any"
                        className="w-24"
                        value={column.green}
                        onChange={(e) => update({ green: Number(e.target.value) })}
                      />
                    </TableCell>
                    <TableCell>
                      <Input
                        aria-label={`${t('settings.amber')} ${column.labelEn}`}
                        type="number"
                        step="any"
                        className="w-24"
                        value={column.amber}
                        onChange={(e) => update({ amber: Number(e.target.value) })}
                      />
                    </TableCell>
                    <TableCell>
                      <Select
                        aria-label={t('settings.direction')}
                        value={column.higherIsBetter ? 'higher' : 'lower'}
                        onValueChange={(v) => update({ higherIsBetter: v === 'higher' })}
                        options={[
                          { value: 'higher', label: t('settings.higherIsBetter') },
                          { value: 'lower', label: t('settings.lowerIsBetter') },
                        ]}
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      </section>
      {canRefresh && (
        <section className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold">{t('settings.refresh')}</h2>
            <Button variant="outline" onClick={() => refresh.mutate(undefined, { onSuccess: () => toast({ title: t('settings.refreshQueued') }) })}>
              <RefreshCw className="size-4" aria-hidden />
              {t('settings.refreshNow')}
            </Button>
          </div>
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('settings.system')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.asOf')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.facts')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.error')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(status.data ?? []).map((s) => (
                <TableRow key={s.systemCode}>
                  <TableCell>{s.systemCode}</TableCell>
                  <TableCell>{s.at ? formatDateTime(s.at, i18n.language) : '—'}</TableCell>
                  <TableCell className="tabular-nums">{s.facts}</TableCell>
                  <TableCell className="text-danger">{s.error ?? ''}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </section>
      )}
    </div>
  );
}
