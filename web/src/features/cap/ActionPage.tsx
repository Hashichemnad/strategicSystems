import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, Ban, CalendarClock, CheckCheck, Link2, Pencil, Sparkles, TrendingUp } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { HistoryTab } from '@/features/audit/HistoryTab';
import { DocumentsPanel } from '@/features/files/DocumentsPanel';
import { LinkedTasksPanel } from '@/features/tasks/LinkedTasksPanel';
import { ApprovalPanel, CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  DatePicker,
  FormField,
  Input,
  PageHeader,
  RecordLayout,
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
  ProgressBar,
  Section,
} from '@/ui';
import {
  capApi,
  useActionExtensions,
  useActionUpdates,
  useCapAction,
  useCapProjects,
  type ActionQuality,
  type CapAction,
} from './api';
import {
  ActionStatusChip,
  CapNav,
  Rows,
  SimpleDialog,
  ValidationChip,
  lookupTypes,
  useCapLabels,
  useCapT,
  useDate,
  useDialogMutation,
  useNames,
  usePct,
  useToastMutation,
} from './components';
import { ActionDialog, ReasonDialog } from './forms';
import { BudgetCell, QualityFlag, ValidateDialog } from './RemarkActionsTab';
import { ExternalReferencesSection, OriginBadge } from '@/features/integration/ExternalReferences';

const route = getRouteApi('/app/cap/actions/$actionId');

export const actionTabs = [
  'overview',
  'progress',
  'evidence',
  'extensions',
  'approvals',
  'tasks',
  'history',
] as const;
export type ActionTab = (typeof actionTabs)[number];

type DialogKind = 'progress' | 'completion' | 'extension' | 'cancel' | 'edit' | 'validate' | 'project';

