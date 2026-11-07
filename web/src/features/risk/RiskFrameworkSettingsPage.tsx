import { Plus, Save, Trash2 } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
  Section,
} from '@/ui';
import {
  projectResidualRules,
  recalculationModes,
  residualStatuses,
  reviewFrequencyChoices,
  riskApi,
  scoringMethods,
  useFramework,
  useFrameworkPreview,
  useFrameworkVersions,
  useRiskCategories,
  useRiskMutation,
  useRiskSettings,
  type CategoryInput,
  type FrameworkContent,
  type FrameworkPreview,
  type RiskCategory,
  type RiskSettingsValue,
} from './api';
import { RiskMatrix, useNames, useNum, useRiskT } from './components';
import { ResponsibilitiesLink } from '@/features/access/ResponsibilitiesLink';

/** Settings → Risk framework (docs/design/risk.md §2, ADR 0100): versioned framework with live preview, categories, settings. */
export function RiskFrameworkSettingsPage() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const framework = useFramework();
  if (framework.isError) return <StatusBanner tone="danger" title={errorMessage(tc, framework.error)} />;
  if (!framework.data) return <Skeleton className="h-96 rounded-lg" />;
  return (
    <div className="flex flex-col gap-6" data-testid="risk-framework-settings">
      <ResponsibilitiesLink system="risk" />
      <PageHeader
        title={t('settings.title')}
        description={t('settings.description', { version: framework.data.versionNo })}
      />
      <FrameworkEditor
        key={framework.data.id}
        initial={framework.data.content}
        versionNo={framework.data.versionNo}
      />
      <VersionsTable />
      <CategoriesSection />
      <SettingsSection />
    </div>
  );
}

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), ms);
    return () => window.clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

