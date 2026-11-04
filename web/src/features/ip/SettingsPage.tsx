import { Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useLookupValues } from '@/features/settings/lookups';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  PageHeader,
  Skeleton,
  StatusBanner,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import {
  ipLookups,
  useIpSettings,
  useSaveIpSettings,
  type IpSettingsValue,
  type ProtectionRule,
} from './api';
import { actionError } from './components';

/** Settings → IP (docs/design/requests.md §4.1): term and internal-only per protection type, reminder months, MOET form header. */
export function IpSettingsPage() {
  const { t, i18n } = useTranslation('ip');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const doc = useIpSettings();
  const save = useSaveIpSettings();
  const protections = useLookupValues(ipLookups.protectionType).data ?? [];
  const [value, setValue] = useState<IpSettingsValue>();
  const [newMonth, setNewMonth] = useState('');
  const [error, setError] = useState<string>();
  useEffect(() => {
    if (doc.data) setValue(doc.data.value ?? {});
  }, [doc.data]);

  if (doc.isError) return <StatusBanner tone="danger" title={errorMessage(tc, doc.error)} />;
  if (!value || !doc.data) return <Skeleton className="h-64 rounded-lg" />;

  const rules = value.protections ?? [];
  const months = value.reminderMonths ?? [];
  const set = (patch: Partial<IpSettingsValue>) => setValue((v) => ({ ...v, ...patch }));
  // Every active protection type gets a row (a type without a rule uses the server default until saved).
  const codes = [
    ...new Set([...protections.filter((p) => p.active).map((p) => p.code), ...rules.map((r) => r.code)]),
  ];
  const ruleOf = (code: string): ProtectionRule =>
    rules.find((r) => r.code === code) ?? {
      code,
      termYears: null,
      internalOnly: false,
      termFrom: 'registration',
    };
  const setRule = (code: string, patch: Partial<ProtectionRule>) =>
    set({ protections: codes.map((c) => (c === code ? { ...ruleOf(c), ...patch } : ruleOf(c))) });
  const label = (code: string) => {
    const lookup = protections.find((p) => p.code === code);
    return (lookup && pickText(i18n.language, lookup.labelAr, lookup.labelEn)) || code;
  };
  const addMonth = () => {
    const month = Number(newMonth);
    if (!Number.isInteger(month) || month < 1 || month > 60 || months.length >= 5 || months.includes(month))
      return;
    set({ reminderMonths: [...months, month].sort((a, b) => b - a) });
    setNewMonth('');
  };

  const submit = () => {
    setError(undefined);
    save.mutate(
      { settings: { ...value, protections: codes.map(ruleOf) }, version: doc.data.version },
      {
        onSuccess: () => toast({ title: t('actions.saved') }),
        onError: (e) => setError(actionError(tc, e)),
      },
    );
  };

  return (
    <div className="flex flex-col gap-6" data-testid="ip-settings">
      <PageHeader
        title={t('settings.title')}
        description={t('settings.description')}
        actions={
          <Button onClick={submit} loading={save.isPending} data-testid="ip-settings-save">
            {tc('actions.save')}
          </Button>
        }
      />
      {error && <StatusBanner tone="danger" title={error} />}
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
        <h2 className="font-semibold">{t('settings.protections')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.protectionsHint')}</p>
        <div className="overflow-x-auto">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.protectionType')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.term')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.termFrom')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.noExpiry')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.internalOnly')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {codes.map((code) => {
                const rule = ruleOf(code);
                return (
                  <TableRow key={code} data-testid={`ip-protection-${code}`}>
                    <TableCell className="font-medium">{label(code)}</TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        min={1}
                        max={100}
                        className="w-24"
                        aria-label={t('settings.termFor', { name: label(code) })}
                        disabled={rule.termYears === null}
                        value={rule.termYears ?? ''}
                        onChange={(e) =>
                          setRule(code, { termYears: e.target.value === '' ? null : Number(e.target.value) })
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <Select
                        aria-label={t('settings.termFromFor', { name: label(code) })}
                        disabled={rule.termYears === null}
                        value={rule.termFrom}
                        onValueChange={(v) => setRule(code, { termFrom: v })}
                        options={[
                          { value: 'registration', label: t('settings.termFromRegistration') },
                          { value: 'publication', label: t('settings.termFromPublication') },
                        ]}
                      />
                    </TableCell>
                    <TableCell>
                      <Checkbox
                        label={
                          <span className="sr-only">{t('settings.noExpiryFor', { name: label(code) })}</span>
                        }
                        checked={rule.termYears === null}
                        onChange={(e) => setRule(code, { termYears: e.target.checked ? null : 20 })}
                      />
                    </TableCell>
                    <TableCell>
                      <Checkbox
                        label={
                          <span className="sr-only">
                            {t('settings.internalOnlyFor', { name: label(code) })}
                          </span>
                        }
                        checked={rule.internalOnly}
                        onChange={(e) => setRule(code, { internalOnly: e.target.checked })}
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      </section>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
        <h2 className="font-semibold">{t('settings.reminders')}</h2>
        <p className="text-sm text-muted-foreground">{t('settings.remindersHint')}</p>
        <ul className="flex flex-wrap gap-2" data-testid="ip-reminder-months">
          {months.map((m) => (
            <li key={m} className="flex items-center gap-1 rounded-md border px-2 py-1 text-sm">
              {t('settings.monthsBefore', { count: m, formatted: formatNumber(m, i18n.language) })}
              <Button
                variant="ghost"
                size="icon"
                className="size-7"
                aria-label={t('settings.removeMonth', { count: m })}
                onClick={() => set({ reminderMonths: months.filter((x) => x !== m) })}
              >
                <Trash2 aria-hidden />
              </Button>
            </li>
          ))}
        </ul>
        <div className="flex flex-wrap items-end gap-2">
          <FormField label={t('settings.addMonth')}>
            <Input
              type="number"
              min={1}
              max={60}
              className="w-28"
              value={newMonth}
              onChange={(e) => setNewMonth(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  addMonth();
                }
              }}
            />
          </FormField>
          <Button variant="outline" onClick={addMonth}>
            <Plus aria-hidden />
            {t('settings.add')}
          </Button>
        </div>
      </section>
      <section className="grid gap-4 rounded-lg border bg-card p-5 md:grid-cols-2">
        <h2 className="font-semibold md:col-span-2">{t('settings.moetHeader')}</h2>
        <FormField label={t('settings.moetHeaderAr')}>
          <Textarea
            dir="rtl"
            lang="ar"
            rows={3}
            value={value.moetHeaderAr ?? ''}
            onChange={(e) => set({ moetHeaderAr: e.target.value || null })}
          />
        </FormField>
        <FormField label={t('settings.moetHeaderEn')}>
          <Textarea
            dir="ltr"
            lang="en"
            rows={3}
            value={value.moetHeaderEn ?? ''}
            onChange={(e) => set({ moetHeaderEn: e.target.value || null })}
          />
        </FormField>
      </section>
    </div>
  );
}
