import { useQueryClient } from '@tanstack/react-query';
import { Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { unwrap } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { formatDate } from '@/lib/format';
import {
  Button,
  EmptyState,
  FormField,
  Input,
  Skeleton,
  StatusBanner,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import {
  useVendorProposals,
  waselApi,
  waselKeys,
  type ProposalDiffEntry,
  type ProposalItem,
  type VendorProject,
  type VendorProposal,
} from './waselApi';
import {
  diffValue,
  formatPercent,
  nextKey,
  parseTasks,
  percentToRatio,
  proposalDiffKind,
  proposedWeight,
  ratioToPercent,
  submissionTones,
} from './waselLogic';

/** Plan proposal (W3.2): the vendor drafts phases / milestones (or epics / stories), the Lead submits, the PM accepts with edits or returns. */
export function WaselPlanTab({ project }: { project: VendorProject }) {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const proposals = useVendorProposals(project.id);
  const queryClient = useQueryClient();
  const toast = useToast();
  const [items, setItems] = useState<ProposalItem[]>([]);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const open = proposals.data?.proposals.find((p) => ['draft', 'submitted', 'returned'].includes(p.status));
  const editable = open === undefined || open.status === 'draft' || open.status === 'returned';
  const canPrepare = project.permissions.includes('wasel.plan.prepare');
  const canSubmit = project.permissions.includes('wasel.plan.submit');

  useEffect(() => {
    if (open) {
      setItems(open.items);
      setNote(open.vendorNote ?? '');
    }
  }, [open]);

  if (proposals.isPending) return <Skeleton className="mt-4 h-40" />;
  if (proposals.error) return <StatusBanner tone="danger" title={errorMessage(tc, proposals.error)} />;
  const data = proposals.data;
  const kind = data.kind;
  const parentType = kind === 'agile' ? 'epic' : 'phase';
  const leafType = kind === 'agile' ? 'story' : 'milestone';
  const total = proposedWeight(items, kind);

  async function run(action: () => Promise<unknown>, message: string) {
    setBusy(true);
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: waselKeys.proposals(project.id) });
      toast({ title: message });
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(false);
    }
  }

  const save = () =>
    run(async () => {
      const body = { items, note: note || null, version: open?.version ?? null };
      return open
        ? unwrap(
            await waselApi.PUT('/api/wasel/v1/projects/{id}/proposals/{pid}', {
              params: { path: { id: project.id, pid: open.id } },
              body,
            }),
          )
        : unwrap(
            await waselApi.POST('/api/wasel/v1/projects/{id}/proposals', {
              params: { path: { id: project.id } },
              body,
            }),
          );
    }, t('plan.saved'));

  const move = (action: 'submit' | 'withdraw') => {
    if (!open) return Promise.resolve();
    const init = {
      params: { path: { id: project.id, pid: open.id } },
      body: { version: open.version },
      headers: { 'Idempotency-Key': crypto.randomUUID() },
    };
    return run(
      async () =>
        unwrap(
          action === 'submit'
            ? await waselApi.POST('/api/wasel/v1/projects/{id}/proposals/{pid}/submit', init)
            : await waselApi.POST('/api/wasel/v1/projects/{id}/proposals/{pid}/withdraw', init),
        ),
      action === 'submit' ? t('plan.submitted') : t('plan.withdrawn'),
    );
  };

  const update = (key: string, patch: Partial<ProposalItem>) =>
    setItems((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));

  const add = (type: string) =>
    setItems((rows) => [
      ...rows,
      {
        key: nextKey(rows, type === parentType ? 'p' : 'm'),
        type,
        parentKey: type === leafType ? (rows.find((r) => r.type === parentType)?.key ?? null) : null,
        titleAr: null,
        titleEn: null,
        plannedStart: project.plannedStart,
        plannedEnd: project.plannedEnd,
        weight: type === (kind === 'agile' ? 'epic' : 'milestone') ? 0 : null,
        isDeliverable: false,
        tasks: [],
        storyPoints: type === 'story' ? 1 : null,
      },
    ]);

  return (
    <div className="flex flex-col gap-6 pt-4" data-testid="wasel-plan">
      {!project.canProposePlan && <StatusBanner tone="info" title={t('plan.notPlanning')} />}
      {open && (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm">{t('plan.current', { sequence: open.sequence })}</span>
          <span data-testid="wasel-plan-status">
            <StatusChip tone={submissionTones[open.status] ?? 'neutral'}>
              {t(`proposalStatus.${open.status}`, { defaultValue: open.status })}
            </StatusChip>
          </span>
        </div>
      )}
      {open?.status === 'returned' && open.pmComment && (
        <StatusBanner tone="warning" title={t('plan.returnedTitle')} description={open.pmComment} />
      )}

      {project.canProposePlan && canPrepare && (
        <section className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="font-semibold">{t('plan.editor')}</h2>
            <p className="text-sm text-muted-foreground" data-testid="wasel-plan-weight">
              {t('plan.weightTotal', {
                total: formatPercent(total, i18n.language),
                available: formatPercent(data.availableWeight, i18n.language),
              })}
            </p>
          </div>
          {items.length === 0 ? (
            <EmptyState title={t('plan.empty')} />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[56rem] text-sm">
                <thead className="text-xs text-muted-foreground">
                  <tr className="text-start">
                    <th className="p-2 text-start">{t('plan.type')}</th>
                    <th className="p-2 text-start">{t('plan.parent')}</th>
                    <th className="p-2 text-start">{t('plan.titleAr')}</th>
                    <th className="p-2 text-start">{t('plan.titleEn')}</th>
                    {kind !== 'agile' && <th className="p-2 text-start">{t('plan.start')}</th>}
                    {kind !== 'agile' && <th className="p-2 text-start">{t('plan.end')}</th>}
                    <th className="p-2 text-start">
                      {kind === 'agile' ? t('plan.weightOrPoints') : t('plan.weight')}
                    </th>
                    <th className="p-2 text-start">{kind === 'agile' ? t('plan.story') : t('plan.tasks')}</th>
                    <th className="p-2" />
                  </tr>
                </thead>
                <tbody>
                  {items.map((row) => (
                    <tr key={row.key} className="border-t align-top" data-testid="wasel-plan-row">
                      <td className="p-2">{t(`itemType.${row.type}`, { defaultValue: row.type })}</td>
                      <td className="p-2">
                        {row.type === leafType ? (
                          <select
                            className="h-10 rounded-md border border-input bg-card px-2"
                            value={row.parentKey ?? ''}
                            disabled={!editable}
                            onChange={(e) => update(row.key, { parentKey: e.target.value || null })}
                            aria-label={t('plan.parent')}
                          >
                            <option value="">—</option>
                            {items
                              .filter((r) => r.type === parentType)
                              .map((r) => (
                                <option key={r.key} value={r.key}>
                                  {r.titleEn ?? r.titleAr ?? r.key}
                                </option>
                              ))}
                          </select>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td className="p-2">
                        <Input
                          dir="rtl"
                          value={row.titleAr ?? ''}
                          disabled={!editable}
                          onChange={(e) => update(row.key, { titleAr: e.target.value || null })}
                          aria-label={t('plan.titleAr')}
                        />
                      </td>
                      <td className="p-2">
                        <Input
                          dir="ltr"
                          value={row.titleEn ?? ''}
                          disabled={!editable}
                          onChange={(e) => update(row.key, { titleEn: e.target.value || null })}
                          aria-label={t('plan.titleEn')}
                          data-testid="wasel-plan-title-en"
                        />
                      </td>
                      {kind !== 'agile' && (
                        <td className="p-2">
                          <Input
                            type="date"
                            value={row.plannedStart ?? ''}
                            disabled={!editable}
                            onChange={(e) => update(row.key, { plannedStart: e.target.value || null })}
                            aria-label={t('plan.start')}
                          />
                        </td>
                      )}
                      {kind !== 'agile' && (
                        <td className="p-2">
                          <Input
                            type="date"
                            value={row.plannedEnd ?? ''}
                            disabled={!editable}
                            onChange={(e) => update(row.key, { plannedEnd: e.target.value || null })}
                            aria-label={t('plan.end')}
                          />
                        </td>
                      )}
                      <td className="p-2">
                        {row.type === 'story' ? (
                          <Input
                            type="number"
                            min={0}
                            max={100}
                            className="w-24"
                            value={row.storyPoints ?? 0}
                            disabled={!editable}
                            onChange={(e) => update(row.key, { storyPoints: Number(e.target.value) })}
                            aria-label={t('plan.points')}
                          />
                        ) : row.type === (kind === 'agile' ? 'epic' : 'milestone') ? (
                          <Input
                            type="number"
                            min={0}
                            max={100}
                            step="0.01"
                            className="w-24"
                            value={ratioToPercent(row.weight)}
                            disabled={!editable}
                            onChange={(e) => update(row.key, { weight: percentToRatio(e.target.value) })}
                            aria-label={t('plan.weight')}
                            data-testid="wasel-plan-weight-input"
                          />
                        ) : (
                          '—'
                        )}
                      </td>
                      <td className="p-2">
                        {row.type === 'milestone' && (
                          <Input
                            value={(row.tasks ?? []).join(', ')}
                            disabled={!editable}
                            onChange={(e) => update(row.key, { tasks: parseTasks(e.target.value) })}
                            aria-label={t('plan.tasks')}
                          />
                        )}
                        {row.type === 'story' && (
                          <Input
                            value={row.iWant ?? ''}
                            disabled={!editable}
                            onChange={(e) => update(row.key, { iWant: e.target.value || null })}
                            aria-label={t('plan.story')}
                          />
                        )}
                      </td>
                      <td className="p-2">
                        {editable && (
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={t('plan.remove')}
                            onClick={() => setItems((rows) => rows.filter((r) => r.key !== row.key))}
                          >
                            <Trash2 aria-hidden />
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {editable && (
            <>
              <div className="flex flex-wrap gap-2">
                <Button variant="outline" onClick={() => add(parentType)} data-testid="wasel-plan-add-parent">
                  <Plus aria-hidden />
                  {t(`plan.add.${parentType}`)}
                </Button>
                <Button variant="outline" onClick={() => add(leafType)} data-testid="wasel-plan-add-leaf">
                  <Plus aria-hidden />
                  {t(`plan.add.${leafType}`)}
                </Button>
              </div>
              <FormField label={t('plan.note')}>
                <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
              </FormField>
              <div className="flex flex-wrap gap-2">
                <Button
                  onClick={() => void save()}
                  loading={busy}
                  disabled={items.length === 0}
                  data-testid="wasel-plan-save"
                >
                  {t('plan.save')}
                </Button>
                {open && canSubmit && open.status === 'draft' && (
                  <Button
                    variant="secondary"
                    onClick={() => void move('submit')}
                    loading={busy}
                    data-testid="wasel-plan-submit"
                  >
                    {t('plan.submit')}
                  </Button>
                )}
                {open && (
                  <Button variant="ghost" onClick={() => void move('withdraw')} disabled={busy}>
                    {t('plan.withdraw')}
                  </Button>
                )}
              </div>
              {!canSubmit && <p className="text-xs text-muted-foreground">{t('plan.contributorHint')}</p>}
            </>
          )}
        </section>
      )}

      <section className="flex flex-col gap-3">
        <h2 className="font-semibold">{t('plan.history')}</h2>
        {data.proposals.length === 0 ? (
          <EmptyState title={t('plan.none')} />
        ) : (
          data.proposals.map((proposal) => <ProposalCard key={proposal.id} proposal={proposal} />)
        )}
      </section>
    </div>
  );
}

function ProposalCard({ proposal }: { proposal: VendorProposal }) {
  const { t, i18n } = useTranslation('wasel');
  const rows = proposal.acceptedItems ?? proposal.items;
  return (
    <article
      className="flex flex-col gap-3 rounded-xl border bg-card p-4 text-sm"
      data-testid="wasel-proposal"
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium">{t('plan.current', { sequence: proposal.sequence })}</span>
        <StatusChip tone={submissionTones[proposal.status] ?? 'neutral'}>
          {t(`proposalStatus.${proposal.status}`, { defaultValue: proposal.status })}
        </StatusChip>
        {proposal.decidedAt && (
          <span className="text-xs text-muted-foreground">
            {formatDate(proposal.decidedAt, i18n.language)}
          </span>
        )}
      </div>
      {proposal.pmComment && (
        <p>
          <span className="text-muted-foreground">{t('plan.pmComment')}: </span>
          {proposal.pmComment}
        </p>
      )}
      <ul className="flex flex-col gap-1">
        {rows.map((row) => (
          <li key={row.key} className={row.type === 'phase' || row.type === 'epic' ? 'font-medium' : 'ps-4'}>
            {row.titleEn ?? row.titleAr ?? row.key}
            {(row.weight ?? 0) > 0 && (
              <span className="text-muted-foreground"> · {formatPercent(row.weight, i18n.language)}</span>
            )}
          </li>
        ))}
      </ul>
      {proposal.diff.length > 0 && <ProposalDiff diff={proposal.diff} />}
    </article>
  );
}

/** The PM's changes before accepting: field, vendor value → PM value, or an added / removed item (ADR 0114). */
export function ProposalDiff({ diff }: { diff: readonly ProposalDiffEntry[] }) {
  const { t, i18n } = useTranslation('wasel');
  return (
    <div
      className="rounded-lg border border-warning/40 bg-warning-soft/40 p-3"
      data-testid="wasel-proposal-diff"
    >
      <p className="mb-2 font-medium">{t('diff.pmChanges')}</p>
      <ul className="flex flex-col gap-1">
        {diff.map((entry) => {
          const kind = proposalDiffKind(entry);
          return (
            <li key={`${entry.key}-${entry.field}`} data-testid="wasel-diff-entry">
              <span className="text-muted-foreground" dir="ltr">
                {entry.key}
              </span>{' '}
              {kind === 'added' && t('diff.added', { value: entry.pm ?? '' })}
              {kind === 'removed' && t('diff.removed', { value: entry.vendor ?? '' })}
              {kind === 'changed' && (
                <>
                  {t(`diff.field.${entry.field}`, { defaultValue: entry.field })}:{' '}
                  <del>{diffValue(entry.field, entry.vendor, i18n.language)}</del> →{' '}
                  <ins className="no-underline font-medium">
                    {diffValue(entry.field, entry.pm, i18n.language)}
                  </ins>
                </>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