/** Corrective action page (SPEC §9.5): achievement updates with history, evidence, extension, completion approval, budget routing. */
export function CapActionPage() {
  const { t } = useTranslation('cap');
  const { t: tc } = useTranslation();
  const { actionId } = route.useParams();
  const { tab } = route.useSearch();
  const navigate = route.useNavigate();
  const action = useCapAction(actionId);
  const names = useNames();
  const date = useDate();
  const [dialog, setDialog] = useState<DialogKind>();
  const [quality, setQuality] = useState<ActionQuality>();
  const check = useToastMutation(capApi.quality);
  const aiFeature = useAiFeature('cap-quality');
  const aiAllowed = useCan('cap.ai.quality');

  if (action.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (action.isError) return <StatusBanner tone="danger" title={errorMessage(tc, action.error)} />;
  const a = action.data;
  const go = (next: ActionTab) => void navigate({ search: { tab: next } });
  const close = () => setDialog(undefined);
  const home = a.remarks.find((r) => r.home) ?? a.remarks[0];

  return (
    <RecordLayout
      testId="cap-action-page"
      // CAP's own navigation first, as on every CAP page (CapPageLayout); hidden in the workspace themes (app tabs).
      back={
        <>
          <CapNav />
          {home && (
            <Link
              to="/cap/remarks/$remarkId"
              params={{ remarkId: home.id }}
              search={{ tab: 'actions' }}
              className="flex items-center gap-1 text-sm text-primary hover:underline"
            >
              <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
              <bdi>{home.number}</bdi>
            </Link>
          )}
        </>
      }
      highlights={[
        { key: 'number', label: t('fields.number'), value: <bdi>{a.number}</bdi> },
        { key: 'remark', label: t('fields.remark'), value: home ? <bdi>{home.number}</bdi> : '—' },
        { key: 'assignee', label: t('fields.assignee'), value: names.person(a.assignee) },
        { key: 'plannedStart', label: t('fields.plannedStart'), value: date(a.values.plannedStart) },
        {
          key: 'plannedEnd',
          label: t('fields.plannedEnd'),
          value: date(a.effectiveEnd ?? a.values.plannedEnd),
        },
        { key: 'actualStart', label: t('fields.actualStart'), value: date(a.actualStart) },
      ]}
      header={
        <PageHeader
          title={<bdi data-testid="cap-action-number">{a.number}</bdi>}
          description={
            <span className="line-clamp-3">{names.text(a.values.detailsAr, a.values.detailsEn)}</span>
          }
          meta={
            <div className="flex flex-wrap items-center gap-2" data-testid="cap-action-meta">
              <OriginBadge type="cap.action" id={a.id} />
              <ValidationChip status={a.validationStatus} />
              {a.validationStatus === 'approved' && <ActionStatusChip status={a.status} />}
              <ProgressBar
                value={a.achievedPct}
                tone={a.status === 'late' ? 'danger' : 'primary'}
                testId="cap-pct"
              />
              <span className="text-sm text-muted-foreground">
                {t('fields.plannedEnd')}: {date(a.effectiveEnd ?? a.values.plannedEnd)}
                {a.revisedEnd && ` (${t('action.revised')})`}
              </span>
              {a.remarks.length > 1 && (
                <StatusChip tone="info">
                  {t('actions.sharedWith', { count: a.remarks.length - 1 })}
                </StatusChip>
              )}
            </div>
          }
          actions={
            <div className="flex flex-wrap gap-2">
              {aiFeature && aiAllowed && a.can.edit && (
                <Button
                  variant="outline"
                  loading={check.busy}
                  onClick={() => check.run(a.id, setQuality)}
                  data-testid="cap-ai-quality"
                >
                  <Sparkles aria-hidden />
                  {t('ai.quality')}
                </Button>
              )}
              {a.can.edit && (
                <Button variant="outline" onClick={() => setDialog('edit')} data-testid="cap-action-edit">
                  <Pencil aria-hidden />
                  {tc('actions.edit')}
                </Button>
              )}
              {a.can.progress && (
                <Button onClick={() => setDialog('progress')} data-testid="cap-action-progress">
                  <TrendingUp aria-hidden />
                  {t('progress.update')}
                </Button>
              )}
              {a.can.requestExtension && (
                <Button
                  variant="outline"
                  onClick={() => setDialog('extension')}
                  data-testid="cap-action-extension"
                >
                  <CalendarClock aria-hidden />
                  {t('extension.request')}
                </Button>
              )}
              {a.can.requestCompletion && (
                <Button
                  variant="outline"
                  onClick={() => setDialog('completion')}
                  data-testid="cap-action-completion"
                >
                  <CheckCheck aria-hidden />
                  {t('completion.request')}
                </Button>
              )}
              {a.can.linkProject && (
                <Button
                  variant="outline"
                  onClick={() => setDialog('project')}
                  data-testid="cap-action-link-project"
                >
                  <Link2 aria-hidden />
                  {t('validation.project')}
                </Button>
              )}
              {a.can.cancel && (
                <Button variant="ghost" onClick={() => setDialog('cancel')} data-testid="cap-action-cancel">
                  <Ban aria-hidden />
                  {t('action.cancel')}
                </Button>
              )}
            </div>
          }
        />
      }
      status={
        <>
          <div data-testid="cap-action-current-status">
            <CurrentStatusBanner
              resourceType="cap.action"
              resourceId={a.id}
              onAction={(code) => {
                if (code === 'progress') setDialog('progress');
                if (code === 'request-completion') setDialog('completion');
                if (code === 'request-extension') setDialog('extension');
                if (code === 'decide') go('approvals');
              }}
            />
          </div>
          {a.validationNote && a.validationStatus !== 'approved' && (
            <StatusBanner tone="warning" title={t('validation.note')} description={a.validationNote} />
          )}
          {a.can.validate && a.validationStatus === 'pending' && (
            <StatusBanner
              tone="info"
              title={t('validation.pendingTitle')}
              description={
                <Button
                  size="sm"
                  className="mt-2"
                  onClick={() => setDialog('validate')}
                  data-testid="cap-action-validate"
                >
                  {t('validation.decide')}
                </Button>
              }
            />
          )}
        </>
      }
      summary={
        quality && (
          <Section
            title={
              <span className="flex items-center gap-2">
                {t('ai.quality')} <AiBadge />
              </span>
            }
            testId="cap-ai-quality-result"
          >
            {quality.flags.length === 0 && quality.issues.length === 0 ? (
              <p className="text-sm text-success">{t('ai.qualityOk')}</p>
            ) : (
              <>
                <span className="flex flex-wrap gap-1">
                  {quality.flags.map((f) => (
                    <QualityFlag key={f} flag={f} />
                  ))}
                </span>
                <ul className="list-inside list-disc text-sm">
                  {quality.issues.map((i) => (
                    <li key={i}>{i}</li>
                  ))}
                </ul>
                {(quality.rewriteAr || quality.rewriteEn) && (
                  <p className="rounded bg-muted p-2 text-sm">
                    {names.text(quality.rewriteAr, quality.rewriteEn)}
                  </p>
                )}
              </>
            )}
          </Section>
        )
      }
      activeSection={tab ?? 'overview'}
      onSectionChange={(key) => go(key as ActionTab)}
      sections={[
        {
          key: 'overview',
          label: t('tabs.overview'),
          testId: 'cap-action-tab-overview',
          content: (
            <>
              <Overview action={a} />
              <ExternalReferencesSection type="cap.action" id={a.id} />
            </>
          ),
        },
        {
          key: 'progress',
          label: t('tabs.progress'),
          testId: 'cap-action-tab-progress',
          content: <ProgressHistory action={a} />,
        },
        {
          key: 'evidence',
          label: t('tabs.evidence'),
          testId: 'cap-action-tab-evidence',
          count: a.evidenceCount,
          content: (
            <div className="flex flex-col gap-3">
              <p className="text-sm text-muted-foreground">{t('evidence.hint')}</p>
              <DocumentsPanel
                ownerType="cap.action"
                ownerId={a.id}
                canEdit={a.can.progress || a.can.requestCompletion}
              />
            </div>
          ),
        },
        {
          key: 'extensions',
          label: t('tabs.extensions'),
          testId: 'cap-action-tab-extensions',
          content: <Extensions action={a} />,
        },
        {
          key: 'approvals',
          label: t('tabs.approvals'),
          testId: 'cap-action-tab-approvals',
          content: <ApprovalPanel resourceType="cap.action" resourceId={a.id} />,
        },
        {
          key: 'tasks',
          label: t('tabs.tasks'),
          testId: 'cap-action-tab-tasks',
          content: <LinkedTasksPanel type="cap.action" id={a.id} />,
        },
        {
          key: 'history',
          label: t('tabs.history'),
          testId: 'cap-action-tab-history',
          content: <HistoryTab record="capAction" id={a.id} />,
        },
      ]}
    >
      {dialog === 'edit' && (
        <ActionDialog actionId={a.id} initial={a.values} version={a.version} onClose={close} />
      )}
      {dialog === 'progress' && <ProgressDialog action={a} onClose={close} />}
      {dialog === 'completion' && <CompletionDialog action={a} onClose={close} />}
      {dialog === 'extension' && <ExtensionDialog action={a} onClose={close} />}
      {dialog === 'cancel' && <CancelDialog action={a} onClose={close} />}
      {dialog === 'validate' && <ValidateDialog action={a} onClose={close} />}
      {dialog === 'project' && <ProjectDialog action={a} onClose={close} />}
    </RecordLayout>
  );
}

function Overview({ action: a }: { action: CapAction }) {
  const { t } = useTranslation('cap');
  const labels = useCapLabels();
  const names = useNames();
  const date = useDate();
  const v = a.values;
  return (
    <div className="flex flex-col gap-4" data-testid="cap-action-overview">
      <Rows
        rows={[
          [t('fields.detailsAr'), v.detailsAr],
          [t('fields.detailsEn'), v.detailsEn],
          [t('fields.actionType'), labels.lookup(lookupTypes.actionType, v.typeCode)],
          [t('fields.relatedProcess'), v.relatedProcess],
          [t('fields.assignee'), names.person(a.assignee)],
          [t('budget.title'), <BudgetCell key="b" action={a} />],
          [t('budget.cost'), v.estimatedCost == null ? '—' : String(v.estimatedCost)],
          [t('fields.plannedStart'), date(v.plannedStart)],
          [t('fields.plannedEnd'), date(v.plannedEnd)],
          [t('fields.revisedEnd'), date(a.revisedEnd)],
          [t('fields.actualStart'), date(a.actualStart)],
          [t('fields.actualEnd'), date(a.actualEnd)],
          [t('fields.evidence'), String(a.evidenceCount)],
          [
            t('fields.project'),
            a.project ? (
              <Link
                key="p"
                to="/portfolio/projects/$projectId"
                params={{ projectId: a.project.id }}
                className="text-primary hover:underline"
              >
                <bdi>{a.project.number}</bdi> {names.text(a.project.nameAr, a.project.nameEn)}
              </Link>
            ) : (
              '—'
            ),
          ],
          ...(a.completionNote ? ([[t('completion.note'), a.completionNote]] as [string, string][]) : []),
          ...(a.cancelReason ? ([[t('fields.cancelReason'), a.cancelReason]] as [string, string][]) : []),
        ]}
      />
      <Section title={t('action.remarks')}>
        <ul className="flex flex-wrap gap-2" data-testid="cap-action-remarks">
          {a.remarks.map((r) => (
            <li key={r.id}>
              <Link
                to="/cap/remarks/$remarkId"
                params={{ remarkId: r.id }}
                className="text-primary hover:underline"
              >
                <bdi>{r.number}</bdi>
              </Link>
              {r.home && <span className="ms-1 text-xs text-muted-foreground">({t('action.home')})</span>}
            </li>
          ))}
        </ul>
      </Section>
    </div>
  );
}

function ProgressHistory({ action: a }: { action: CapAction }) {
  const { t } = useTranslation('cap');
  const { language } = useCapT();
  const pct = usePct();
  const date = useDate();
  const updates = useActionUpdates(a.id);
  const confirm = useToastMutation(capApi.confirmDraft);
  return (
    <Section title={t('progress.history')} testId="cap-progress-history">
      {!updates.data ? (
        <Skeleton className="h-24" />
      ) : updates.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('progress.empty')}</p>
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('progress.at')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.achieved')}</TableHeaderCell>
              <TableHeaderCell>{t('progress.note')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.actualStart')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.actualEnd')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{t('common.actions')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {updates.data.map((u) => (
              <TableRow key={u.id} data-testid="cap-progress-row">
                <TableCell>{formatDateTime(u.at, language)}</TableCell>
                <TableCell>
                  <bdi className="tabular-nums">{pct(u.pct)}</bdi>
                  {u.draft && (
                    <StatusChip tone="info" className="ms-2">
                      {t('progress.draft')}
                    </StatusChip>
                  )}
                </TableCell>
                <TableCell className="max-w-sm whitespace-pre-line">{u.note ?? '—'}</TableCell>
                <TableCell>{date(u.actualStart)}</TableCell>
                <TableCell>{date(u.actualEnd)}</TableCell>
                <TableCell>
                  {u.draft && a.can.progress && (
                    <Button
                      size="sm"
                      variant="outline"
                      loading={confirm.busy}
                      onClick={() => confirm.run({ id: a.id, updateId: u.id })}
                    >
                      {t('progress.confirmDraft')}
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Section>
  );
}

function Extensions({ action: a }: { action: CapAction }) {
  const { t } = useTranslation('cap');
  const { k } = useCapT();
  const date = useDate();
  const extensions = useActionExtensions(a.id);
  return (
    <Section title={t('extension.title')} testId="cap-extensions">
      {!extensions.data ? (
        <Skeleton className="h-24" />
      ) : extensions.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('extension.empty')}</p>
      ) : (
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('extension.previousEnd')}</TableHeaderCell>
              <TableHeaderCell>{t('extension.requestedEnd')}</TableHeaderCell>
              <TableHeaderCell>{t('extension.justification')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
              <TableHeaderCell>{t('extension.decisionNote')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {extensions.data.map((x) => (
              <TableRow key={x.id} data-testid="cap-extension-row">
                <TableCell>{date(x.previousEnd)}</TableCell>
                <TableCell>{date(x.requestedEnd)}</TableCell>
                <TableCell className="max-w-sm whitespace-pre-line">{x.justification}</TableCell>
                <TableCell>
                  <StatusChip
                    tone={
                      x.status === 'approved'
                        ? 'success'
                        : x.status === 'rejected'
                          ? 'danger'
                          : x.status === 'pending'
                            ? 'info'
                            : 'neutral'
                    }
                  >
                    {k(`extensionStatus.${x.status}`)}
                  </StatusChip>
                </TableCell>
                <TableCell>{x.decisionNote ?? '—'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Section>
  );
}

function ProgressDialog({ action: a, onClose }: { action: CapAction; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const [pct, setPct] = useState(Math.round(a.achievedPct * 100));
  const [note, setNote] = useState('');
  const [actualStart, setActualStart] = useState<string | undefined>(a.actualStart ?? undefined);
  const [actualEnd, setActualEnd] = useState<string | undefined>(a.actualEnd ?? undefined);
  const m = useDialogMutation(capApi.progress, onClose);
  const valid = pct >= 0 && pct <= 100;
  return (
    <SimpleDialog
      title={t('progress.update')}
      description={t('progress.hint')}
      onClose={onClose}
      busy={m.busy}
      disabled={!valid}
      submitLabel={t('common.save')}
      testId="cap-progress-save"
      onSubmit={() =>
        m.run({
          id: a.id,
          pct: pct / 100,
          note: note.trim() || null,
          actualStart: actualStart ?? null,
          actualEnd: pct === 100 ? (actualEnd ?? null) : null,
          version: a.version,
        })
      }
    >
      <FormField label={t('progress.pct')} required>
        <Input
          type="number"
          min={0}
          max={100}
          step={5}
          value={pct}
          onChange={(e) => setPct(Number(e.target.value))}
          data-testid="cap-progress-pct"
        />
      </FormField>
      <FormField label={t('fields.actualStart')}>
        <DatePicker value={actualStart} onChange={setActualStart} />
      </FormField>
      {pct === 100 && (
        <FormField label={t('fields.actualEnd')} hint={t('progress.endHint')}>
          <DatePicker value={actualEnd} onChange={setActualEnd} />
        </FormField>
      )}
      <FormField label={t('progress.note')}>
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} data-testid="cap-progress-note" />
      </FormField>
    </SimpleDialog>
  );
}

function CompletionDialog({ action: a, onClose }: { action: CapAction; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const [note, setNote] = useState('');
  const m = useDialogMutation(capApi.requestCompletion, onClose);
  return (
    <SimpleDialog
      title={t('completion.request')}
      description={a.evidenceCount === 0 ? t('completion.noEvidence') : t('completion.hint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('completion.request')}
      testId="cap-completion-confirm"
      onSubmit={() => m.run({ id: a.id, note: note.trim() || null })}
    >
      <FormField label={t('completion.note')}>
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
      </FormField>
    </SimpleDialog>
  );
}

function ExtensionDialog({ action: a, onClose }: { action: CapAction; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const date = useDate();
  const [requestedEnd, setRequestedEnd] = useState<string>();
  const [justification, setJustification] = useState('');
  const m = useDialogMutation(capApi.requestExtension, onClose);
  return (
    <SimpleDialog
      title={t('extension.request')}
      description={t('extension.hint', { date: date(a.effectiveEnd ?? a.values.plannedEnd) })}
      onClose={onClose}
      busy={m.busy}
      disabled={!requestedEnd || !justification.trim()}
      submitLabel={t('extension.request')}
      testId="cap-extension-confirm"
      onSubmit={() => requestedEnd && m.run({ id: a.id, requestedEnd, justification: justification.trim() })}
    >
      <FormField label={t('extension.requestedEnd')} required>
        <DatePicker value={requestedEnd} onChange={setRequestedEnd} />
      </FormField>
      <FormField label={t('extension.justification')} required>
        <Textarea
          value={justification}
          onChange={(e) => setJustification(e.target.value)}
          data-testid="cap-extension-justification"
        />
      </FormField>
    </SimpleDialog>
  );
}

function CancelDialog({ action: a, onClose }: { action: CapAction; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const m = useDialogMutation(capApi.cancelAction, onClose);
  return (
    <ReasonDialog
      title={t('action.cancel')}
      label={t('fields.cancelReason')}
      submitLabel={t('action.cancel')}
      testId="cap-action-cancel-confirm"
      busy={m.busy}
      onClose={onClose}
      onSubmit={(reason) => m.run({ id: a.id, reason })}
    />
  );
}

function ProjectDialog({ action: a, onClose }: { action: CapAction; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const names = useNames();
  const [search, setSearch] = useState('');
  const [projectId, setProjectId] = useState<string | undefined>(a.projectId ?? undefined);
  const projects = useCapProjects(search, true);
  const m = useDialogMutation(capApi.linkProject, onClose);
  return (
    <SimpleDialog
      title={t('validation.project')}
      description={t('validation.projectHint')}
      onClose={onClose}
      busy={m.busy}
      submitLabel={t('common.save')}
      testId="cap-link-project-confirm"
      onSubmit={() => m.run({ id: a.id, projectId: projectId ?? null })}
    >
      <FormField label={t('validation.projectSearch')}>
        <Input value={search} onChange={(e) => setSearch(e.target.value)} />
      </FormField>
      <FormField label={t('validation.project')}>
        <Select
          value={projectId ?? '-'}
          onValueChange={(v) => setProjectId(v === '-' ? undefined : v)}
          options={[
            { value: '-', label: '—' },
            ...(projects.data ?? []).map((p) => ({
              value: p.id,
              label: `${p.number ?? ''} ${names.text(p.nameAr, p.nameEn)}`.trim(),
            })),
          ]}
        />
      </FormField>
    </SimpleDialog>
  );
}
