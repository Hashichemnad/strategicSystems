import { Plus, Trash2, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
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
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
} from '@/ui';
import {
  docregApi,
  isConflict,
  useDocRegMutation,
  useDocRegSettings,
  useRoleOptions,
  type DocRegSettings,
  type DocTypeRule,
  type SensitivityRule,
} from './api';
import { useLookupLabel } from './components';
import { SystemSettingsLinks } from '@/features/access/SystemSettingsLinks';

type FieldErrors = NonNullable<components['schemas']['Problem']['errors']>;

/** Settings → Document Registry (docs/design/requests.md §3.1): types, sensitivity clearance, reminders, watermark, organisation code. */
export function DocRegSettingsPage() {
  const { t } = useTranslation('docreg');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const doc = useDocRegSettings();
  const save = useDocRegMutation(docregApi.saveSettings);
  const [value, setValue] = useState<DocRegSettings>();
  const [errors, setErrors] = useState<FieldErrors>([]);
  useEffect(() => {
    if (doc.data?.value) setValue(doc.data.value);
  }, [doc.data]);

  if (doc.isError) return <StatusBanner tone="danger" title={errorMessage(tc, doc.error)} />;
  if (!value || !doc.data) return <Skeleton className="h-96 rounded-lg" />;

  const set = (patch: Partial<DocRegSettings>) => setValue((v) => ({ ...v, ...patch }));
  const errorFor = (field: string) => {
    const error = errors.find((e) => e.field === field || e.field === `settings.${field}`);
    return error ? translateMessageKey(tc, error.messageKey, error.params ?? undefined) : undefined;
  };
  const types = value.types ?? [];
  const sensitivities = value.sensitivities ?? [];
  const reminders = value.reminderDays ?? [];

  const submit = () => {
    setErrors([]);
    save.mutate(
      { settings: value, version: doc.data.version },
      {
        onSuccess: () => toast({ tone: 'success', title: t('settings.saved') }),
        onError: (e) => {
          if (isConflict(e)) {
            toast({ tone: 'error', title: t('settings.conflict') });
            void doc.refetch();
          } else if (e instanceof ApiError && e.problem.errors?.length) setErrors(e.problem.errors);
          else toast({ tone: 'error', title: errorMessage(tc, e) });
        },
      },
    );
  };

  return (
    <div className="flex flex-col gap-6" data-testid="docreg-settings">
      <PageHeader
        title={t('settings.title')}
        description={t('settings.description')}
        actions={
          <Button onClick={submit} loading={save.isPending} data-testid="docreg-settings-save">
            {tc('actions.save')}
          </Button>
        }
      />
      <SystemSettingsLinks system="docreg" />

      <section className="grid gap-4 rounded-lg border bg-card p-5 md:grid-cols-2">
        <h2 className="font-semibold md:col-span-2">{t('settings.general')}</h2>
        <FormField
          label={t('settings.organisationCode')}
          hint={t('settings.organisationCodeHint')}
          error={errorFor('organisationCode')}
          required
        >
          <Input
            value={value.organisationCode ?? ''}
            maxLength={10}
            dir="ltr"
            onChange={(e) => set({ organisationCode: e.target.value.toUpperCase() })}
          />
        </FormField>
        <Switch
          label={t('settings.publicLinks')}
          checked={!!value.publicLinks}
          onCheckedChange={(publicLinks) => set({ publicLinks })}
        />
      </section>

      <TypesSection types={types} onChange={(next) => set({ types: next })} errorFor={errorFor} />
      <SensitivitiesSection rules={sensitivities} onChange={(next) => set({ sensitivities: next })} />

      <section
        className="flex flex-col gap-3 rounded-lg border bg-card p-5"
        aria-labelledby="docreg-reminders"
      >
        <header>
          <h2 id="docreg-reminders" className="font-semibold">
            {t('settings.reminders')}
          </h2>
          <p className="text-sm text-muted-foreground">{t('settings.remindersHint')}</p>
        </header>
        <ul className="flex flex-wrap gap-3">
          {reminders.map((days, index) => (
            <li key={index} className="flex items-center gap-1">
              <Input
                type="number"
                min={1}
                max={365}
                className="w-24"
                aria-label={t('settings.reminderDays')}
                value={days}
                onChange={(e) =>
                  set({ reminderDays: reminders.map((d, i) => (i === index ? Number(e.target.value) : d)) })
                }
              />
              <Button
                variant="ghost"
                size="icon"
                aria-label={t('settings.removeReminder', { count: days })}
                onClick={() => set({ reminderDays: reminders.filter((_, i) => i !== index) })}
              >
                <Trash2 aria-hidden />
              </Button>
            </li>
          ))}
        </ul>
        {errorFor('reminderDays') && (
          <p role="alert" className="text-xs font-medium text-danger">
            {errorFor('reminderDays')}
          </p>
        )}
        <Button
          variant="outline"
          size="sm"
          className="w-fit"
          disabled={reminders.length >= 5}
          onClick={() => set({ reminderDays: [...reminders, 7] })}
        >
          <Plus aria-hidden />
          {t('settings.addReminder')}
        </Button>
      </section>

      <section className="grid gap-4 rounded-lg border bg-card p-5 md:grid-cols-2">
        <Switch
          className="md:col-span-2"
          label={t('settings.watermark')}
          description={t('settings.watermarkHint')}
          checked={!!value.watermark}
          onCheckedChange={(watermark) => set({ watermark })}
        />
        <FormField label={t('settings.watermarkAr')} error={errorFor('watermarkAr')}>
          <Input
            dir="rtl"
            lang="ar"
            maxLength={120}
            disabled={!value.watermark}
            value={value.watermarkAr ?? ''}
            onChange={(e) => set({ watermarkAr: e.target.value })}
          />
        </FormField>
        <FormField label={t('settings.watermarkEn')} error={errorFor('watermarkEn')}>
          <Input
            dir="ltr"
            lang="en"
            maxLength={120}
            disabled={!value.watermark}
            value={value.watermarkEn ?? ''}
            onChange={(e) => set({ watermarkEn: e.target.value })}
          />
        </FormField>
      </section>

      <div className="flex justify-end">
        <Button onClick={submit} loading={save.isPending}>
          {tc('actions.save')}
        </Button>
      </div>
    </div>
  );
}

