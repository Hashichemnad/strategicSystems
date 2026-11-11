import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Sparkles } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  PageHeader,
  RecordLayout,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  Wizard,
  Chart,
  lineOption,
  Section,
  token,
  useToast,
} from '@/ui';
import {
  kpiApi,
  previewPerformance,
  ragOf,
  resultTone,
  targetText,
  useKpiMutation,
  useKpiOptions,
  useKpiResult,
  type KpiEntryInput,
  type KpiResult,
} from './api';
import { KpiNav, PerformanceValue, RagChip, ScoreBar, useKpiT, useNumber, usePercent } from './components';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/kpi/results/$resultId');

/** KPI result page (SPEC K3, K4): status banner, Submit result wizard, reviewer checklist, AI evidence check, approvals and tasks. */
export function KpiResultPage() {
  const { t: tc } = useTranslation();
  const { resultId } = route.useParams();
  const result = useKpiResult(resultId);
  if (result.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (result.isError) return <StatusBanner tone="danger" title={errorMessage(tc, result.error)} />;
  return <ResultView key={result.data.id} result={result.data} />;
}

function ResultView({ result }: { result: KpiResult }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const toast = useToast();
  const recall = useKpiMutation(kpiApi.recall);
  const openNow = useKpiMutation(kpiApi.openNow);
  const [reopen, setReopen] = useState(false);
  const onError = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });
  const current = tab ?? (result.can.review ? 'review' : 'result');

  const go = (next: 'result' | 'review' | 'approvals' | 'tasks' | 'history') =>
    void navigate({ search: { tab: next } });
  return (
    <RecordLayout
      testId="kpi-result-page"
      back={
        <>
          <KpiNav />
          <Link
            to="/kpi/cards/$cardId"
            params={{ cardId: result.card.id }}
            className="flex items-center gap-1 text-sm text-primary hover:underline"
          >
            <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
            <bdi>{result.card.code}</bdi>
          </Link>
        </>
      }
      highlights={[
        { key: 'code', label: t('fields.code'), value: <bdi>{result.card.code}</bdi> },
        {
          key: 'period',
          label: t('fields.period'),
          value: `${k(`period.${result.periodCode}`)} ${result.year}`,
        },
        {
          key: 'dueOn',
          label: t('result.dueOn'),
          value: result.dueOn ? formatDate(result.dueOn, language) : '—',
        },
        {
          key: 'submittedAt',
          label: t('result.submittedAt'),
          value: result.submittedAt ? formatDateTime(result.submittedAt, language) : '—',
        },
        {
          key: 'owner',
          label: t('result.owner'),
          value: result.owner ? pickText(language, result.owner.nameAr, result.owner.nameEn) : '—',
        },
        {
          key: 'evidence',
          label: t('result.evidence'),
          value: t('result.evidenceCount', { count: result.evidenceCount }),
        },
      ]}
      related={(['review', 'approvals', 'tasks', 'history'] as const).map((key) => ({
        key,
        label: t(`result.tabs.${key}`),
        onOpen: () => go(key),
      }))}
      header={
        <PageHeader
          title={pickText(language, result.card.nameAr, result.card.nameEn)}
          description={
            <>
              <bdi>{result.card.code}</bdi> · {k(`period.${result.periodCode}`)} {result.year} ·{' '}
              {formatDate(result.periodStart, language)} – {formatDate(result.periodEnd, language)}
            </>
          }
          meta={
            <>
              <OriginBadge type="kpi.result" id={result.id} />
              <StatusChip tone={resultTone[result.status] ?? 'neutral'}>
                {k(`resultStatus.${result.status}`)}
              </StatusChip>
              <PerformanceValue capped={result.capped} actual={result.actual} />
              <RagChip rag={result.rag} />
              {result.late && (
                <StatusChip tone="warning">{t('result.lateDays', { count: result.daysLate })}</StatusChip>
              )}
              {result.notApplicable && <StatusChip tone="neutral">{t('result.na')}</StatusChip>}
              {result.draftFromMcp && <StatusChip tone="info">{t('result.draftFromMcp')}</StatusChip>}
            </>
          }
          actions={
            <div className="flex flex-wrap gap-2">
              {result.can.recall && (
                <Button
                  variant="outline"
                  loading={recall.isPending}
                  onClick={() => recall.mutate({ id: result.id, version: result.version }, { onError })}
                >
                  {t('actions.recall')}
                </Button>
              )}
              {result.can.reopen && (
                <Button variant="outline" onClick={() => setReopen(true)} data-testid="kpi-reopen">
                  {t('actions.reopen')}
                </Button>
              )}
              {result.can.openNow && (
                <Button
                  variant="outline"
                  loading={openNow.isPending}
                  onClick={() => openNow.mutate(result.id, { onError })}
                >
                  {t('actions.openNow')}
                </Button>
              )}
            </div>
          }
        />
      }
      status={
        <CurrentStatusBanner
          resourceType="kpi.result"
          resourceId={result.id}
          actionLabel={(code) => k(`actions.${code}`)}
          onAction={(code) => {
            if (code === 'submit') void navigate({ search: { tab: 'result' } });
            if (code === 'review') void navigate({ search: { tab: 'review' } });
            if (code === 'recall') recall.mutate({ id: result.id, version: result.version }, { onError });
          }}
        />
      }
    >
      <Tabs value={current} onValueChange={(value) => void navigate({ search: { tab: value as 'result' } })}>
        <TabsList className="flex-wrap">
          {(['result', 'review', 'approvals', 'tasks', 'history'] as const).map((key) => (
            <TabsTrigger key={key} value={key}>
              {t(`result.tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="result">
          <div className="grid gap-6 xl:grid-cols-[2fr_1fr]">
            {result.can.submit ? <SubmitWizard result={result} /> : <ResultSummary result={result} />}
            <History result={result} />
          </div>
        </TabsContent>
        <TabsContent value="review">
          <div className="grid gap-6 xl:grid-cols-[2fr_1fr]">
            <div className="flex flex-col gap-6">
              <ResultSummary result={result} />
              {result.can.review && <ReviewPanel result={result} />}
              <DocumentsPanel ownerType="kpi.result" ownerId={result.id} canEdit={false} />
            </div>
            <div className="flex flex-col gap-6">
              <Anomalies result={result} />
              <EvidenceCheck result={result} />
              <History result={result} />
              <ExternalReferencesSection type="kpi.result" id={result.id} />
            </div>
          </div>
        </TabsContent>
        <TabsContent value="approvals">
          <ApprovalPanel resourceType="kpi.result" resourceId={result.id} />
        </TabsContent>
        <TabsContent value="tasks">
          <LinkedTasksPanel type="kpi.result" id={result.id} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="kpiResult" id={result.id} />
        </TabsContent>
      </Tabs>
      {reopen && <ReopenDialog result={result} onClose={() => setReopen(false)} />}
    </RecordLayout>
  );
}

function ResultSummary({ result }: { result: KpiResult }) {
  const { t } = useTranslation('kpi');
  const { language } = useKpiT();
  const num = useNumber();
  const rows: [string, string | null][] = [
    [t('fields.measuringEquation'), result.card.measuringEquation],
    [t('fields.target'), targetText(num, result.targetType, result.target, result.rangeMin, result.rangeMax)],
    [t('fields.achieve'), result.notApplicable ? t('result.na') : num(result.achieve)],
    [t('result.naReason'), result.naReason],
    [t('fields.causality'), result.causality],
    [t('fields.correctiveActions'), result.correctiveActions],
    [t('fields.notes'), result.notes],
    [t('result.dueOn'), result.dueOn ? formatDate(result.dueOn, language) : null],
    [t('result.submittedAt'), result.submittedAt ? formatDateTime(result.submittedAt, language) : null],
    [t('result.owner'), result.owner ? pickText(language, result.owner.nameAr, result.owner.nameEn) : null],
    [t('result.statusReason'), result.statusReason],
  ];
  return (
    <dl className="grid gap-4 rounded-lg border bg-card p-6 md:grid-cols-2" data-testid="kpi-result-summary">
      {rows
        .filter(([, value]) => value)
        .map(([label, value]) => (
          <div key={label}>
            <dt className="text-sm text-muted-foreground">{label}</dt>
            <dd className="mt-1 whitespace-pre-line">{value}</dd>
          </div>
        ))}
      <div>
        <dt className="text-sm text-muted-foreground">{t('result.evidence')}</dt>
        <dd className="mt-1">{t('result.evidenceCount', { count: result.evidenceCount })}</dd>
      </div>
    </dl>
  );
}

/** Submit result wizard (SPEC K3): value (or data elements / scale level) → causality and corrective actions → evidence; or Not Applicable. */
function SubmitWizard({ result }: { result: KpiResult }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { k, language } = useKpiT();
  const num = useNumber();
  const toast = useToast();
  const options = useKpiOptions();
  const aiCausality = useAiFeature('kpi-causality');
  const card = result.card;
  const composite = !!card.formula && card.dataElements.length > 0;
  const qualitative = card.nature === 'qualitative';
  const [entry, setEntry] = useState<KpiEntryInput>({
    achieve: result.achieve,
    elementValues: result.elementValues,
    scaleValue: result.scaleValue,
    causality: result.causality,
    correctiveActions: result.correctiveActions,
    notes: result.notes,
    notApplicable: result.notApplicable,
    naReason: result.naReason,
  });
  const [comment, setComment] = useState('');
  const [aiBusy, setAiBusy] = useState(false);
  const [weak, setWeak] = useState<string[]>();
  const save = useKpiMutation(kpiApi.saveDraft);
  const submit = useKpiMutation(kpiApi.submit);
  const set = (patch: Partial<KpiEntryInput>) => setEntry((e) => ({ ...e, ...patch }));
  const n = (v: string) => (v === '' ? null : Number(v));
  const value = qualitative ? entry.scaleValue : composite ? null : entry.achieve;
  const preview = useMemo(
    () =>
      previewPerformance(
        result.targetType,
        card.direction,
        result.target,
        result.rangeMin,
        result.rangeMax,
        value,
      ),
    [result, card.direction, value],
  );
  const missed = !composite && preview.capped != null && preview.capped < 1;
  const onError = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });
  const target = targetText(num, result.targetType, result.target, result.rangeMin, result.rangeMax);
  const unit = options.data?.lookups['kpi.unit']?.find((o) => o.code === card.unitCode);
  const unitLabel = unit ? pickText(language, unit.labelAr, unit.labelEn) : card.unitCode;

  return (
    <section className="flex flex-col gap-4 rounded-lg border bg-card p-5" data-testid="kpi-submit-wizard">
      <h2 className="font-semibold">{t('submit.title')}</h2>
      {result.status === 'returned' && result.statusReason && (
        <StatusBanner tone="warning" title={t('submit.returnedTitle')} description={result.statusReason} />
      )}
      <Wizard
        busy={submit.isPending || save.isPending}
        finishLabel={t('submit.submit')}
        onSaveDraft={() =>
          save.mutate(
            { id: result.id, entry, version: result.version },
            { onSuccess: () => toast({ title: t('submit.saved') }), onError },
          )
        }
        onFinish={() =>
          submit.mutate(
            { id: result.id, entry, version: result.version, comment: comment || null },
            { onSuccess: () => toast({ title: t('submit.submitted') }), onError },
          )
        }
        steps={[
          {
            key: 'value',
            title: t('submit.steps.value'),
            content: (
              <div className="flex flex-col gap-4" data-testid="kpi-step-value">
                <p className="text-sm">
                  {t('fields.target')}: <bdi className="font-semibold">{target}</bdi> · {t('fields.unitCode')}
                  : {unitLabel} · {k(`direction.${card.direction}`)}
                </p>
                <Switch
                  label={t('submit.notApplicable')}
                  description={t('submit.notApplicableHint')}
                  checked={entry.notApplicable}
                  onCheckedChange={(checked) => set({ notApplicable: checked })}
                />
                {entry.notApplicable ? (
                  <FormField label={t('result.naReason')} required>
                    <Textarea
                      value={entry.naReason ?? ''}
                      onChange={(e) => set({ naReason: e.target.value || null })}
                      data-testid="kpi-na-reason"
                    />
                  </FormField>
                ) : qualitative ? (
                  <FormField label={t('submit.level')} required>
                    <Select
                      value={entry.scaleValue == null ? undefined : String(entry.scaleValue)}
                      onValueChange={(v) => set({ scaleValue: Number(v) })}
                      options={card.scale.map((s) => ({
                        value: String(s.value),
                        label: `${num(s.value)} — ${pickText(language, s.labelAr, s.labelEn)}`,
                      }))}
                    />
                  </FormField>
                ) : composite ? (
                  <div className="grid gap-3 md:grid-cols-2">
                    {card.dataElements.map((el) => (
                      <FormField key={el.code} label={pickText(language, el.nameAr, el.nameEn) || el.code}>
                        <Input
                          type="number"
                          value={entry.elementValues?.[el.code] ?? ''}
                          onChange={(e) =>
                            set({
                              elementValues: { ...(entry.elementValues ?? {}), [el.code]: n(e.target.value) },
                            })
                          }
                        />
                      </FormField>
                    ))}
                    <p className="text-sm text-muted-foreground md:col-span-2">
                      {t('fields.formula')}: <code dir="ltr">{card.formula}</code>
                    </p>
                  </div>
                ) : (
                  <FormField label={t('submit.achieved', { unit: unitLabel })} required>
                    <Input
                      type="number"
                      value={entry.achieve ?? ''}
                      onChange={(e) => set({ achieve: n(e.target.value) })}
                      data-testid="kpi-achieve"
                    />
                  </FormField>
                )}
                {!entry.notApplicable && !composite && (
                  <div
                    className="flex flex-col gap-2 rounded-md bg-muted/50 p-3 text-sm"
                    data-testid="kpi-preview"
                  >
                    <span className="flex items-center gap-2">
                      {t('submit.preview')}:{' '}
                      <PerformanceValue capped={preview.capped} actual={preview.actual} />
                      <RagChip
                        rag={ragOf(
                          preview.capped,
                          options.data?.greenFrom ?? 1,
                          options.data?.amberFrom ?? 1,
                        )}
                      />
                    </span>
                    <ScoreBar
                      score={preview.capped}
                      rag={ragOf(preview.capped, options.data?.greenFrom ?? 1, options.data?.amberFrom ?? 1)}
                    />
                  </div>
                )}
              </div>
            ),
          },
          {
            key: 'narrative',
            title: t('submit.steps.narrative'),
            content: (
              <div className="flex flex-col gap-4" data-testid="kpi-step-narrative">
                {missed && <StatusBanner tone="warning" title={t('submit.missedHint')} />}
                <FormField label={t('fields.causality')} required={missed}>
                  <Textarea
                    value={entry.causality ?? ''}
                    onChange={(e) => set({ causality: e.target.value || null })}
                    data-testid="kpi-causality"
                  />
                </FormField>
                <FormField label={t('fields.correctiveActions')} required={missed}>
                  <Textarea
                    value={entry.correctiveActions ?? ''}
                    onChange={(e) => set({ correctiveActions: e.target.value || null })}
                    data-testid="kpi-corrective-actions"
                  />
                </FormField>
                <FormField label={t('fields.notes')}>
                  <Textarea
                    value={entry.notes ?? ''}
                    onChange={(e) => set({ notes: e.target.value || null })}
                  />
                </FormField>
                {aiCausality && (
                  <div className="flex flex-col gap-2">
                    <Button
                      variant="outline"
                      className="w-fit"
                      loading={aiBusy}
                      onClick={() => {
                        setAiBusy(true);
                        kpiApi
                          .causality({ id: result.id, notes: entry.notes })
                          .then((draft) => {
                            set({ causality: draft.causality, correctiveActions: draft.correctiveActions });
                            setWeak(draft.weak ? draft.weakReasons : []);
                          })
                          .catch(onError)
                          .finally(() => setAiBusy(false));
                      }}
                    >
                      <Sparkles aria-hidden />
                      {t('ai.causality.action')}
                    </Button>
                    {weak && (
                      <StatusBanner
                        tone={weak.length ? 'warning' : 'info'}
                        title={weak.length ? t('ai.causality.weak') : t('ai.draftLabel')}
                        description={weak.join(' · ') || t('ai.causality.review')}
                      />
                    )}
                  </div>
                )}
              </div>
            ),
          },
          {
            key: 'evidence',
            title: t('submit.steps.evidence'),
            content: (
              <div className="flex flex-col gap-4" data-testid="kpi-step-evidence">
                <p className="text-sm text-muted-foreground">{t('submit.evidenceHint')}</p>
                <DocumentsPanel ownerType="kpi.result" ownerId={result.id} canEdit />
                <FormField label={t('submit.comment')}>
                  <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
                </FormField>
              </div>
            ),
          },
        ]}
      />
    </section>
  );
}

/** Reviewer panel (SPEC K4.3): the configurable verification checklist, then approve / return / reject with a comment. */
function ReviewPanel({ result }: { result: KpiResult }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { language } = useKpiT();
  const toast = useToast();
  const options = useKpiOptions();
  const items = options.data?.checklist ?? [];
  const [answers, setAnswers] = useState<Record<string, boolean>>(result.checklist);
  const [comment, setComment] = useState('');
  const decide = useKpiMutation(kpiApi.decide);
  const complete = items.filter((i) => i.required).every((i) => answers[i.code]);
  const run = (action: 'approve' | 'return' | 'reject') =>
    decide.mutate(
      { id: result.id, action, comment: comment || null, checklist: answers },
      {
        onSuccess: () => toast({ title: t(`review.done.${action}`) }),
        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      },
    );
  return (
    <section
      className="flex flex-col gap-4 rounded-lg border border-primary/40 bg-card p-5"
      data-testid="kpi-review-panel"
    >
      <h2 className="font-semibold">{t('review.title')}</h2>
      <fieldset className="flex flex-col gap-2" data-testid="kpi-checklist">
        <legend className="mb-1 text-sm text-muted-foreground">{t('review.checklist')}</legend>
        {items.map((item) => (
          <Checkbox
            key={item.code}
            data-testid="kpi-checklist-item"
            label={
              <>
                {pickText(language, item.labelAr, item.labelEn)}
                {item.required && (
                  <span className="ms-0.5 text-danger" aria-hidden>
                    *
                  </span>
                )}
              </>
            }
            checked={!!answers[item.code]}
            onChange={(e) => setAnswers((a) => ({ ...a, [item.code]: e.target.checked }))}
          />
        ))}
      </fieldset>
      <FormField label={t('review.comment')} hint={t('review.commentHint')}>
        <Textarea
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          data-testid="kpi-review-comment"
        />
      </FormField>
      <div className="flex flex-wrap gap-2">
        <Button
          disabled={!complete}
          loading={decide.isPending}
          onClick={() => run('approve')}
          data-testid="kpi-decide-approve"
        >
          {t('review.approve')}
        </Button>
        <Button
          variant="outline"
          disabled={!comment.trim()}
          onClick={() => run('return')}
          data-testid="kpi-decide-return"
        >
          {t('review.return')}
        </Button>
        <Button
          variant="danger"
          disabled={!comment.trim()}
          onClick={() => run('reject')}
          data-testid="kpi-decide-reject"
        >
          {t('review.reject')}
        </Button>
      </div>
      {!complete && <p className="text-xs text-muted-foreground">{t('review.checklistRequired')}</p>}
    </section>
  );
}

function Anomalies({ result }: { result: KpiResult }) {
  const { t } = useTranslation('kpi');
  const { k } = useKpiT();
  return (
    <section className="flex flex-col gap-2 rounded-lg border bg-card p-5" data-testid="kpi-anomalies">
      <h2 className="font-semibold">{t('ai.anomalies.title')}</h2>
      {result.anomalies.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('ai.anomalies.none')}</p>
      ) : (
        <ul className="flex flex-col gap-1 text-sm">
          {result.anomalies.map((a) => (
            <li key={a}>
              <StatusChip tone="warning">{k(`anomaly.${a}`)}</StatusChip>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function EvidenceCheck({ result }: { result: KpiResult }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const enabled = useAiFeature('kpi-evidence-check');
  const [check, setCheck] = useState<Awaited<ReturnType<typeof kpiApi.evidenceCheck>>>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  if (!enabled) return null;
  const yesNo = (v: boolean | null) =>
    v == null ? t('ai.evidence.unclear') : v ? t('common.yes') : t('common.no');
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" data-testid="kpi-evidence-check">
      <header className="flex items-center justify-between gap-2">
        <h2 className="font-semibold">{t('ai.evidence.title')}</h2>
        <Button
          variant="outline"
          size="sm"
          loading={busy}
          disabled={result.evidenceCount === 0}
          onClick={() => {
            setBusy(true);
            setError(undefined);
            kpiApi
              .evidenceCheck(result.id)
              .then(setCheck)
              .catch((e: unknown) => setError(errorMessage(tc, e)))
              .finally(() => setBusy(false));
          }}
        >
          <Sparkles aria-hidden />
          {t('ai.evidence.action')}
        </Button>
      </header>
      {error && <StatusBanner tone="danger" title={error} />}
      {check && (
        <div className="flex flex-col gap-2 text-sm">
          <StatusChip tone="info" className="w-fit">
            {t('ai.advisoryLabel')}
          </StatusChip>
          <p>
            {t('ai.evidence.valueFound')}: <strong>{yesNo(check.valueFound)}</strong> ·{' '}
            {t('ai.evidence.periodMatches')}: <strong>{yesNo(check.periodMatches)}</strong>
          </p>
          <p>{check.summary}</p>
          {check.missing.length > 0 && (
            <ul className="list-disc ps-5">
              {check.missing.map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}

function History({ result }: { result: KpiResult }) {
  const { t } = useTranslation('kpi');
  const { k } = useKpiT();
  const num = useNumber();
  const pct = usePercent();
  const label = (h: KpiResult['history'][number]) => `${k(`period.${h.periodCode}`)} ${h.year}`;
  const spec = lineOption({
    categories: result.history.map((h) => ({ key: h.resultId, label: label(h) })),
    series: [
      {
        name: t('result.trend'),
        values: result.history.map((h) => h.capped ?? null),
        color: token('primary'),
      },
    ],
    format: (v) => pct(v),
    min: 0,
  });
  return (
    <Section title={t('result.trend')} testId="kpi-history-trend">
      {result.history.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('common.noData')}</p>
      ) : (
        <Chart
          {...spec}
          height={220}
          label={t('result.trend')}
          table={{
            columns: [t('fields.period'), t('result.trend'), t('fields.achieve')],
            rows: result.history.map((h) => [
              label(h),
              pct(h.capped),
              h.notApplicable ? t('result.na') : num(h.achieve),
            ]),
          }}
        />
      )}
    </Section>
  );
}

function ReopenDialog({ result, onClose }: { result: KpiResult; onClose: () => void }) {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const reopen = useKpiMutation(kpiApi.reopen);
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('actions.reopen')}
        description={t('reopen.hint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!reason.trim()}
              loading={reopen.isPending}
              onClick={() =>
                reopen.mutate(
                  { id: result.id, reason },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {t('actions.reopen')}
            </Button>
          </>
        }
      >
        <FormField label={t('reopen.reason')} required>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}
