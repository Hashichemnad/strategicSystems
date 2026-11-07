import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import {
  Button,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Switch,
  Textarea,
  useToast,
  Section,
} from '@/ui';
import { capApi, raiseModes, useCapMutation, useCapSettings, type CapSettings } from './api';
import { useCapT } from './components';
import { SystemSettingsLinks } from '@/features/access/SystemSettingsLinks';

/** Settings → Corrective action plan (docs/design/cap.md §2.1): every threshold is configuration with a seeded default. */
export function CapSettingsPage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { k } = useCapT();
  const toast = useToast();
  const doc = useCapSettings(true);
  const save = useCapMutation(capApi.saveSettings);
  const [value, setValue] = useState<CapSettings>();
  useEffect(() => {
    if (doc.data?.value) setValue(doc.data.value);
  }, [doc.data]);

  if (doc.isError) return <StatusBanner tone="danger" title={errorMessage(tc, doc.error)} />;
  if (!value || !doc.data) return <Skeleton className="h-64 rounded-lg" />;
  const set = (patch: Partial<CapSettings>) => setValue((v) => (v ? { ...v, ...patch } : v));
  const num = (key: keyof CapSettings, min: number, max: number) => (
    <FormField label={t(`settings.${key}`)}>
      <Input
        type="number"
        min={min}
        max={max}
        value={String(value[key])}
        onChange={(e) => set({ [key]: Number(e.target.value) })}
        data-testid={`cap-setting-${key}`}
      />
    </FormField>
  );
  const mode = (key: 'kpiMissMode' | 'riskEventMode' | 'riskControlMode') => (
    <FormField label={t(`settings.${key}`)}>
      <Select
        value={value[key]}
        onValueChange={(v) => set({ [key]: v })}
        options={raiseModes.map((m) => ({ value: m, label: k(`raiseMode.${m}`) }))}
      />
    </FormField>
  );

  return (
    <div className="flex flex-col gap-6" data-testid="cap-settings">
      <PageHeader
        title={t('settings.title')}
        description={t('settings.description')}
        actions={
          <Button
            loading={save.isPending}
            data-testid="cap-settings-save"
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
      <SystemSettingsLinks system="cap" />
      <Section title={t('settings.dueDays')}>
        <div className="grid gap-3 md:grid-cols-4">
          {num('dueDaysCritical', 1, 730)}
          {num('dueDaysHigh', 1, 730)}
          {num('dueDaysMedium', 1, 730)}
          {num('dueDaysLow', 1, 730)}
        </div>
        <Switch
          checked={value.countWorkingDays ?? false}
          onCheckedChange={(countWorkingDays) => set({ countWorkingDays })}
          label={t('settings.countWorkingDays')}
          description={t('settings.countWorkingDaysHint')}
        />
      </Section>
      <Section title={t('settings.responses')}>
        <Switch
          checked={value.rejectionNeedsPmoConfirmation ?? false}
          onCheckedChange={(rejectionNeedsPmoConfirmation) => set({ rejectionNeedsPmoConfirmation })}
          label={t('settings.rejectionNeedsPmoConfirmation')}
          description={t('settings.rejectionNeedsPmoConfirmationHint')}
        />
        <Switch
          checked={value.reassignWithinUnit ?? false}
          onCheckedChange={(reassignWithinUnit) => set({ reassignWithinUnit })}
          label={t('settings.reassignWithinUnit')}
        />
        <FormField label={t('settings.rootCauseRule')} hint={t('settings.rootCauseRuleHint')}>
          <Textarea
            dir="ltr"
            className="font-mono text-sm"
            value={value.rootCauseRule}
            onChange={(e) => set({ rootCauseRule: e.target.value })}
          />
        </FormField>
      </Section>
      <Section title={t('settings.monitoring')}>
        <div className="grid gap-3 md:grid-cols-3">
          {num('monthlyReminderDay', 1, 28)}
          {num('escalatePmoWorkingDays', 0, 60)}
          {num('effectivenessMonths', 1, 24)}
        </div>
        <Switch
          checked={value.effectivenessVerification ?? false}
          onCheckedChange={(effectivenessVerification) => set({ effectivenessVerification })}
          label={t('settings.effectivenessVerification')}
        />
        <Switch
          checked={value.mirrorProjectProgress ?? false}
          onCheckedChange={(mirrorProjectProgress) => set({ mirrorProjectProgress })}
          label={t('settings.mirrorProjectProgress')}
        />
      </Section>
      <Section title={t('settings.integrations')}>
        <div className="grid gap-3 md:grid-cols-4">
          {mode('kpiMissMode')}
          {num('kpiMissConsecutive', 1, 12)}
          {mode('riskEventMode')}
          {mode('riskControlMode')}
        </div>
      </Section>
    </div>
  );
}
