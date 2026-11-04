import { useNavigate, useParams } from '@tanstack/react-router';
import { Plus, Sparkles, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '@/api/client';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useAiFeature } from '@/features/ai/api';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  FileDrop,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import { kpiApi, useKpiCard, useKpiMutation, useKpiOptions, type CardValues, type KpiCardDraft } from './api';
import { SmartList } from './CardPage';
import { KpiPage, useKpiT } from './components';

const NONE = '-';

const empty: CardValues = {
  nameAr: null,
  nameEn: null,
  descriptionAr: null,
  descriptionEn: null,
  measuringEquation: null,
  processName: null,
  initiatedOn: null,
  typeCode: null,
  indexClassificationCode: null,
  effectivenessCode: null,
  nature: 'quantitative',
  sourceCodes: [],
  measuringMethodCode: null,
  competitivenessIndicator: false,
  isGlobalStar: false,
  unitCode: 'number',
  measuringPeriod: 'quarterly',
  direction: 'increase',
  targetType: 'single',
  targetSettingMechanism: null,
  allowsNegative: false,
  objectiveId: null,
  sectorGoal: null,
  nationalGoalCode: null,
  visionLinkCode: null,
  gemCriteriaCodes: [],
  criteriaText: null,
  excellenceLink: null,
  departmentUnitId: null,
  responsibleUnitId: null,
  responsibleEmployeeId: null,
  responsibleTeamId: null,
  benchmarkDescription: null,
  benchmarkResult: null,
  level: 'organisation',
  parentCardId: null,
  weight: null,
  baselineValue: null,
  baselineYear: null,
  greenFrom: null,
  amberFrom: null,
  formula: null,
  dataElements: [],
  scale: [],
};

