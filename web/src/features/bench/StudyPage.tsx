import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Ban, Sparkles } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { actionTone, remarkTone } from '@/features/cap/api';
import { Rows, SimpleDialog, useCapT, useDate, useNames } from '@/features/cap/components';
import { ReasonDialog } from '@/features/cap/forms';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import {
  Button,
  Checkbox,
  FormField,
  PageHeader,
  Skeleton,
  StageStepper,
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
  ProgressBar,
  Section,
} from '@/ui';
import {
  benchApi,
  benchLookups,
  studyStages,
  useBenchStudy,
  useStudyActions,
  useStudyImpact,
  type BenchStudy,
  type StudySummary,
} from './api';
import { BenchPageLayout, StageChip, useBenchAction, useBenchLabels, useBenchT } from './components';
import { AnalysisTab, SearchTab, VisitsTab } from './StudyTabs';

const route = getRouteApi('/app/bench/studies/$studyId');

export const studyTabs = [
  'overview',
  'search',
  'visit',
  'analysis',
  'actions',
  'impact',
  'approvals',
  'tasks',
  'documents',
  'history',
] as const;
export type StudyTab = (typeof studyTabs)[number];

/** Study page (SPEC §8): four stages with PM Office gates, linked CAP actions live, closure, impact follow-up. */
export function BenchStudyPage() {
  const { t } = useTranslation('bench');
  const { t: tc } = useTranslation();
  const { k } = useBenchT();
  const { studyId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const study = useBenchStudy(studyId);
  const names = useNames();
  const [dialog, setDialog] = useState<'closure' | 'cancel'>();
  const cancel = useBenchAction(benchApi.cancel, () => setDialog(undefined));

  if (study.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (study.isError) return <StatusBanner tone="danger" title={errorMessage(tc, study.error)} />;
  const s = study.data;
  const go = (next: StudyTab) => void navigate({ search: { tab: next } });

  return (
    <BenchPageLayout testId="bench-study-page">
      <Link to="/bench/studies" className="flex items-center gap-1 text-sm text-primary hover:underline">
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('studies.title')}
      </Link>
      <PageHeader
        title={names.text(s.titleAr, s.titleEn) || s.number}
        description={
          <>
            <bdi data-testid="bench-study-number">{s.number}</bdi> · {names.unit(s.responsibleUnit)} ·{' '}
            <bdi>
              Q{s.targetQuarter} {s.targetYear}
            </bdi>
          </>
        }
        meta={
          <div className="flex flex-wrap items-center gap-2">
            <StageChip stage={s.stage} status={s.status} />
            {s.pendingProcess && <StatusChip tone="info">{k(`process.${s.pendingProcess}`)}</StatusChip>}
            {s.returned && <StatusChip tone="warning">{t('study.returned')}</StatusChip>}
            {s.analysisApprovedAt && <StatusChip tone="success">{t('study.analysisApproved')}</StatusChip>}
          </div>
        }
        actions={
          s.can.cancel && (
            <Button variant="ghost" onClick={() => setDialog('cancel')} data-testid="bench-study-cancel">
              <Ban aria-hidden />
              {t('study.cancel')}
            </Button>
          )
        }
      />
      {s.status !== 'cancelled' && (
        <StageStepper
          stages={studyStages.map((x) => ({ key: x, label: k(`stage.${x}`) }))}
          current={s.stage}
        />
      )}
      <div data-testid="bench-current-status">
        <CurrentStatusBanner
          resourceType="bench.study"
          resourceId={s.id}
          onAction={(code) => {
            if (code === 'search') go('search');
            if (code === 'visit') go('visit');
            if (code === 'analysis') go('analysis');
            if (code === 'closure') setDialog('closure');
            if (code === 'impact') go('impact');
            if (code === 'decide') go('approvals');
          }}
        />
      </div>
      {s.returned && s.returnComment && (
        <StatusBanner tone="warning" title={t('study.returnComment')} description={s.returnComment} />
      )}
      <Tabs value={tab ?? 'overview'} onValueChange={(value) => go(value as StudyTab)}>
        <TabsList className="flex-wrap" data-testid="bench-study-tabs">
          {studyTabs.map((key) => (
            <TabsTrigger key={key} value={key} data-testid={`bench-tab-${key}`}>
              {t(`tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <Overview study={s} />
        </TabsContent>
        <TabsContent value="search">
          <SearchTab study={s} />
        </TabsContent>
        <TabsContent value="visit">
          <VisitsTab study={s} />
        </TabsContent>
        <TabsContent value="analysis">
          <AnalysisTab study={s} />
        </TabsContent>
        <TabsContent value="actions">
          <LinkedActions study={s} onClosure={() => setDialog('closure')} />
        </TabsContent>
        <TabsContent value="impact">
          <ImpactTab study={s} />
        </TabsContent>
        <TabsContent value="approvals">
          {s.stage === 'plan' ? (
            <Link
              to="/requests/$requestId"
              params={{ requestId: s.requestId }}
              search={{ tab: 'overview' }}
              className="text-primary hover:underline"
            >
              {t('study.planRequest')}: <bdi>{s.requestNumber}</bdi>
            </Link>
          ) : (
            <ApprovalPanel resourceType="bench.study" resourceId={s.id} />
          )}
        </TabsContent>
        <TabsContent value="tasks">
          <LinkedTasksPanel type="bench.study" id={s.id} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel
            ownerType="bench.study"
            ownerId={s.id}
            canEdit={s.can.editSearch || s.can.editVisits || s.can.editAnalysis}
          />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="benchStudy" id={s.id} />
        </TabsContent>
      </Tabs>
      {dialog === 'closure' && <ClosureDialog study={s} onClose={() => setDialog(undefined)} />}
      {dialog === 'cancel' && (
        <ReasonDialog
          title={t('study.cancel')}
          label={t('study.cancelReason')}
          submitLabel={t('study.cancel')}
          testId="bench-study-cancel-confirm"
          busy={cancel.busy}
          onClose={() => setDialog(undefined)}
          onSubmit={(reason) => cancel.run({ id: s.id, reason })}
        />
      )}
    </BenchPageLayout>
  );
}

function Overview({ study: s }: { study: BenchStudy }) {
  const { t } = useTranslation('bench');
  const names = useNames();
  const labels = useBenchLabels();
  const date = useDate();
  const [summary, setSummary] = useState<StudySummary>();
  const ai = useBenchAction(benchApi.aiSummary);
  const aiFeature = useAiFeature('bench-summary');
  const aiAllowed = useCan('bench.ai.summary');
  return (
    <div className="flex flex-col gap-4" data-testid="bench-study-overview">
      <Rows
        rows={[
          [t('fields.responsibleUnit'), names.unit(s.responsibleUnit)],
          [t('fields.section'), names.unit(s.section)],
          [t('fields.coordinator'), names.person(s.responsibleEmployee)],
          [t('fields.requester'), names.person(s.requester)],
          [t('fields.target'), `Q${s.targetQuarter} ${s.targetYear}`],
          [t('fields.comparisonType'), labels.lookup(benchLookups.comparisonType, s.comparisonTypeCode)],
          [t('fields.purpose'), labels.lookup(benchLookups.purpose, s.purposeCode)],
          [t('fields.scope'), s.scope],
          [
            t('study.planRequest'),
            <Link
              key="r"
              to="/requests/$requestId"
              params={{ requestId: s.requestId }}
              search={{ tab: 'overview' }}
              className="text-primary hover:underline"
            >
              <bdi>{s.requestNumber}</bdi>
            </Link>,
          ],
          [t('study.searchNoticeOn'), date(s.searchNoticeOn)],
          [t('study.planApprovedAt'), date(s.planApprovedAt)],
          [t('study.analysisApprovedAt'), date(s.analysisApprovedAt)],
          [t('study.closedAt'), date(s.closedAt)],
          [
            t('study.team'),
            s.team ? (
              <Link
                key="tm"
                to="/teams/$teamId"
                params={{ teamId: s.team.id }}
                search={{ tab: 'overview' }}
                className="text-primary hover:underline"
              >
                <bdi>{s.team.number}</bdi> {names.text(s.team.nameAr, s.team.nameEn)}
              </Link>
            ) : (
              '—'
            ),
          ],
          ...(s.capSourceId
            ? ([
                [
                  t('study.capSource'),
                  <Link
                    key="cs"
                    to="/cap/sources/$sourceId"
                    params={{ sourceId: s.capSourceId }}
                    className="text-primary hover:underline"
                  >
                    {t('study.openCapSource')}
                  </Link>,
                ],
              ] as [string, ReactNode][])
            : []),
          ...(s.cancelReason ? ([[t('study.cancelReason'), s.cancelReason]] as [string, string][]) : []),
        ]}
      />
      <Section title={t('study.topics')} testId="bench-topics">
        <ul className="flex flex-col divide-y text-sm">
          {s.topics.map((x) => (
            <li key={x.id} className="py-2">
              <span className="font-medium">{x.title}</span> · {names.person(x.owner)}
              {x.description && <span className="block text-muted-foreground">{x.description}</span>}
            </li>
          ))}
        </ul>
      </Section>
      {aiFeature && aiAllowed && (
        <Section
          title={
            <span className="flex items-center gap-2">
              {t('ai.summary')} {summary && <AiBadge />}
            </span>
          }
          actions={
            <Button
              variant="outline"
              size="sm"
              loading={ai.busy}
              onClick={() => ai.run(s.id, setSummary)}
              data-testid="bench-ai-summary"
            >
              <Sparkles aria-hidden />
              {t('ai.run')}
            </Button>
          }
        >
          {summary && (
            <p className="whitespace-pre-line text-sm">{names.text(summary.summaryAr, summary.summaryEn)}</p>
          )}
        </Section>
      )}
    </div>
  );
}

/** CAP remarks and actions created from the development plan, read live through the CAP contract. */
function LinkedActions({ study: s, onClosure }: { study: BenchStudy; onClosure: () => void }) {
  const { t } = useTranslation('bench');
  const { t: tcap } = useTranslation('cap');
  const { k: kc } = useCapT();
  const names = useNames();
  const date = useDate();
  const progress = useStudyActions(s.id, !!s.capSourceId);
  if (!s.capSourceId)
    return (
      <p className="rounded-lg border bg-card p-6 text-sm text-muted-foreground">{t('actions.notYet')}</p>
    );
  if (!progress.data) return <Skeleton className="h-48" />;
  const p = progress.data;
  return (
    <Section
      title={t('actions.title')}
      testId="bench-linked-actions"
      actions={
        <span className="flex items-center gap-2">
          <StatusChip tone={p.allActionsCompleted ? 'success' : 'info'}>
            <span data-testid="bench-actions-progress">
              {t('actions.progress', { done: p.completedCount, total: p.actionCount })}
            </span>
          </StatusChip>
          {s.can.requestClosure && (
            <Button size="sm" onClick={onClosure} data-testid="bench-request-closure">
              {t('study.requestClosure')}
            </Button>
          )}
        </span>
      }
    >
      {p.remarks.map((r) => (
        <div
          key={r.remarkId}
          className="flex flex-col gap-2 rounded-md border p-3"
          data-testid="bench-linked-remark"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <Link
              to="/cap/remarks/$remarkId"
              params={{ remarkId: r.remarkId }}
              className="whitespace-nowrap font-medium text-primary hover:underline"
            >
              <bdi>{r.number}</bdi> {names.text(r.textAr, r.textEn)}
            </Link>
            <StatusChip tone={remarkTone[r.status] ?? 'neutral'}>{kc(`remarkStatus.${r.status}`)}</StatusChip>
          </div>
          {r.actions.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('actions.noneYet')}</p>
          ) : (
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{tcap('fields.number')}</TableHeaderCell>
                  <TableHeaderCell>{tcap('fields.details')}</TableHeaderCell>
                  <TableHeaderCell>{tcap('fields.plannedEnd')}</TableHeaderCell>
                  <TableHeaderCell>{tcap('fields.status')}</TableHeaderCell>
                  <TableHeaderCell>{tcap('fields.achieved')}</TableHeaderCell>
                  <TableHeaderCell>{tcap('fields.evidence')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {r.actions.map((a) => (
                  <TableRow key={a.actionId} data-testid="bench-linked-action">
                    <TableCell>
                      <Link
                        to="/cap/actions/$actionId"
                        params={{ actionId: a.actionId }}
                        className="whitespace-nowrap text-primary hover:underline"
                      >
                        <bdi>{a.number}</bdi>
                      </Link>
                    </TableCell>
                    <TableCell>{names.text(a.detailsAr, a.detailsEn)}</TableCell>
                    <TableCell>{date(a.revisedEnd ?? a.plannedEnd)}</TableCell>
                    <TableCell>
                      {a.validationStatus === 'approved' ? (
                        <StatusChip tone={actionTone[a.status] ?? 'neutral'}>
                          {kc(`actionStatus.${a.status}`)}
                        </StatusChip>
                      ) : (
                        <StatusChip tone="neutral">{kc(`validationStatus.${a.validationStatus}`)}</StatusChip>
                      )}
                    </TableCell>
                    <TableCell>
                      <ProgressBar
                        value={a.achievedPct}
                        tone={a.status === 'late' ? 'danger' : 'primary'}
                        testId="cap-pct"
                      />
                    </TableCell>
                    <TableCell>{a.hasEvidence ? '✓' : '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </div>
      ))}
    </Section>
  );
}

function ImpactTab({ study: s }: { study: BenchStudy }) {
  const { t } = useTranslation('bench');
  const date = useDate();
  const impact = useStudyImpact(s.id);
  const [implemented, setImplemented] = useState(false);
  const [kpisMoved, setKpisMoved] = useState(false);
  const [notes, setNotes] = useState('');
  const record = useBenchAction(benchApi.recordImpact, () => setNotes(''));
  return (
    <Section title={t('impact.title')} testId="bench-impact">
      <p className="text-sm text-muted-foreground">{t('impact.hint', { date: date(s.impactDueOn) })}</p>
      {impact.data && impact.data.length > 0 && (
        <ul className="flex flex-col divide-y text-sm">
          {impact.data.map((i) => (
            <li key={i.id} className="py-2">
              {date(i.reviewedAt)} · {i.implemented ? t('impact.implemented') : t('impact.notImplemented')} ·{' '}
              {i.kpisMoved ? t('impact.kpisMoved') : t('impact.kpisNotMoved')}
              {i.notes && <span className="block text-muted-foreground">{i.notes}</span>}
            </li>
          ))}
        </ul>
      )}
      {s.can.impact && (
        <div className="flex flex-col gap-3 border-t pt-3">
          <Checkbox
            label={t('impact.implemented')}
            checked={implemented}
            onChange={(e) => setImplemented(e.target.checked)}
          />
          <Checkbox
            label={t('impact.kpisMoved')}
            checked={kpisMoved}
            onChange={(e) => setKpisMoved(e.target.checked)}
          />
          <FormField label={t('impact.notes')}>
            <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} />
          </FormField>
          <div>
            <Button
              loading={record.busy}
              onClick={() => record.run({ id: s.id, implemented, kpisMoved, notes: notes || null })}
              data-testid="bench-impact-save"
            >
              {t('impact.record')}
            </Button>
          </div>
        </div>
      )}
    </Section>
  );
}

function ClosureDialog({ study: s, onClose }: { study: BenchStudy; onClose: () => void }) {
  const { t } = useTranslation('bench');
  const [comment, setComment] = useState('');
  const m = useBenchAction(benchApi.requestClosure, onClose);
  return (
    <SimpleDialog
      title={t('study.requestClosure')}
      description={t('study.closureHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('study.requestClosure')}
      testId="bench-closure-confirm"
      onSubmit={() => m.run({ id: s.id, comment: comment || null })}
    >
      <FormField label={t('common.comment')}>
        <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}
