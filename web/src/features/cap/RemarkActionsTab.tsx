import { Link } from '@tanstack/react-router';
import { Link2, Pencil, Plus, Send, Sparkles, Unlink } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import {
  Button,
  FormField,
  Input,
  Select,
  Skeleton,
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
  useCapProjects,
  useLinkableActions,
  useRemarkActions,
  type ActionSuggestion,
  type CapAction,
  type CapActionValues,
  type CapRemark,
} from './api';
import {
  ActionStatusChip,
  SimpleDialog,
  ValidationChip,
  useCapT,
  useDate,
  useDialogMutation,
  useNames,
  useToastMutation,
} from './components';
import { ActionDialog, emptyAction } from './forms';

/** Actions of a remark (new or linked — one action can serve several remarks), submission and the PM Office validation per action. */
export function RemarkActionsTab({ remark: r, onSubmit }: { remark: CapRemark; onSubmit: () => void }) {
  const { t } = useTranslation('cap');
  const actions = useRemarkActions(r.id);
  const names = useNames();
  const date = useDate();
  const unlink = useToastMutation(capApi.unlinkAction);
  const [creating, setCreating] = useState<CapActionValues | null>(null);
  const [editing, setEditing] = useState<CapAction>();
  const [linking, setLinking] = useState(false);
  const [suggesting, setSuggesting] = useState(false);
  const [validating, setValidating] = useState<CapAction>();
  const aiFeature = useAiFeature('cap-suggest');
  const aiAllowed = useCan('cap.ai.suggest');

  return (
    <div className="flex flex-col gap-4" data-testid="cap-remark-actions">
      <Section
        title={t('actions.title')}
        actions={
          <>
            {r.can.manageActions && (
              <>
                {aiFeature && aiAllowed && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => setSuggesting(true)}
                    data-testid="cap-ai-suggest"
                  >
                    <Sparkles aria-hidden />
                    {t('ai.suggest')}
                  </Button>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setLinking(true)}
                  data-testid="cap-action-link"
                >
                  <Link2 aria-hidden />
                  {t('actions.linkExisting')}
                </Button>
                <Button size="sm" onClick={() => setCreating(emptyAction())} data-testid="cap-action-new">
                  <Plus aria-hidden />
                  {t('action.new')}
                </Button>
              </>
            )}
            {r.can.submit && (
              <Button size="sm" onClick={onSubmit} data-testid="cap-remark-submit">
                <Send aria-hidden />
                {t('remark.submit')}
              </Button>
            )}
          </>
        }
      >
        {!actions.data ? (
          <Skeleton className="h-32" />
        ) : actions.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('actions.empty')}</p>
        ) : (
          <Table data-testid="cap-actions-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.details')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.assignee')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.plannedEnd')}</TableHeaderCell>
                <TableHeaderCell>{t('budget.title')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.validation')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.achieved')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{t('common.actions')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {actions.data.map((a) => (
                <TableRow key={a.id} data-testid="cap-action-row">
                  <TableCell>
                    <Link
                      to="/cap/actions/$actionId"
                      params={{ actionId: a.id }}
                      className="whitespace-nowrap font-medium text-primary hover:underline"
                    >
                      <bdi>{a.number}</bdi>
                    </Link>
                    {a.remarks.length > 1 && (
                      <span className="block text-xs text-muted-foreground" data-testid="cap-action-shared">
                        {t('actions.sharedWith', { count: a.remarks.length - 1 })}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="max-w-sm">
                    <span className="line-clamp-2">{names.text(a.values.detailsAr, a.values.detailsEn)}</span>
                    {a.qualityFlags.length > 0 && (
                      <span className="mt-1 flex flex-wrap gap-1">
                        {a.qualityFlags.map((f) => (
                          <QualityFlag key={f} flag={f} />
                        ))}
                      </span>
                    )}
                  </TableCell>
                  <TableCell>{names.person(a.assignee)}</TableCell>
                  <TableCell>{date(a.effectiveEnd ?? a.values.plannedEnd)}</TableCell>
                  <TableCell>
                    <BudgetCell action={a} />
                  </TableCell>
                  <TableCell>
                    <ValidationChip status={a.validationStatus} />
                    {a.validationNote && (
                      <span className="block text-xs text-muted-foreground">{a.validationNote}</span>
                    )}
                  </TableCell>
                  <TableCell>
                    {a.validationStatus === 'approved' ? <ActionStatusChip status={a.status} /> : '—'}
                  </TableCell>
                  <TableCell>
                    <ProgressBar
                      value={a.achievedPct}
                      tone={a.status === 'late' ? 'danger' : 'primary'}
                      testId="cap-pct"
                    />
                  </TableCell>
                  <TableCell>
                    <span className="flex flex-wrap justify-end gap-1">
                      {a.can.validate && a.validationStatus === 'pending' && (
                        <Button size="sm" onClick={() => setValidating(a)} data-testid="cap-action-validate">
                          {t('validation.decide')}
                        </Button>
                      )}
                      {a.can.edit && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setEditing(a)}
                          aria-label={t('action.edit')}
                        >
                          <Pencil aria-hidden />
                        </Button>
                      )}
                      {a.can.unlink && r.can.manageActions && a.homeRemarkId !== r.id && (
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={t('actions.unlink')}
                          loading={unlink.busy}
                          onClick={() => unlink.run({ remarkId: r.id, actionId: a.id })}
                        >
                          <Unlink aria-hidden />
                        </Button>
                      )}
                    </span>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>
      {creating && <ActionDialog remarkId={r.id} initial={creating} onClose={() => setCreating(null)} />}
      {editing && (
        <ActionDialog
          actionId={editing.id}
          initial={editing.values}
          version={editing.version}
          onClose={() => setEditing(undefined)}
        />
      )}
      {linking && <LinkActionDialog remark={r} onClose={() => setLinking(false)} />}
      {suggesting && (
        <SuggestDialog
          remark={r}
          onClose={() => setSuggesting(false)}
          onUse={(values) => {
            setSuggesting(false);
            setCreating(values);
          }}
        />
      )}
      {validating && <ValidateDialog action={validating} onClose={() => setValidating(undefined)} />}
    </div>
  );
}

export function QualityFlag({ flag }: { flag: string }) {
  const { k } = useCapT();
  return (
    <StatusChip tone="warning">
      <span data-testid="cap-quality-flag">{k(`quality.${flag}`)}</span>
    </StatusChip>
  );
}

export function BudgetCell({ action: a }: { action: CapAction }) {
  const { t } = useTranslation('cap');
  if (!a.values.budgetRequired)
    return <span className="text-muted-foreground">{t('budget.notRequired')}</span>;
  return (
    <span className="flex flex-col gap-1 text-sm" data-testid="cap-action-budget-cell">
      <span>{a.values.budgetAvailable ? t('budget.availableYes') : t('budget.availableNo')}</span>
      {a.programRequestId && (
        <Link
          to="/portfolio/requests/$requestId"
          params={{ requestId: a.programRequestId }}
          search={{ tab: 'overview' }}
          className="text-xs text-primary hover:underline"
          data-testid="cap-program-request-link"
        >
          {t('budget.programRequest')}: <bdi>{a.programRequestNumber}</bdi>
        </Link>
      )}
    </span>
  );
}

function LinkActionDialog({ remark, onClose }: { remark: CapRemark; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const names = useNames();
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string>();
  const candidates = useLinkableActions(remark.id, search, true);
  const m = useDialogMutation(capApi.linkAction, onClose);
  return (
    <SimpleDialog
      title={t('actions.linkExisting')}
      description={t('actions.linkHint')}
      onClose={onClose}
      busy={m.busy}
      disabled={!selected}
      submitLabel={t('actions.link')}
      testId="cap-action-link-confirm"
      wide
      onSubmit={() => selected && m.run({ remarkId: remark.id, actionId: selected })}
    >
      <Input
        placeholder={t('common.search')}
        aria-label={t('common.search')}
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        data-testid="cap-action-link-search"
      />
      {!candidates.data ? (
        <Skeleton className="h-24" />
      ) : candidates.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('actions.noLinkable')}</p>
      ) : (
        <ul
          className="flex flex-col divide-y rounded-md border"
          role="listbox"
          aria-label={t('actions.linkExisting')}
        >
          {candidates.data.map((a) => (
            <li key={a.id}>
              <button
                type="button"
                role="option"
                aria-selected={selected === a.id}
                onClick={() => setSelected(a.id)}
                className={`flex w-full flex-col items-start gap-0.5 p-2 text-start text-sm hover:bg-muted ${selected === a.id ? 'bg-muted' : ''}`}
                data-testid="cap-action-link-option"
              >
                <span className="font-medium">
                  <bdi>{a.number}</bdi> · <bdi>{a.homeRemarkNumber}</bdi>
                </span>
                <span>{names.text(a.detailsAr, a.detailsEn)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </SimpleDialog>
  );
}

function SuggestDialog({
  remark,
  onClose,
  onUse,
}: {
  remark: CapRemark;
  onClose: () => void;
  onUse: (values: CapActionValues) => void;
}) {
  const { t } = useTranslation('cap');
  const names = useNames();
  const [result, setResult] = useState<ActionSuggestion>();
  const run = useToastMutation(capApi.suggestActions);
  const link = useDialogMutation(capApi.linkAction, onClose);
  const addDays = (days: number | null) => {
    if (!days) return null;
    const d = new Date();
    d.setDate(d.getDate() + days);
    return d.toISOString().slice(0, 10);
  };
  return (
    <SimpleDialog
      title={t('ai.suggest')}
      description={t('ai.suggestHint')}
      onClose={onClose}
      busy={run.busy}
      submitLabel={t('ai.run')}
      testId="cap-ai-suggest-run"
      wide
      onSubmit={() => run.run(remark.id, setResult)}
    >
      {result && (
        <>
          <Section
            title={
              <span className="flex items-center gap-2">
                {t('ai.drafts')} <AiBadge />
              </span>
            }
          >
            <ul className="flex flex-col divide-y" data-testid="cap-ai-suggestions">
              {result.actions.map((a, i) => (
                <li key={i} className="flex items-start justify-between gap-3 py-2 text-sm">
                  <span>{names.text(a.detailsAr, a.detailsEn)}</span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      onUse({
                        ...emptyAction(),
                        detailsAr: a.detailsAr ?? '',
                        detailsEn: a.detailsEn ?? '',
                        typeCode: a.typeCode,
                        plannedStart: new Date().toISOString().slice(0, 10),
                        plannedEnd: addDays(a.durationDays),
                      })
                    }
                  >
                    {t('ai.use')}
                  </Button>
                </li>
              ))}
            </ul>
          </Section>
          {result.duplicates.length > 0 && (
            <Section title={t('ai.duplicates')}>
              <ul className="flex flex-col divide-y text-sm">
                {result.duplicates.map((d) => (
                  <li key={d.id} className="flex items-start justify-between gap-3 py-2">
                    <span>
                      <bdi className="font-medium">{d.number}</bdi> · {names.text(d.detailsAr, d.detailsEn)}
                    </span>
                    <Button
                      size="sm"
                      variant="outline"
                      loading={link.busy}
                      onClick={() => link.run({ remarkId: remark.id, actionId: d.id })}
                    >
                      {t('actions.link')}
                    </Button>
                  </li>
                ))}
              </ul>
            </Section>
          )}
        </>
      )}
    </SimpleDialog>
  );
}

/** PM Office validation of one action: approve / reject / return (comment required unless approving), optional project link. */
export function ValidateDialog({ action: a, onClose }: { action: CapAction; onClose: () => void }) {
  const { t } = useTranslation('cap');
  const names = useNames();
  const [decision, setDecision] = useState<string>();
  const [comment, setComment] = useState('');
  const [projectSearch, setProjectSearch] = useState('');
  const [projectId, setProjectId] = useState<string>();
  const projects = useCapProjects(projectSearch, decision === 'approve');
  const m = useDialogMutation(capApi.validate, onClose);
  const routed = a.values.budgetRequired && a.values.budgetAvailable === false;
  return (
    <SimpleDialog
      title={t('validation.title', { number: a.number })}
      description={names.text(a.values.detailsAr, a.values.detailsEn)}
      onClose={onClose}
      busy={m.busy}
      disabled={!decision || (decision !== 'approve' && !comment.trim())}
      submitLabel={t('validation.decide')}
      testId="cap-validate-confirm"
      wide
      onSubmit={() =>
        decision &&
        m.run({ id: a.id, decision, comment: comment.trim() || null, projectId: projectId ?? null })
      }
    >
      <FormField label={t('validation.decision')} required>
        <Select
          value={decision}
          onValueChange={setDecision}
          aria-label={t('validation.decision')}
          options={(['approve', 'return', 'reject'] as const).map((d) => ({
            value: d,
            label: t(`validation.${d}`),
          }))}
        />
      </FormField>
      {routed && decision === 'approve' && (
        <p className="rounded bg-info/10 p-2 text-sm" data-testid="cap-validate-routing">
          {t('budget.routingOnApprove')}
        </p>
      )}
      {decision === 'approve' && (
        <FormField label={t('validation.projectSearch')} hint={t('validation.projectHint')}>
          <Input value={projectSearch} onChange={(e) => setProjectSearch(e.target.value)} />
        </FormField>
      )}
      {decision === 'approve' && projects.data && projects.data.length > 0 && (
        <FormField label={t('validation.project')}>
          <Select
            value={projectId ?? '-'}
            onValueChange={(v) => setProjectId(v === '-' ? undefined : v)}
            options={[
              { value: '-', label: '—' },
              ...projects.data.map((p) => ({
                value: p.id,
                label: `${p.number ?? ''} ${names.text(p.nameAr, p.nameEn)}`.trim(),
              })),
            ]}
          />
        </FormField>
      )}
      <FormField label={t('common.comment')} required={decision !== undefined && decision !== 'approve'}>
        <Textarea
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          data-testid="cap-validate-comment"
        />
      </FormField>
    </SimpleDialog>
  );
}