/** New / edit KPI card (SPEC §2.3 A, §2.7): every definition field, composite and qualitative KPIs, and the AI card builder. */
export function KpiCardFormPage() {
  const params = useParams({ strict: false });
  const cardId = params.cardId;
  const card = useKpiCard(cardId);
  const { t: tc } = useTranslation();
  if (cardId && card.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (cardId && card.isError) return <StatusBanner tone="danger" title={errorMessage(tc, card.error)} />;
  return (
    <CardForm
      key={card.data?.id ?? 'new'}
      cardId={cardId}
      initial={card.data?.values ?? empty}
      code={card.data?.code}
      version={card.data?.version}
    />
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-5">
      <h2 className="font-semibold">{title}</h2>
      <div className="grid gap-4 md:grid-cols-2">{children}</div>
    </section>
  );
}

function CardForm({
  cardId,
  initial,
  code,
  version,
}: {
  cardId?: string;
  initial: CardValues;
  code?: string;
  version?: number;
}) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const navigate = useNavigate();
  const toast = useToast();
  const options = useKpiOptions();
  const [v, setV] = useState<CardValues>(initial);
  const [manualCode, setManualCode] = useState('');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const create = useKpiMutation(kpiApi.createCard);
  const update = useKpiMutation(kpiApi.updateCard);
  const set = <K extends keyof CardValues>(key: K, value: CardValues[K]) =>
    setV((prev) => ({ ...prev, [key]: value }));
  const text = (key: keyof CardValues) => (v[key] as string | null) ?? '';
  const num = (value: string) => (value === '' ? null : Number(value));
  const lookup = (type: string, emptyLabel?: string) => [
    ...(emptyLabel ? [{ value: NONE, label: emptyLabel }] : []),
    ...(options.data?.lookups[type] ?? []).map((o) => ({
      value: o.code,
      label: pickText(language, o.labelAr, o.labelEn) || o.code,
    })),
  ];
  const selectLookup = (key: keyof CardValues, type: string, required = false) => (
    <Select
      value={(v[key] as string | null) ?? NONE}
      onValueChange={(value) => set(key, (value === NONE ? null : value) as never)}
      options={lookup(type, required ? undefined : t('form.none'))}
    />
  );
  const field = (key: string) => errors[key];

  function onError(error: unknown) {
    if (error instanceof ApiError && error.problem.errors?.length) {
      const next: Record<string, string> = {};
      for (const e of error.problem.errors)
        next[e.field] = translateMessageKey(tc, e.messageKey, e.params ?? undefined);
      setErrors(next);
    }
    toast({ tone: 'error', title: errorMessage(tc, error) });
  }

  function save() {
    setErrors({});
    const input = { code: manualCode || null, values: v, reason: reason || null };
    if (cardId && version !== undefined)
      update.mutate(
        { id: cardId, card: input, version },
        {
          onSuccess: (card) => void navigate({ to: '/kpi/cards/$cardId', params: { cardId: card.id } }),
          onError,
        },
      );
    else
      create.mutate(input, {
        onSuccess: (card) => void navigate({ to: '/kpi/cards/$cardId', params: { cardId: card.id } }),
        onError,
      });
  }

  function acceptDraft(draft: KpiCardDraft) {
    setV((prev) => ({
      ...prev,
      nameAr: draft.nameAr ?? prev.nameAr,
      nameEn: draft.nameEn ?? prev.nameEn,
      descriptionAr: draft.descriptionAr ?? prev.descriptionAr,
      descriptionEn: draft.descriptionEn ?? prev.descriptionEn,
      measuringEquation: draft.measuringEquation ?? prev.measuringEquation,
      unitCode: draft.unitCode ?? prev.unitCode,
      measuringPeriod: draft.measuringPeriod ?? prev.measuringPeriod,
      direction: draft.direction ?? prev.direction,
      targetType: draft.targetType ?? prev.targetType,
    }));
  }

  return (
    <KpiPage testId="kpi-card-form">
      <PageHeader
        title={cardId ? t('form.editTitle', { code }) : t('form.newTitle')}
        description={t('form.description')}
        actions={
          <Button onClick={save} loading={create.isPending || update.isPending} data-testid="kpi-card-save">
            {tc('actions.save')}
          </Button>
        }
      />
      {!cardId && <AiBuilder onAccept={acceptDraft} />}
      <Section title={t('form.sections.identity')}>
        {!cardId && options.data?.manualCodes && (
          <FormField label={t('fields.code')} hint={t('form.codeHint')} error={field('code')}>
            <Input value={manualCode} onChange={(e) => setManualCode(e.target.value)} />
          </FormField>
        )}
        <FormField label={t('fields.nameAr')} error={field('nameAr')}>
          <Input
            dir="rtl"
            value={text('nameAr')}
            onChange={(e) => set('nameAr', e.target.value || null)}
            data-testid="kpi-name-ar"
          />
        </FormField>
        <FormField label={t('fields.nameEn')} error={field('nameEn')}>
          <Input
            dir="ltr"
            value={text('nameEn')}
            onChange={(e) => set('nameEn', e.target.value || null)}
            data-testid="kpi-name-en"
          />
        </FormField>
        <FormField label={t('fields.descriptionAr')}>
          <Textarea
            dir="rtl"
            value={text('descriptionAr')}
            onChange={(e) => set('descriptionAr', e.target.value || null)}
          />
        </FormField>
        <FormField label={t('fields.descriptionEn')}>
          <Textarea
            dir="ltr"
            value={text('descriptionEn')}
            onChange={(e) => set('descriptionEn', e.target.value || null)}
          />
        </FormField>
        <FormField label={t('fields.processName')}>
          <Input value={text('processName')} onChange={(e) => set('processName', e.target.value || null)} />
        </FormField>
        <FormField label={t('fields.initiatedOn')}>
          <Input
            type="date"
            value={text('initiatedOn')}
            onChange={(e) => set('initiatedOn', e.target.value || null)}
          />
        </FormField>
      </Section>
      <Section title={t('form.sections.classification')}>
        <FormField label={t('fields.typeCode')} error={field('typeCode')}>
          {selectLookup('typeCode', 'kpi.type')}
        </FormField>
        <FormField label={t('fields.indexClassificationCode')}>
          {selectLookup('indexClassificationCode', 'kpi.index-classification')}
        </FormField>
        <FormField label={t('fields.effectivenessCode')}>
          {selectLookup('effectivenessCode', 'kpi.effectiveness')}
        </FormField>
        <FormField label={t('fields.nature')}>
          <Select
            value={v.nature}
            onValueChange={(value) => set('nature', value)}
            options={(options.data?.natures ?? ['quantitative', 'qualitative']).map((n) => ({
              value: n,
              label: k(`nature.${n}`),
            }))}
          />
        </FormField>
        <FormField label={t('fields.level')}>
          <Select
            value={v.level}
            onValueChange={(value) => set('level', value)}
            options={(options.data?.levels ?? []).map((n) => ({ value: n, label: k(`level.${n}`) }))}
          />
        </FormField>
        <FormField label={t('fields.parentCardId')} error={field('parentCardId')}>
          <Select
            value={v.parentCardId ?? NONE}
            onValueChange={(value) => set('parentCardId', value === NONE ? null : value)}
            options={[
              { value: NONE, label: t('form.none') },
              ...(options.data?.cards ?? [])
                .filter((c) => c.id !== cardId)
                .map((c) => ({
                  value: c.id,
                  label: `${c.code} · ${pickText(language, c.nameAr, c.nameEn)}`,
                })),
            ]}
          />
        </FormField>
        <fieldset className="flex flex-col gap-2 md:col-span-2">
          <legend className="text-sm font-medium">{t('fields.sourceCodes')}</legend>
          <div className="flex flex-wrap gap-4">
            {(options.data?.lookups['kpi.source'] ?? []).map((o) => (
              <Checkbox
                key={o.code}
                label={pickText(language, o.labelAr, o.labelEn) || o.code}
                checked={(v.sourceCodes ?? []).includes(o.code)}
                onChange={(e) =>
                  set(
                    'sourceCodes',
                    e.target.checked
                      ? [...(v.sourceCodes ?? []), o.code]
                      : (v.sourceCodes ?? []).filter((c) => c !== o.code),
                  )
                }
              />
            ))}
          </div>
        </fieldset>
        <Checkbox
          label={t('fields.competitivenessIndicator')}
          checked={v.competitivenessIndicator}
          onChange={(e) => set('competitivenessIndicator', e.target.checked)}
        />
      </Section>
      <Section title={t('form.sections.measurement')}>
        <FormField
          label={t('fields.measuringEquation')}
          className="md:col-span-2"
          error={field('measuringEquation')}
        >
          <Input
            value={text('measuringEquation')}
            onChange={(e) => set('measuringEquation', e.target.value || null)}
            data-testid="kpi-equation"
          />
        </FormField>
        <FormField label={t('fields.unitCode')} error={field('unitCode')}>
          {selectLookup('unitCode', 'kpi.unit', true)}
        </FormField>
        <FormField label={t('fields.measuringPeriod')}>
          <Select
            value={v.measuringPeriod}
            onValueChange={(value) => set('measuringPeriod', value)}
            options={(options.data?.measuringPeriods ?? []).map((n) => ({
              value: n,
              label: k(`measuringPeriod.${n}`),
            }))}
          />
        </FormField>
        <FormField label={t('fields.direction')}>
          <Select
            value={v.direction}
            onValueChange={(value) => set('direction', value)}
            options={(options.data?.directions ?? []).map((n) => ({ value: n, label: k(`direction.${n}`) }))}
          />
        </FormField>
        <FormField label={t('fields.targetType')}>
          <Select
            value={v.targetType}
            onValueChange={(value) => set('targetType', value)}
            options={(options.data?.targetTypes ?? []).map((n) => ({
              value: n,
              label: k(`targetType.${n}`),
            }))}
          />
        </FormField>
        <FormField label={t('fields.measuringMethodCode')}>
          {selectLookup('measuringMethodCode', 'kpi.measuring-method')}
        </FormField>
        <FormField label={t('fields.targetSettingMechanism')}>
          <Input
            value={text('targetSettingMechanism')}
            onChange={(e) => set('targetSettingMechanism', e.target.value || null)}
          />
        </FormField>
        <FormField label={t('fields.baselineValue')}>
          <Input
            type="number"
            value={v.baselineValue ?? ''}
            onChange={(e) => set('baselineValue', num(e.target.value))}
          />
        </FormField>
        <FormField label={t('fields.baselineYear')} error={field('baselineYear')}>
          <Input
            type="number"
            value={v.baselineYear ?? ''}
            onChange={(e) => set('baselineYear', num(e.target.value))}
          />
        </FormField>
        <FormField label={t('fields.weight')} hint={t('form.weightHint')} error={field('weight')}>
          <Input type="number" value={v.weight ?? ''} onChange={(e) => set('weight', num(e.target.value))} />
        </FormField>
        <Checkbox
          label={t('fields.allowsNegative')}
          checked={v.allowsNegative}
          onChange={(e) => set('allowsNegative', e.target.checked)}
        />
        <FormField label={t('form.greenFrom')} hint={t('form.bandsHint')} error={field('amberFrom')}>
          <Input
            type="number"
            step="1"
            value={v.greenFrom == null ? '' : v.greenFrom * 100}
            onChange={(e) => set('greenFrom', e.target.value === '' ? null : Number(e.target.value) / 100)}
          />
        </FormField>
        <FormField label={t('form.amberFrom')}>
          <Input
            type="number"
            step="1"
            value={v.amberFrom == null ? '' : v.amberFrom * 100}
            onChange={(e) => set('amberFrom', e.target.value === '' ? null : Number(e.target.value) / 100)}
          />
        </FormField>
      </Section>
      <Section title={t('form.sections.composite')}>
        <p className="text-sm text-muted-foreground md:col-span-2">{t('form.compositeHint')}</p>
        <ElementsEditor
          value={v.dataElements ?? []}
          onChange={(value) => set('dataElements', value)}
          error={field('dataElements')}
        />
        <FormField
          label={t('fields.formula')}
          className="md:col-span-2"
          hint={t('form.formulaHint')}
          error={field('formula')}
        >
          <Input dir="ltr" value={text('formula')} onChange={(e) => set('formula', e.target.value || null)} />
        </FormField>
        {v.nature === 'qualitative' && (
          <ScaleEditor
            value={v.scale ?? []}
            onChange={(value) => set('scale', value)}
            error={field('scale')}
          />
        )}
      </Section>
      <Section title={t('form.sections.alignment')}>
        <FormField label={t('fields.objectiveId')} error={field('objectiveId')}>
          <Select
            value={v.objectiveId ?? NONE}
            onValueChange={(value) => set('objectiveId', value === NONE ? null : value)}
            options={[
              { value: NONE, label: t('form.none') },
              ...(options.data?.elements ?? [])
                .filter((e) => e.kind === 'objective' && e.active)
                .map((e) => ({
                  value: e.id,
                  label: `${e.code} · ${pickText(language, e.nameAr, e.nameEn)}`,
                })),
            ]}
          />
        </FormField>
        <FormField label={t('fields.sectorGoal')}>
          <Input value={text('sectorGoal')} onChange={(e) => set('sectorGoal', e.target.value || null)} />
        </FormField>
        <FormField label={t('fields.nationalGoalCode')}>
          {selectLookup('nationalGoalCode', 'kpi.national-goal')}
        </FormField>
        <FormField label={t('fields.visionLinkCode')}>
          {selectLookup('visionLinkCode', 'kpi.vision-link')}
        </FormField>
        <fieldset className="flex flex-col gap-2 md:col-span-2">
          <legend className="text-sm font-medium">{t('fields.gemCriteriaCodes')}</legend>
          <div className="flex flex-wrap gap-4">
            {(options.data?.lookups['kpi.gem-criterion'] ?? []).length === 0 && (
              <span className="text-sm text-muted-foreground">{t('form.noGem')}</span>
            )}
            {(options.data?.lookups['kpi.gem-criterion'] ?? []).map((o) => (
              <Checkbox
                key={o.code}
                label={pickText(language, o.labelAr, o.labelEn) || o.code}
                checked={(v.gemCriteriaCodes ?? []).includes(o.code)}
                onChange={(e) =>
                  set(
                    'gemCriteriaCodes',
                    e.target.checked
                      ? [...(v.gemCriteriaCodes ?? []), o.code]
                      : (v.gemCriteriaCodes ?? []).filter((c) => c !== o.code),
                  )
                }
              />
            ))}
          </div>
        </fieldset>
        <FormField label={t('fields.criteriaText')}>
          <Input value={text('criteriaText')} onChange={(e) => set('criteriaText', e.target.value || null)} />
        </FormField>
        <FormField label={t('fields.excellenceLink')}>
          <Input
            value={text('excellenceLink')}
            onChange={(e) => set('excellenceLink', e.target.value || null)}
          />
        </FormField>
        <Checkbox
          label={t('fields.isGlobalStar')}
          checked={v.isGlobalStar}
          onChange={(e) => set('isGlobalStar', e.target.checked)}
        />
      </Section>
      <Section title={t('form.sections.ownership')}>
        <FormField label={t('fields.departmentUnitId')}>
          <UnitSelect
            allowEmpty
            value={v.departmentUnitId ?? undefined}
            onChange={(value) => set('departmentUnitId', value ?? null)}
          />
        </FormField>
        <FormField label={t('fields.responsibleUnitId')}>
          <UnitSelect
            allowEmpty
            value={v.responsibleUnitId ?? undefined}
            onChange={(value) => set('responsibleUnitId', value ?? null)}
          />
        </FormField>
        <FormField label={t('fields.responsibleEmployeeId')}>
          <EmployeePicker
            value={v.responsibleEmployeeId ?? undefined}
            onChange={(value) => set('responsibleEmployeeId', value ?? null)}
          />
        </FormField>
      </Section>
      <Section title={t('form.sections.benchmark')}>
        <FormField label={t('fields.benchmarkDescription')}>
          <Input
            value={text('benchmarkDescription')}
            onChange={(e) => set('benchmarkDescription', e.target.value || null)}
          />
        </FormField>
        <FormField label={t('form.benchmarkPercent')}>
          <Input
            type="number"
            value={v.benchmarkResult == null ? '' : v.benchmarkResult * 100}
            onChange={(e) =>
              set('benchmarkResult', e.target.value === '' ? null : Number(e.target.value) / 100)
            }
          />
        </FormField>
      </Section>
      {cardId && (
        <FormField label={t('form.reason')}>
          <Input value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      )}
    </KpiPage>
  );
}

