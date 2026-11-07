import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import {
  ArrowLeft,
  Flag,
  Gavel,
  Pencil,
  ShieldCheck,
  Trash2,
  TrendingDown,
  TrendingUp,
  TriangleAlert,
  XCircle,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDate, formatDateTime } from '@/lib/format';
import {
  Button,
  Checkbox,
  ConfirmDialog,
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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  Chart,
  lineOption,
  token,
  useToast,
  Section,
} from '@/ui';
import { ActionsTab } from './ActionsTab';
import {
  residualStatuses,
  riskApi,
  statusTone,
  useCommitteeDecisions,
  useRisk,
  useRiskAssessments,
  useRiskMutation,
  useRiskOptions,
  type Risk,
  type RiskAssessment,
} from './api';
import {
  RatingChip,
  RiskMatrix,
  RiskPageLayout,
  Rows,
  useAxisLabels,
  useBands,
  useNames,
  useNum,
  useRiskLabels,
  useRiskT,
} from './components';
import { ControlsTab } from './ControlsTab';
import { EventsTab } from './EventsTab';
import { KrisTab } from './KrisTab';
import { ReviewsTab } from './ReviewsTab';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/risk/risks/$riskId');

export const riskTabs = [
  'overview',
  'assessment',
  'controls',
  'actions',
  'kris',
  'events',
  'reviews',
  'documents',
  'activity',
] as const;
export type RiskTab = (typeof riskTabs)[number];

type DialogKind = 'submit' | 'closure' | 'reopen' | 'office' | 'escalate' | 'decision' | 'discard';

