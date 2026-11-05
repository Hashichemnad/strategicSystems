import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { Plus, RefreshCw, Send } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { ApprovalPanel } from '@/features/workflow/ApprovalPanel';
import {
  Button,
  DatePicker,
  EmptyState,
  FormField,
  Input,
  PageHeader,
  Select,
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
  Textarea,
} from '@/ui';
import {
  auditApi,
  engagementStages,
  lookupTypes,
  useAuditOptions,
  useEngagement,
  usePlan,
  usePlans,
  useRecommendation,
  useUniverse,
  useWorkPapers,
  type AuditEngagementValues,
  type AuditTeamOption,
  type AuditEntityValues,
  type AuditWorkPaperValues,
} from './api';
import {
  AuditPageLayout,
  PctBar,
  ReportStatusChip,
  Rows,
  Section,
  SimpleDialog,
  Tile,
  useAuditLabels,
  useAuditT,
  useDate,
  useDialogMutation,
  useNames,
  usePct,
  useToastMutation,
} from './components';
import { AuditTeamPicker } from './TeamPicker';

/** Shown instead of a Layer 2 page while the switch is off (the API answers 404 as well). */
function LifecycleOff() {
  const { t } = useTranslation('auditmgmt');
  return (
    <AuditPageLayout testId="audit-lifecycle-off">
      <StatusBanner tone="info" title={t('lifecycle.off')} description={t('lifecycle.offHint')} />
    </AuditPageLayout>
  );
}

