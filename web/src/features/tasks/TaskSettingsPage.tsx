import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useLookupValues } from '@/features/settings/lookups';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Switch,
  useToast,
} from '@/ui';
import { useSaveTaskSettings, useTaskSettings, type TaskSettings } from './api';
import { SystemSettingsLinks } from '@/features/access/SystemSettingsLinks';
import { ResponsibilitiesLink } from '@/features/access/ResponsibilitiesLink';

/** Settings → Tasks (docs/design/tasks.md §2): templates per type, assignment rule, reminders, escalation, digest, feedback scale. */
export function TaskSettingsPage() {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const current = useTaskSettings();
  const save = useSaveTaskSettings();
  const types = useLookupValues('tasks.type').data ?? [];
  const categories = useLookupValues('tasks.category').data ?? [];
  const [draft, setDraft] = useState<TaskSettings>();
  const [error, setError] = useState<string>();
  useEffect(() => setDraft(current.data?.value ?? undefined), [current.data]);
  if (!draft || !current.data) return <Skeleton className="h-96" />;
  const set = (change: Partial<TaskSettings>) => setDraft({ ...draft, ...change });
  type NumberKey =
    | 'feedbackScaleMax'
    | 'reminderDaysBeforeDue'
    | 'escalateAfterOverdueDays'
    | 'acceptReminderHours'
    | 'workloadThreshold'
    | 'digestWeekday';
  const number = (key: NumberKey, min: number, max: number) => (
    <FormField label={t(`settings.${key}` as 'settings.feedbackScaleMax')}>
      <Input
        type="number"
        min={min}
        max={max}
        value={String(draft[key])}
        onChange={(e) => set({ [key]: Number(e.target.value) })}
      />
    </FormField>
  );
  return (
    <div className="flex flex-col gap-6">
      <ResponsibilitiesLink system="tasks" />
      <PageHeader title={t('settings.title')} description={t('settings.description')} />
      <SystemSettingsLinks system="tasks" />
      <form
        className="flex max-w-3xl flex-col gap-5 rounded-lg border bg-card p-5"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(
            { value: draft, version: current.data.version },
            {
              onSuccess: () => {
                setError(undefined);
                toast({ title: t('settings.saved') });
              },
              onError: (failure) => setError(errorMessage(tc, failure)),
            },
          );
        }}
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <p className="text-sm text-muted-foreground">{t('settings.lookupsHint')}</p>
        <fieldset className="flex flex-col gap-3">
          <legend className="mb-2 font-semibold">{t('settings.typeTemplates')}</legend>
          {types.map((type) => (
            <div key={type.code} className="grid items-center gap-2 sm:grid-cols-2">
              <span className="text-sm">{pickText(i18n.language, type.labelAr, type.labelEn)}</span>
              <Select
                aria-label={pickText(i18n.language, type.labelAr, type.labelEn)}
                value={draft.typeTemplates?.[type.code] ?? 'full'}
                onValueChange={(template) =>
                  set({ typeTemplates: { ...(draft.typeTemplates ?? {}), [type.code]: template } })
                }
                options={(['full', 'simple'] as const).map((value) => ({
                  value,
                  label: t(`settings.template.${value}`),
                }))}
              />
            </div>
          ))}
        </fieldset>
        <FormField label={t('settings.assignScope')}>
          <Select
            value={draft.assignScope}
            onValueChange={(assignScope) => set({ assignScope })}
            options={(['anyone', 'unit', 'direct-reports'] as const).map((value) => ({
              value,
              label: t(`settings.assignScopes.${value}`),
            }))}
          />
        </FormField>
        <Switch
          checked={draft.secretaryMayCreateOnBehalf}
          onCheckedChange={(secretaryMayCreateOnBehalf) => set({ secretaryMayCreateOnBehalf })}
          label={t('settings.secretaryMayCreateOnBehalf')}
        />
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">{t('settings.confidentialCategories')}</legend>
          {categories.map((category) => (
            <Checkbox
              key={category.code}
              label={pickText(i18n.language, category.labelAr, category.labelEn)}
              checked={(draft.confidentialCategories ?? []).includes(category.code)}
              onChange={(e) =>
                set({
                  confidentialCategories: e.target.checked
                    ? [...(draft.confidentialCategories ?? []), category.code]
                    : (draft.confidentialCategories ?? []).filter((c) => c !== category.code),
                })
              }
            />
          ))}
        </fieldset>
        <div className="grid gap-4 sm:grid-cols-2">
          {number('feedbackScaleMax', 3, 10)}
          {number('reminderDaysBeforeDue', 0, 30)}
          {number('escalateAfterOverdueDays', 0, 60)}
          {number('acceptReminderHours', 0, 240)}
          {number('workloadThreshold', 1, 500)}
        </div>
        <Switch
          checked={draft.overdueNotice}
          onCheckedChange={(overdueNotice) => set({ overdueNotice })}
          label={t('settings.overdueNotice')}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label={t('settings.digest')}>
            <Select
              value={draft.digest}
              onValueChange={(digest) => set({ digest })}
              options={(['off', 'daily', 'weekly'] as const).map((value) => ({
                value,
                label: t(`settings.digests.${value}`),
              }))}
            />
          </FormField>
          {draft.digest === 'weekly' && number('digestWeekday', 1, 7)}
        </div>
        <Button type="submit" className="self-end" loading={save.isPending}>
          {tc('actions.save')}
        </Button>
      </form>
    </div>
  );
}