function ElementsEditor({
  value,
  onChange,
  error,
}: {
  value: { code: string; nameAr: string | null; nameEn: string | null }[];
  onChange: (value: { code: string; nameAr: string | null; nameEn: string | null }[]) => void;
  error?: string;
}) {
  const { t } = useTranslation('kpi');
  return (
    <fieldset className="flex flex-col gap-2 md:col-span-2">
      <legend className="text-sm font-medium">{t('fields.dataElements')}</legend>
      {value.map((e, i) => (
        <div key={i} className="grid grid-cols-[10rem_1fr_1fr_auto] gap-2">
          <Input
            dir="ltr"
            aria-label={t('form.elementCode')}
            value={e.code}
            onChange={(ev) => onChange(value.map((x, j) => (j === i ? { ...x, code: ev.target.value } : x)))}
          />
          <Input
            dir="rtl"
            aria-label={t('fields.nameAr')}
            value={e.nameAr ?? ''}
            onChange={(ev) =>
              onChange(value.map((x, j) => (j === i ? { ...x, nameAr: ev.target.value } : x)))
            }
          />
          <Input
            dir="ltr"
            aria-label={t('fields.nameEn')}
            value={e.nameEn ?? ''}
            onChange={(ev) =>
              onChange(value.map((x, j) => (j === i ? { ...x, nameEn: ev.target.value } : x)))
            }
          />
          <Button
            variant="ghost"
            size="icon"
            aria-label={t('actions.remove')}
            onClick={() => onChange(value.filter((_, j) => j !== i))}
          >
            <Trash2 aria-hidden />
          </Button>
        </div>
      ))}
      {error && <p className="text-xs font-medium text-danger">{error}</p>}
      <Button
        variant="outline"
        size="sm"
        className="w-fit"
        onClick={() => onChange([...value, { code: '', nameAr: null, nameEn: null }])}
      >
        <Plus aria-hidden />
        {t('form.addElement')}
      </Button>
    </fieldset>
  );
}

