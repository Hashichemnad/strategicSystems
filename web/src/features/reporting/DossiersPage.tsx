import { CalendarClock, FileDown, FileText, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogContent,
  DialogTrigger,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  useToast,
} from '@/ui';
import { useSaveSchedule, useSchedules } from './api';
import {
  renderDossier,
  renderDossierSet,
  saveBlob,
  useDossierRecords,
  useDossierTemplates,
  type ReportFilter,
  type UsableDossierTemplate,
} from './api2';
import { operatorsFor } from './ReportBuilderPage';
import { PrincipalListEditor, type PrincipalRef } from './shared';

type Language = 'ar' | 'en' | 'bilingual';

function FilterEditor({
  template,
  value,
  onChange,
}: {
  template: UsableDossierTemplate;
  value: ReportFilter[];
  onChange: (next: ReportFilter[]) => void;
}) {
  const { t, i18n } = useTranslation('reportBuilder');
  return (
    <div className="flex flex-col gap-2" data-testid="dossier-filters">
      {value.map((filter, index) => {
        const field = template.filterFields.find((f) => f.code === filter.field);
        const update = (patch: Partial<ReportFilter>) =>
          onChange(value.map((f, i) => (i === index ? { ...f, ...patch } : f)));
        return (
          <div key={index} className="grid gap-2 rounded border p-2 sm:grid-cols-4">
            <Select
              aria-label={t('builder.field')}
              value={filter.field || undefined}
              onValueChange={(v) =>
                update({
                  field: v,
                  operator:
                    operatorsFor(template.filterFields.find((f) => f.code === v)?.type ?? 'text')[0] ?? 'eq',
                  values: [],
                })
              }
              options={template.filterFields.map((f) => ({
                value: f.code,
                label: pickText(i18n.language, f.labelAr, f.labelEn),
              }))}
            />
            {field && (
              <Select
                aria-label={t('builder.operator')}
                value={filter.operator}
                onValueChange={(v) => update({ operator: v, values: v === 'relative' ? ['thisMonth'] : [] })}
                options={operatorsFor(field.type).map((op) => ({
                  value: op,
                  label: t(`operators.${op}` as 'operators.eq'),
                }))}
              />
            )}
            {field && !['empty', 'notEmpty'].includes(filter.operator) && (
              <Input
                aria-label={t('builder.value')}
                value={filter.values.join(filter.operator === 'in' ? ',' : '|')}
                onChange={(e) =>
                  update({
                    values: e.target.value.split(filter.operator === 'in' ? ',' : '|').map((v) => v.trim()),
                  })
                }
              />
            )}
            <Button size="sm" variant="ghost" onClick={() => onChange(value.filter((_, i) => i !== index))}>
              <Trash2 className="size-4" aria-hidden />
              {t('builder.remove')}
            </Button>
          </div>
        );
      })}
      <Button
        size="sm"
        variant="outline"
        className="self-start"
        onClick={() => onChange([...value, { field: '', operator: '', values: [] }])}
      >
        <Plus className="size-4" aria-hidden />
        {t('builder.addFilter')}
      </Button>
    </div>
  );
}

function LanguageSelect({ value, onChange }: { value: Language; onChange: (v: Language) => void }) {
  const { t } = useTranslation('reportBuilder');
  return (
    <FormField label={t('designer.language')}>
      <Select
        aria-label={t('designer.language')}
        value={value}
        onValueChange={(v) => onChange(v as Language)}
        options={(['ar', 'en', 'bilingual'] as const).map((l) => ({ value: l, label: t(`languages.${l}`) }))}
      />
    </FormField>
  );
}

