import { ArrowDown, ArrowUp, FileDown, Plus, Save, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusChip,
  Switch,
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
  renderDossier,
  saveBlob,
  useAdminDossierTemplates,
  useDossierRecords,
  useReportable,
  useSaveDossierTemplate,
  type DossierDefinition,
  type DossierSection,
  type DossierTemplate,
  type ReportableMetadata,
} from './api2';

export const sectionKinds = [
  'fields',
  'text',
  'related',
  'chart',
  'trend',
  'gantt',
  'matrix',
  'approvals',
  'attachments',
] as const;
const levelAliases = ['a', 'b', 'c', 'd'];

/** The list with the items at <code>i</code> and <code>j</code> exchanged (section order). */
export function swap<T>(items: readonly T[], i: number, j: number): T[] {
  const next = [...items];
  const a = next[i];
  const b = next[j];
  if (a === undefined || b === undefined) return next;
  next[i] = b;
  next[j] = a;
  return next;
}

interface Draft {
  id?: string;
  version?: number;
  code: string;
  dataset: string;
  labelAr: string;
  labelEn: string;
  active: boolean;
  definition: DossierDefinition;
}

/** The objects a section reaches: the record's type, then each relationship of the path (aliases a, b, c, d). */
export function sectionLevels(
  dataset: string,
  path: readonly string[],
  metadata: ReportableMetadata | undefined,
) {
  const levels: { alias: string; dataset: string }[] = [{ alias: 'a', dataset }];
  for (const code of path) {
    const r = metadata?.relationships.find(
      (x) => x.code === code && x.parentDataset === levels[levels.length - 1]?.dataset,
    );
    if (!r || levels.length >= levelAliases.length) break;
    levels.push({ alias: levelAliases[levels.length] ?? 'z', dataset: r.childDataset });
  }
  return levels;
}

/**
 * Settings → Reporting → Dossier templates (SPEC-ADDENDUM E3, ADR 0184): cover, header / footer, language and ordered sections bound to the record or
 * to related objects (lists, charts, KPI trend, Gantt, risk matrix), approval history and attachments, with conditions and page breaks; preview as
 * PDF or Word for a record.
 */