function ScaleEditor({
  value,
  onChange,
  error,
}: {
  value: { value: number; labelAr: string | null; labelEn: string | null }[];
  onChange: (value: { value: number; labelAr: string | null; labelEn: string | null }[]) => void;
  error?: string;
}) {
  const { t } = useTranslation('kpi');
  return (
    <fieldset className="flex flex-col gap-2 md:col-span-2">
      <legend className="text-sm font-medium">{t('fields.scale')}</legend>
      {value.map((s, i) => (
        <div key={i} className="grid grid-cols-[6rem_1fr_1fr_auto] gap-2">
          <Input
            type="number"
            aria-label={t('form.levelValue')}
            value={s.value}
            onChange={(ev) =>
              onChange(value.map((x, j) => (j === i ? { ...x, value: Number(ev.target.value) } : x)))
            }
          />
          <Input
            dir="rtl"
            aria-label={t('fields.nameAr')}
            value={s.labelAr ?? ''}
            onChange={(ev) =>
              onChange(value.map((x, j) => (j === i ? { ...x, labelAr: ev.target.value } : x)))
            }
          />
          <Input
            dir="ltr"
            aria-label={t('fields.nameEn')}
            value={s.labelEn ?? ''}
            onChange={(ev) =>
              onChange(value.map((x, j) => (j === i ? { ...x, labelEn: ev.target.value } : x)))
            }
          />
          <Button
            variant="ghost"
            size="icon"
            aria-label={t('actions.remove')}
            onClick={() => onChange(value.filter((_, j) => j !== i))}
          >
            <Trash2 aria-hidden />
          </Button>
        </div>
      ))}
      {error && <p className="text-xs font-medium text-danger">{error}</p>}
      <Button
        variant="outline"
        size="sm"
        className="w-fit"
        onClick={() => onChange([...value, { value: value.length + 1, labelAr: null, labelEn: null }])}
      >
        <Plus aria-hidden />
        {t('form.addLevel')}
      </Button>
    </fieldset>
  );
}