/** Audit universe: auditable entities from the organisation and Risk Management, with inherent rating and risk score. */
export function AuditUniversePage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const options = useAuditOptions();
  const on = options.data?.lifecycleEnabled === true && options.data.canLifecycle;
  const universe = useUniverse(on);
  const labels = useAuditLabels();
  const names = useNames();
  const date = useDate();
  const sync = useToastMutation(auditApi.syncUniverse);
  const [adding, setAdding] = useState(false);
  if (options.data && !on) return <LifecycleOff />;
  return (
    <AuditPageLayout testId="audit-universe">
      <PageHeader
        title={t('lifecycle.universe')}
        description={t('lifecycle.universeHint')}
        actions={
          <div className="flex gap-2">
            <Button
              variant="outline"
              loading={sync.busy}
              onClick={() => sync.run(undefined)}
              data-testid="audit-universe-sync"
            >
              <RefreshCw aria-hidden />
              {t('lifecycle.sync')}
            </Button>
            <Button onClick={() => setAdding(true)}>
              <Plus aria-hidden />
              {t('lifecycle.addEntity')}
            </Button>
          </div>
        }
      />
      {universe.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, universe.error)} />
      ) : !universe.data ? (
        <Skeleton className="h-64" />
      ) : universe.data.length === 0 ? (
        <EmptyState title={t('lifecycle.universeEmpty')} />
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.kind')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.inherentRating')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.lastAudited')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.riskScore')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {universe.data.map((e) => (
              <TableRow key={e.id}>
                <TableCell>{names.text(e.values.nameAr, e.values.nameEn)}</TableCell>
                <TableCell>{labels.lookup(lookupTypes.entityKind, e.values.kindCode)}</TableCell>
                <TableCell>{names.unit(e.unit)}</TableCell>
                <TableCell>{labels.lookup(lookupTypes.priority, e.values.inherentRating)}</TableCell>
                <TableCell>{date(e.values.lastAuditedOn)}</TableCell>
                <TableCell className="tabular-nums">{e.riskScore}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {adding && <EntityDialog onClose={() => setAdding(false)} />}
    </AuditPageLayout>
  );
}

function EntityDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('auditmgmt');
  const labels = useAuditLabels();
  const [v, setV] = useState<AuditEntityValues>({
    kindCode: 'process',
    nameAr: '',
    nameEn: '',
    unitId: null,
    riskId: null,
    inherentRating: 'medium',
    lastAuditedOn: null,
    active: true,
  });
  const m = useDialogMutation(auditApi.createEntity, onClose);
  const set = (patch: Partial<AuditEntityValues>) => setV((x) => ({ ...x, ...patch }));
  return (
    <SimpleDialog
      title={t('lifecycle.addEntity')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.create')}
      disabled={!(v.nameAr || v.nameEn)}
      testId="audit-entity-save"
      onSubmit={() => m.run(v)}
    >
      <FormField label={t('fields.nameAr')}>
        <Input dir="rtl" value={v.nameAr ?? ''} onChange={(e) => set({ nameAr: e.target.value })} />
      </FormField>
      <FormField label={t('fields.nameEn')}>
        <Input dir="ltr" value={v.nameEn ?? ''} onChange={(e) => set({ nameEn: e.target.value })} />
      </FormField>
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.kind')}>
          <Select
            value={v.kindCode}
            onValueChange={(kindCode) => set({ kindCode })}
            options={labels.lookupOptions(lookupTypes.entityKind)}
          />
        </FormField>
        <FormField label={t('fields.inherentRating')}>
          <Select
            value={v.inherentRating ?? 'medium'}
            onValueChange={(inherentRating) => set({ inherentRating })}
            options={labels.lookupOptions(lookupTypes.priority)}
          />
        </FormField>
        <FormField label={t('fields.unit')}>
          <UnitSelect
            allowEmpty
            value={v.unitId ?? undefined}
            onChange={(u) => set({ unitId: u && u !== '-' ? u : null })}
          />
        </FormField>
        <FormField label={t('fields.lastAudited')}>
          <DatePicker
            value={v.lastAuditedOn ?? undefined}
            onChange={(d) => set({ lastAuditedOn: d ?? null })}
          />
        </FormField>
      </div>
    </SimpleDialog>
  );
}

/** Risk-based annual audit plans (approved by the audit committee / DG) with QAIP measures. */
export function AuditPlansPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { k } = useAuditT();
  const options = useAuditOptions();
  const on = options.data?.lifecycleEnabled === true;
  const plans = usePlans(on);
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  if (options.data && !on) return <LifecycleOff />;
  return (
    <AuditPageLayout testId="audit-plans">
      <PageHeader
        title={t('lifecycle.plans')}
        description={t('lifecycle.plansHint')}
        actions={
          options.data?.canLifecycle && (
            <Button onClick={() => setCreating(true)} data-testid="audit-plan-new">
              <Plus aria-hidden />
              {t('lifecycle.newPlan')}
            </Button>
          )
        }
      />
      {plans.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, plans.error)} />
      ) : !plans.data ? (
        <Skeleton className="h-48" />
      ) : plans.data.length === 0 ? (
        <EmptyState title={t('lifecycle.plansEmpty')} />
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {plans.data.map((p) => (
            <Link
              key={p.id}
              to="/audit-management/plans/$planId"
              params={{ planId: p.id }}
              className="rounded-lg border bg-card p-4 hover:bg-muted/50"
            >
              <div className="flex items-center justify-between gap-2">
                <span className="text-lg font-semibold">{p.year}</span>
                <StatusChip
                  tone={p.status === 'approved' ? 'success' : p.status === 'returned' ? 'warning' : 'info'}
                >
                  {k(`planStatus.${p.status}`)}
                </StatusChip>
              </div>
              <p className="text-sm text-muted-foreground">
                {t('lifecycle.engagementCount', { count: p.engagements.length })}
              </p>
            </Link>
          ))}
        </div>
      )}
      {creating && (
        <PlanDialog
          onClose={() => setCreating(false)}
          onCreated={(id) => void navigate({ to: '/audit-management/plans/$planId', params: { planId: id } })}
        />
      )}
    </AuditPageLayout>
  );
}

function PlanDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const { t } = useTranslation('auditmgmt');
  const [year, setYear] = useState(new Date().getFullYear() + 1);
  const [title, setTitle] = useState('');
  const m = useDialogMutation(auditApi.createPlan, onClose);
  return (
    <SimpleDialog
      title={t('lifecycle.newPlan')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.create')}
      testId="audit-plan-save"
      onSubmit={() => m.run({ year, titleAr: null, titleEn: title || null }, (p) => onCreated(p.id))}
    >
      <FormField label={t('fields.year')}>
        <Input type="number" value={year} onChange={(e) => setYear(Number(e.target.value))} />
      </FormField>
      <FormField label={t('fields.title')}>
        <Input value={title} onChange={(e) => setTitle(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

const planRoute = getRouteApi('/app/audit-management/plans/$planId');

export function AuditPlanPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { k } = useAuditT();
  const { planId } = planRoute.useParams();
  const options = useAuditOptions();
  const plan = usePlan(planId);
  const names = useNames();
  const pct = usePct();
  const submit = useToastMutation(auditApi.submitPlan);
  const [adding, setAdding] = useState(false);
  if (options.data && !options.data.lifecycleEnabled) return <LifecycleOff />;
  if (plan.isPending) return <Skeleton className="h-96" />;
  if (plan.isError) return <StatusBanner tone="danger" title={errorMessage(tc, plan.error)} />;
  const p = plan.data;
  return (
    <AuditPageLayout testId="audit-plan-page">
      <PageHeader
        title={t('lifecycle.planTitle', { year: p.year })}
        description={names.text(p.titleAr, p.titleEn)}
        meta={
          <StatusChip tone={p.status === 'approved' ? 'success' : 'info'}>
            {k(`planStatus.${p.status}`)}
          </StatusChip>
        }
        actions={
          p.canEdit && (
            <div className="flex gap-2">
              <Button variant="outline" onClick={() => setAdding(true)} data-testid="audit-engagement-new">
                <Plus aria-hidden />
                {t('lifecycle.newEngagement')}
              </Button>
              <Button
                loading={submit.busy}
                disabled={p.engagements.length === 0}
                onClick={() => submit.run({ id: p.id, reason: null })}
                data-testid="audit-plan-submit"
              >
                <Send aria-hidden />
                {t('lifecycle.submitPlan')}
              </Button>
            </div>
          )
        }
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" data-testid="audit-qaip">
        <Tile label={t('qaip.planCompletion')} value={pct(p.qaip.planCompletion)} />
        <Tile label={t('qaip.reportTimeliness')} value={pct(p.qaip.reportTimeliness)} />
        <Tile label={t('qaip.closureRate')} value={pct(p.qaip.closureRate)} />
        <Tile label={t('qaip.satisfaction')} value={p.qaip.satisfaction ?? '—'} />
      </div>
      <Section title={t('lifecycle.engagements')} testId="audit-plan-engagements">
        {p.engagements.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('lifecycle.noEngagements')}</p>
        ) : (
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.entity')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.quarter')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.riskScore')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.stage')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.leadAuditor')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {p.engagements.map((e) => (
                <TableRow key={e.id}>
                  <TableCell>
                    <Link
                      to="/audit-management/engagements/$engagementId"
                      params={{ engagementId: e.id }}
                      className="text-primary hover:underline"
                    >
                      <bdi>{e.number}</bdi>
                    </Link>
                  </TableCell>
                  <TableCell>{names.text(e.titleAr, e.titleEn)}</TableCell>
                  <TableCell>{names.text(e.entityNameAr, e.entityNameEn) || '—'}</TableCell>
                  <TableCell>{e.plannedQuarter ? `Q${e.plannedQuarter}` : '—'}</TableCell>
                  <TableCell className="tabular-nums">{e.riskScore}</TableCell>
                  <TableCell>{k(`stage.${e.stage}`)}</TableCell>
                  <TableCell>{names.person(e.leadAuditor)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>
      <Section title={t('lifecycle.quarterStatus')}>
        <div className="grid gap-3 sm:grid-cols-4">
          {[1, 2, 3, 4].map((q) => {
            const stages = p.quarterStatus[String(q)] ?? {};
            const total = Object.values(stages).reduce((a, b) => a + b, 0);
            const done = (stages['follow_up'] ?? 0) + (stages['closed'] ?? 0);
            return (
              <div key={q} className="rounded-md border p-3">
                <div className="font-semibold">Q{q}</div>
                <PctBar value={total ? done / total : 0} />
                <div className="text-xs text-muted-foreground">
                  {t('lifecycle.quarterCounts', { done, total })}
                </div>
              </div>
            );
          })}
        </div>
      </Section>
      <ApprovalPanel resourceType="auditmgmt.plan" resourceId={p.id} />
      {adding && <EngagementDialog planId={p.id} onClose={() => setAdding(false)} />}
    </AuditPageLayout>
  );
}

function EngagementDialog({ planId, onClose }: { planId: string | null; onClose: () => void }) {
  const { t } = useTranslation('auditmgmt');
  const universe = useUniverse(true);
  const names = useNames();
  const [v, setV] = useState<AuditEngagementValues>({
    titleAr: '',
    titleEn: '',
    entityId: null,
    objectives: '',
    scope: '',
    teamId: null,
    leadAuditorEmployeeId: null,
    plannedQuarter: 1,
    plannedStart: null,
    plannedEnd: null,
    budgetHours: null,
  });
  const [team, setTeam] = useState<AuditTeamOption | null>(null);
  const m = useDialogMutation(auditApi.createEngagement, onClose);
  const set = (patch: Partial<AuditEngagementValues>) => setV((x) => ({ ...x, ...patch }));
  return (
    <SimpleDialog
      title={t('lifecycle.newEngagement')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.create')}
      disabled={!(v.titleAr || v.titleEn)}
      testId="audit-engagement-save"
      wide
      onSubmit={() => m.run({ planId, values: v })}
    >
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('fields.titleAr')}>
          <Input
            dir="rtl"
            value={v.titleAr ?? ''}
            onChange={(e) => set({ titleAr: e.target.value })}
            data-testid="audit-engagement-title-ar"
          />
        </FormField>
        <FormField label={t('fields.titleEn')}>
          <Input dir="ltr" value={v.titleEn ?? ''} onChange={(e) => set({ titleEn: e.target.value })} />
        </FormField>
        <FormField label={t('fields.entity')}>
          <Select
            value={v.entityId ?? '-'}
            onValueChange={(id) => set({ entityId: id === '-' ? null : id })}
            options={[
              { value: '-', label: t('common.none') },
              ...(universe.data ?? []).map((e) => ({
                value: e.id,
                label: names.text(e.values.nameAr, e.values.nameEn),
              })),
            ]}
          />
        </FormField>
        <FormField label={t('fields.leadAuditor')}>
          <EmployeePicker
            value={v.leadAuditorEmployeeId ?? undefined}
            onChange={(e) => set({ leadAuditorEmployeeId: e ?? null })}
          />
        </FormField>
        <FormField label={t('fields.quarter')}>
          <Select
            value={String(v.plannedQuarter ?? 1)}
            onValueChange={(q) => set({ plannedQuarter: Number(q) })}
            options={[1, 2, 3, 4].map((q) => ({ value: String(q), label: `Q${q}` }))}
          />
        </FormField>
        <FormField label={t('fields.budgetHours')}>
          <Input
            type="number"
            value={v.budgetHours ?? ''}
            onChange={(e) => set({ budgetHours: e.target.value ? Number(e.target.value) : null })}
          />
        </FormField>
        <FormField label={t('fields.plannedStart')}>
          <DatePicker
            value={v.plannedStart ?? undefined}
            onChange={(d) => set({ plannedStart: d ?? null })}
          />
        </FormField>
        <FormField label={t('fields.plannedEnd')}>
          <DatePicker value={v.plannedEnd ?? undefined} onChange={(d) => set({ plannedEnd: d ?? null })} />
        </FormField>
      </div>
      <FormField label={t('fields.objectives')}>
        <Textarea value={v.objectives ?? ''} onChange={(e) => set({ objectives: e.target.value })} />
      </FormField>
      <FormField label={t('fields.scope')}>
        <Textarea value={v.scope ?? ''} onChange={(e) => set({ scope: e.target.value })} />
      </FormField>
      <FormField label={t('fields.team')} hint={t('lifecycle.teamHint')}>
        <AuditTeamPicker
          value={team}
          onChange={(picked) => {
            setTeam(picked);
            set({ teamId: picked?.id ?? null });
          }}
          testId="audit-engagement-team"
        />
      </FormField>
    </SimpleDialog>
  );
}

const engagementRoute = getRouteApi('/app/audit-management/engagements/$engagementId');

/** Engagement: stages Planning → Fieldwork → Reporting → Follow-up, objectives / scope, team, programme and working papers (Audit Office only). */
export function AuditEngagementPage() {
  const { t } = useTranslation('auditmgmt');
  const { t: tc } = useTranslation();
  const { k } = useAuditT();
  const { engagementId } = engagementRoute.useParams();
  const options = useAuditOptions();
  const engagement = useEngagement(engagementId);
  const papers = useWorkPapers(engagementId, options.data?.canWorkPapers === true);
  const names = useNames();
  const date = useDate();
  const move = useToastMutation(auditApi.transition);
  const paperAction = useToastMutation(auditApi.workPaperAction);
  const [adding, setAdding] = useState(false);
  if (options.data && !options.data.lifecycleEnabled) return <LifecycleOff />;
  if (engagement.isPending) return <Skeleton className="h-96" />;
  if (engagement.isError) return <StatusBanner tone="danger" title={errorMessage(tc, engagement.error)} />;
  const e = engagement.data;
  const stepper = engagementStages.includes(e.stage as (typeof engagementStages)[number]);
  return (
    <AuditPageLayout testId="audit-engagement-page">
      <PageHeader
        title={<bdi data-testid="audit-engagement-number">{e.number}</bdi>}
        description={names.text(e.values.titleAr, e.values.titleEn)}
        meta={<StatusChip tone="info">{k(`stage.${e.stage}`)}</StatusChip>}
        actions={
          <div className="flex flex-wrap gap-2">
            {e.nextActions
              .filter((a) => a !== 'cancel')
              .map((a) => (
                <Button
                  key={a}
                  loading={move.busy}
                  onClick={() => move.run({ id: e.id, action: a, reason: null })}
                  data-testid={`audit-engagement-${a}`}
                >
                  {k(`engagementAction.${a}`)}
                </Button>
              ))}
          </div>
        }
      />
      {(stepper || e.stage === 'closed') && (
        <StageStepper
          stages={engagementStages.map((s) => ({ key: s, label: k(`stage.${s}`) }))}
          current={e.stage === 'closed' ? 'follow_up' : e.stage}
        />
      )}
      <Rows
        rows={[
          [t('fields.entity'), names.text(e.entityNameAr, e.entityNameEn)],
          [t('fields.leadAuditor'), names.person(e.leadAuditor)],
          [t('fields.quarter'), e.values.plannedQuarter ? `Q${e.values.plannedQuarter}` : '—'],
          [t('fields.period'), `${date(e.values.plannedStart)} – ${date(e.values.plannedEnd)}`],
          [t('fields.budgetHours'), e.values.budgetHours ?? '—'],
          [t('fields.riskScore'), e.riskScore],
          [t('fields.objectives'), e.values.objectives],
          [t('fields.scope'), e.values.scope],
          [
            t('fields.team'),
            e.team
              ? `${names.text(e.team.nameAr, e.team.nameEn)} (${e.team.members.map((m) => names.person(m)).join('، ')})`
              : '—',
          ],
          [
            t('fields.report'),
            e.reportId ? (
              <span key="r" className="flex items-center gap-2">
                <Link
                  to="/audit-management/reports/$reportId"
                  params={{ reportId: e.reportId }}
                  className="text-primary hover:underline"
                >
                  <bdi>{e.reportNumber}</bdi>
                </Link>
                {e.reportStatus && <ReportStatusChip status={e.reportStatus} />}
              </span>
            ) : (
              '—'
            ),
          ],
        ]}
      />
      {options.data?.canWorkPapers && (
        <Section
          title={t('lifecycle.workPapers', { count: e.workPapers, reviewed: e.workPapersReviewed })}
          testId="audit-work-papers"
          actions={
            <Button size="sm" onClick={() => setAdding(true)} data-testid="audit-work-paper-new">
              <Plus aria-hidden />
              {t('lifecycle.addWorkPaper')}
            </Button>
          }
        >
          <StatusBanner tone="info" title={t('lifecycle.workPapersPrivate')} />
          {(papers.data ?? []).map((w) => (
            <article key={w.id} className="flex flex-col gap-2 rounded-md border p-3">
              <header className="flex flex-wrap items-center gap-2">
                <bdi className="font-semibold">{w.values.reference}</bdi>
                <span>{w.values.title}</span>
                <StatusChip tone={w.status === 'reviewed' ? 'success' : 'info'}>
                  {k(`workPaperStatus.${w.status}`)}
                </StatusChip>
                {w.status === 'draft' && (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => paperAction.run({ engagementId: e.id, paperId: w.id, action: 'prepare' })}
                  >
                    {t('lifecycle.prepare')}
                  </Button>
                )}
                {w.status === 'prepared' && (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => paperAction.run({ engagementId: e.id, paperId: w.id, action: 'sign-off' })}
                  >
                    {t('lifecycle.signOff')}
                  </Button>
                )}
              </header>
              <dl className="grid gap-2 text-sm md:grid-cols-2">
                <div>
                  <dt className="text-muted-foreground">{t('fields.testStep')}</dt>
                  <dd>{w.values.testStep ?? '—'}</dd>
                </div>
                <div>
                  <dt className="text-muted-foreground">{t('fields.sample')}</dt>
                  <dd>{w.values.sample ?? '—'}</dd>
                </div>
                <div>
                  <dt className="text-muted-foreground">{t('fields.result')}</dt>
                  <dd>{w.values.result ?? '—'}</dd>
                </div>
                <div>
                  <dt className="text-muted-foreground">{t('fields.conclusion')}</dt>
                  <dd>{w.values.conclusion ?? '—'}</dd>
                </div>
              </dl>
              {w.reviewedBy && (
                <p className="text-xs text-muted-foreground">
                  {t('lifecycle.reviewedBy', {
                    name: names.person(w.reviewedBy),
                    date: date(w.reviewedAt?.slice(0, 10)),
                  })}
                </p>
              )}
              <DocumentsPanel ownerType="auditmgmt.work-paper" ownerId={w.id} canEdit />
            </article>
          ))}
        </Section>
      )}
      {adding && <WorkPaperDialog engagementId={e.id} onClose={() => setAdding(false)} />}
    </AuditPageLayout>
  );
}