export function DossierTemplatesPage() {
  const { t, i18n } = useTranslation('reportBuilder');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const templates = useAdminDossierTemplates();
  const metadata = useReportable('dossiers');
  const [draft, setDraft] = useState<Draft | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [previewQuery, setPreviewQuery] = useState('');
  const [previewRecord, setPreviewRecord] = useState<string>();
  const save = useSaveDossierTemplate(draft?.id);
  const records = useDossierRecords(draft?.id ? draft.code : undefined, previewQuery);
  const datasets = useMemo(
    () => new Map((metadata.data?.datasets ?? []).map((d) => [d.code, d])),
    [metadata.data],
  );
  const text = (ar?: string | null, en?: string | null) => pickText(i18n.language, ar ?? '', en ?? '');

  if (templates.isPending || metadata.isPending) return <Skeleton className="h-96" />;
  const toDraft = (x: DossierTemplate): Draft => ({
    id: x.id,
    version: x.version,
    code: x.code,
    dataset: x.dataset,
    labelAr: x.labelAr,
    labelEn: x.labelEn,
    active: x.active,
    definition: structuredClone(x.definition),
  });
  const sections = draft?.definition.sections ?? [];
  const setDefinition = (patch: Partial<DossierDefinition>) =>
    draft && setDraft({ ...draft, definition: { ...draft.definition, ...patch } });
  const setSections = (next: DossierSection[]) => setDefinition({ sections: next });
  const updateSection = (index: number, patch: Partial<DossierSection>) =>
    setSections(sections.map((s, i) => (i === index ? { ...s, ...patch } : s)));
  const fieldOptions = (path: readonly string[], filter?: (type: string) => boolean) =>
    sectionLevels(draft?.dataset ?? '', path, metadata.data).flatMap((level) => {
      const d = datasets.get(level.dataset);
      return (d?.fields ?? [])
        .filter((f) => f.type !== 'reference' && (!filter || filter(f.type)))
        .map((f) => ({
          value: `${level.alias}.${f.code}`,
          label: `${text(d?.labelAr, d?.labelEn)} › ${text(f.labelAr, f.labelEn)}`,
        }));
    });
  const download = async (format: 'pdf' | 'docx', language: string) => {
    if (!draft || !previewRecord) return;
    const { data, error } = await renderDossier(draft.code, previewRecord, format, language);
    if (data) saveBlob(data, `${draft.code}-${language}.${format}`);
    else toast({ tone: 'error', title: errorMessage(tc, error) });
  };

  const binding = (section: DossierSection, index: number) => {
    const path = section.path ?? [];
    const lastDataset = sectionLevels(draft?.dataset ?? '', path, metadata.data).at(-1)?.dataset;
    const next = (metadata.data?.relationships ?? []).filter((r) => r.parentDataset === lastDataset);
    const single = (key: keyof DossierSection, labelKey: string, filter?: (type: string) => boolean) => (
      <Select
        aria-label={t(labelKey as 'designer.field')}
        value={(section[key] as string | null | undefined) ?? undefined}
        placeholder={t(labelKey as 'designer.field')}
        onValueChange={(v) => updateSection(index, { [key]: v })}
        options={fieldOptions(path, filter)}
      />
    );
    const pathEditor = (
      <div className="flex flex-wrap items-center gap-2" data-testid="dossier-section-path">
        <span className="text-sm">{t('designer.path')}:</span>
        {path.map((code) => {
          const r = metadata.data?.relationships.find((x) => x.code === code);
          return (
            <StatusChip key={code} tone="info">
              {r ? text(r.labelAr, r.labelEn) : code}
            </StatusChip>
          );
        })}
        {path.length < 3 && next.length > 0 && (
          <Select
            aria-label={t('designer.addRelationship')}
            value={undefined}
            placeholder={t('designer.addRelationship')}
            onValueChange={(v) => updateSection(index, { path: [...path, v] })}
            options={next.map((r) => ({ value: r.code, label: text(r.labelAr, r.labelEn) }))}
          />
        )}
        {path.length > 0 && (
          <Button size="sm" variant="ghost" onClick={() => updateSection(index, { path: path.slice(0, -1) })}>
            {t('designer.back')}
          </Button>
        )}
      </div>
    );
    const multi = (key: 'fields' | 'measures', filter?: (type: string) => boolean, rootOnly = false) => (
      <div className="grid max-h-56 gap-1 overflow-y-auto rounded border p-2 sm:grid-cols-2">
        {fieldOptions(rootOnly ? [] : path, filter)
          .filter((o) => !rootOnly || o.value.startsWith('a.'))
          .map((o) => (
            <Checkbox
              key={o.value}
              label={o.label}
              checked={(section[key] ?? []).includes(o.value)}
              onChange={(e) => {
                const current = section[key] ?? [];
                updateSection(index, {
                  [key]: e.target.checked ? [...current, o.value] : current.filter((x) => x !== o.value),
                });
              }}
            />
          ))}
      </div>
    );
    const numeric = (type: string) => ['number', 'money', 'ratio'].includes(type);
    const dates = (type: string) => ['date', 'dateTime'].includes(type);
    switch (section.kind) {
      case 'fields':
        return multi('fields', undefined, true);
      case 'text':
        return (
          <div className="grid gap-2 sm:grid-cols-2">
            <Textarea
              dir="rtl"
              aria-label={t('designer.textAr')}
              value={section.textAr ?? ''}
              onChange={(e) => updateSection(index, { textAr: e.target.value })}
            />
            <Textarea
              dir="ltr"
              aria-label={t('designer.textEn')}
              value={section.textEn ?? ''}
              onChange={(e) => updateSection(index, { textEn: e.target.value })}
            />
          </div>
        );
      case 'related':
        return (
          <>
            {pathEditor}
            {multi('fields')}
          </>
        );
      case 'chart':
        return (
          <>
            {pathEditor}
            <div className="grid gap-2 sm:grid-cols-3">
              <Select
                aria-label={t('chart.type')}
                value={section.chart?.type ?? 'bar'}
                onValueChange={(v) =>
                  updateSection(index, {
                    chart: { category: '', aggregate: 'sum', ...section.chart, type: v },
                  })
                }
                options={(['bar', 'stackedBar', 'line', 'pie', 'donut'] as const).map((c) => ({
                  value: c,
                  label: t(`chart.types.${c}`),
                }))}
              />
              <Select
                aria-label={t('chart.category')}
                value={section.chart?.category || undefined}
                placeholder={t('chart.category')}
                onValueChange={(v) =>
                  updateSection(index, {
                    chart: { type: 'bar', aggregate: 'sum', ...section.chart, category: v },
                  })
                }
                options={fieldOptions(path)}
              />
              <Select
                aria-label={t('chart.measure')}
                value={section.chart?.measure ?? '-'}
                onValueChange={(v) =>
                  updateSection(index, {
                    chart: {
                      type: 'bar',
                      category: '',
                      aggregate: 'sum',
                      ...section.chart,
                      measure: v === '-' ? null : v,
                    },
                  })
                }
                options={[{ value: '-', label: t('chart.count') }, ...fieldOptions(path, numeric)]}
              />
            </div>
          </>
        );
      case 'trend':
        return (
          <>
            {pathEditor}
            {single('category', 'designer.category')}
            {multi('measures', numeric)}
          </>
        );
      case 'gantt':
        return (
          <>
            {pathEditor}
            <div className="grid gap-2 sm:grid-cols-2">
              {single('labelAr', 'designer.labelAr')}
              {single('labelEn', 'designer.labelEn')}
              {single('start', 'designer.start', dates)}
              {single('end', 'designer.end', dates)}
              {single('actualStart', 'designer.actualStart', dates)}
              {single('actualEnd', 'designer.actualEnd', dates)}
            </div>
          </>
        );
      case 'matrix':
        return (
          <>
            {pathEditor}
            <div className="grid gap-2 sm:grid-cols-3">
              {single('likelihood', 'designer.likelihood', numeric)}
              {single('impact', 'designer.impact', numeric)}
              <Input
                type="number"
                min={3}
                max={7}
                aria-label={t('designer.size')}
                value={section.size ?? 5}
                onChange={(e) => updateSection(index, { size: Number(e.target.value) })}
              />
            </div>
          </>
        );
      case 'approvals':
        return (
          <FormField label={t('designer.resourceType')}>
            <Input
              dir="ltr"
              value={section.resourceType ?? ''}
              onChange={(e) => updateSection(index, { resourceType: e.target.value })}
            />
          </FormField>
        );
      case 'attachments':
        return (
          <FormField label={t('designer.ownerType')}>
            <Input
              dir="ltr"
              value={section.ownerType ?? ''}
              onChange={(e) => updateSection(index, { ownerType: e.target.value })}
            />
          </FormField>
        );
      default:
        return null;
    }
  };

  return (
    <div className="flex flex-col gap-6" data-testid="dossier-templates-page">
      <PageHeader
        title={t('designer.title')}
        description={t('designer.description')}
        actions={
          <Button
            onClick={() =>
              setDraft({
                code: '',
                dataset: '',
                labelAr: '',
                labelEn: '',
                active: true,
                definition: { cover: { show: true }, language: 'ar', sections: [] },
              })
            }
          >
            <Plus className="size-4" aria-hidden />
            {t('designer.new')}
          </Button>
        }
      />
      <div className="grid gap-6 xl:grid-cols-5">
        <div className="xl:col-span-2">
          <Table data-testid="dossier-template-list">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('designer.template')}</TableHeaderCell>
                <TableHeaderCell>{t('designer.recordType')}</TableHeaderCell>
                <TableHeaderCell>{t('designer.sections')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(templates.data ?? []).map((x) => (
                <TableRow key={x.id}>
                  <TableCell>
                    <button
                      type="button"
                      className="text-start text-primary hover:underline"
                      onClick={() => setDraft(toDraft(x))}
                    >
                      {text(x.labelAr, x.labelEn)}
                    </button>
                    {x.seeded && (
                      <span className="ms-2">
                        <StatusChip tone="info">{t('types.seeded')}</StatusChip>
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    {text(datasets.get(x.dataset)?.labelAr, datasets.get(x.dataset)?.labelEn)}
                  </TableCell>
                  <TableCell>{x.definition.sections?.length ?? 0}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        {draft && (
          <section
            className="flex flex-col gap-4 rounded-lg border bg-card p-4 xl:col-span-3"
            aria-label={t('designer.editor')}
            data-testid="dossier-designer"
          >
            <div className="grid gap-3 sm:grid-cols-2">
              <FormField label={t('types.code')}>
                <Input
                  dir="ltr"
                  disabled={!!draft.id}
                  value={draft.code}
                  onChange={(e) => setDraft({ ...draft, code: e.target.value })}
                />
              </FormField>
              <FormField label={t('designer.recordType')}>
                <Select
                  aria-label={t('designer.recordType')}
                  value={draft.dataset || undefined}
                  onValueChange={(v) =>
                    setDraft({ ...draft, dataset: v, definition: { ...draft.definition, sections: [] } })
                  }
                  options={(metadata.data?.datasets ?? [])
                    .filter((d) => !d.joinOnly && d.keyField)
                    .map((d) => ({ value: d.code, label: text(d.labelAr, d.labelEn) }))}
                />
              </FormField>
              <FormField label={t('types.labelAr')}>
                <Input
                  dir="rtl"
                  value={draft.labelAr}
                  onChange={(e) => setDraft({ ...draft, labelAr: e.target.value })}
                />
              </FormField>
              <FormField label={t('types.labelEn')}>
                <Input
                  dir="ltr"
                  value={draft.labelEn}
                  onChange={(e) => setDraft({ ...draft, labelEn: e.target.value })}
                />
              </FormField>
              <FormField label={t('designer.language')}>
                <Select
                  aria-label={t('designer.language')}
                  value={draft.definition.language ?? 'ar'}
                  onValueChange={(v) => setDefinition({ language: v })}
                  options={(['ar', 'en', 'bilingual'] as const).map((l) => ({
                    value: l,
                    label: t(`languages.${l}`),
                  }))}
                />
              </FormField>
              <Switch
                label={t('types.active')}
                checked={draft.active}
                onCheckedChange={(active) => setDraft({ ...draft, active })}
              />
            </div>
            <fieldset className="grid gap-2 rounded border p-3 sm:grid-cols-2">
              <legend className="px-1 text-sm font-medium">{t('designer.coverAndPage')}</legend>
              <Switch
                label={t('designer.cover')}
                checked={draft.definition.cover?.show ?? true}
                onCheckedChange={(show) => setDefinition({ cover: { ...draft.definition.cover, show } })}
              />
              <span />
              {(['headerAr', 'headerEn', 'footerAr', 'footerEn'] as const).map((key) => (
                <FormField key={key} label={t(`designer.${key}`)}>
                  <Input
                    dir={key.endsWith('Ar') ? 'rtl' : 'ltr'}
                    value={draft.definition[key] ?? ''}
                    onChange={(e) => setDefinition({ [key]: e.target.value || null })}
                  />
                </FormField>
              ))}
            </fieldset>
            <ol className="flex flex-col gap-2" data-testid="dossier-sections">
              {sections.map((section, index) => (
                <li key={section.key ?? index} className="rounded border">
                  <div className="flex flex-wrap items-center justify-between gap-2 p-2">
                    <button
                      type="button"
                      className="text-start font-medium hover:underline"
                      aria-expanded={open === section.key}
                      onClick={() => setOpen(open === section.key ? null : (section.key ?? null))}
                    >
                      {index + 1}. {text(section.titleAr, section.titleEn)}
                      <span className="ms-2 text-xs text-muted-foreground">
                        {t(`kinds.${section.kind as 'fields'}`)}
                      </span>
                    </button>
                    <div className="flex gap-1">
                      <Button
                        size="sm"
                        variant="ghost"
                        aria-label={t('designer.up')}
                        disabled={index === 0}
                        onClick={() => {
                          setSections(swap(sections, index, index - 1));
                        }}
                      >
                        <ArrowUp className="size-4" aria-hidden />
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        aria-label={t('designer.down')}
                        disabled={index === sections.length - 1}
                        onClick={() => {
                          setSections(swap(sections, index, index + 1));
                        }}
                      >
                        <ArrowDown className="size-4" aria-hidden />
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        aria-label={t('builder.remove')}
                        onClick={() => setSections(sections.filter((_, i) => i !== index))}
                      >
                        <Trash2 className="size-4" aria-hidden />
                      </Button>
                    </div>
                  </div>
                  {open === section.key && (
                    <div className="flex flex-col gap-2 border-t p-3" data-testid="dossier-section-editor">
                      <div className="grid gap-2 sm:grid-cols-3">
                        <Select
                          aria-label={t('designer.kind')}
                          value={section.kind ?? 'fields'}
                          onValueChange={(v) => updateSection(index, { kind: v })}
                          options={sectionKinds.map((k) => ({ value: k, label: t(`kinds.${k}`) }))}
                        />
                        <Input
                          dir="rtl"
                          aria-label={t('designer.titleAr')}
                          value={section.titleAr ?? ''}
                          onChange={(e) => updateSection(index, { titleAr: e.target.value })}
                        />
                        <Input
                          dir="ltr"
                          aria-label={t('designer.titleEn')}
                          value={section.titleEn ?? ''}
                          onChange={(e) => updateSection(index, { titleEn: e.target.value })}
                        />
                      </div>
                      {binding(section, index)}
                      <div className="grid gap-2 sm:grid-cols-3">
                        <Select
                          aria-label={t('designer.condition')}
                          value={section.condition?.field ?? '-'}
                          onValueChange={(v) =>
                            updateSection(index, {
                              condition:
                                v === '-'
                                  ? null
                                  : {
                                      field: v,
                                      operator: section.condition?.operator ?? 'notEmpty',
                                      values: section.condition?.values ?? [],
                                    },
                            })
                          }
                          options={[
                            { value: '-', label: t('designer.always') },
                            ...fieldOptions([]).filter((o) => o.value.startsWith('a.')),
                          ]}
                        />
                        {section.condition && (
                          <>
                            <Select
                              aria-label={t('builder.operator')}
                              value={section.condition.operator ?? 'notEmpty'}
                              onValueChange={(v) =>
                                section.condition &&
                                updateSection(index, { condition: { ...section.condition, operator: v } })
                              }
                              options={['eq', 'ne', 'in', 'gte', 'lte', 'empty', 'notEmpty'].map((op) => ({
                                value: op,
                                label: t(`operators.${op}` as 'operators.eq'),
                              }))}
                            />
                            <Input
                              aria-label={t('builder.value')}
                              value={(section.condition.values ?? []).join(',')}
                              onChange={(e) =>
                                section.condition &&
                                updateSection(index, {
                                  condition: {
                                    ...section.condition,
                                    values: e.target.value.split(',').map((v) => v.trim()),
                                  },
                                })
                              }
                            />
                          </>
                        )}
                      </div>
                      <Switch
                        label={t('designer.pageBreak')}
                        checked={section.pageBreak ?? false}
                        onCheckedChange={(pageBreak) => updateSection(index, { pageBreak })}
                      />
                    </div>
                  )}
                </li>
              ))}
            </ol>
            <Button
              size="sm"
              variant="outline"
              className="self-start"
              disabled={!draft.dataset}
              onClick={() => {
                const key = `section-${sections.length + 1}`;
                setSections([
                  ...sections,
                  { key, kind: 'fields', titleAr: 'قسم جديد', titleEn: 'New section', fields: [] },
                ]);
                setOpen(key);
              }}
            >
              <Plus className="size-4" aria-hidden />
              {t('designer.addSection')}
            </Button>
            <div className="flex flex-wrap items-end gap-2 border-t pt-3">
              <Button
                disabled={save.isPending || !draft.code || !draft.dataset}
                data-testid="dossier-template-save"
                onClick={() =>
                  save.mutate(
                    {
                      input: {
                        code: draft.code.trim(),
                        dataset: draft.dataset,
                        labelAr: draft.labelAr,
                        labelEn: draft.labelEn,
                        definition: draft.definition,
                        active: draft.active,
                      },
                      version: draft.version,
                    },
                    {
                      onSuccess: (saved) => {
                        toast({ title: t('designer.saved') });
                        setDraft(toDraft(saved));
                      },
                      onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                    },
                  )
                }
              >
                <Save className="size-4" aria-hidden />
                {t('designer.save')}
              </Button>
              {draft.id && (
                <>
                  <FormField label={t('designer.previewRecord')}>
                    <div className="flex gap-2">
                      <Input
                        aria-label={t('dossiers.search')}
                        placeholder={t('dossiers.search')}
                        value={previewQuery}
                        onChange={(e) => setPreviewQuery(e.target.value)}
                      />
                      <Select
                        aria-label={t('designer.previewRecord')}
                        value={previewRecord}
                        placeholder={t('dossiers.chooseRecord')}
                        onValueChange={setPreviewRecord}
                        options={(records.data ?? []).map((r) => ({
                          value: r.id,
                          label: `${r.number ?? ''} ${text(r.nameAr, r.nameEn)}`.trim(),
                        }))}
                      />
                    </div>
                  </FormField>
                  {(['pdf', 'docx'] as const).map((format) => (
                    <Button
                      key={format}
                      variant="outline"
                      disabled={!previewRecord}
                      onClick={() => void download(format, i18n.language)}
                    >
                      <FileDown className="size-4" aria-hidden />
                      {t(`dossiers.${format}`)}
                    </Button>
                  ))}
                </>
              )}
            </div>
          </section>
        )}
      </div>
    </div>
  );
}
