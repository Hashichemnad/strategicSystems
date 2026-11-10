import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, CircleCheck, CircleX, Pencil, Pause, Play, Ban, Undo2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage, translateMessageKey } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  RichTextEditor,
  RichTextView,
  PageHeader,
  RecordLayout,
  Select,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  useToast,
} from '@/ui';
import { projectResource, usePortfolioOptions, type PortfolioOptions } from '../api';
import {
  projectApi,
  toPercent,
  useProjectFull,
  useProjectMutation,
  visibleTabs,
  type CharterValues,
  type ProjectFull,
  type ProjectTab,
} from '../projectApi';
import { FieldErrors } from './FieldErrors';
import { offersRecall } from '../projectApi';
import { MilestonesTab } from './MilestonesTab';
import {
  ChallengesTab,
  ContractsTab,
  KpisTab,
  LessonsTab,
  MeetingsTab,
  PlanningTab,
  RisksTab,
  StakeholdersTab,
  TeamTab,
} from './ProjectItemTabs';
import { ChangesTab, ClosureTab, ImpactTab, MonthlyTab } from './StageTabs';
import { AiPlanDialog } from '../agile/AiPlanDialog';
import { BacklogTab } from '../agile/BacklogTab';
import { BoardTab } from '../agile/BoardTab';
import { MethodologyCard } from '../agile/MethodologyCard';
import { SprintsTab } from '../agile/SprintsTab';
import { usePortfolioFeatures } from '../featuresApi';
import { BenefitsTab } from '../standards/BenefitsTab';
import { CharterAssistantDialog } from '../standards/CharterAssistantDialog';
import { CommunicationTab } from '../standards/CommunicationTab';
import { GateChecklistPanel } from '../standards/GateChecklistPanel';
import { HealthTab } from '../standards/HealthTab';
import { RaciTab } from '../standards/RaciTab';
import { RaidTab } from '../standards/RaidTab';
import { useAiAssist } from '../standards/standardsApi';
import { GroupingCard } from '../views/GroupingCard';
import { ProjectGanttTab } from '../views/ProjectGanttTab';
import { WaselTab } from '../wasel/WaselTab';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/portfolio/projects/$projectId');

type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';
export const projectStatusTone: Record<string, Tone> = {
  active: 'success',
  'on-hold': 'warning',
  cancelled: 'neutral',
  completed: 'success',
  draft: 'neutral',
};

type SubmitStage = 'charter' | 'planning' | 'closure' | 'impact';
type Dialogs =
  | { kind: 'submit'; stage: SubmitStage }
  | { kind: 'status'; action: 'hold' | 'resume' | 'cancel' }
  | { kind: 'charter' }
  | { kind: 'recall' };

