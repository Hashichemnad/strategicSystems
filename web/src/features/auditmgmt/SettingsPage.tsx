import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { KpiCardPicker } from '@/features/kpi/KpiCardPicker';
import {
  Button,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Switch,
  useToast,
  Section,
} from '@/ui';
import { auditApi, departmentScopes, useAuditMutation, useAuditSettings, type AuditSettings } from './api';
import { useAuditT } from './components';
import { SystemSettingsLinks } from '@/features/access/SystemSettingsLinks';

type CardKey =
  | 'kpiClosureRateCardId'
  | 'kpiOverdueCardId'
  | 'kpiPlanCompletionCardId'
  | 'kpiReportTimelinessCardId'
  | 'kpiSatisfactionCardId';
const cardKeys: CardKey[] = [
  'kpiClosureRateCardId',
  'kpiOverdueCardId',
  'kpiPlanCompletionCardId',
  'kpiReportTimelinessCardId',
  'kpiSatisfactionCardId',
];

/**
 * Settings → Audit management (docs/design/audit.md §2.1): department visibility (section only / department and below), optional department
 * notices, optional follow-up review, reminders, CAP observations, KPI publishing. The Layer 2 switch is in Settings → Features.
 */
export function AuditSettingsPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { k } = useAuditT();
  const toast = useToast();
  const doc = useAuditSettings(true);
  const save = useAuditMutation(auditApi.saveSettings);
  const [value, setValue] = useState<AuditSettings>();
  useEffect(() => {
    if (doc.data?.value) setValue(doc.data.value);
  }, [doc.data]);

  if (doc.isError) return <StatusBanner tone="danger" title={errorMessage(tc, doc.error)} />;
  if (!value || !doc.data) return <Skeleton className="h-64 rounded-lg" />;
  const set = (patch: Partial<AuditSettings>) => setValue((v) => (v ? { ...v, ...patch } : v));
  const num = (
    key: 'followUpReminderDays' | 'dueReminderDays' | 'reportTimelinessDays',
    min: number,
    max: number,
  ) => (
    <FormField label={t(`settings.${key}`)}>
      <Input
        type="number"
        min={min}
        max={max}
        value={String(value[key])}
        onChange={(e) => set({ [key]: Number(e.target.value) })}
        data-testid={`audit-setting-${key}`}
      />
    </FormField>
  );

  return (
    <div className="flex flex-col gap-6" data-testid="audit-settings">
      <PageHeader
        title={t('settings.title')}
        description={t('settings.description')}
        actions={
          <Button
            loading={save.isPending}
            data-testid="audit-settings-save"
            onClick={() =>
              save.mutate(
                { settings: value, version: doc.data.version },
                {
                  onSuccess: () => toast({ title: t('settings.saved') }),
                  onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                },
              )
            }
          >
            {t('common.save')}
          </Button>
        }
      />
      <SystemSettingsLinks system="auditmgmt" />
      <Section title={t('settings.visibility')}>
        <FormField
          label={t('settings.departmentScope')}
          hint={k(`settings.scopeHint.${value.departmentScope}`)}
        >
          <Select
            value={value.departmentScope}
            onValueChange={(departmentScope) => set({ departmentScope })}
            options={departmentScopes.map((s) => ({ value: s, label: k(`settings.scope.${s}`) }))}
          />
        </FormField>
        <FormField label={t('settings.departmentLevelCode')}>
          <Input
            value={value.departmentLevelCode}
            onChange={(e) => set({ departmentLevelCode: e.target.value })}
          />
        </FormField>
        <Switch
          checked={value.notifyDepartments ?? false}
          onCheckedChange={(notifyDepartments) => set({ notifyDepartments })}
          label={t('settings.notifyDepartments')}
        />
      </Section>
      <Section title={t('settings.followUp')}>
        <Switch
          checked={value.followUpReview ?? false}
          onCheckedChange={(followUpReview) => set({ followUpReview })}
          label={t('settings.followUpReview')}
          description={t('settings.followUpReviewHint')}
        />
        <div className="grid gap-3 md:grid-cols-3">
          {num('followUpReminderDays', 0, 60)}
          {num('dueReminderDays', 0, 90)}
          {num('reportTimelinessDays', 1, 365)}
        </div>
        <Switch
          checked={value.reminderWorkingDays ?? false}
          onCheckedChange={(reminderWorkingDays) => set({ reminderWorkingDays })}
          label={t('settings.reminderWorkingDays')}
        />
      </Section>
      <Section title={t('settings.integrations')}>
        <FormField label={t('settings.capObservations')}>
          <Select
            value={value.capObservations}
            onValueChange={(capObservations) => set({ capObservations })}
            options={['off', 'manual'].map((m) => ({ value: m, label: k(`settings.capMode.${m}`) }))}
          />
        </FormField>
        <p className="text-sm text-muted-foreground">{t('settings.kpiHint')}</p>
        <div className="grid gap-3 md:grid-cols-2">
          {cardKeys.map((key) => (
            <FormField key={key} label={t(`settings.${key}`)}>
              <KpiCardPicker
                value={value[key]}
                onChange={(id) => set({ [key]: id })}
                testId={`audit-setting-${key}`}
              />
            </FormField>
          ))}
        </div>
      </Section>
    </div>
  );
}