/** Risk page (docs/design/risk.md §12): header with I → R → T chips, current status with actions, approvals and the tabs. */
export function RiskPage() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k } = useRiskT();
  const { riskId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const toast = useToast();
  const risk = useRisk(riskId);
  const names = useNames();
  const recall = useRiskMutation(riskApi.recall);
  const startReview = useRiskMutation(riskApi.startReview);
  const [dialog, setDialog] = useState<DialogKind>();
  const onError = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });

  if (risk.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (risk.isError) return <StatusBanner tone="danger" title={errorMessage(tc, risk.error)} />;
  const r = risk.data;
  const go = (next: RiskTab) => void navigate({ search: { tab: next } });

  return (
    <RiskPageLayout testId="risk-page">
      <Link to="/risk/risks" className="flex items-center gap-1 text-sm text-primary hover:underline">
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('register.title')}
      </Link>
      <PageHeader
        title={names.text(r.values.titleAr, r.values.titleEn) || r.number}
        description={
          <>
            <bdi data-testid="risk-number">{r.number}</bdi> · {names.unit(r.unit)} · {t('fields.owner')}:{' '}
            {names.person(r.owner)}
          </>
        }
        meta={
          <div className="flex flex-wrap items-center gap-2" data-testid="risk-header-meta">
            <OriginBadge type="risk.risk" id={r.id} />
            <StatusChip tone={statusTone[r.status] ?? 'neutral'}>
              <span data-testid="risk-status">{k(`status.${r.status}`)}</span>
            </StatusChip>
            <span className="flex flex-wrap items-center gap-1" data-testid="risk-ratings">
              <RatingChip
                band={r.inherentBand}
                score={r.inherentScore}
                prefix={t('rating.inherentShort')}
                testId="risk-chip-inherent"
              />
              <span aria-hidden className="rtl:rotate-180">
                →
              </span>
              <RatingChip
                band={r.residualBand}
                score={r.residualScore}
                prefix={t('rating.residualShort')}
                testId="risk-chip-residual"
              />
              {r.targetBand && (
                <>
                  <span aria-hidden className="rtl:rotate-180">
                    →
                  </span>
                  <RatingChip
                    band={r.targetBand}
                    score={r.targetScore}
                    prefix={t('rating.targetShort')}
                    testId="risk-chip-target"
                  />
                </>
              )}
            </span>
            {r.top10 && (
              <StatusChip tone="danger">
                <Flag className="me-1 size-3" aria-hidden />
                {t('flags.top10', { rank: r.top10Rank ?? '' })}
              </StatusChip>
            )}
            {r.escalated && (
              <StatusChip tone="warning">
                <TrendingUp className="me-1 size-3" aria-hidden />
                {t('flags.escalated')}
              </StatusChip>
            )}
            {r.reassessmentRequired && (
              <StatusChip tone="warning">
                <TriangleAlert className="me-1 size-3" aria-hidden />
                {t('flags.reassessment')}
              </StatusChip>
            )}
            {r.withinAppetite === false && (
              <StatusChip tone="danger">{t('flags.outsideAppetite')}</StatusChip>
            )}
            <span
              className="text-xs text-muted-foreground"
              title={r.onCurrentFramework ? undefined : t('risk.oldFramework')}
              data-testid="risk-framework-version"
            >
              {t('risk.frameworkVersion', { version: r.frameworkVersionNo })}
              {!r.onCurrentFramework && (
                <span className="ms-1 text-warning">({t('risk.oldFrameworkShort')})</span>
              )}
            </span>
          </div>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            {r.can.edit && (
              <Button variant="outline" asChild>
                <Link to="/risk/risks/$riskId/edit" params={{ riskId: r.id }} data-testid="risk-edit">
                  <Pencil aria-hidden />
                  {tc('actions.edit')}
                </Link>
              </Button>
            )}
            {r.can.requestClosure && (
              <Button
                variant="outline"
                onClick={() => setDialog('closure')}
                data-testid="risk-request-closure"
              >
                <XCircle aria-hidden />
                {t('risk.requestClosure')}
              </Button>
            )}
            {r.can.office && (
              <>
                <Button variant="outline" onClick={() => setDialog('office')} data-testid="risk-office">
                  <ShieldCheck aria-hidden />
                  {t('office.action')}
                </Button>
                <Button variant="outline" onClick={() => setDialog('escalate')} data-testid="risk-escalate">
                  {r.escalated ? <TrendingDown aria-hidden /> : <TrendingUp aria-hidden />}
                  {r.escalated ? t('escalation.deescalate') : t('escalation.escalate')}
                </Button>
              </>
            )}
            {r.can.committeeDecide && (
              <Button
                variant="outline"
                onClick={() => setDialog('decision')}
                data-testid="risk-committee-decision"
              >
                <Gavel aria-hidden />
                {t('committee.record')}
              </Button>
            )}
            {r.can.discard && (
              <Button variant="ghost" onClick={() => setDialog('discard')} data-testid="risk-discard">
                <Trash2 aria-hidden />
                {t('risk.discard')}
              </Button>
            )}
          </div>
        }
      />
      <div data-testid="risk-current-status">
        <CurrentStatusBanner
          resourceType="risk.risk"
          resourceId={r.id}
          onAction={(code) => {
            if (code === 'submit') setDialog('submit');
            if (code === 'recall') recall.mutate(r.id, { onError });
            if (code === 'reopen') setDialog('reopen');
            if (code === 'review') {
              go('overview');
              setTimeout(
                () => document.getElementById('risk-approvals')?.scrollIntoView({ behavior: 'smooth' }),
                50,
              );
            }
            if (code === 'start-review')
              startReview.mutate(r.id, {
                onSuccess: () => go('reviews'),
                onError: (e) => {
                  go('reviews');
                  onError(e);
                },
              });
          }}
        />
      </div>
      {r.returned && r.returnComment && (
        <StatusBanner tone="warning" title={t('risk.returnComment')} description={r.returnComment} />
      )}
      <Tabs value={tab ?? 'overview'} onValueChange={(value) => go(value as RiskTab)}>
        <TabsList className="flex-wrap" data-testid="risk-tabs">
          {riskTabs.map((key) => (
            <TabsTrigger key={key} value={key} data-testid={`risk-tab-${key}`}>
              {t(`tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <div className="grid gap-6 xl:grid-cols-[2fr_1fr]">
            <Overview risk={r} />
            <div id="risk-approvals" className="flex flex-col gap-3" data-testid="risk-approval-panel">
              <h2 className="font-semibold">{t('risk.approvals')}</h2>
              <ApprovalPanel resourceType="risk.risk" resourceId={r.id} />
            </div>
          </div>
          <ExternalReferencesSection type="risk.risk" id={r.id} />
        </TabsContent>
        <TabsContent value="assessment">
          <AssessmentTab risk={r} />
        </TabsContent>
        <TabsContent value="controls">
          <ControlsTab risk={r} />
        </TabsContent>
        <TabsContent value="actions">
          <ActionsTab risk={r} />
        </TabsContent>
        <TabsContent value="kris">
          <KrisTab risk={r} />
        </TabsContent>
        <TabsContent value="events">
          <EventsTab risk={r} />
        </TabsContent>
        <TabsContent value="reviews">
          <ReviewsTab risk={r} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel ownerType="risk.risk" ownerId={r.id} canEdit={r.can.edit || r.can.manageChildren} />
        </TabsContent>
        <TabsContent value="activity">
          <HistoryTab record="risk" id={r.id} />
        </TabsContent>
      </Tabs>
      {dialog === 'submit' && <SubmitDialog risk={r} onClose={() => setDialog(undefined)} />}
      {dialog === 'closure' && <ClosureDialog risk={r} onClose={() => setDialog(undefined)} />}
      {dialog === 'reopen' && <ReopenDialog risk={r} onClose={() => setDialog(undefined)} />}
      {dialog === 'office' && <OfficeDialog risk={r} onClose={() => setDialog(undefined)} />}
      {dialog === 'escalate' && <EscalationDialog risk={r} onClose={() => setDialog(undefined)} />}
      {dialog === 'decision' && <DecisionDialog risk={r} onClose={() => setDialog(undefined)} />}
      {dialog === 'discard' && <DiscardDialog risk={r} onClose={() => setDialog(undefined)} />}
    </RiskPageLayout>
  );
}

function Overview({ risk: r }: { risk: Risk }) {
  const { t } = useTranslation('risk');
  const { k, language } = useRiskT();
  const labels = useRiskLabels();
  const names = useNames();
  const num = useNum();
  const v = r.values;
  const date = (d: string | null | undefined) => (d ? formatDate(d, language) : '—');
  return (
    <div className="flex flex-col gap-4" data-testid="risk-overview">
      <Section title={t('overview.statement')}>
        <ol className="flex flex-col gap-2 text-sm md:flex-row md:items-stretch" data-testid="risk-statement">
          {(
            [
              ['cause', v.cause],
              ['eventText', v.eventText],
              ['consequence', v.consequence],
            ] as const
          ).map(([key, value], index) => (
            <li key={key} className="flex flex-1 items-center gap-2">
              <div className="flex-1 rounded border bg-muted/30 p-3">
                <div className="text-xs text-muted-foreground">{t(`fields.${key}`)}</div>
                <div className="whitespace-pre-line">{value || '—'}</div>
              </div>
              {index < 2 && (
                <span aria-hidden className="hidden text-muted-foreground md:inline rtl:rotate-180">
                  →
                </span>
              )}
            </li>
          ))}
        </ol>
      </Section>
      <Rows
        rows={[
          [t('fields.categories'), v.categoryCodes.map(labels.category).join('، ')],
          [t('fields.riskType'), k(`riskType.${v.riskType}`)],
          [t('fields.determinedBy'), labels.lookup('risk.determined-by', v.determinedByCode)],
          [t('fields.identifiedOn'), date(v.identifiedOn)],
          [t('fields.exposureStart'), date(v.exposureStart)],
          [t('fields.emerging'), v.emerging ? t('common.yes') : t('common.no')],
          [t('fields.opportunity'), v.opportunity ? t('common.yes') : t('common.no')],
          [t('fields.processes'), v.processes],
          [t('fields.objectives'), (v.objectiveCodes ?? []).map(labels.objective).join('، ')],
          [t('fields.unit'), names.unit(r.unit)],
          [t('fields.owner'), names.person(r.owner)],
          [t('fields.coordinator'), names.person(r.coordinator)],
          [t('fields.followUp'), names.person(r.followUp)],
          [t('fields.response'), labels.lookup('risk.response', v.responseCode)],
          [t('fields.targetDate'), date(v.targetDate)],
          [t('fields.needsFollowUp'), v.needsFollowUp ? t('common.yes') : t('common.no')],
          [
            t('fields.controlEffectiveness'),
            labels.effectiveness(r.controlEffectivenessOverride ?? r.controlEffectiveness),
          ],
          [
            t('fields.residualStatus'),
            <span key="rs">
              {k(`residualStatus.${r.residualStatus}`)}
              {r.residualStatusOverride && (
                <span className="ms-1 text-xs text-muted-foreground">({t('office.overridden')})</span>
              )}
              {r.residualStatusJustification && (
                <span className="block text-xs text-muted-foreground">{r.residualStatusJustification}</span>
              )}
            </span>,
          ],
          [t('fields.appetite'), r.appetite == null ? t('common.notSet') : num(r.appetite)],
          [
            t('fields.withinAppetite'),
            r.withinAppetite == null ? '—' : r.withinAppetite ? t('common.yes') : t('flags.outsideAppetite'),
          ],
          [t('fields.lastReviewOn'), date(r.lastReviewOn)],
          [
            t('fields.nextReviewOn'),
            <span key="nr" className={r.reviewOverdue ? 'font-medium text-danger' : undefined}>
              {date(r.nextReviewOn)}
            </span>,
          ],
          ...(r.escalated
            ? ([
                [
                  t('escalation.reason'),
                  r.escalationReason ? k(`escalationReason.${r.escalationReason}`) : '—',
                ],
              ] as [string, ReactNode][])
            : []),
          ...(r.closureReasonCode
            ? ([
                [t('closure.reason'), labels.lookup('risk.closure-reason', r.closureReasonCode)],
                [t('closure.note'), r.closureNote],
                [t('closure.closedAt'), r.closedAt ? formatDateTime(r.closedAt, language) : '—'],
              ] as [string, ReactNode][])
            : []),
          ...(r.rejectionReason
            ? ([[t('risk.rejectionReason'), r.rejectionReason]] as [string, ReactNode][])
            : []),
        ]}
      />
      <CommitteeDecisions riskId={r.id} />
    </div>
  );
}

function CommitteeDecisions({ riskId }: { riskId: string }) {
  const { t } = useTranslation('risk');
  const { language } = useRiskT();
  const labels = useRiskLabels();
  const decisions = useCommitteeDecisions(riskId);
  return (
    <Section title={t('committee.title')} testId="risk-committee-decisions">
      {!decisions.data ? (
        <Skeleton className="h-12" />
      ) : decisions.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('committee.empty')}</p>
      ) : (
        <ul className="flex flex-col divide-y text-sm">
          {decisions.data.map((d) => (
            <li key={d.id} className="flex flex-col gap-0.5 py-2">
              <span className="font-medium">
                {labels.lookup('risk.committee-decision', d.decisionCode)} ·{' '}
                {formatDate(d.decidedOn, language)}
              </span>
              {d.note && <span className="text-muted-foreground">{d.note}</span>}
            </li>
          ))}
        </ul>
      )}
    </Section>
  );
}

function AssessmentTab({ risk: r }: { risk: Risk }) {
  const { t } = useTranslation('risk');
  const { k, language } = useRiskT();
  const options = useRiskOptions();
  const axis = useAxisLabels();
  const names = useNames();
  const labels = useRiskLabels();
  const assessments = useRiskAssessments(r.id);
  const v = r.values;
  const markers = [
    {
      likelihood: v.inherentLikelihood,
      impact: v.inherentImpact,
      label: t('rating.inherentShort'),
      title: t('rating.inherent'),
    },
    ...(v.residualLikelihood != null
      ? [
          {
            likelihood: v.residualLikelihood,
            impact: v.residualImpact,
            label: t('rating.residualShort'),
            title: t('rating.residual'),
          },
        ]
      : []),
    ...(v.targetLikelihood != null
      ? [
          {
            likelihood: v.targetLikelihood,
            impact: v.targetImpact,
            label: t('rating.targetShort'),
            title: t('rating.target'),
          },
        ]
      : []),
  ];
  return (
    <div className="flex flex-col gap-4" data-testid="risk-assessment">
      <div className="grid gap-4 xl:grid-cols-2">
        <Section title={t('assessment.matrix')}>
          {options.data && (
            <RiskMatrix
              cells={options.data.matrix}
              bands={options.data.framework.content.bands}
              prefix="assessment"
              label={t('assessment.matrix')}
              axis={axis}
              markers={markers}
            />
          )}
          <p className="text-xs text-muted-foreground">{t('assessment.legend')}</p>
        </Section>
        <Section title={t('assessment.history')} testId="risk-assessment-chart">
          {assessments.data ? <HistoryChart rows={assessments.data} /> : <Skeleton className="h-48" />}
        </Section>
      </div>
      <Section title={t('assessment.snapshots')}>
        {!assessments.data ? (
          <Skeleton className="h-32" />
        ) : assessments.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('common.noData')}</p>
        ) : (
          <Table data-testid="risk-assessments-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('assessment.kind')}</TableHeaderCell>
                <TableHeaderCell>{t('assessment.at')}</TableHeaderCell>
                <TableHeaderCell>{t('assessment.by')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.inherent')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.residual')}</TableHeaderCell>
                <TableHeaderCell>{t('rating.target')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.controlEffectiveness')}</TableHeaderCell>
                <TableHeaderCell>{t('assessment.framework')}</TableHeaderCell>
                <TableHeaderCell>{t('assessment.comment')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {assessments.data.map((a) => (
                <TableRow key={a.id}>
                  <TableCell>{k(`assessmentKind.${a.kind}`)}</TableCell>
                  <TableCell>{formatDateTime(a.at, language)}</TableCell>
                  <TableCell>{names.person(a.by)}</TableCell>
                  <TableCell>
                    <RatingChip band={a.inherentBand} score={a.inherentScore} />
                  </TableCell>
                  <TableCell>
                    <RatingChip band={a.residualBand} score={a.residualScore} />
                  </TableCell>
                  <TableCell>
                    <RatingChip band={a.targetBand} score={a.targetScore} />
                  </TableCell>
                  <TableCell>{labels.effectiveness(a.controlEffectiveness)}</TableCell>
                  <TableCell>v{a.frameworkVersionNo}</TableCell>
                  <TableCell className="max-w-64 whitespace-pre-line">{a.comment ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>
    </div>
  );
}

/** Inherent and residual score over the assessment snapshots (shared ECharts wrapper: time runs from the start side in both directions). */
export function HistoryChart({ rows }: { rows: readonly RiskAssessment[] }) {
  const { t } = useTranslation('risk');
  const { language } = useRiskT();
  const num = useNum();
  const { bands } = useBands();
  const points = [...rows].sort((a, b) => a.at.localeCompare(b.at));
  if (points.length === 0) return <p className="text-sm text-muted-foreground">{t('common.noData')}</p>;
  const max = Math.max(
    1,
    ...bands.map((b) => b.max),
    ...points.map((p) => Math.max(p.inherentScore, p.residualScore)),
  );
  const spec = lineOption({
    categories: points.map((p) => ({ key: p.id, label: formatDate(p.at, language) })),
    series: [
      { name: t('rating.inherent'), values: points.map((p) => p.inherentScore), color: token('danger') },
      { name: t('rating.residual'), values: points.map((p) => p.residualScore), color: token('primary') },
    ],
    format: (v) => num(v),
    min: 0,
    max,
    categoryHeader: t('assessment.at'),
  });
  return (
    <Chart
      {...spec}
      height={220}
      label={t('assessment.history')}
      description={t('assessment.chartLabel', {
        values: points
          .map((p) => `${formatDate(p.at, language)}: ${num(p.inherentScore)} / ${num(p.residualScore)}`)
          .join('; '),
      })}
    />
  );
}

function SimpleDialog({
  title,
  description,
  onClose,
  onSubmit,
  busy,
  submitLabel,
  disabled,
  testId,
  children,
}: {
  title: string;
  description?: string;
  onClose: () => void;
  onSubmit: () => void;
  busy: boolean;
  submitLabel: string;
  disabled?: boolean;
  testId: string;
  children: ReactNode;
}) {
  const { t: tc } = useTranslation();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={title}
        description={description}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button onClick={onSubmit} loading={busy} disabled={disabled} data-testid={testId}>
              {submitLabel}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">{children}</div>
      </DialogContent>
    </Dialog>
  );
}

function useDialogMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>, onClose: () => void) {
  const { t: tc } = useTranslation();
  const toast = useToast();
  const mutation = useRiskMutation(run);
  return {
    busy: mutation.isPending,
    run: (input: TInput, after?: (output: TOutput) => void) =>
      mutation.mutate(input, {
        onSuccess: (output) => {
          onClose();
          after?.(output);
        },
        onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
      }),
  };
}

function SubmitDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { k } = useRiskT();
  const [comment, setComment] = useState('');
  const m = useDialogMutation(riskApi.submit, onClose);
  return (
    <SimpleDialog
      title={k('actions.submit')}
      description={t('risk.submitHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={k('actions.submit')}
      testId="risk-submit-confirm"
      onSubmit={() => m.run({ id: risk.id, comment: comment || null })}
    >
      <FormField label={t('common.comment')}>
        <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function ClosureDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const labels = useRiskLabels();
  const [reasonCode, setReasonCode] = useState<string>();
  const [note, setNote] = useState('');
  const m = useDialogMutation(riskApi.requestClosure, onClose);
  return (
    <SimpleDialog
      title={t('risk.requestClosure')}
      description={t('closure.hint')}
      onClose={onClose}
      busy={m.busy}
      disabled={!reasonCode}
      submitLabel={t('risk.requestClosure')}
      testId="risk-closure-confirm"
      onSubmit={() => reasonCode && m.run({ id: risk.id, reasonCode, note: note || null })}
    >
      <FormField label={t('closure.reason')} required>
        <Select
          value={reasonCode}
          onValueChange={setReasonCode}
          options={labels.lookupOptions('risk.closure-reason')}
        />
      </FormField>
      <FormField label={t('closure.note')}>
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function ReopenDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { k } = useRiskT();
  const [reason, setReason] = useState('');
  const m = useDialogMutation(riskApi.reopen, onClose);
  return (
    <SimpleDialog
      title={k('actions.reopen')}
      onClose={onClose}
      busy={m.busy}
      disabled={!reason.trim()}
      submitLabel={k('actions.reopen')}
      testId="risk-reopen-confirm"
      onSubmit={() => m.run({ id: risk.id, reason })}
    >
      <FormField label={t('common.reason')} required>
        <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function OfficeDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { k } = useRiskT();
  const options = useRiskOptions();
  const labels = useRiskLabels();
  const NONE = '-';
  const [effectiveness, setEffectiveness] = useState(risk.controlEffectivenessOverride ?? NONE);
  const [status, setStatus] = useState(risk.residualStatusOverride ?? NONE);
  const [justification, setJustification] = useState(risk.residualStatusJustification ?? '');
  const [top10, setTop10] = useState(risk.top10);
  const [rank, setRank] = useState(risk.top10Rank == null ? '' : String(risk.top10Rank));
  const m = useDialogMutation(riskApi.office, onClose);
  return (
    <SimpleDialog
      title={t('office.title')}
      description={t('office.hint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('office.save')}
      testId="risk-office-save"
      onSubmit={() =>
        m.run({
          id: risk.id,
          input: {
            controlEffectivenessOverride: effectiveness === NONE ? null : effectiveness,
            residualStatusOverride: status === NONE ? null : status,
            justification: justification || null,
            top10,
            top10Rank: top10 && rank ? Number(rank) : null,
            version: risk.version,
          },
        })
      }
    >
      <FormField
        label={t('office.effectivenessOverride')}
        hint={t('office.computed', { value: labels.effectiveness(risk.controlEffectiveness) })}
      >
        <Select
          value={effectiveness}
          onValueChange={setEffectiveness}
          options={[
            { value: NONE, label: t('office.noOverride') },
            ...(options.data?.framework.content.controlEffectiveness ?? []).map((e) => ({
              value: e.code,
              label: labels.effectiveness(e.code),
            })),
          ]}
        />
      </FormField>
      <FormField label={t('office.statusOverride')}>
        <Select
          value={status}
          onValueChange={setStatus}
          options={[
            { value: NONE, label: t('office.noOverride') },
            ...residualStatuses.map((s) => ({ value: s, label: k(`residualStatus.${s}`) })),
          ]}
        />
      </FormField>
      <FormField label={t('office.justification')}>
        <Textarea value={justification} onChange={(e) => setJustification(e.target.value)} />
      </FormField>
      <Checkbox label={t('office.top10')} checked={top10} onChange={(e) => setTop10(e.target.checked)} />
      {top10 && (
        <FormField label={t('fields.rank')}>
          <Input type="number" min={1} value={rank} onChange={(e) => setRank(e.target.value)} />
        </FormField>
      )}
    </SimpleDialog>
  );
}

function EscalationDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const [note, setNote] = useState('');
  const m = useDialogMutation(riskApi.escalate, onClose);
  const label = risk.escalated ? t('escalation.deescalate') : t('escalation.escalate');
  return (
    <SimpleDialog
      title={label}
      onClose={onClose}
      busy={m.busy}
      submitLabel={label}
      testId="risk-escalation-confirm"
      onSubmit={() => m.run({ id: risk.id, escalated: !risk.escalated, note: note || null })}
    >
      <FormField label={t('common.note')}>
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function DecisionDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const labels = useRiskLabels();
  const [code, setCode] = useState<string>();
  const [note, setNote] = useState('');
  const [decidedOn, setDecidedOn] = useState('');
  const m = useDialogMutation(riskApi.decide, onClose);
  return (
    <SimpleDialog
      title={t('committee.record')}
      onClose={onClose}
      busy={m.busy}
      disabled={!code}
      submitLabel={t('committee.record')}
      testId="risk-decision-confirm"
      onSubmit={() =>
        code && m.run({ id: risk.id, decisionCode: code, note: note || null, decidedOn: decidedOn || null })
      }
    >
      <FormField label={t('committee.decision')} required>
        <Select
          value={code}
          onValueChange={setCode}
          options={labels.lookupOptions('risk.committee-decision')}
        />
      </FormField>
      <FormField label={t('committee.decidedOn')}>
        <Input type="date" value={decidedOn} onChange={(e) => setDecidedOn(e.target.value)} />
      </FormField>
      <FormField label={t('common.note')}>
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function DiscardDialog({ risk, onClose }: { risk: Risk; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const navigate = useNavigate();
  const m = useDialogMutation(riskApi.discard, onClose);
  return (
    <ConfirmDialog
      open
      onOpenChange={(open) => !open && onClose()}
      title={t('risk.discard')}
      description={t('risk.discardHint')}
      confirmLabel={t('risk.discard')}
      destructive
      loading={m.busy}
      onConfirm={() => m.run(risk.id, () => void navigate({ to: '/risk/risks' }))}
    />
  );
}