/** Project / initiative record page (SPEC §1.8, docs/design/portfolio.md §27). */
export function ProjectPage() {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const { projectId } = route.useParams();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const toast = useToast();
  const project = useProjectFull(projectId);
  const options = usePortfolioOptions();
  const features = usePortfolioFeatures();
  const take = useProjectMutation(projectId, projectApi.takeOwnership);
  const confirm = useProjectMutation(projectId, projectApi.confirmCompletion);
  const open = useProjectMutation(projectId, projectApi.openWindow);
  const [dialog, setDialog] = useState<Dialogs>();
  if (project.isPending || options.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (project.isError) return <StatusBanner tone="danger" title={errorMessage(tc, project.error)} />;
  if (options.isError) return <StatusBanner tone="danger" title={errorMessage(tc, options.error)} />;
  const p = project.data;
  const lang = i18n.language;
  const tabs = visibleTabs(p.template, p.delivery.methodology, features.data?.features);
  const tab: ProjectTab = tabs.includes(search.tab as ProjectTab) ? (search.tab as ProjectTab) : 'overview';
  const go = (next: ProjectTab) => void navigate({ search: { tab: next } });
  const fail = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });
  const actionLabels: Record<string, string> = {
    'take-ownership': t('actions.takeOwnership'),
    'submit-charter': t('actions.submitCharter'),
    'upload-lpo': t('actions.uploadLpo'),
    'request-team': t('actions.requestTeam'),
    'submit-planning': t('actions.submitPlanning'),
    'open-window': t('actions.openWindow'),
    'monthly-update': t('actions.monthlyUpdate'),
    'confirm-completion': t('actions.confirmCompletion'),
    closure: t('actions.closure'),
    impact: t('actions.impact'),
    resume: t('actions.resume'),
  };
  const onAction = (code: string) => {
    switch (code) {
      case 'take-ownership':
        take.mutate(
          { id: p.id, version: p.version },
          { onSuccess: () => toast({ title: t('project.toast.owner') }), onError: fail },
        );
        break;
      case 'submit-charter':
        setDialog({ kind: 'submit', stage: 'charter' });
        break;
      case 'submit-planning':
        setDialog({ kind: 'submit', stage: 'planning' });
        break;
      case 'upload-lpo':
        go('contracts');
        break;
      case 'request-team':
        go('team');
        break;
      case 'open-window':
        open.mutate(
          { id: p.id },
          { onSuccess: () => toast({ title: t('project.monthly.opened') }), onError: fail },
        );
        break;
      case 'monthly-update':
        go('milestones');
        break;
      case 'confirm-completion':
        confirm.mutate(
          { id: p.id, version: p.version },
          { onSuccess: () => toast({ title: t('project.toast.completed') }), onError: fail },
        );
        break;
      case 'closure':
        go('closure');
        break;
      case 'impact':
        go('impact');
        break;
      case 'resume':
        setDialog({ kind: 'status', action: 'resume' });
        break;
      case 'recall':
        setDialog({ kind: 'recall' });
        break;
    }
  };
  const live = p.status === 'active' || p.status === 'on-hold';
  const person = (x: typeof p.pm) => (x ? pickText(lang, x.nameAr, x.nameEn) : '—');
  const date = (value: string | null) => (value ? formatDate(value, lang) : '—');
  // Related lists of the Enterprise / Modern record layout: count cards that open the related tab (ADR 0170).
  const relatedTabs: { key: ProjectTab; count?: number }[] = [
    { key: 'milestones', count: p.counts.milestones },
    { key: 'risks', count: p.counts.risks },
    { key: 'kpis', count: p.counts.kpis },
    { key: 'stakeholders', count: p.counts.stakeholders },
    { key: 'team', count: p.counts.members },
    { key: 'meetings', count: p.counts.meetings },
    { key: 'contracts', count: p.counts.contracts },
    { key: 'changes', count: p.counts.changeRequests },
    { key: 'documents' },
    { key: 'approvals' },
  ];
  return (
    <RecordLayout
      testId="project-page"
      back={
        <Link
          to="/portfolio/projects"
          search={{ page: 1 }}
          className="flex items-center gap-1 text-sm text-primary hover:underline"
        >
          <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
          {t('projectsTitle')}
        </Link>
      }
      header={
        <PageHeader
          title={<span dir="auto">{pickText(lang, p.nameAr, p.nameEn)}</span>}
          description={
            <>
              <bdi>{p.number}</bdi>
              {p.pm && (
                <>
                  {' · '}
                  {t('project.pm')}: {pickText(lang, p.pm.nameAr, p.pm.nameEn)}
                </>
              )}
              {p.owner && (
                <>
                  {' · '}
                  {t('fields.owner')}: {pickText(lang, p.owner.nameAr, p.owner.nameEn)}
                </>
              )}
            </>
          }
          meta={
            <>
              <OriginBadge type="portfolio.project" id={p.id} />
              <StatusChip tone={projectStatusTone[p.status] ?? 'neutral'}>
                {t(`projectStatus.${p.status as 'active'}`)}
              </StatusChip>
              <StatusChip tone="info">{t(`classification.${p.classification as 'project'}`)}</StatusChip>
              <StatusChip tone="info">{t(`projectStages.${p.stage as 'planning'}`)}</StatusChip>
              <StatusChip tone={p.progressStatus === 'delayed' ? 'danger' : 'success'}>
                {formatNumber(toPercent(p.performance) ?? 0, lang)} % ·{' '}
                {t(`project.progress.${p.progressStatus === 'delayed' ? 'delayed' : 'onTrack'}`)}
              </StatusChip>
              {p.approval.process && p.approval.result === null && (
                <StatusChip tone="warning">
                  {pickText(lang, p.approval.stepNameAr, p.approval.stepNameEn)} –{' '}
                  {t(`project.processes.${p.approval.process.replace('portfolio.', '') as 'charter'}`)}
                </StatusChip>
              )}
            </>
          }
          actions={
            p.permissions.canManage &&
            live && (
              <div className="flex flex-wrap gap-2">
                {p.status === 'active' ? (
                  <Button variant="outline" onClick={() => setDialog({ kind: 'status', action: 'hold' })}>
                    <Pause aria-hidden />
                    {t('project.actions.hold')}
                  </Button>
                ) : (
                  <Button variant="outline" onClick={() => setDialog({ kind: 'status', action: 'resume' })}>
                    <Play aria-hidden />
                    {t('actions.resume')}
                  </Button>
                )}
                <Button variant="outline" onClick={() => setDialog({ kind: 'status', action: 'cancel' })}>
                  <Ban aria-hidden />
                  {t('project.actions.cancel')}
                </Button>
              </div>
            )
          }
        />
      }
      highlights={[
        { key: 'number', label: t('fields.number'), value: <bdi>{p.number}</bdi> },
        { key: 'pm', label: t('project.pm'), value: person(p.pm) },
        { key: 'owner', label: t('fields.owner'), value: person(p.owner) },
        {
          key: 'unit',
          label: t('fields.unit'),
          value: p.unit ? pickText(lang, p.unit.nameAr, p.unit.nameEn) : '—',
        },
        { key: 'start', label: t('fields.plannedStart'), value: date(p.plannedStart) },
        { key: 'end', label: t('fields.plannedEnd'), value: date(p.plannedEnd) },
      ]}
      path={
        <div data-testid="project-stepper">
          <StageStepper
            stages={p.stagePath.map((key) => ({ key, label: t(`projectStages.${key as 'planning'}`) }))}
            current={p.stage}
          />
        </div>
      }
      status={
        <CurrentStatusBanner
          resourceType={projectResource}
          resourceId={p.id}
          actionLabel={(code, key) => actionLabels[code] ?? translateMessageKey(tc, key)}
          onAction={onAction}
        />
      }
      summary={<ProgressStrip project={p} />}
      related={relatedTabs
        .filter((item) => tabs.includes(item.key))
        .map((item) => ({
          key: item.key,
          label: t(`project.tabs.${item.key}`),
          count: item.count,
          onOpen: () => go(item.key),
        }))}
    >
      <Tabs value={tab} onValueChange={(value) => go(value as ProjectTab)}>
        <TabsList className="flex-wrap">
          {tabs.map((key) => (
            <TabsTrigger key={key} value={key}>
              {t(`project.tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <Overview project={p} options={options.data} onEdit={() => setDialog({ kind: 'charter' })} />
          <ExternalReferencesSection type="portfolio.project" id={p.id} />
        </TabsContent>
        <TabsContent value="kpis">
          <KpisTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="risks">
          <RisksTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="stakeholders">
          <StakeholdersTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="team">
          <TeamTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="scope">
          <PlanningTab project={p} options={options.data} group="scope" />
        </TabsContent>
        <TabsContent value="assumptions">
          <PlanningTab project={p} options={options.data} group="assumptions" />
        </TabsContent>
        <TabsContent value="values">
          <PlanningTab project={p} options={options.data} group="values" />
        </TabsContent>
        <TabsContent value="milestones">
          <MilestonesTab
            project={p}
            today={options.data.today}
            canUpdate={p.permissions.canEdit && p.subState === 'execution'}
          />
        </TabsContent>
        <TabsContent value="backlog">
          <BacklogTab project={p} />
        </TabsContent>
        <TabsContent value="board">
          <BoardTab project={p} />
        </TabsContent>
        <TabsContent value="sprints">
          <SprintsTab project={p} />
        </TabsContent>
        <TabsContent value="gantt">
          <ProjectGanttTab project={p} />
        </TabsContent>
        <TabsContent value="health">
          <HealthTab project={p} />
        </TabsContent>
        <TabsContent value="raid">
          <RaidTab project={p} />
        </TabsContent>
        <TabsContent value="raci">
          <RaciTab project={p} />
        </TabsContent>
        <TabsContent value="communication">
          <CommunicationTab project={p} />
        </TabsContent>
        <TabsContent value="benefits">
          <BenefitsTab project={p} />
        </TabsContent>
        <TabsContent value="monthly">
          <MonthlyTab project={p} />
        </TabsContent>
        <TabsContent value="tasks">
          <LinkedTasksPanel type={projectResource} id={p.id} />
        </TabsContent>
        <TabsContent value="challenges">
          <ChallengesTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="meetings">
          <MeetingsTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="lessons">
          <LessonsTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="contracts">
          <ContractsTab project={p} options={options.data} />
        </TabsContent>
        <TabsContent value="vendors">
          <WaselTab project={p} />
        </TabsContent>
        <TabsContent value="changes">
          <ChangesTab project={p} />
        </TabsContent>
        <TabsContent value="closure">
          <ClosureTab
            project={p}
            options={options.data}
            onSubmit={() => setDialog({ kind: 'submit', stage: 'closure' })}
          />
        </TabsContent>
        <TabsContent value="impact">
          <ImpactTab project={p} onSubmit={() => setDialog({ kind: 'submit', stage: 'impact' })} />
        </TabsContent>
        <TabsContent value="approvals">
          {p.permissions.canRecall && (
            <div className="mb-3 flex justify-end">
              <Button
                variant="outline"
                onClick={() => setDialog({ kind: 'recall' })}
                data-testid="recall-approval"
              >
                <Undo2 aria-hidden />
                {t('actions.recall')}
              </Button>
            </div>
          )}
          <ApprovalPanel resourceType={projectResource} resourceId={p.id} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel
            ownerType={projectResource}
            ownerId={p.id}
            canEdit={p.permissions.canEdit && p.status === 'active'}
          />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="project" id={p.id} />
        </TabsContent>
      </Tabs>
      {dialog?.kind === 'submit' && (
        <SubmitDialog project={p} stage={dialog.stage} onClose={() => setDialog(undefined)} />
      )}
      {dialog?.kind === 'recall' && <RecallDialog project={p} onClose={() => setDialog(undefined)} />}
      {dialog?.kind === 'status' && (
        <StatusDialog
          project={p}
          action={dialog.action}
          onClose={() => setDialog(undefined)}
          onRecall={() => setDialog({ kind: 'recall' })}
        />
      )}
      {dialog?.kind === 'charter' && (
        <CharterDialog project={p} options={options.data} onClose={() => setDialog(undefined)} />
      )}
    </RecordLayout>
  );
}

function ProgressStrip({ project: p }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('portfolio');
  const lang = i18n.language;
  const pct = (r: number) => `${formatNumber(toPercent(r) ?? 0, lang)} %`;
  const bar = (value: number, tone: string) => (
    <div className="h-2 w-full overflow-hidden rounded-full bg-muted" aria-hidden>
      <div className={`h-full ${tone}`} style={{ width: `${Math.min(100, Math.max(0, value * 100))}%` }} />
    </div>
  );
  return (
    <section className="grid gap-4 rounded-lg border bg-card p-4 md:grid-cols-3" data-testid="progress-strip">
      <div className="flex flex-col gap-1">
        <span className="text-sm text-muted-foreground">{t('project.progress.performance')}</span>
        <span className="text-lg font-semibold tabular-nums">{pct(p.performance)}</span>
        {bar(p.performance, 'bg-primary')}
      </div>
      <div className="flex flex-col gap-1">
        <span className="text-sm text-muted-foreground">{t('project.progress.planned')}</span>
        <span className="text-lg font-semibold tabular-nums">{pct(p.plannedProgress)}</span>
        {bar(p.plannedProgress, 'bg-info')}
      </div>
      <div className="flex flex-col gap-1">
        <span className="text-sm text-muted-foreground">{t('project.progress.status')}</span>
        <span>
          <StatusChip tone={p.progressStatus === 'delayed' ? 'danger' : 'success'}>
            {t(`project.progress.${p.progressStatus === 'delayed' ? 'delayed' : 'onTrack'}`)}
          </StatusChip>
        </span>
      </div>
    </section>
  );
}

function Overview({
  project: p,
  options,
  onEdit,
}: {
  project: ProjectFull;
  options: PortfolioOptions;
  onEdit: () => void;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const lang = i18n.language;
  const money = (value: number | null | undefined) =>
    value === null || value === undefined ? '—' : `${formatNumber(value, lang)} ${options.currencyCode}`;
  const objective = options.objectives.find((o) => o.code === p.strategicObjectiveCode);
  const themes = p.operationalThemes
    .map((code) => options.themes.find((o) => o.code === code))
    .map((o) => (o ? pickText(lang, o.labelAr, o.labelEn) : null))
    .filter(Boolean)
    .join('، ');
  const c = p.charter;
  const rows: [string, string][] = [
    [t('fields.unit'), p.unit ? pickText(lang, p.unit.nameAr, p.unit.nameEn) : '—'],
    [t('project.section'), p.sectionUnit ? pickText(lang, p.sectionUnit.nameAr, p.sectionUnit.nameEn) : '—'],
    [t('project.pm'), p.pm ? pickText(lang, p.pm.nameAr, p.pm.nameEn) : '—'],
    [t('fields.owner'), p.owner ? pickText(lang, p.owner.nameAr, p.owner.nameEn) : '—'],
    [
      t('fields.strategicObjectiveCode'),
      objective ? pickText(lang, objective.labelAr, objective.labelEn) : (p.strategicObjectiveCode ?? '—'),
    ],
    [t('project.themes'), themes || '—'],
    [t('fields.budget'), money(p.budget)],
    [t('fields.plannedStart'), p.plannedStart ? formatDate(p.plannedStart, lang) : '—'],
    [t('fields.plannedEnd'), p.plannedEnd ? formatDate(p.plannedEnd, lang) : '—'],
  ];
  // The description is rich text (ADR 0161); it is shown through RichTextView, the other texts stay plain.
  const texts: [string, string | null | undefined][] = [
    [t('fields.scopeOfWork'), p.scopeOfWork],
    [t('project.charter.structuralChanges'), c.structuralChanges],
    [t('project.charter.resourcesHuman'), c.resourcesHuman],
    [t('project.charter.resourcesFinancial'), c.resourcesFinancial],
    [t('project.charter.resourcesTechnical'), c.resourcesTechnical],
    [t('project.charter.resourcesContract'), c.resourcesContract],
    [t('project.charter.resourcesOther'), c.resourcesOther],
  ];
  const gate =
    p.subState === 'planning-draft' || p.stage !== 'initiation' ? p.gates.planning : p.gates.charter;
  const gateTitle = gate === p.gates.charter ? t('project.gates.charter') : t('project.gates.planning');
  return (
    <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
      <section className="flex flex-col gap-4 rounded-lg border bg-card p-6" data-testid="project-overview">
        <header className="flex items-center justify-between gap-2">
          <h2 className="font-semibold">{t('project.charter.title')}</h2>
          {p.permissions.canEditCharter && (
            <Button variant="outline" size="sm" onClick={onEdit}>
              <Pencil aria-hidden />
              {t('project.charter.edit')}
            </Button>
          )}
        </header>
        <dl className="grid gap-4 md:grid-cols-2">
          {rows.map(([key, value]) => (
            <div key={key}>
              <dt className="text-sm text-muted-foreground">{key}</dt>
              <dd className="mt-1" dir="auto">
                {value}
              </dd>
            </div>
          ))}
        </dl>
        {p.description && (
          <div>
            <h3 className="text-sm text-muted-foreground">{t('fields.description')}</h3>
            <RichTextView className="mt-1" value={p.description} />
          </div>
        )}
        {texts
          .filter(([, value]) => value)
          .map(([key, value]) => (
            <div key={key}>
              <h3 className="text-sm text-muted-foreground">{key}</h3>
              <p className="mt-1 whitespace-pre-line" dir="auto">
                {value}
              </p>
            </div>
          ))}
        {p.holdReason && (
          <StatusBanner tone="warning" title={t('project.holdReason')} description={p.holdReason} />
        )}
        {p.cancelReason && (
          <StatusBanner tone="info" title={t('fields.cancelReason')} description={p.cancelReason} />
        )}
        <p className="text-sm">
          <Link
            to="/portfolio/requests/$requestId"
            params={{ requestId: p.requestId }}
            search={{ tab: 'overview' }}
            className="text-primary hover:underline"
          >
            {t('projects.fromRequest', { number: p.requestNumber ?? '' })}
          </Link>
        </p>
      </section>
      <div className="flex flex-col gap-4">
        <MethodologyCard project={p} />
        <PlanAssists project={p} options={options} />
        {p.status === 'active' && p.subState !== 'done' && (
          <section className="flex flex-col gap-2 rounded-lg border bg-card p-4" data-testid="project-gate">
            <h3 className="font-medium">{gateTitle}</h3>
            <ul className="flex flex-col gap-1 text-sm">
              {gate.map((g) => (
                <li key={g.code} className="flex items-start gap-2" data-met={g.met}>
                  {g.met ? (
                    <CircleCheck
                      className="mt-0.5 size-4 shrink-0 text-success"
                      aria-label={t('project.gates.met')}
                    />
                  ) : (
                    <CircleX
                      className="mt-0.5 size-4 shrink-0 text-danger"
                      aria-label={t('project.gates.missing')}
                    />
                  )}
                  <GateText messageKey={g.messageKey} params={g.params} />
                </li>
              ))}
            </ul>
            <GateChecklistPanel project={p} gate={gate === p.gates.charter ? 'charter' : 'planning'} />
          </section>
        )}
        <GroupingCard project={p} />
        <section className="flex flex-col gap-2 rounded-lg border bg-card p-4">
          <h3 className="font-medium">{t('project.costs')}</h3>
          {(
            [
              ['year1Cost', p.year1PlannedCost, c.year1ContractedCost, c.year1ActualCost],
              ['year2Cost', p.year2PlannedCost, c.year2ContractedCost, c.year2ActualCost],
              ['year3Cost', p.year3PlannedCost, c.year3ContractedCost, c.year3ActualCost],
              ['year4Cost', p.year4PlannedCost, c.year4ContractedCost, c.year4ActualCost],
            ] as const
          ).map(([key, planned, contracted, actual]) => (
            <div key={key} className="text-sm">
              <span className="text-muted-foreground">{t(`fields.${key}`)}</span>
              <div className="flex justify-between tabular-nums">
                <span>
                  {t('project.planned')}: {money(planned)}
                </span>
                <span>
                  {t('project.contracted')}: {money(contracted)}
                </span>
                <span>
                  {t('project.actual')}: {money(actual)}
                </span>
              </div>
            </div>
          ))}
        </section>
      </div>
    </div>
  );
}

function GateText({ messageKey, params }: { messageKey: string; params?: Record<string, unknown> | null }) {
  const { t: tc } = useTranslation();
  return <span>{translateMessageKey(tc, messageKey, params)}</span>;
}

function SubmitDialog({
  project,
  stage,
  onClose,
}: {
  project: ProjectFull;
  stage: SubmitStage;
  onClose: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const submit = useProjectMutation(project.id, projectApi.submit);
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t(`project.submit.${stage}`)}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={submit.isPending}
              onClick={() =>
                submit.mutate(
                  { id: project.id, version: project.version, stage, comment: comment.trim() || null },
                  {
                    onSuccess: () => {
                      toast({ title: t('toast.submitted') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('actions.submit')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="project-submit-dialog">
          <FormField label={t('dialogs.submitComment')}>
            <Textarea value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
          {submit.isError && <FieldErrors error={submit.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Recall a pending approval with a reason (submitter or PM Office) — then the project can be corrected or put on hold. */
function RecallDialog({ project, onClose }: { project: ProjectFull; onClose: () => void }) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const recall = useProjectMutation(project.id, projectApi.recall);
  const [reason, setReason] = useState('');
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('project.recall.title')}
        description={t('project.recall.hint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              disabled={reason.trim() === ''}
              loading={recall.isPending}
              data-testid="recall-confirm"
              onClick={() =>
                recall.mutate(
                  { id: project.id, version: project.version, reason: reason.trim() },
                  {
                    onSuccess: () => {
                      toast({ title: t('project.recall.done') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('actions.recall')}
            </Button>
          </>
        }
      >
        <FormField label={t('project.reason')} required>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {recall.isError && <FieldErrors error={recall.error} />}
      </DialogContent>
    </Dialog>
  );
}

function StatusDialog({
  project,
  action,
  onClose,
  onRecall,
}: {
  project: ProjectFull;
  action: 'hold' | 'resume' | 'cancel';
  onClose: () => void;
  onRecall: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const change = useProjectMutation(project.id, projectApi.status);
  const [reason, setReason] = useState('');
  const needsReason = action !== 'resume';
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t(`project.statusDialog.${action}`)}
        description={t(`project.statusDialog.${action}Hint`)}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.close')}</Button>
            </DialogClose>
            <Button
              variant={action === 'cancel' ? 'danger' : 'primary'}
              disabled={needsReason && reason.trim() === ''}
              loading={change.isPending}
              onClick={() =>
                change.mutate(
                  { id: project.id, version: project.version, action, reason: reason.trim() || null },
                  { onSuccess: onClose },
                )
              }
            >
              {t(`project.statusDialog.${action}`)}
            </Button>
          </>
        }
      >
        {needsReason && (
          <FormField label={t('project.reason')} required>
            <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
          </FormField>
        )}
        {change.isError && <FieldErrors error={change.error} />}
        {change.isError && offersRecall(change.error) && project.permissions.canRecall && (
          <Button variant="outline" onClick={onRecall} data-testid="hold-recall">
            <Undo2 aria-hidden />
            {t('actions.recall')}
          </Button>
        )}
      </DialogContent>
    </Dialog>
  );
}

function CharterDialog({
  project: p,
  options,
  onClose,
}: {
  project: ProjectFull;
  options: PortfolioOptions;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(p.id, projectApi.charter);
  const [v, setV] = useState<CharterValues>({ ...p.charter, operationalThemes: p.operationalThemes });
  const set = (patch: Partial<CharterValues>) => setV((c) => ({ ...c, ...patch }));
  const lang = i18n.language;
  const text = (key: keyof CharterValues, label: string, area = false) => (
    <FormField label={label} className={area ? 'md:col-span-2' : undefined}>
      {area ? (
        <Textarea
          value={(v[key] as string | null) ?? ''}
          onChange={(e) => set({ [key]: e.target.value || null })}
        />
      ) : (
        <Input
          dir="auto"
          value={(v[key] as string | null) ?? ''}
          onChange={(e) => set({ [key]: e.target.value || null })}
        />
      )}
    </FormField>
  );
  const money = (key: keyof CharterValues, label: string) => (
    <FormField label={label}>
      <Input
        dir="ltr"
        inputMode="decimal"
        value={v[key] === null || v[key] === undefined ? '' : String(v[key])}
        onChange={(e) => set({ [key]: e.target.value.trim() === '' ? null : Number(e.target.value) })}
      />
    </FormField>
  );
  const themes = v.operationalThemes ?? [];
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('project.charter.edit')}
        className="max-w-3xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  { id: p.id, version: p.version, values: v },
                  {
                    onSuccess: () => {
                      toast({ title: t('project.saved') });
                      onClose();
                    },
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid max-h-[65vh] gap-3 overflow-y-auto md:grid-cols-2" data-testid="charter-dialog">
          {text('nameAr', t('fields.nameAr'))}
          {text('nameEn', t('fields.nameEn'))}
          <FormField label={t('fields.description')} className="md:col-span-2">
            <RichTextEditor
              aria-label={t('fields.description')}
              value={v.description ?? ''}
              onChange={(html) => set({ description: html })}
            />
          </FormField>
          {text('scopeOfWork', t('fields.scopeOfWork'), true)}
          <FormField label={t('fields.strategicObjectiveCode')}>
            <Select
              value={v.strategicObjectiveCode ?? '*'}
              onValueChange={(value) => set({ strategicObjectiveCode: value === '*' ? null : value })}
              options={[
                { value: '*', label: t('values.notSet') },
                ...options.objectives.map((o) => ({
                  value: o.code,
                  label: pickText(lang, o.labelAr, o.labelEn),
                })),
              ]}
            />
          </FormField>
          <fieldset className="flex flex-col gap-1">
            <legend className="text-sm font-medium">{t('project.themes')}</legend>
            {options.themes.map((o) => (
              <Checkbox
                key={o.code}
                label={pickText(lang, o.labelAr, o.labelEn)}
                checked={themes.includes(o.code)}
                onChange={(e) =>
                  set({
                    operationalThemes: e.target.checked
                      ? [...themes, o.code]
                      : themes.filter((x) => x !== o.code),
                  })
                }
              />
            ))}
          </fieldset>
          <FormField label={t('fields.plannedStart')}>
            <DatePicker
              value={v.plannedStart ?? undefined}
              onChange={(d) => set({ plannedStart: d ?? null })}
            />
          </FormField>
          <FormField label={t('fields.plannedEnd')}>
            <DatePicker value={v.plannedEnd ?? undefined} onChange={(d) => set({ plannedEnd: d ?? null })} />
          </FormField>
          {text('structuralChanges', t('project.charter.structuralChanges'), true)}
          {text('resourcesHuman', t('project.charter.resourcesHuman'), true)}
          {text('resourcesFinancial', t('project.charter.resourcesFinancial'), true)}
          {text('resourcesTechnical', t('project.charter.resourcesTechnical'), true)}
          {text('resourcesContract', t('project.charter.resourcesContract'), true)}
          {text('resourcesOther', t('project.charter.resourcesOther'), true)}
          {money('year1ContractedCost', `${t('fields.year1Cost')} — ${t('project.contracted')}`)}
          {money('year1ActualCost', `${t('fields.year1Cost')} — ${t('project.actual')}`)}
          {money('year2ContractedCost', `${t('fields.year2Cost')} — ${t('project.contracted')}`)}
          {money('year2ActualCost', `${t('fields.year2Cost')} — ${t('project.actual')}`)}
          {money('year3ContractedCost', `${t('fields.year3Cost')} — ${t('project.contracted')}`)}
          {money('year3ActualCost', `${t('fields.year3Cost')} — ${t('project.actual')}`)}
          {money('year4ContractedCost', `${t('fields.year4Cost')} — ${t('project.contracted')}`)}
          {money('year4ActualCost', `${t('fields.year4Cost')} — ${t('project.actual')}`)}
          {money('contractedInitialCost', t('project.charter.contractedInitialCost'))}
          {money('annualOperationCost', t('fields.annualOperationCost'))}
          {money('annualMaintenanceCost', t('fields.annualMaintenanceCost'))}
          {money('annualLicenceCost', t('fields.annualLicenceCost'))}
          {money('annualOtherCost', t('fields.annualOtherCost'))}
          {save.isError && (
            <div className="md:col-span-2">
              <FieldErrors error={save.error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** AI assists for planning (SPEC §1.15 B): the charter assistant and the plan generator — drafts the PM reviews before anything is saved. */
function PlanAssists({ project: p, options }: { project: ProjectFull; options: PortfolioOptions }) {
  const { t } = useTranslation('portfolio');
  const charterAi = useAiAssist('portfolio-charter-assistant');
  const planAi = useAiAssist('portfolio-plan-generator');
  const [open, setOpen] = useState<'charter' | 'plan'>();
  const showCharter = charterAi && p.permissions.canEditContent;
  const showPlan = planAi && p.permissions.canPlanMilestones;
  if (!showCharter && !showPlan) return null;
  return (
    <section className="flex flex-wrap gap-2 rounded-lg border bg-card p-4" data-testid="plan-assists">
      {showCharter && (
        <Button variant="outline" size="sm" onClick={() => setOpen('charter')} data-testid="ai-charter">
          {t('project.assist.aiCharter')}
        </Button>
      )}
      {showPlan && (
        <Button variant="outline" size="sm" onClick={() => setOpen('plan')} data-testid="ai-plan">
          {t('project.assist.aiPlan')}
        </Button>
      )}
      {open === 'charter' && (
        <CharterAssistantDialog
          project={p}
          options={options}
          open
          onOpenChange={(v) => {
            if (!v) setOpen(undefined);
          }}
        />
      )}
      {open === 'plan' && (
        <AiPlanDialog
          project={p}
          open
          onOpenChange={(v) => {
            if (!v) setOpen(undefined);
          }}
        />
      )}
    </section>
  );
}
