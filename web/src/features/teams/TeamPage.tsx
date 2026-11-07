import { getRouteApi, Link, useNavigate } from '@tanstack/react-router';
import { ArrowLeft, Download, Pencil, RefreshCw, Upload } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { contentUrl, useAttachments } from '@/features/files/api';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkBadge } from '@/features/links/LinkBadge';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DatePicker,
  FormField,
  Input,
  PageHeader,
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
import { statusTone, teamApi, useTeam, useTeamMutation, type Team } from './api';
import {
  EvaluationsPanel,
  MeetingsPanel,
  MembersPanel,
  RecommendationsPanel,
  ReportsPanel,
} from './TeamPanels';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/teams/$teamId');

export const teamTabs = [
  'overview',
  'members',
  'meetings',
  'recommendations',
  'tasks',
  'reports',
  'documents',
  'evaluations',
  'approvals',
  'history',
] as const;

function stageOf(status: string): string {
  switch (status) {
    case 'draft':
    case 'cancelled':
      return 'request';
    case 'pending_approval':
      return 'approval';
    case 'pending_evaluation':
    case 'evaluation_waiting_approval':
      return 'evaluation';
    case 'closed':
    case 'replaced':
      return 'closed';
    default:
      return 'active';
  }
}

/** Team page (SPEC §4.6): header, stage stepper, current status with the next actions, and the tabs of the team's life. */
export function TeamPage() {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const { teamId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const go = useNavigate();
  const team = useTeam(teamId);
  const [action, setAction] = useState<string>();

  if (team.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (team.isError) return <StatusBanner tone="danger" title={errorMessage(tc, team.error)} />;
  const data = team.data;
  const name = pickText(i18n.language, data.nameAr, data.nameEn);
  const fieldLabels: Record<string, string> = {
    NameAr: t('fields.nameAr'),
    NameEn: t('fields.nameEn'),
    Status: t('fields.status'),
    Subject: t('fields.subject'),
    Objectives: t('fields.objectives'),
    Mandate: t('fields.mandate'),
    StartDate: t('fields.startDate'),
    EndDate: t('fields.endDate'),
    MemoNumber: t('fields.memoNumber'),
    RoleCode: t('form.role'),
  };

  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/teams"
        search={{ view: 'all', page: 1 }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('title')}
      </Link>
      <PageHeader
        title={name}
        description={
          <>
            <bdi>{data.number}</bdi> · {pickText(i18n.language, data.typeNameAr, data.typeNameEn)}
          </>
        }
        meta={
          <>
            <OriginBadge type="teams.team" id={data.id} />
            <StatusChip tone={statusTone[data.status] ?? 'neutral'}>
              {t(`status.${data.status as 'active'}`)}
            </StatusChip>
            <StatusChip tone="neutral">
              {t(data.permanent ? 'permanence.permanent' : 'permanence.temporary')}
            </StatusChip>
          </>
        }
        actions={
          <div className="flex flex-wrap gap-2">
            {data.permissions.canEdit && (
              <Button variant="outline" asChild>
                <Link to="/teams/$teamId/edit" params={{ teamId: data.id }}>
                  <Pencil aria-hidden />
                  {t('actions.edit')}
                </Link>
              </Button>
            )}
            {data.actions
              .filter((a) => a !== 'submit')
              .map((a) => (
                <Button
                  key={a}
                  variant={a === 'open-evaluation' ? 'primary' : 'outline'}
                  onClick={() => setAction(a)}
                >
                  {t(`actions.${a as 'close'}`)}
                </Button>
              ))}
          </div>
        }
      />
      <StageStepper
        stages={(['request', 'approval', 'active', 'evaluation', 'closed'] as const).map((key) => ({
          key,
          label: t(`stages.${key}`),
        }))}
        current={stageOf(data.status)}
      />
      <CurrentStatusBanner
        resourceType="teams.team"
        resourceId={data.id}
        actionLabel={(code, key) =>
          code === 'submit'
            ? t('actions.submit')
            : code === 'evaluate'
              ? t('actions.evaluate')
              : tc(key as 'actions.edit')
        }
        onAction={(code) => {
          if (code === 'submit') setAction('submit');
          if (code === 'evaluate') void navigate({ search: { tab: 'evaluations' } });
        }}
      />
      <Tabs value={tab} onValueChange={(value) => void navigate({ search: { tab: value as 'overview' } })}>
        <TabsList className="flex-wrap">
          {teamTabs.map((key) => (
            <TabsTrigger key={key} value={key}>
              {t(`tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <Overview team={data} />
          <ExternalReferencesSection type="teams.team" id={data.id} />
        </TabsContent>
        <TabsContent value="members">
          <MembersPanel team={data} />
        </TabsContent>
        <TabsContent value="meetings">
          <MeetingsPanel
            team={data}
            onOpen={(id) => void go({ to: '/teams/meetings/$meetingId', params: { meetingId: id } })}
          />
        </TabsContent>
        <TabsContent value="recommendations">
          <RecommendationsPanel team={data} />
        </TabsContent>
        <TabsContent value="tasks">
          <LinkedTasksPanel type="teams.team" id={data.id} />
        </TabsContent>
        <TabsContent value="reports">
          <ReportsPanel team={data} />
        </TabsContent>
        <TabsContent value="documents">
          <DocumentsPanel
            ownerType="teams.team"
            ownerId={data.id}
            canEdit={data.permissions.canManage || data.permissions.canOperate}
          />
        </TabsContent>
        <TabsContent value="evaluations">
          <EvaluationsPanel team={data} />
        </TabsContent>
        <TabsContent value="approvals">
          <ApprovalPanel resourceType="teams.team" resourceId={data.id} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryTab record="team" id={data.id} fieldLabels={fieldLabels} />
        </TabsContent>
      </Tabs>
      {action && <ActionDialog team={data} action={action} onClose={() => setAction(undefined)} />}
    </div>
  );
}

function Overview({ team }: { team: Team }) {
  const { t, i18n } = useTranslation('teams');
  const person = (p: { nameAr?: string | null; nameEn?: string | null } | null) =>
    p ? pickText(i18n.language, p.nameAr, p.nameEn) : '—';
  const rows: [string, string][] = [
    [
      t('fields.owningUnit'),
      team.owningUnit ? pickText(i18n.language, team.owningUnit.nameAr, team.owningUnit.nameEn) : '—',
    ],
    [t('fields.leader'), person(team.leader)],
    [t('fields.sponsor'), person(team.sponsor)],
    [t('fields.requestedBy'), person(team.requestedBy)],
    [t('fields.startDate'), team.startDate ? formatDate(team.startDate, i18n.language) : '—'],
    [t('fields.endDate'), team.endDate ? formatDate(team.endDate, i18n.language) : t('permanence.permanent')],
    [t('fields.cadence'), t(`cadence.${team.cadence as 'monthly'}`)],
    [t('fields.workload'), t(`workload.${team.workload as 'high'}`)],
  ];
  return (
    <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
      <div className="flex flex-col gap-4 rounded-lg border bg-card p-6">
        <dl className="grid gap-4 md:grid-cols-2">
          {rows.map(([label, value]) => (
            <div key={label}>
              <dt className="text-sm text-muted-foreground">{label}</dt>
              <dd className="mt-1">{value}</dd>
            </div>
          ))}
        </dl>
        {(
          [
            ['subject', team.subject],
            ['objectives', team.objectives],
            ['mandate', team.mandate],
          ] as const
        ).map(([key, value]) => (
          <div key={key}>
            <h3 className="text-sm text-muted-foreground">{t(`fields.${key}`)}</h3>
            <p className="mt-1 whitespace-pre-line" dir="auto">
              {value ?? '—'}
            </p>
          </div>
        ))}
        {team.linkedRecords.length > 0 && (
          <div>
            <h3 className="mb-2 text-sm text-muted-foreground">{t('fields.linkedRecords')}</h3>
            <ul className="flex flex-col gap-2">
              {team.linkedRecords.map((view, i) => (
                <li key={i}>
                  <LinkBadge view={view} />
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
      <div className="flex flex-col gap-4">
        <MemoCard team={team} />
        <section
          className="grid grid-cols-2 gap-3 rounded-lg border bg-card p-4 text-center"
          data-testid="team-stats"
        >
          <Stat label={t('tabs.members')} value={team.stats.activeMembers} />
          <Stat label={t('tabs.meetings')} value={team.stats.meetingsHeld} />
          <Stat label={t('recommendations.statuses.open')} value={team.stats.recommendationsOpen} />
          <Stat
            label={t('recommendations.statuses.closed')}
            value={
              team.stats.closurePercent === null
                ? '—'
                : `${formatNumber(team.stats.closurePercent, i18n.language)} %`
            }
          />
          {team.permissions.canViewResults && team.lastScore !== null && (
            <Stat label={t('card.lastScore')} value={formatNumber(team.lastScore, i18n.language)} />
          )}
        </section>
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: number | string }) {
  const { i18n } = useTranslation();
  return (
    <div>
      <p className="text-2xl font-semibold tabular-nums">
        {typeof value === 'number' ? formatNumber(value, i18n.language) : value}
      </p>
      <p className="text-xs text-muted-foreground">{label}</p>
    </div>
  );
}

/** The formation memo: download, "being generated", or use an uploaded external memo (ADR 0044). */
function MemoCard({ team }: { team: Team }) {
  const { t, i18n } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const files = useAttachments('teams.team', team.id);
  const [external, setExternal] = useState(false);
  const regenerate = useTeamMutation(teamApi.regenerateMemo);
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-4" data-testid="memo-card">
      <h3 className="font-semibold">{t('fields.memoMode')}</h3>
      <dl className="grid grid-cols-2 gap-2 text-sm">
        <dt className="text-muted-foreground">{t('fields.memoNumber')}</dt>
        <dd>{team.memoNumber ?? '—'}</dd>
        <dt className="text-muted-foreground">{t('fields.memoDate')}</dt>
        <dd>{team.memoDate ? formatDate(team.memoDate, i18n.language) : '—'}</dd>
      </dl>
      <p className="text-xs text-muted-foreground">{t(`memoMode.${team.memoMode as 'generate'}`)}</p>
      <div className="flex flex-wrap gap-2">
        {team.memoFileId ? (
          <Button variant="outline" asChild>
            <a
              href={contentUrl(team.memoFileId)}
              target="_blank"
              rel="noreferrer"
              data-testid="memo-download"
            >
              <Download aria-hidden />
              {t('actions.downloadMemo')}
            </a>
          </Button>
        ) : team.activatedAt && team.memoMode === 'generate' ? (
          <StatusChip tone="info">{t('actions.memoPending')}</StatusChip>
        ) : null}
        {(team.permissions.canManage || (team.status === 'draft' && team.permissions.canSubmit)) && (
          <Button variant="ghost" onClick={() => setExternal(true)}>
            <Upload aria-hidden />
            {t('actions.uploadMemo')}
          </Button>
        )}
        {team.permissions.canManage && team.activatedAt && team.memoMode === 'generate' && (
          <Button
            variant="ghost"
            loading={regenerate.isPending}
            onClick={() =>
              regenerate.mutate(team.id, {
                onError: (f) => toast({ title: errorMessage(tc, f), tone: 'error' }),
              })
            }
          >
            <RefreshCw aria-hidden />
            {t('actions.regenerateMemo')}
          </Button>
        )}
      </div>
      {external && (
        <ExternalMemoDialog
          team={team}
          pdfs={(files.data ?? [])
            .filter((f) => f.contentType === 'application/pdf')
            .map((f) => ({ value: f.id, label: f.name }))}
          onClose={() => setExternal(false)}
        />
      )}
    </section>
  );
}

function ExternalMemoDialog({
  team,
  pdfs,
  onClose,
}: {
  team: Team;
  pdfs: { value: string; label: string }[];
  onClose: () => void;
}) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const [number, setNumber] = useState('');
  const [date, setDate] = useState<string>();
  const [fileId, setFileId] = useState<string>();
  const [error, setError] = useState<string>();
  const save = useTeamMutation(teamApi.externalMemo);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('actions.uploadMemo')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={!number.trim() || !date || !fileId}
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  { id: team.id, number, date: date ?? '', fileId: fileId ?? '', version: team.version },
                  { onSuccess: onClose, onError: (f) => setError(errorMessage(tc, f)) },
                )
              }
            >
              {t('actions.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        <FormField label={t('fields.memoNumber')}>
          <Input value={number} onChange={(e) => setNumber(e.target.value)} />
        </FormField>
        <FormField label={t('fields.memoDate')}>
          <DatePicker value={date} onChange={setDate} />
        </FormField>
        <FormField label={t('tabs.documents')}>
          <Select value={fileId} onValueChange={setFileId} options={pdfs} />
        </FormField>
      </DialogContent>
    </Dialog>
  );
}

/** Lifecycle actions with a reason (cancel, deactivate, close), submit with a comment, reactivate, open evaluation. */
function ActionDialog({ team, action, onClose }: { team: Team; action: string; onClose: () => void }) {
  const { t } = useTranslation('teams');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const [text, setText] = useState('');
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [error, setError] = useState<string>();
  const run = useTeamMutation(async () => {
    if (action === 'submit')
      return teamApi.submit({ id: team.id, version: team.version, comment: text || null });
    if (action === 'open-evaluation')
      return teamApi.openEvaluation({ teamId: team.id, year: team.permanent ? Number(year) : null });
    return teamApi.status({
      id: team.id,
      action: action as 'close',
      version: team.version,
      reason: text || null,
    });
  });
  const needsReason = action === 'cancel' || action === 'deactivate' || action === 'close';
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t(`actions.${action as 'close'}`)}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={needsReason && !text.trim()}
              loading={run.isPending}
              onClick={() =>
                run.mutate(undefined, {
                  onSuccess: () => {
                    toast({ title: t(`actions.${action as 'close'}`) });
                    onClose();
                  },
                  onError: (f) => setError(errorMessage(tc, f)),
                })
              }
            >
              {t('actions.confirm')}
            </Button>
          </>
        }
      >
        {error && <StatusBanner tone="danger" title={error} />}
        {action === 'open-evaluation' ? (
          team.permanent && (
            <FormField label={t('evaluations.year')}>
              <Input type="number" value={year} onChange={(e) => setYear(e.target.value)} />
            </FormField>
          )
        ) : action !== 'reactivate' ? (
          <FormField label={t('actions.reason')} required={needsReason}>
            <Textarea rows={3} value={text} onChange={(e) => setText(e.target.value)} />
          </FormField>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}