/** One record's dossier from its record page (SPEC-ADDENDUM E3): template of the record type, language, PDF or Word. */
export function DossierButton({ dataset, recordId }: { dataset: string; recordId: string }) {
  const { t, i18n } = useTranslation('reportBuilder');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const canRun = useCan('reporting.dossier.run');
  const templates = useDossierTemplates();
  const usable = (templates.data ?? []).filter((x) => x.dataset === dataset);
  const [code, setCode] = useState<string>();
  const [language, setLanguage] = useState<Language>(i18n.language === 'en' ? 'en' : 'ar');
  const [busy, setBusy] = useState(false);
  if (!canRun || usable.length === 0) return null;
  const selected = code ?? usable[0]?.code;
  const download = async (format: 'pdf' | 'docx') => {
    if (!selected) return;
    setBusy(true);
    const { data, error } = await renderDossier(selected, recordId, format, language);
    setBusy(false);
    if (data) saveBlob(data, `${selected}.${format}`);
    else toast({ tone: 'error', title: errorMessage(tc, error) });
  };
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="outline" data-testid="dossier-open">
          <FileText className="size-4" aria-hidden />
          {t('dossiers.button')}
        </Button>
      </DialogTrigger>
      <DialogContent title={t('dossiers.button')} description={t('dossiers.buttonHint')}>
        <div className="flex flex-col gap-3" data-testid="dossier-dialog">
          <FormField label={t('dossiers.template')}>
            <Select
              aria-label={t('dossiers.template')}
              value={selected}
              onValueChange={setCode}
              options={usable.map((x) => ({
                value: x.code,
                label: pickText(i18n.language, x.labelAr, x.labelEn),
              }))}
            />
          </FormField>
          <LanguageSelect value={language} onChange={setLanguage} />
          <div className="flex gap-2">
            {(['pdf', 'docx'] as const).map((format) => (
              <Button
                key={format}
                disabled={busy}
                onClick={() => void download(format)}
                data-testid={`dossier-${format}`}
              >
                <FileDown className="size-4" aria-hidden />
                {t(`dossiers.${format}`)}
              </Button>
            ))}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Reports → Dossiers: one record, a filtered set (merged PDF or ZIP), and scheduled dossiers (ADRs 0184–0186). */
export function DossiersPage() {
  const { t, i18n } = useTranslation('reportBuilder');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const templates = useDossierTemplates();
  const canSchedule = useCan('reporting.schedule.manage');
  const schedules = useSchedules(canSchedule);
  const saveSchedule = useSaveSchedule();
  const [code, setCode] = useState<string>();
  const [query, setQuery] = useState('');
  const [record, setRecord] = useState<string>();
  const [language, setLanguage] = useState<Language>(i18n.language === 'en' ? 'en' : 'ar');
  const [filters, setFilters] = useState<ReportFilter[]>([]);
  const [setFormat, setSetFormat] = useState<'pdf' | 'zip-pdf' | 'zip-docx'>('pdf');
  const [busy, setBusy] = useState(false);
  const [scheduleName, setScheduleName] = useState('');
  const [recipientField, setRecipientField] = useState<string>('-');
  const [recipients, setRecipients] = useState<PrincipalRef[]>([]);
  const [day, setDay] = useState('1');
  const template = templates.data?.find((x) => x.code === code) ?? templates.data?.[0];
  const records = useDossierRecords(template?.code, query);
  const text = (ar?: string | null, en?: string | null) => pickText(i18n.language, ar ?? '', en ?? '');
  const valid = filters.filter((f) => f.field && f.operator);

  if (templates.isPending) return <Skeleton className="h-96" />;
  if (!template) return <EmptyState title={t('dossiers.none')} />;
  const one = async (format: 'pdf' | 'docx') => {
    if (!record) return;
    setBusy(true);
    const { data, error } = await renderDossier(template.code, record, format, language);
    setBusy(false);
    if (data) saveBlob(data, `${template.code}.${format}`);
    else toast({ tone: 'error', title: errorMessage(tc, error) });
  };
  const bulk = async () => {
    setBusy(true);
    const { data, error } = await renderDossierSet(template.code, valid, setFormat, language);
    setBusy(false);
    if (data) saveBlob(data, setFormat === 'pdf' ? 'dossiers.pdf' : 'dossiers.zip');
    else toast({ tone: 'error', title: errorMessage(tc, error) });
  };
  const employeeFields = template.filterFields.filter((f) => f.type === 'employee');
  const dossierSchedules = (schedules.data ?? []).filter((s) => s.targetKind === 'dossier');
  return (
    <div className="flex flex-col gap-6" data-testid="dossiers-page">
      <PageHeader title={t('dossiers.title')} description={t('dossiers.description')} />
      <div className="grid gap-3 sm:grid-cols-2">
        <FormField label={t('dossiers.template')}>
          <Select
            aria-label={t('dossiers.template')}
            value={template.code}
            onValueChange={(v) => {
              setCode(v);
              setRecord(undefined);
              setFilters([]);
            }}
            options={(templates.data ?? []).map((x) => ({
              value: x.code,
              label: text(x.labelAr, x.labelEn),
            }))}
          />
        </FormField>
        <LanguageSelect value={language} onChange={setLanguage} />
      </div>
      <Tabs defaultValue="one">
        <TabsList>
          <TabsTrigger value="one">{t('dossiers.one')}</TabsTrigger>
          <TabsTrigger value="set">{t('dossiers.set')}</TabsTrigger>
          {canSchedule && <TabsTrigger value="schedule">{t('dossiers.schedule')}</TabsTrigger>}
        </TabsList>
        <TabsContent value="one">
          <div className="flex flex-col gap-3 rounded-lg border bg-card p-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <FormField label={t('dossiers.search')}>
                <Input value={query} onChange={(e) => setQuery(e.target.value)} />
              </FormField>
              <FormField label={t('dossiers.record')}>
                <Select
                  aria-label={t('dossiers.record')}
                  value={record}
                  placeholder={t('dossiers.chooseRecord')}
                  onValueChange={setRecord}
                  options={(records.data ?? []).map((r) => ({
                    value: r.id,
                    label: `${r.number ?? ''} ${text(r.nameAr, r.nameEn)}`.trim(),
                  }))}
                />
              </FormField>
            </div>
            <div className="flex gap-2">
              {(['pdf', 'docx'] as const).map((format) => (
                <Button
                  key={format}
                  disabled={!record || busy}
                  onClick={() => void one(format)}
                  data-testid={`dossier-one-${format}`}
                >
                  <FileDown className="size-4" aria-hidden />
                  {t(`dossiers.${format}`)}
                </Button>
              ))}
            </div>
          </div>
        </TabsContent>
        <TabsContent value="set">
          <div className="flex flex-col gap-3 rounded-lg border bg-card p-4">
            <p className="text-sm text-muted-foreground">{t('dossiers.setHint')}</p>
            <FilterEditor template={template} value={filters} onChange={setFilters} />
            <div className="flex flex-wrap items-end gap-2">
              <FormField label={t('dossiers.format')}>
                <Select
                  aria-label={t('dossiers.format')}
                  value={setFormat}
                  onValueChange={(v) => setSetFormat(v as typeof setFormat)}
                  options={(['pdf', 'zip-pdf', 'zip-docx'] as const).map((f) => ({
                    value: f,
                    label: t(`dossiers.formats.${f}`),
                  }))}
                />
              </FormField>
              <Button disabled={busy} onClick={() => void bulk()} data-testid="dossier-set-run">
                <FileDown className="size-4" aria-hidden />
                {t('dossiers.generate')}
              </Button>
            </div>
          </div>
        </TabsContent>
        {canSchedule && (
          <TabsContent value="schedule">
            <div className="flex flex-col gap-3 rounded-lg border bg-card p-4" data-testid="dossier-schedule">
              {saveSchedule.isError && (
                <StatusBanner tone="danger" title={errorMessage(tc, saveSchedule.error)} />
              )}
              <div className="grid gap-3 sm:grid-cols-3">
                <FormField label={t('dossiers.scheduleName')}>
                  <Input
                    value={scheduleName}
                    onChange={(e) => setScheduleName(e.target.value)}
                    maxLength={200}
                  />
                </FormField>
                <FormField label={t('dossiers.dayOfMonth')}>
                  <Select
                    aria-label={t('dossiers.dayOfMonth')}
                    value={day}
                    onValueChange={setDay}
                    options={Array.from({ length: 28 }, (_, i) => ({
                      value: String(i + 1),
                      label: String(i + 1),
                    }))}
                  />
                </FormField>
                <FormField label={t('dossiers.format')}>
                  <Select
                    aria-label={t('dossiers.format')}
                    value={setFormat}
                    onValueChange={(v) => setSetFormat(v as typeof setFormat)}
                    options={(['pdf', 'zip-pdf', 'zip-docx'] as const).map((f) => ({
                      value: f,
                      label: t(`dossiers.formats.${f}`),
                    }))}
                  />
                </FormField>
              </div>
              <FilterEditor template={template} value={filters} onChange={setFilters} />
              <FormField label={t('dossiers.recipientField')} hint={t('dossiers.recipientFieldHint')}>
                <Select
                  aria-label={t('dossiers.recipientField')}
                  value={recipientField}
                  onValueChange={setRecipientField}
                  options={[
                    { value: '-', label: t('dossiers.fixedRecipients') },
                    ...employeeFields.map((f) => ({ value: f.code, label: text(f.labelAr, f.labelEn) })),
                  ]}
                />
              </FormField>
              {recipientField === '-' && <PrincipalListEditor value={recipients} onChange={setRecipients} />}
              <Button
                className="self-start"
                disabled={
                  !scheduleName.trim() ||
                  (recipientField === '-' && recipients.length === 0) ||
                  saveSchedule.isPending
                }
                data-testid="dossier-schedule-save"
                onClick={() =>
                  saveSchedule.mutate(
                    {
                      input: {
                        name: scheduleName.trim(),
                        targetKind: 'dossier',
                        targetId: template.id,
                        frequency: 'monthly',
                        time: '07:00',
                        day: Number(day),
                        periodRule: 'previous-month',
                        format: setFormat,
                        active: true,
                        recipients:
                          recipientField === '-' ? recipients.map((r) => ({ kind: r.kind, ref: r.ref })) : [],
                        dossier: {
                          filters: valid,
                          language: language,
                          recipientField: recipientField === '-' ? null : recipientField,
                        },
                      },
                    },
                    { onSuccess: () => toast({ title: t('dossiers.scheduled') }) },
                  )
                }
              >
                <CalendarClock className="size-4" aria-hidden />
                {t('dossiers.saveSchedule')}
              </Button>
              {dossierSchedules.length > 0 && (
                <Table data-testid="dossier-schedule-list">
                  <TableHead>
                    <TableRow>
                      <TableHeaderCell>{t('dossiers.scheduleName')}</TableHeaderCell>
                      <TableHeaderCell>{t('dossiers.recipientField')}</TableHeaderCell>
                      <TableHeaderCell>{t('dossiers.nextRun')}</TableHeaderCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {dossierSchedules.map((s) => (
                      <TableRow key={s.id}>
                        <TableCell>{s.name}</TableCell>
                        <TableCell>
                          {s.dossier?.recipientField
                            ? text(
                                template.filterFields.find((f) => f.code === s.dossier?.recipientField)
                                  ?.labelAr,
                                template.filterFields.find((f) => f.code === s.dossier?.recipientField)
                                  ?.labelEn,
                              ) || s.dossier.recipientField
                            : t('dossiers.fixedRecipients')}
                        </TableCell>
                        <TableCell dir="ltr">
                          {s.nextRunAt ? new Date(s.nextRunAt).toLocaleString(i18n.language) : '—'}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </div>
          </TabsContent>
        )}
      </Tabs>
    </div>
  );
}