function WorkPaperDialog({ engagementId, onClose }: { engagementId: string; onClose: () => void }) {
  const { t } = useTranslation('auditmgmt');
  const [v, setV] = useState<AuditWorkPaperValues>({
    reference: '',
    title: '',
    testStep: '',
    sample: '',
    result: '',
    conclusion: '',
  });
  const m = useDialogMutation(auditApi.createWorkPaper, onClose);
  const set = (patch: Partial<AuditWorkPaperValues>) => setV((x) => ({ ...x, ...patch }));
  return (
    <SimpleDialog
      title={t('lifecycle.addWorkPaper')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.create')}
      disabled={!v.title}
      testId="audit-work-paper-save"
      onSubmit={() => m.run({ engagementId, values: v })}
    >
      <div className="grid gap-3 md:grid-cols-3">
        <FormField label={t('fields.reference')}>
          <Input value={v.reference ?? ''} onChange={(e) => set({ reference: e.target.value })} />
        </FormField>
        <FormField label={t('fields.title')} required className="md:col-span-2">
          <Input
            value={v.title ?? ''}
            onChange={(e) => set({ title: e.target.value })}
            data-testid="audit-work-paper-title"
          />
        </FormField>
      </div>
      {(['testStep', 'sample', 'result', 'conclusion'] as const).map((key) => (
        <FormField key={key} label={t(`fields.${key}`)}>
          <Textarea value={v[key] ?? ''} onChange={(e) => set({ [key]: e.target.value })} />
        </FormField>
      ))}
    </SimpleDialog>
  );
}

const recommendationRoute = getRouteApi('/app/audit-management/recommendations/$recommendationId');

/** Link target of the follow-up review approval: opens the finding of the recommendation. */
export function AuditRecommendationRedirect() {
  const { recommendationId } = recommendationRoute.useParams();
  const { t: tc } = useTranslation();
  const navigate = useNavigate();
  const recommendation = useRecommendation(recommendationId);
  const findingId = recommendation.data?.findingId;
  useEffect(() => {
    if (findingId)
      void navigate({
        to: '/audit-management/findings/$findingId',
        params: { findingId },
        search: { tab: 'recommendations' },
        replace: true,
      });
  }, [findingId, navigate]);
  return recommendation.isError ? (
    <StatusBanner tone="danger" title={errorMessage(tc, recommendation.error)} />
  ) : (
    <Skeleton className="h-48" />
  );
}
