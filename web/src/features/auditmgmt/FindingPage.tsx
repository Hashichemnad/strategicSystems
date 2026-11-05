import { getRouteApi, Link } from '@tanstack/react-router';
import {
  ArrowLeft,
  Ban,
  CalendarClock,
  Link2,
  Pencil,
  Plus,
  Send,
  ShieldCheck,
  Sparkles,
  Undo2,
} from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { useCan } from '@/features/access/session';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import {
  Button,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  ProgressBar,
  Section,
} from '@/ui';
import {
  auditApi,
  lookupTypes,
  useAuditFinding,
  useRecommendations,
  type AuditFinding,
  type AuditRecommendation,
  type RepeatCheck,
} from './api';
import {
  AuditPageLayout,
  FindingStatusChip,
  PriorityChip,
  RecommendationStatusChip,
  Rows,
  useAuditLabels,
  useAuditT,
  useDate,
  useNames,
  usePct,
  useToastMutation,
} from './components';
import {
  AcceptRiskDialog,
  FindingDialog,
  FollowUpDialog,
  ReasonDialog,
  RecommendationDialog,
  ReviseDialog,
  RiskLinkDialog,
} from './forms';

const route = getRouteApi('/app/audit-management/findings/$findingId');

export const findingTabs = ['overview', 'recommendations', 'evidence', 'links', 'history'] as const;
export type FindingTab = (typeof findingTabs)[number];

type Dialog =
  | { kind: 'edit' | 'cancel' | 'add-rec' | 'risk' }
  | {
      kind: 'follow-up' | 'revise' | 'accept' | 'reopen' | 'cancel-rec' | 'edit-rec';
      rec: AuditRecommendation;
    };

/**
 * Finding page (SPEC §10.3 C): header (number, priority, status, completion), condition / criteria / cause / effect, recommendations with the
 * follow-up timeline, evidence viewer, linked risks / controls / criteria document, CAP, history. Department users see it read-only.
 */