/** AI card builder (SPEC §2.9): drafts a card from a description or a document with the SMART check; the user accepts the draft. */
function AiBuilder({ onAccept }: { onAccept: (draft: KpiCardDraft) => void }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k } = useKpiT();
  const enabled = useAiFeature('kpi-card-builder');
  const toast = useToast();
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [draft, setDraft] = useState<KpiCardDraft>();
  if (!enabled) return null;
  const run = (call: Promise<KpiCardDraft>) => {
    setBusy(true);
    call
      .then(setDraft)
      .catch((e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) }))
      .finally(() => setBusy(false));
  };
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border border-dashed bg-card p-5"
      data-testid="kpi-ai-builder"
    >
      <h2 className="flex items-center gap-2 font-semibold">
        <Sparkles className="size-4 text-primary" aria-hidden />
        {t('ai.builder.title')}
      </h2>
      <Textarea
        aria-label={t('ai.builder.description')}
        placeholder={t('ai.builder.description')}
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        data-testid="kpi-ai-description"
      />
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          loading={busy}
          disabled={!description.trim()}
          onClick={() => run(kpiApi.buildCard(description))}
          data-testid="kpi-ai-draft"
        >
          <Sparkles aria-hidden />
          {t('ai.builder.fromText')}
        </Button>
        <FileDrop
          label={t('ai.builder.fromDocument')}
          accept=".pdf,.docx,.pptx,.txt,.png,.jpg,.jpeg"
          file={null}
          disabled={busy}
          onFile={(file) => {
            if (file) run(kpiApi.buildCardFromDocument({ file, description: description || null }));
          }}
        />
      </div>
      {draft && (
        <div className="flex flex-col gap-3 rounded-md border p-4" data-testid="kpi-ai-card-draft">
          <StatusChip tone="info" className="w-fit">
            {t('ai.draftLabel')}
          </StatusChip>
          <dl className="grid gap-2 text-sm md:grid-cols-2">
            <div>
              <dt className="text-muted-foreground">{t('fields.nameAr')}</dt>
              <dd dir="rtl">{draft.nameAr}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t('fields.nameEn')}</dt>
              <dd dir="ltr">{draft.nameEn}</dd>
            </div>
            <div className="md:col-span-2">
              <dt className="text-muted-foreground">{t('fields.measuringEquation')}</dt>
              <dd>{draft.measuringEquation}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t('fields.measuringPeriod')}</dt>
              <dd>{draft.measuringPeriod ? k(`measuringPeriod.${draft.measuringPeriod}`) : '—'}</dd>
            </div>
            <div>
              <dt className="text-muted-foreground">{t('fields.direction')}</dt>
              <dd>{draft.direction ? k(`direction.${draft.direction}`) : '—'}</dd>
            </div>
          </dl>
          <SmartList checks={draft.smart} />
          {draft.suggestions.length > 0 && (
            <ul className="list-disc ps-5 text-sm">
              {draft.suggestions.map((s) => (
                <li key={s}>{s}</li>
              ))}
            </ul>
          )}
          <div className="flex gap-2">
            <Button
              onClick={() => {
                onAccept(draft);
                setDraft(undefined);
              }}
              data-testid="kpi-ai-accept"
            >
              {t('ai.accept')}
            </Button>
            <Button variant="ghost" onClick={() => setDraft(undefined)}>
              {t('ai.discard')}
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}