/** Codes of a lookup that are not used yet, for "Add" (plus the row's own code). */
function useCodeOptions(type: string, used: string[], own?: string) {
  const { i18n } = useTranslation();
  const values = useLookupValues(type).data ?? [];
  const options = values
    .filter((v) => v.code === own || (v.active && !used.includes(v.code)))
    .map((v) => ({ value: v.code, label: pickText(i18n.language, v.labelAr, v.labelEn) || v.code }));
  if (own && !options.some((o) => o.value === own)) options.push({ value: own, label: own });
  return options;
}

function TypesSection({
  types,
  onChange,
  errorFor,
}: {
  types: DocTypeRule[];
  onChange: (types: DocTypeRule[]) => void;
  errorFor: (field: string) => string | undefined;
}) {
  const { t } = useTranslation('docreg');
  const label = useLookupLabel('docreg.type');
  const free = useCodeOptions(
    'docreg.type',
    types.map((r) => r.code),
  );
  const update = (index: number, patch: Partial<DocTypeRule>) =>
    onChange(types.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  const duplicate = errorFor('types');
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-labelledby="docreg-types">
      <header>
        <h2 id="docreg-types" className="font-semibold">
          {t('settings.types')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('settings.typesHint')}</p>
      </header>
      {duplicate && <StatusBanner tone="danger" title={duplicate} />}
      <Table data-testid="docreg-types">
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('fields.type')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.numberCode')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.validityRule')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.createsIp')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.ackRequired')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{t('settings.removeType', { name: '' })}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {types.map((rule, index) => {
            const numberError = errorFor(`types[${index}].numberCode`);
            const monthsError = errorFor(`types[${index}].validityMonths`);
            return (
              <TableRow key={`${rule.code}-${index}`}>
                <TableCell className="font-medium">{label(rule.code)}</TableCell>
                <TableCell>
                  <Input
                    className="w-24"
                    dir="ltr"
                    maxLength={10}
                    aria-label={`${t('settings.numberCode')} – ${label(rule.code)}`}
                    aria-invalid={numberError ? true : undefined}
                    value={rule.numberCode}
                    onChange={(e) => update(index, { numberCode: e.target.value.toUpperCase() })}
                  />
                  {numberError && <p className="mt-1 text-xs text-danger">{numberError}</p>}
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap items-center gap-2">
                    <Select
                      className="w-44"
                      aria-label={`${t('settings.validityRule')} – ${label(rule.code)}`}
                      value={rule.validityMonths == null ? 'superseded' : 'months'}
                      onValueChange={(v) => update(index, { validityMonths: v === 'months' ? 24 : null })}
                      options={[
                        { value: 'months', label: t('settings.byMonths') },
                        { value: 'superseded', label: t('settings.superseded') },
                      ]}
                    />
                    {rule.validityMonths != null && (
                      <Input
                        type="number"
                        min={1}
                        max={240}
                        className="w-24"
                        aria-label={`${t('settings.validityMonths')} – ${label(rule.code)}`}
                        aria-invalid={monthsError ? true : undefined}
                        value={rule.validityMonths}
                        onChange={(e) => update(index, { validityMonths: Number(e.target.value) })}
                      />
                    )}
                  </div>
                  {monthsError && <p className="mt-1 text-xs text-danger">{monthsError}</p>}
                </TableCell>
                <TableCell>
                  <Checkbox
                    label={
                      <span className="sr-only">{`${t('settings.createsIp')} – ${label(rule.code)}`}</span>
                    }
                    checked={rule.createsIp}
                    onChange={(e) => update(index, { createsIp: e.target.checked })}
                  />
                </TableCell>
                <TableCell>
                  <Checkbox
                    label={
                      <span className="sr-only">{`${t('settings.ackRequired')} – ${label(rule.code)}`}</span>
                    }
                    checked={rule.ackRequired}
                    onChange={(e) => update(index, { ackRequired: e.target.checked })}
                  />
                </TableCell>
                <TableCell>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={t('settings.removeType', { name: label(rule.code) })}
                    onClick={() => onChange(types.filter((_, i) => i !== index))}
                  >
                    <Trash2 aria-hidden />
                  </Button>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
      {free.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <Select
            className="w-56"
            aria-label={t('settings.addType')}
            placeholder={t('settings.addType')}
            value={undefined}
            onValueChange={(code) =>
              onChange([
                ...types,
                {
                  code,
                  numberCode: code.slice(0, 3).toUpperCase(),
                  validityMonths: null,
                  createsIp: false,
                  ackRequired: false,
                },
              ])
            }
            options={free}
          />
        </div>
      )}
    </section>
  );
}

function SensitivitiesSection({
  rules,
  onChange,
}: {
  rules: SensitivityRule[];
  onChange: (rules: SensitivityRule[]) => void;
}) {
  const { t } = useTranslation('docreg');
  const label = useLookupLabel('docreg.sensitivity');
  const free = useCodeOptions(
    'docreg.sensitivity',
    rules.map((r) => r.code),
  );
  const update = (index: number, patch: Partial<SensitivityRule>) =>
    onChange(rules.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-labelledby="docreg-sensitivities"
    >
      <header>
        <h2 id="docreg-sensitivities" className="font-semibold">
          {t('settings.sensitivities')}
        </h2>
        <p className="text-sm text-muted-foreground">{t('settings.sensitivitiesHint')}</p>
      </header>
      <Table data-testid="docreg-sensitivities">
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('fields.sensitivity')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.allEmployees')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.ownerUnit')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.roleKeys')}</TableHeaderCell>
            <TableHeaderCell>{t('settings.allowShareToAll')}</TableHeaderCell>
            <TableHeaderCell>
              <span className="sr-only">{t('settings.removeSensitivity', { name: '' })}</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rules.map((rule, index) => (
            <TableRow key={`${rule.code}-${index}`}>
              <TableCell className="font-medium">{label(rule.code)}</TableCell>
              <TableCell>
                <Checkbox
                  label={
                    <span className="sr-only">{`${t('settings.allEmployees')} – ${label(rule.code)}`}</span>
                  }
                  checked={rule.allEmployees}
                  onChange={(e) => update(index, { allEmployees: e.target.checked })}
                />
              </TableCell>
              <TableCell>
                <Checkbox
                  label={
                    <span className="sr-only">{`${t('settings.ownerUnit')} – ${label(rule.code)}`}</span>
                  }
                  checked={rule.ownerUnit}
                  disabled={rule.allEmployees}
                  onChange={(e) => update(index, { ownerUnit: e.target.checked })}
                />
              </TableCell>
              <TableCell className="min-w-56">
                <RoleKeys
                  value={rule.roleKeys}
                  disabled={rule.allEmployees}
                  onChange={(roleKeys) => update(index, { roleKeys })}
                />
              </TableCell>
              <TableCell>
                <Checkbox
                  label={
                    <span className="sr-only">{`${t('settings.allowShareToAll')} – ${label(rule.code)}`}</span>
                  }
                  checked={rule.allowShareToAll}
                  onChange={(e) => update(index, { allowShareToAll: e.target.checked })}
                />
              </TableCell>
              <TableCell>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={t('settings.removeSensitivity', { name: label(rule.code) })}
                  onClick={() => onChange(rules.filter((_, i) => i !== index))}
                >
                  <Trash2 aria-hidden />
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {free.length > 0 && (
        <Select
          className="w-56"
          aria-label={t('settings.addSensitivity')}
          placeholder={t('settings.addSensitivity')}
          value={undefined}
          onValueChange={(code) =>
            onChange([
              ...rules,
              { code, allEmployees: false, ownerUnit: true, roleKeys: [], allowShareToAll: false },
            ])
          }
          options={free}
        />
      )}
    </section>
  );
}

/** Multi-select of role keys (`<system>.<code>`): chips plus an "Add a role" list; a typed key when roles cannot be listed. */
function RoleKeys({
  value,
  onChange,
  disabled,
}: {
  value: string[];
  onChange: (value: string[]) => void;
  disabled?: boolean;
}) {
  const { t, i18n } = useTranslation('docreg');
  const roles = useRoleOptions();
  const [typed, setTyped] = useState('');
  const options = (roles.data ?? [])
    .filter((r) => r.active)
    .map((r) => ({
      value: `${r.systemCode}.${r.code}`,
      label: `${pickText(i18n.language, r.nameAr, r.nameEn) || r.code} (${r.systemCode}.${r.code})`,
    }));
  const name = (key: string) => {
    const role = roles.data?.find((r) => `${r.systemCode}.${r.code}` === key);
    return (role && pickText(i18n.language, role.nameAr, role.nameEn)) || key;
  };
  const free = options.filter((o) => !value.includes(o.value));
  return (
    <div className="flex flex-col gap-2">
      {value.length > 0 && (
        <ul className="flex flex-wrap gap-1">
          {value.map((key) => (
            <li key={key}>
              <StatusChip tone="info" className="gap-1 pe-1">
                <span title={key}>{name(key)}</span>
                <button
                  type="button"
                  className="rounded-full p-0.5 hover:bg-info/20"
                  aria-label={t('settings.removeRole', { name: name(key) })}
                  disabled={disabled}
                  onClick={() => onChange(value.filter((k) => k !== key))}
                >
                  <X className="size-3" aria-hidden />
                </button>
              </StatusChip>
            </li>
          ))}
        </ul>
      )}
      {roles.isError ? (
        <form
          className="flex gap-1"
          onSubmit={(e) => {
            e.preventDefault();
            const key = typed.trim();
            if (key && !value.includes(key)) onChange([...value, key]);
            setTyped('');
          }}
        >
          <Input
            dir="ltr"
            className="h-8"
            aria-label={t('settings.roleKeyManual')}
            placeholder={t('settings.roleKeyManual')}
            value={typed}
            disabled={disabled}
            onChange={(e) => setTyped(e.target.value)}
          />
          <Button type="submit" size="sm" variant="outline" disabled={disabled || !typed.trim()}>
            <Plus aria-hidden />
            <span className="sr-only">{t('settings.addRole')}</span>
          </Button>
        </form>
      ) : (
        free.length > 0 && (
          <Select
            className="h-8"
            aria-label={t('settings.addRole')}
            placeholder={t('settings.addRole')}
            value={undefined}
            disabled={disabled}
            onValueChange={(key) => onChange([...value, key])}
            options={free}
          />
        )
      )}
    </div>
  );
}