export function AuditFindingPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { findingId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const finding = useAuditFinding(findingId);
  const recs = useRecommendations(findingId);
  const names = useNames();
  const pct = usePct();
  const [dialog, setDialog] = useState<Dialog>();
  const close = () => setDialog(undefined);
  const sendToCap = useToastMutation(auditApi.sendToCap);

  if (finding.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (finding.isError) return <StatusBanner tone="danger" title={errorMessage(tc, finding.error)} />;
  const f = finding.data;
  const go = (next: FindingTab) => void navigate({ search: { tab: next } });
  const recommendations = recs.data ?? [];

  return (
    <AuditPageLayout testId="audit-finding-page">
      <Link
        to="/audit-management/reports/$reportId"
        params={{ reportId: f.reportId }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        <bdi>{f.reportNumber}</bdi> — {names.text(f.reportTitleAr, f.reportTitleEn)}
      </Link>
      <PageHeader
        title={<bdi data-testid="audit-finding-number">{f.number}</bdi>}
        description={
          <span data-testid="audit-finding-title">{names.text(f.values.titleAr, f.values.titleEn)}</span>
        }
        meta={
          <div className="flex flex-wrap items-center gap-3" data-testid="audit-finding-meta">
            <FindingStatusChip status={f.status} />
            <PriorityChip priority={f.values.priority} />
            {f.values.repeat && <StatusChip tone="warning">{t('finding.repeat')}</StatusChip>}
            <span className="text-sm text-muted-foreground">
              {t('fields.responsibleUnit')}: {names.unit(f.responsibleUnit)}
            </span>
            <div className="w-40">
              <ProgressBar
                value={f.completion}
                tone={f.status === 'late' ? 'danger' : 'primary'}
                testId="audit-pct"
              />
            </div>
          </div>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            {f.can.edit && (
              <Button
                variant="outline"
                onClick={() => setDialog({ kind: 'edit' })}
                data-testid="audit-finding-edit"
              >
                <Pencil aria-hidden />
                {tc('actions.edit')}
              </Button>
            )}
            {f.can.sendToCap && (
              <Button
                variant="outline"
                loading={sendToCap.busy}
                onClick={() => sendToCap.run(f.id)}
                data-testid="audit-send-to-cap"
              >
                <Send aria-hidden />
                {t('finding.sendToCap')}
              </Button>
            )}
            {f.can.cancel && (
              <Button
                variant="ghost"
                onClick={() => setDialog({ kind: 'cancel' })}
                data-testid="audit-finding-cancel"
              >
                <Ban aria-hidden />
                {t('finding.cancel')}
              </Button>
            )}
          </div>
        }
      />
      {!f.can.edit && (
        <StatusBanner tone="info" title={t('finding.readOnly')} description={t('finding.readOnlyHint')} />
      )}
      <div data-testid="audit-finding-current-status">
        <CurrentStatusBanner
          resourceType="auditmgmt.finding"
          resourceId={f.id}
          onAction={(code) => {
            if (code === 'record-follow-up') go('recommendations');
            if (code === 'add-recommendation') setDialog({ kind: 'add-rec' });
          }}
        />
      </div>
      {f.cancelReason && (
        <StatusBanner tone="info" title={t('finding.cancelled')} description={f.cancelReason} />
      )}
      <Tabs value={tab ?? 'overview'} onValueChange={(value) => go(value as FindingTab)}>
        <TabsList className="flex-wrap" data-testid="audit-finding-tabs">
          {findingTabs.map((key) => (
            <TabsTrigger key={key} value={key} data-testid={`audit-tab-${key}`}>
              {t(`tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <Overview finding={f} />
        </TabsContent>
        <TabsContent value="recommendations">
          <Section
            title={`${t('tabs.recommendations')} (${recommendations.length})`}
            testId="audit-recommendations"
            actions={
              f.can.addRecommendation && (
                <Button
                  size="sm"
                  onClick={() => setDialog({ kind: 'add-rec' })}
                  data-testid="audit-add-recommendation"
                >
                  <Plus aria-hidden />
                  {t('recommendation.add')}
                </Button>
              )
            }
          >
            {recs.isPending ? (
              <Skeleton className="h-40" />
            ) : recommendations.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('recommendation.none')}</p>
            ) : (
              recommendations.map((r) => (
                <RecommendationCard
                  key={r.id}
                  rec={r}
                  canAct={f.can.followUp}
                  onDialog={(kind) => setDialog({ kind, rec: r })}
                />
              ))
            )}
            <p className="text-xs text-muted-foreground">
              {t('finding.completion')}: {pct(f.completion)}
            </p>
          </Section>
        </TabsContent>
        <TabsContent value="evidence">
          <EvidenceTab recommendations={recommendations} canEdit={f.can.followUp} />
        </TabsContent>
        <TabsContent value="links">
          <LinksTab finding={f} onLink={() => setDialog({ kind: 'risk' })} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="auditFinding" id={f.id} />
        </TabsContent>
      </Tabs>
      {dialog?.kind === 'edit' && (
        <FindingDialog findingId={f.id} initial={f.values} version={f.version} onClose={close} />
      )}
      {dialog?.kind === 'cancel' && (
        <ReasonDialog
          title={t('finding.cancel')}
          description={t('finding.cancelHint')}
          submitLabel={t('finding.cancel')}
          testId="audit-finding-cancel-save"
          run={(reason) => auditApi.cancelFinding({ id: f.id, reason })}
          onClose={close}
        />
      )}
      {dialog?.kind === 'add-rec' && <RecommendationDialog findingId={f.id} onClose={close} />}
      {dialog?.kind === 'risk' && <RiskLinkDialog findingId={f.id} onClose={close} />}
      {dialog?.kind === 'follow-up' && <FollowUpDialog recommendation={dialog.rec} onClose={close} />}
      {dialog?.kind === 'revise' && <ReviseDialog recommendation={dialog.rec} onClose={close} />}
      {dialog?.kind === 'accept' && <AcceptRiskDialog recommendation={dialog.rec} onClose={close} />}
      {dialog?.kind === 'edit-rec' && (
        <RecommendationDialog findingId={f.id} recommendation={dialog.rec} onClose={close} />
      )}
      {dialog?.kind === 'reopen' && (
        <ReasonDialog
          title={t('recommendation.reopen')}
          submitLabel={t('recommendation.reopen')}
          testId="audit-reopen-save"
          run={(reason) => auditApi.reopen({ id: dialog.rec.id, reason })}
          onClose={close}
        />
      )}
      {dialog?.kind === 'cancel-rec' && (
        <ReasonDialog
          title={t('recommendation.cancel')}
          submitLabel={t('recommendation.cancel')}
          testId="audit-rec-cancel-save"
          run={(reason) => auditApi.cancelRecommendation({ id: dialog.rec.id, reason })}
          onClose={close}
        />
      )}
    </AuditPageLayout>
  );
}

function Overview({ finding: f }: { finding: AuditFinding }) {
  const { t } = useTranslation('auditmgmt');
  const labels = useAuditLabels();
  const names = useNames();
  const date = useDate();
  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 lg:grid-cols-2" data-testid="audit-ccce">
        {(
          [
            ['condition', f.values.condition],
            ['criteria', f.values.criteria],
            ['cause', f.values.cause],
            ['effect', f.values.effect],
          ] as const
        ).map(([key, value]) => (
          <Section key={key} title={t(`fields.${key}`)} testId={`audit-${key}`}>
            <p className="whitespace-pre-line text-sm">{value || '—'}</p>
            {key === 'criteria' && f.criteriaDocument && (
              <a
                href={f.criteriaDocument.link}
                className="text-sm text-primary hover:underline"
                data-testid="audit-criteria-document"
              >
                <bdi>{f.criteriaDocument.documentNo}</bdi>{' '}
                {names.text(f.criteriaDocument.nameAr, f.criteriaDocument.nameEn)}
              </a>
            )}
          </Section>
        ))}
      </div>
      <Rows
        rows={[
          [t('fields.report'), <bdi key="r">{f.reportNumber}</bdi>],
          [t('fields.reportType'), labels.lookup(lookupTypes.reportType, f.reportTypeCode)],
          [t('fields.findingType'), labels.lookup(lookupTypes.findingType, f.values.typeCode)],
          [t('fields.findingDate'), date(f.values.findingDate)],
          [t('fields.responsibleUnit'), names.unit(f.responsibleUnit)],
          [t('fields.department'), names.unit(f.department)],
          [t('fields.auditor'), names.person(f.auditor)],
          [t('fields.expectedClosingOn'), date(f.values.expectedClosingOn)],
          [t('fields.actualClosedOn'), date(f.actualClosedOn)],
          [
            t('fields.repeatOf'),
            f.repeatOf ? (
              <Link
                key="rp"
                to="/audit-management/findings/$findingId"
                params={{ findingId: f.repeatOf.id }}
                className="text-primary hover:underline"
              >
                <bdi>{f.repeatOf.number}</bdi>
              </Link>
            ) : (
              '—'
            ),
          ],
        ]}
      />
    </div>
  );
}

function RecommendationCard({
  rec: r,
  canAct,
  onDialog,
}: {
  rec: AuditRecommendation;
  canAct: boolean;
  onDialog: (kind: 'follow-up' | 'revise' | 'accept' | 'reopen' | 'cancel-rec' | 'edit-rec') => void;
}) {
  const { t } = useTranslation('auditmgmt');
  const { k } = useAuditT();
  const labels = useAuditLabels();
  const names = useNames();
  const date = useDate();
  const pct = usePct();
  const open = r.status === 'not_started' || r.status === 'continuing' || r.status === 'late';
  return (
    <article className="flex flex-col gap-3 rounded-md border p-4" data-testid="audit-recommendation">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <bdi className="font-semibold" data-testid="audit-recommendation-number">
            {r.number}
          </bdi>
          <RecommendationStatusChip status={r.status} />
          {r.pendingReview && <StatusChip tone="info">{t('followUp.pendingReview')}</StatusChip>}
        </div>
        {canAct && (
          <div className="flex flex-wrap gap-2">
            {open && !r.pendingReview && (
              <Button size="sm" onClick={() => onDialog('follow-up')} data-testid="audit-record-follow-up">
                <CalendarClock aria-hidden />
                {t('followUp.record')}
              </Button>
            )}
            {open && (
              <>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => onDialog('revise')}
                  data-testid="audit-revise"
                >
                  {t('recommendation.revise')}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => onDialog('accept')}
                  data-testid="audit-accept-risk"
                >
                  <ShieldCheck aria-hidden />
                  {t('recommendation.acceptRisk')}
                </Button>
                <Button size="sm" variant="ghost" onClick={() => onDialog('edit-rec')}>
                  <Pencil aria-hidden />
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => onDialog('cancel-rec')}
                  data-testid="audit-rec-cancel"
                >
                  <Ban aria-hidden />
                </Button>
              </>
            )}
            {r.status === 'risk_accepted' && (
              <Button size="sm" variant="outline" onClick={() => onDialog('reopen')}>
                <Undo2 aria-hidden />
                {t('recommendation.reopen')}
              </Button>
            )}
          </div>
        )}
      </header>
      <p className="whitespace-pre-line text-sm">{names.text(r.values.actionAr, r.values.actionEn)}</p>
      <dl className="grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <div>
          <dt className="text-muted-foreground">{t('fields.dueOn')}</dt>
          <dd>
            {date(r.effectiveDue)}
            {r.revisedDueOn && (
              <span className="block text-xs text-muted-foreground">
                {t('recommendation.original')}: {date(r.values.dueOn)}
              </span>
            )}
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('fields.pct')}</dt>
          <dd>
            <ProgressBar
              value={r.latestPct}
              tone={r.status === 'late' ? 'danger' : 'primary'}
              testId="audit-pct"
            />
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('fields.nextFollowUpOn')}</dt>
          <dd>{date(r.nextFollowUpOn)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('fields.contact')}</dt>
          <dd>{names.person(r.contact)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t('fields.managementResponse')}</dt>
          <dd>{labels.lookup(lookupTypes.response, r.values.managementResponseCode)}</dd>
        </div>
        <div className="sm:col-span-3">
          <dt className="text-muted-foreground">{t('fields.implementationPlan')}</dt>
          <dd className="whitespace-pre-line">{r.values.implementationPlan || '—'}</dd>
        </div>
      </dl>
      {r.riskAcceptanceJustification && (
        <StatusBanner
          tone="warning"
          title={t('recommendation.riskAccepted', { date: date(r.riskAcceptedOn) })}
          description={`${r.riskAcceptanceJustification}${r.riskAcceptanceReference ? ` — ${r.riskAcceptanceReference}` : ''}`}
        />
      )}
      {r.followUps.length > 0 && (
        <ol className="relative flex flex-col gap-3 border-s ps-4" data-testid="audit-follow-up-timeline">
          {r.followUps.map((u) => (
            <li key={u.id} className="flex flex-col gap-1">
              <span className="absolute -start-1.5 mt-1.5 size-3 rounded-full bg-primary" aria-hidden />
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <bdi className="font-semibold">{u.number}</bdi>
                <span>{date(u.followUpOn)}</span>
                <span className="tabular-nums">{pct(u.pct)}</span>
                {u.reviewStatus !== 'none' && (
                  <StatusChip
                    tone={
                      u.reviewStatus === 'approved'
                        ? 'success'
                        : u.reviewStatus === 'returned'
                          ? 'warning'
                          : 'info'
                    }
                  >
                    {k(`reviewStatus.${u.reviewStatus}`)}
                  </StatusChip>
                )}
                <span className="text-muted-foreground">{names.person(u.recordedBy)}</span>
              </div>
              {u.actionsImplemented && <p className="text-sm">{u.actionsImplemented}</p>}
              {u.evidenceText && (
                <p className="text-sm text-muted-foreground">
                  {t('fields.evidenceText')}: {u.evidenceText}
                </p>
              )}
              {u.comment && (
                <p className="rounded bg-muted px-2 py-1 text-sm" data-testid="audit-office-comment">
                  {t('fields.officeComment')}: {u.comment}
                </p>
              )}
              {u.files.length > 0 && (
                <p className="text-xs text-muted-foreground">
                  {t('followUp.files', { count: u.files.length })}
                </p>
              )}
            </li>
          ))}
        </ol>
      )}
      {r.log.length > 0 && (
        <ul className="text-xs text-muted-foreground" data-testid="audit-recommendation-log">
          {r.log.map((l) => (
            <li key={l.id}>
              {k(`logKind.${l.kind}`)} · {date(l.at.slice(0, 10))}
              {l.newDueOn ? ` · ${date(l.previousDueOn)} → ${date(l.newDueOn)}` : ''} · {l.justification}
              {l.reference ? ` (${l.reference})` : ''}
            </li>
          ))}
        </ul>
      )}
    </article>
  );
}

/** Evidence viewer: the files received with each follow-up (preview in page), uploaded by the Audit Office only. */
function EvidenceTab({
  recommendations,
  canEdit,
}: {
  recommendations: AuditRecommendation[];
  canEdit: boolean;
}) {
  const { t } = useTranslation('auditmgmt');
  const date = useDate();
  const followUps = recommendations.flatMap((r) => r.followUps.map((u) => ({ rec: r, u })));
  if (followUps.length === 0)
    return <p className="text-sm text-muted-foreground">{t('followUp.noEvidence')}</p>;
  return (
    <div className="flex flex-col gap-4" data-testid="audit-evidence">
      {followUps.map(({ rec, u }) => (
        <Section key={u.id} title={`${rec.number} · ${u.number} · ${date(u.followUpOn)}`}>
          <DocumentsPanel ownerType="auditmgmt.follow-up" ownerId={u.id} canEdit={canEdit} />
        </Section>
      ))}
    </div>
  );
}

function LinksTab({ finding: f, onLink }: { finding: AuditFinding; onLink: () => void }) {
  const { t } = useTranslation('auditmgmt');
  const { k, language } = useAuditT();
  const names = useNames();
  const unlink = useToastMutation(auditApi.unlinkRisk);
  const repeat = useToastMutation(auditApi.repeatCheck);
  const [check, setCheck] = useState<RepeatCheck>();
  const repeatAi = useAiFeature('auditmgmt-repeat');
  const canRepeat = useCan('auditmgmt.ai.repeat');
  return (
    <div className="flex flex-col gap-4">
      <Section
        title={t('risks.title')}
        testId="audit-linked-risks"
        actions={
          f.can.linkRisk && (
            <Button size="sm" variant="outline" onClick={onLink} data-testid="audit-link-risk">
              <Link2 aria-hidden />
              {t('risks.link')}
            </Button>
          )
        }
      >
        {f.risks.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('risks.noneLinked')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {f.risks.map((r) => (
              <li key={r.id} className="flex flex-wrap items-center gap-2 text-sm">
                {r.visible && r.link ? (
                  <a href={r.link} className="text-primary hover:underline">
                    <bdi>{r.riskNumber}</bdi> {names.text(r.riskTitleAr, r.riskTitleEn)}
                  </a>
                ) : (
                  <bdi>{r.riskNumber}</bdi>
                )}
                {r.residualBand && <StatusChip tone="neutral">{r.residualBand}</StatusChip>}
                {r.controlId && (
                  <span className="text-muted-foreground">
                    · {names.text(r.controlNameAr, r.controlNameEn) || t('risks.control')}
                  </span>
                )}
                {r.flagged && <StatusChip tone="warning">{t('risks.flagged')}</StatusChip>}
                {r.controlLoweredTo && (
                  <StatusChip tone="danger">{t('risks.lowered', { level: r.controlLoweredTo })}</StatusChip>
                )}
                {f.can.linkRisk && (
                  <Button size="sm" variant="ghost" onClick={() => unlink.run({ id: f.id, linkId: r.id })}>
                    {t('common.remove')}
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </Section>
      {f.capRemark && (
        <Section title={t('finding.capRemark')} testId="audit-cap-remark">
          <a href={f.capRemark.link} className="text-sm text-primary hover:underline">
            <bdi>{f.capRemark.number}</bdi>
          </a>
          {f.capRemark.status && <span className="text-sm text-muted-foreground">{f.capRemark.status}</span>}
        </Section>
      )}
      {f.can.edit && canRepeat && (
        <Section
          title={t('finding.repeatCheck')}
          testId="audit-repeat-check"
          actions={
            <Button
              size="sm"
              variant="outline"
              loading={repeat.busy}
              onClick={() => repeat.run(f.id, setCheck)}
              data-testid="audit-repeat-run"
            >
              <Sparkles aria-hidden />
              {t('finding.repeatRun')}
            </Button>
          }
        >
          {check &&
            (check.similar.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('finding.noSimilar')}</p>
            ) : (
              <ul className="flex flex-col gap-1 text-sm">
                {check.similar.map((s) => (
                  <li key={s.id}>
                    <Link
                      to="/audit-management/findings/$findingId"
                      params={{ findingId: s.id }}
                      className="text-primary hover:underline"
                    >
                      <bdi>{s.number}</bdi>
                    </Link>{' '}
                    {names.text(s.titleAr, s.titleEn)} · {names.unit(s.unit)} · {s.year} ·{' '}
                    {k(`findingStatus.${s.status}`)} · {Math.round(s.score * 100)} %
                  </li>
                ))}
              </ul>
            ))}
          {check?.rationaleEn && repeatAi && (
            <p className="flex items-center gap-2 text-sm">
              <AiBadge /> {language === 'ar' ? check.rationaleAr : check.rationaleEn}
            </p>
          )}
        </Section>
      )}
    </div>
  );
}