function FrameworkEditor({ initial, versionNo }: { initial: FrameworkContent; versionNo: number }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k } = useRiskT();
  const [c, setC] = useState<FrameworkContent>(() => structuredClone(initial));
  const [saving, setSaving] = useState(false);
  const debounced = useDebounced(c, 400);
  const preview = useFrameworkPreview(debounced);
  const changed = JSON.stringify(c) !== JSON.stringify(initial);
  const errors = preview.data?.errors ?? [];
  const err = (field: string) =>
    errors
      .filter((e) => e.field === field)
      .map((e) => translateMessageKey(tc, e.messageKey, e.params ?? undefined))
      .join(' ') || undefined;
  const errPrefix = (prefix: string) =>
    errors
      .filter((e) => e.field.startsWith(prefix))
      .map((e) => translateMessageKey(tc, e.messageKey, e.params ?? undefined))
      .join(' ') || undefined;
  const update = (patch: Partial<FrameworkContent>) => setC((p) => ({ ...p, ...patch }));
  const setAt = <K extends 'likelihood' | 'impact' | 'impactAreas' | 'bands' | 'controlEffectiveness'>(
    key: K,
    index: number,
    patch: Partial<FrameworkContent[K][number]>,
  ) => setC((p) => ({ ...p, [key]: p[key].map((row, i) => (i === index ? { ...row, ...patch } : row)) }));
  const removeAt = (key: 'impactAreas' | 'bands' | 'controlEffectiveness', index: number) =>
    setC((p) => ({ ...p, [key]: p[key].filter((_, i) => i !== index) }));
  const n = (value: string) => (value === '' ? 0 : Number(value));

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 xl:grid-cols-[3fr_2fr]">
        <Section title={t('settings.scoring')} testId="risk-settings-scoring">
          <fieldset className="flex flex-wrap gap-4">
            <legend className="sr-only">{t('settings.scoring')}</legend>
            {scoringMethods.map((m) => (
              <label key={m} className="flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  name="scoring-method"
                  value={m}
                  checked={c.scoringMethod === m}
                  onChange={() => update({ scoringMethod: m })}
                  data-testid={`risk-method-${m}`}
                />
                {k(`scoringMethod.${m}`)}
              </label>
            ))}
          </fieldset>
          <FormField label={t('settings.formula')} hint={t('settings.formulaHint')} error={err('formula')}>
            <Textarea
              dir="ltr"
              className="font-mono"
              rows={3}
              value={c.formula ?? ''}
              disabled={c.scoringMethod !== 'custom'}
              onChange={(e) => update({ formula: e.target.value || null })}
              data-testid="risk-formula"
            />
          </FormField>
          <FormField label={t('settings.projectResidualRule')}>
            <Select
              value={c.projectResidualRule}
              onValueChange={(value) => update({ projectResidualRule: value })}
              options={projectResidualRules.map((r) => ({ value: r, label: k(`projectResidualRule.${r}`) }))}
            />
          </FormField>
        </Section>
        <PreviewPanel
          content={debounced}
          preview={preview.data}
          loading={preview.isFetching}
          failed={preview.error}
        />
      </div>

      <Section title={t('settings.likelihood')} testId="risk-settings-likelihood">
        {err('likelihood') && <p className="text-sm text-danger">{err('likelihood')}</p>}
        <div className="overflow-x-auto">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('mechanism.level')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.labelAr')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.labelEn')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.descriptionAr')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.descriptionEn')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {c.likelihood.map((l, i) => (
                <TableRow key={i}>
                  <TableCell className="tabular-nums">{l.value}</TableCell>
                  <TableCell>
                    <Input
                      dir="rtl"
                      aria-label={t('settings.labelAr')}
                      value={l.labelAr ?? ''}
                      onChange={(e) => setAt('likelihood', i, { labelAr: e.target.value || null })}
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      dir="ltr"
                      aria-label={t('settings.labelEn')}
                      value={l.labelEn ?? ''}
                      onChange={(e) => setAt('likelihood', i, { labelEn: e.target.value || null })}
                    />
                  </TableCell>
                  <TableCell>
                    <Textarea
                      dir="rtl"
                      rows={2}
                      aria-label={t('settings.descriptionAr')}
                      value={l.descriptionAr ?? ''}
                      onChange={(e) => setAt('likelihood', i, { descriptionAr: e.target.value || null })}
                    />
                  </TableCell>
                  <TableCell>
                    <Textarea
                      dir="ltr"
                      rows={2}
                      aria-label={t('settings.descriptionEn')}
                      value={l.descriptionEn ?? ''}
                      onChange={(e) => setAt('likelihood', i, { descriptionEn: e.target.value || null })}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </Section>

      <Section
        title={t('settings.impactAreas')}
        testId="risk-settings-areas"
        actions={
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              update({ impactAreas: [...c.impactAreas, { code: '', labelAr: null, labelEn: null }] })
            }
          >
            <Plus aria-hidden />
            {t('settings.add')}
          </Button>
        }
      >
        {err('impactAreas') && <p className="text-sm text-danger">{err('impactAreas')}</p>}
        <div className="grid gap-2">
          {c.impactAreas.map((a, i) => (
            <div key={i} className="grid grid-cols-[8rem_1fr_1fr_auto] items-center gap-2">
              <Input
                dir="ltr"
                aria-label={t('settings.code')}
                value={a.code}
                onChange={(e) => setAt('impactAreas', i, { code: e.target.value })}
              />
              <Input
                dir="rtl"
                aria-label={t('settings.labelAr')}
                value={a.labelAr ?? ''}
                onChange={(e) => setAt('impactAreas', i, { labelAr: e.target.value || null })}
              />
              <Input
                dir="ltr"
                aria-label={t('settings.labelEn')}
                value={a.labelEn ?? ''}
                onChange={(e) => setAt('impactAreas', i, { labelEn: e.target.value || null })}
              />
              <Button
                size="icon"
                variant="ghost"
                aria-label={t('settings.remove')}
                onClick={() => removeAt('impactAreas', i)}
              >
                <Trash2 aria-hidden />
              </Button>
            </div>
          ))}
        </div>
      </Section>

      <Section title={t('settings.impact')} testId="risk-settings-impact">
        {err('impact') && <p className="text-sm text-danger">{err('impact')}</p>}
        <div className="overflow-x-auto">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('mechanism.level')}</TableHeaderCell>
                <TableHeaderCell>{t('mechanism.label')}</TableHeaderCell>
                {c.impactAreas.map((a, i) => (
                  <TableHeaderCell key={i}>{a.labelEn || a.labelAr || a.code}</TableHeaderCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {c.impact.map((l, i) => (
                <TableRow key={i}>
                  <TableCell className="tabular-nums">{l.value}</TableCell>
                  <TableCell className="min-w-44">
                    <div className="flex flex-col gap-1">
                      <Input
                        dir="rtl"
                        aria-label={t('settings.labelAr')}
                        value={l.labelAr ?? ''}
                        onChange={(e) => setAt('impact', i, { labelAr: e.target.value || null })}
                      />
                      <Input
                        dir="ltr"
                        aria-label={t('settings.labelEn')}
                        value={l.labelEn ?? ''}
                        onChange={(e) => setAt('impact', i, { labelEn: e.target.value || null })}
                      />
                    </div>
                  </TableCell>
                  {c.impactAreas.map((a) => {
                    const crit = l.criteria?.[a.code] ?? { ar: null, en: null };
                    const setCrit = (patch: Partial<typeof crit>) =>
                      setAt('impact', i, {
                        criteria: { ...(l.criteria ?? {}), [a.code]: { ...crit, ...patch } },
                      });
                    return (
                      <TableCell key={a.code} className="min-w-48">
                        <div className="flex flex-col gap-1">
                          <Textarea
                            dir="rtl"
                            rows={2}
                            aria-label={`${a.labelAr ?? a.code} — ${t('settings.criteriaAr')}`}
                            value={crit.ar ?? ''}
                            onChange={(e) => setCrit({ ar: e.target.value || null })}
                          />
                          <Textarea
                            dir="ltr"
                            rows={2}
                            aria-label={`${a.labelEn ?? a.code} — ${t('settings.criteriaEn')}`}
                            value={crit.en ?? ''}
                            onChange={(e) => setCrit({ en: e.target.value || null })}
                          />
                        </div>
                      </TableCell>
                    );
                  })}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </Section>

      <Section
        title={t('settings.bands')}
        testId="risk-settings-bands"
        actions={
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              update({
                bands: [
                  ...c.bands,
                  {
                    code: '',
                    labelAr: null,
                    labelEn: null,
                    color: '#64748b',
                    min: 0,
                    max: 0,
                    residualStatus: 'continuous-review',
                  },
                ],
              })
            }
          >
            <Plus aria-hidden />
            {t('settings.add')}
          </Button>
        }
      >
        {err('bands') && <p className="text-sm text-danger">{err('bands')}</p>}
        <div className="overflow-x-auto">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('settings.code')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.labelAr')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.labelEn')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.color')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.min')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.max')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.residualStatus')}</TableHeaderCell>
                <TableHeaderCell>{t('settings.reviewFrequency')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{t('settings.remove')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {c.bands.map((b, i) => (
                <TableRow key={i} data-testid="risk-band-row">
                  <TableCell>
                    <Input
                      dir="ltr"
                      className="w-28"
                      aria-label={t('settings.code')}
                      value={b.code}
                      onChange={(e) => setAt('bands', i, { code: e.target.value })}
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      dir="rtl"
                      aria-label={t('settings.labelAr')}
                      value={b.labelAr ?? ''}
                      onChange={(e) => setAt('bands', i, { labelAr: e.target.value || null })}
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      dir="ltr"
                      aria-label={t('settings.labelEn')}
                      value={b.labelEn ?? ''}
                      onChange={(e) => setAt('bands', i, { labelEn: e.target.value || null })}
                    />
                  </TableCell>
                  <TableCell>
                    <input
                      type="color"
                      aria-label={t('settings.color')}
                      value={/^#[0-9a-f]{6}$/i.test(b.color) ? b.color : '#000000'}
                      onChange={(e) => setAt('bands', i, { color: e.target.value })}
                      className="h-9 w-12 cursor-pointer rounded border"
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      type="number"
                      step="any"
                      className="w-20"
                      aria-label={t('settings.min')}
                      value={b.min}
                      onChange={(e) => setAt('bands', i, { min: n(e.target.value) })}
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      type="number"
                      step="any"
                      className="w-20"
                      aria-label={t('settings.max')}
                      value={b.max}
                      onChange={(e) => setAt('bands', i, { max: n(e.target.value) })}
                    />
                  </TableCell>
                  <TableCell>
                    <Select
                      aria-label={t('fields.residualStatus')}
                      value={b.residualStatus}
                      onValueChange={(value) => setAt('bands', i, { residualStatus: value })}
                      options={residualStatuses.map((s) => ({ value: s, label: k(`residualStatus.${s}`) }))}
                    />
                  </TableCell>
                  <TableCell>
                    <Select
                      aria-label={t('settings.reviewFrequency')}
                      value={String(c.reviewFrequencyMonths[b.code] ?? 3)}
                      onValueChange={(value) =>
                        update({
                          reviewFrequencyMonths: { ...c.reviewFrequencyMonths, [b.code]: Number(value) },
                        })
                      }
                      options={reviewFrequencyChoices.map((m) => ({
                        value: String(m),
                        label: t('mechanism.months', { count: m }),
                      }))}
                    />
                  </TableCell>
                  <TableCell>
                    <Button
                      size="icon"
                      variant="ghost"
                      aria-label={t('settings.remove')}
                      onClick={() => removeAt('bands', i)}
                    >
                      <Trash2 aria-hidden />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        {(errPrefix('bands[') || errPrefix('reviewFrequencyMonths')) && (
          <p className="text-sm text-danger">
            {[errPrefix('bands['), errPrefix('reviewFrequencyMonths')].filter(Boolean).join(' ')}
          </p>
        )}
        <p className="text-xs text-muted-foreground">{t('mechanism.gapRule')}</p>
      </Section>

      <Section
        title={t('settings.effectiveness')}
        testId="risk-settings-effectiveness"
        actions={
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              update({
                controlEffectiveness: [
                  ...c.controlEffectiveness,
                  { code: '', labelAr: null, labelEn: null, factor: 0 },
                ],
              })
            }
          >
            <Plus aria-hidden />
            {t('settings.add')}
          </Button>
        }
      >
        {err('controlEffectiveness') && <p className="text-sm text-danger">{err('controlEffectiveness')}</p>}
        <div className="grid gap-2">
          {c.controlEffectiveness.map((e, i) => (
            <div key={i} className="grid grid-cols-[8rem_1fr_1fr_6rem_auto] items-center gap-2">
              <Input
                dir="ltr"
                aria-label={t('settings.code')}
                value={e.code}
                onChange={(ev) => setAt('controlEffectiveness', i, { code: ev.target.value })}
              />
              <Input
                dir="rtl"
                aria-label={t('settings.labelAr')}
                value={e.labelAr ?? ''}
                onChange={(ev) => setAt('controlEffectiveness', i, { labelAr: ev.target.value || null })}
              />
              <Input
                dir="ltr"
                aria-label={t('settings.labelEn')}
                value={e.labelEn ?? ''}
                onChange={(ev) => setAt('controlEffectiveness', i, { labelEn: ev.target.value || null })}
              />
              <Input
                type="number"
                min={0}
                max={10}
                step="any"
                aria-label={t('mechanism.factor')}
                value={e.factor}
                onChange={(ev) => setAt('controlEffectiveness', i, { factor: n(ev.target.value) })}
              />
              <Button
                size="icon"
                variant="ghost"
                aria-label={t('settings.remove')}
                onClick={() => removeAt('controlEffectiveness', i)}
              >
                <Trash2 aria-hidden />
              </Button>
            </div>
          ))}
        </div>
      </Section>

      <div className="sticky bottom-0 z-10 flex flex-wrap items-center justify-end gap-3 rounded-lg border bg-card p-4 shadow">
        {changed && <span className="text-sm text-muted-foreground">{t('settings.unsaved')}</span>}
        <Button variant="outline" disabled={!changed} onClick={() => setC(structuredClone(initial))}>
          {t('settings.reset')}
        </Button>
        <Button
          disabled={!changed || preview.data?.valid === false}
          onClick={() => setSaving(true)}
          data-testid="risk-framework-save"
        >
          <Save aria-hidden />
          {t('settings.saveVersion', { version: versionNo + 1 })}
        </Button>
      </div>
      {saving && <SaveDialog content={c} onClose={() => setSaving(false)} />}
    </div>
  );
}

function PreviewPanel({
  content,
  preview,
  loading,
  failed,
}: {
  content: FrameworkContent;
  preview: FrameworkPreview | undefined;
  loading: boolean;
  failed: unknown;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const names = useNames();
  const band = (code: string) => {
    const b = content.bands.find((x) => x.code === code);
    return b ? names.text(b.labelAr, b.labelEn) || code : code;
  };
  return (
    <Section
      title={t('settings.preview')}
      testId="risk-framework-preview"
      actions={loading && <span className="text-xs text-muted-foreground">{tc('status.loading')}</span>}
    >
      {failed ? (
        <StatusBanner tone="danger" title={errorMessage(tc, failed)} />
      ) : !preview ? (
        <Skeleton className="h-48" />
      ) : (
        <>
          {!preview.valid && (
            <StatusBanner
              tone="danger"
              title={t('settings.invalid')}
              description={preview.errors
                .map((e) => translateMessageKey(tc, e.messageKey, e.params ?? undefined))
                .join(' · ')}
            />
          )}
          {preview.matrix.length > 0 && (
            <RiskMatrix
              cells={preview.matrix}
              bands={content.bands}
              prefix="preview"
              label={t('settings.preview')}
            />
          )}
          <p className="text-sm font-medium" data-testid="risk-preview-summary">
            {t('settings.affected', { count: preview.affectedRisks, total: preview.openRisks })}
            {preview.affectedProjectRisks > 0 &&
              ` ${t('settings.affectedProject', { count: preview.affectedProjectRisks })}`}
          </p>
          {preview.examples.length > 0 && (
            <ul className="flex flex-col gap-1 text-sm" data-testid="risk-preview-examples">
              {preview.examples.map((e) => (
                <li key={`${e.id}-${e.kind}`} className="flex flex-wrap items-center gap-2">
                  <bdi className="font-medium">{e.number}</bdi>
                  <span>{names.text(e.titleAr, e.titleEn)}</span>
                  <span className="text-muted-foreground">
                    ({e.kind === 'project' ? t('common.projectRisk') : t('rating.residual')})
                  </span>
                  <span>
                    {band(e.fromBand)} <span className="rtl:rotate-180 inline-block">→</span> {band(e.toBand)}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </Section>
  );
}

function SaveDialog({ content, onClose }: { content: FrameworkContent; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k } = useRiskT();
  const toast = useToast();
  const save = useRiskMutation(riskApi.saveFramework);
  const [reason, setReason] = useState('');
  const [recalculation, setRecalculation] = useState<string>('now');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('settings.saveTitle')}
        description={t('settings.saveHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!reason.trim()}
              data-testid="risk-framework-save-confirm"
              onClick={() =>
                save.mutate(
                  { content, reason, recalculation },
                  {
                    onSuccess: (v) => {
                      toast({ tone: 'success', title: t('settings.saved', { version: v.versionNo }) });
                      onClose();
                    },
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <FormField label={t('common.reason')} required>
          <Textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            data-testid="risk-framework-reason"
          />
        </FormField>
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">{t('settings.recalculation')}</legend>
          {recalculationModes.map((m) => (
            <label key={m} className="flex items-start gap-2 text-sm">
              <input
                type="radio"
                name="recalculation"
                value={m}
                checked={recalculation === m}
                onChange={() => setRecalculation(m)}
                className="mt-1"
                data-testid={`risk-recalc-${m}`}
              />
              {k(`recalculation.${m}`)}
            </label>
          ))}
        </fieldset>
      </DialogContent>
    </Dialog>
  );
}

function VersionsTable() {
  const { t } = useTranslation('risk');
  const { k, language } = useRiskT();
  const versions = useFrameworkVersions();
  return (
    <Section title={t('settings.versions')} testId="risk-framework-versions">
      {!versions.data ? (
        <Skeleton className="h-24" />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('settings.version')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.scoring')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.formula')}</TableHeaderCell>
              <TableHeaderCell>{t('common.reason')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.recalculation')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.date')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {versions.data.map((v) => (
              <TableRow key={v.id}>
                <TableCell>v{v.versionNo}</TableCell>
                <TableCell>{k(`scoringMethod.${v.scoringMethod}`)}</TableCell>
                <TableCell>
                  {v.formula ? (
                    <code dir="ltr" className="text-xs">
                      {v.formula}
                    </code>
                  ) : (
                    '—'
                  )}
                </TableCell>
                <TableCell>{v.reason ?? '—'}</TableCell>
                <TableCell>{k(`recalculationShort.${v.recalculation}`)}</TableCell>
                <TableCell>{formatDateTime(v.createdAt, language)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Section>
  );
}

function CategoriesSection() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const names = useNames();
  const num = useNum();
  const categories = useRiskCategories(true);
  const [editing, setEditing] = useState<{ category?: RiskCategory }>();
  const parent = (id: string | null) => {
    const p = categories.data?.find((c) => c.id === id);
    return p ? names.text(p.nameAr, p.nameEn) || p.code : '—';
  };
  return (
    <Section
      title={t('settings.categories.title')}
      testId="risk-settings-categories"
      actions={
        <Button size="sm" onClick={() => setEditing({})} data-testid="risk-category-add">
          <Plus aria-hidden />
          {t('settings.categories.add')}
        </Button>
      }
    >
      <p className="text-sm text-muted-foreground">{t('settings.categories.hint')}</p>
      {!categories.data ? (
        <Skeleton className="h-24" />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('settings.categories.code')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.labelAr')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.labelEn')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.categories.parent')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.appetite')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.categories.sort')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.edit')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {categories.data.map((c) => (
              <TableRow key={c.id} data-testid="risk-category-row">
                <TableCell>
                  <bdi>{c.code}</bdi>
                </TableCell>
                <TableCell>{c.nameAr ?? '—'}</TableCell>
                <TableCell>{c.nameEn ?? '—'}</TableCell>
                <TableCell>{parent(c.parentId)}</TableCell>
                <TableCell className="tabular-nums">
                  {c.appetite == null ? t('common.notSet') : num(c.appetite)}
                </TableCell>
                <TableCell className="tabular-nums">{c.sort}</TableCell>
                <TableCell>
                  <StatusChip tone={c.active ? 'success' : 'neutral'}>
                    {c.active ? t('settings.active') : t('settings.inactive')}
                  </StatusChip>
                </TableCell>
                <TableCell>
                  <Button size="sm" variant="ghost" onClick={() => setEditing({ category: c })}>
                    {tc('actions.edit')}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {editing && (
        <CategoryDialog
          category={editing.category}
          all={categories.data ?? []}
          onClose={() => setEditing(undefined)}
        />
      )}
    </Section>
  );
}

function CategoryDialog({
  category,
  all,
  onClose,
}: {
  category?: RiskCategory;
  all: RiskCategory[];
  onClose: () => void;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const names = useNames();
  const toast = useToast();
  const save = useRiskMutation(riskApi.saveCategory);
  const NONE = '-';
  const [v, setV] = useState<CategoryInput>({
    code: category?.code ?? '',
    nameAr: category?.nameAr ?? null,
    nameEn: category?.nameEn ?? null,
    parentId: category?.parentId ?? null,
    appetite: category?.appetite ?? null,
    sort: category?.sort ?? (all.length + 1) * 10,
    active: category?.active ?? true,
    version: category?.version ?? null,
  });
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={category ? t('settings.categories.edit') : t('settings.categories.add')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!v.code.trim()}
              data-testid="risk-category-save"
              onClick={() =>
                save.mutate(
                  { id: category?.id, input: v },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField
            label={t('settings.categories.code')}
            required
            hint={category ? t('settings.categories.codeFixed') : undefined}
          >
            <Input
              dir="ltr"
              value={v.code}
              disabled={!!category}
              onChange={(e) => setV({ ...v, code: e.target.value })}
            />
          </FormField>
          <FormField label={t('settings.categories.sort')}>
            <Input
              type="number"
              value={v.sort}
              onChange={(e) => setV({ ...v, sort: Number(e.target.value) || 0 })}
            />
          </FormField>
          <FormField label={t('settings.labelAr')}>
            <Input
              dir="rtl"
              value={v.nameAr ?? ''}
              onChange={(e) => setV({ ...v, nameAr: e.target.value || null })}
            />
          </FormField>
          <FormField label={t('settings.labelEn')}>
            <Input
              dir="ltr"
              value={v.nameEn ?? ''}
              onChange={(e) => setV({ ...v, nameEn: e.target.value || null })}
            />
          </FormField>
          <FormField label={t('settings.categories.parent')}>
            <Select
              value={v.parentId ?? NONE}
              onValueChange={(value) => setV({ ...v, parentId: value === NONE ? null : value })}
              options={[
                { value: NONE, label: t('common.none') },
                ...all
                  .filter((c) => c.id !== category?.id)
                  .map((c) => ({ value: c.id, label: names.text(c.nameAr, c.nameEn) || c.code })),
              ]}
            />
          </FormField>
          <FormField label={t('fields.appetite')} hint={t('settings.categories.appetiteHint')}>
            <Input
              type="number"
              min={0}
              step="any"
              value={v.appetite ?? ''}
              onChange={(e) =>
                setV({ ...v, appetite: e.target.value === '' ? null : Number(e.target.value) })
              }
              data-testid="risk-category-appetite"
            />
          </FormField>
          <Checkbox
            label={t('settings.active')}
            checked={v.active}
            onChange={(e) => setV({ ...v, active: e.target.checked })}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}

const defaultSettings: Required<RiskSettingsValue> = {
  topCount: 10,
  reviewReminderWorkingDays: 10,
  actionReminderWorkingDays: 3,
  actionEscalationWorkingDays: 2,
  opportunitiesEnabled: false,
  escalateCritical: true,
  escalateOutsideAppetite: true,
  kriWarningNotifies: false,
  includeProjectRisksByDefault: true,
  organisationCode: '',
};

function SettingsSection() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const settings = useRiskSettings();
  if (settings.isError) return <StatusBanner tone="danger" title={errorMessage(tc, settings.error)} />;
  if (!settings.data) return <Skeleton className="h-32 rounded-lg" />;
  return (
    <Section title={t('settings.general.title')} testId="risk-settings-general">
      <SettingsForm
        key={settings.data.version}
        initial={{ ...defaultSettings, ...(settings.data.value ?? {}) }}
        version={settings.data.version}
      />
    </Section>
  );
}

function SettingsForm({ initial, version }: { initial: Required<RiskSettingsValue>; version: number }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useRiskMutation(riskApi.saveSettings);
  const [v, setV] = useState(initial);
  const number = (
    key:
      'topCount' | 'reviewReminderWorkingDays' | 'actionReminderWorkingDays' | 'actionEscalationWorkingDays',
  ): ReactNode => (
    <FormField label={t(`settings.general.${key}`)}>
      <Input
        type="number"
        min={0}
        value={v[key]}
        onChange={(e) => setV({ ...v, [key]: Number(e.target.value) || 0 })}
      />
    </FormField>
  );
  const flag = (
    key:
      | 'opportunitiesEnabled'
      | 'escalateCritical'
      | 'escalateOutsideAppetite'
      | 'kriWarningNotifies'
      | 'includeProjectRisksByDefault',
  ) => (
    <Checkbox
      label={t(`settings.general.${key}`)}
      checked={v[key]}
      onChange={(e) => setV({ ...v, [key]: e.target.checked })}
    />
  );
  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {number('topCount')}
        {number('reviewReminderWorkingDays')}
        {number('actionReminderWorkingDays')}
        {number('actionEscalationWorkingDays')}
        <FormField label={t('settings.general.organisationCode')}>
          <Input
            dir="ltr"
            value={v.organisationCode}
            onChange={(e) => setV({ ...v, organisationCode: e.target.value })}
          />
        </FormField>
      </div>
      <div className="flex flex-col gap-2">
        {flag('opportunitiesEnabled')}
        {flag('escalateCritical')}
        {flag('escalateOutsideAppetite')}
        {flag('kriWarningNotifies')}
        {flag('includeProjectRisksByDefault')}
      </div>
      <Button
        className="self-end"
        loading={save.isPending}
        data-testid="risk-settings-save"
        onClick={() =>
          save.mutate(
            { settings: v, version },
            {
              onSuccess: () => toast({ tone: 'success', title: tc('status.saved') }),
              onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
            },
          )
        }
      >
        {tc('actions.save')}
      </Button>
    </div>
  );
}
