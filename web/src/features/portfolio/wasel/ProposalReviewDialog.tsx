import { MessageSquare } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDate, formatDateTime, pickText } from '@/lib/format';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
} from '@/ui';
import { fromPercent, type ProjectFull, toPercent } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { type PmProposal, type ProposalItem, usePanelMutation, waselApi } from './waselApi';
import { DiffList } from './DiffList';
import { percent } from './waselFormat';
import { statusTone } from './WaselTab';

/** A vendor's proposed plan (§51): read-only card; the PM reviews an editable copy and accepts it into the plan or returns it. */
export function ProposalReviewDialog({
  project,
  proposal,
  canManage,
  onComments,
}: {
  project: ProjectFull;
  proposal: PmProposal;
  canManage: boolean;
  onComments: () => void;
}) {
  const { t, i18n } = useTranslation('vendors');
  const [open, setOpen] = useState<'review' | 'return' | null>(null);
  const lang = i18n.language;
  const items = proposal.acceptedItems ?? proposal.items;
  return (
    <article className="rounded-lg border bg-card p-4" data-testid="wasel-proposal">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="font-medium">{t('proposal.number', { n: proposal.sequence })}</h3>
          <StatusChip tone={statusTone[proposal.status] ?? 'neutral'}>{t(`proposal.status.${proposal.status}`, proposal.status)}</StatusChip>
          <span className="text-sm text-muted-foreground">{pickText(lang, proposal.vendorNameAr, proposal.vendorNameEn)}</span>
          {proposal.submittedAt && (
            <span className="text-xs text-muted-foreground">
              {t('proposal.submittedAt', { at: formatDateTime(proposal.submittedAt, lang) })}
            </span>
          )}
        </div>
        <div className="flex gap-2">
          {canManage && proposal.status === 'submitted' && (
            <>
              <Button size="sm" onClick={() => setOpen('review')} data-testid="wasel-proposal-review">
                {t('proposal.review')}
              </Button>
              <Button size="sm" variant="outline" onClick={() => setOpen('return')} data-testid="wasel-proposal-return">
                {t('actions.return')}
              </Button>
            </>
          )}
          <Button size="sm" variant="ghost" aria-label={t('comments.open')} onClick={onComments}>
            <MessageSquare aria-hidden />
          </Button>
        </div>
      </header>
      {proposal.vendorNote && (
        <p dir="auto" className="mt-2 text-sm">
          {proposal.vendorNote}
        </p>
      )}
      <ProposalTable items={items} lang={lang} />
      {proposal.pmComment && (
        <p className="mt-2 text-sm">
          <span className="font-medium">{t('proposal.pmComment')}: </span>
          <span dir="auto">{proposal.pmComment}</span>
        </p>
      )}
      {proposal.diff.length > 0 && (
        <div className="mt-3">
          <h4 className="text-sm font-medium">{t('proposal.diff')}</h4>
          <DiffList entries={proposal.diff.map((d) => ({ key: d.key, field: d.field, vendor: d.vendor, pm: d.pm }))} />
        </div>
      )}
      {open === 'review' && <AcceptDialog project={project} proposal={proposal} onClose={() => setOpen(null)} />}
      {open === 'return' && <ReturnDialog project={project} proposal={proposal} onClose={() => setOpen(null)} />}
    </article>
  );
}

function ProposalTable({ items, lang }: { items: ProposalItem[]; lang: string }) {
  const { t } = useTranslation('vendors');
  return (
    <div className="mt-3 overflow-x-auto">
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('proposal.type')}</TableHeaderCell>
            <TableHeaderCell>{t('proposal.titleCol')}</TableHeaderCell>
            <TableHeaderCell>{t('proposal.dates')}</TableHeaderCell>
            <TableHeaderCell>{t('panel.weight')}</TableHeaderCell>
            <TableHeaderCell>{t('proposal.tasks')}</TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {items.map((item) => (
            <TableRow key={item.key} data-testid="wasel-proposal-item">
              <TableCell>{t(`proposal.types.${item.type}`, item.type)}</TableCell>
              <TableCell className={item.parentKey ? 'ps-6' : 'font-medium'} dir="auto">
                {pickText(lang, item.titleAr, item.titleEn)}
              </TableCell>
              <TableCell>
                {item.plannedStart ? formatDate(item.plannedStart, lang) : '—'} – {item.plannedEnd ? formatDate(item.plannedEnd, lang) : '—'}
              </TableCell>
              <TableCell>{item.weight === null || item.weight === undefined ? '—' : percent(item.weight, lang)}</TableCell>
              <TableCell dir="auto">
                {(item.tasks ?? []).join('، ')}
                {item.storyPoints !== null && item.storyPoints !== undefined && t('proposal.points', { n: item.storyPoints })}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

/** The PM's editable copy: titles, dates, weights (and parent through the vendor's order). Accepting records the diff. */
function AcceptDialog({ project, proposal, onClose }: { project: ProjectFull; proposal: PmProposal; onClose: () => void }) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const accept = usePanelMutation(project.id, waselApi.acceptProposal);
  const [items, setItems] = useState<ProposalItem[]>(() => proposal.items.map((i) => ({ ...i })));
  const [comment, setComment] = useState('');
  const update = (key: string, patch: Partial<ProposalItem>) =>
    setItems((list) => list.map((i) => (i.key === key ? { ...i, ...patch } : i)));
  const parents = items.filter((i) => i.type === 'phase' || i.type === 'epic');
  const total = items.filter((i) => i.type === 'milestone' || i.type === 'epic').reduce((sum, i) => sum + (i.weight ?? 0), 0);
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        className="max-w-5xl"
        title={t('proposal.review')}
        description={t('proposal.reviewHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={accept.isPending}
              data-testid="wasel-proposal-accept"
              onClick={() =>
                accept.mutate(
                  { proposalId: proposal.id, items, comment: comment.trim() || null, version: proposal.version },
                  {
                    onSuccess: () => {
                      toast({ title: t('proposal.accepted') });
                      onClose();
                    },
                  },
                )
              }
            >
              {t('proposal.acceptIntoPlan')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3" data-testid="wasel-proposal-dialog">
          <div className="overflow-x-auto">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('proposal.type')}</TableHeaderCell>
                  <TableHeaderCell>{t('proposal.titleAr')}</TableHeaderCell>
                  <TableHeaderCell>{t('proposal.titleEn')}</TableHeaderCell>
                  <TableHeaderCell>{t('proposal.parent')}</TableHeaderCell>
                  <TableHeaderCell>{t('proposal.start')}</TableHeaderCell>
                  <TableHeaderCell>{t('proposal.end')}</TableHeaderCell>
                  <TableHeaderCell>{t('panel.weight')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {items.map((item) => {
                  const leaf = item.type === 'milestone' || item.type === 'story';
                  const weighted = item.type === 'milestone' || item.type === 'epic';
                  return (
                    <TableRow key={item.key} data-testid="wasel-proposal-edit-row">
                      <TableCell>{t(`proposal.types.${item.type}`, item.type)}</TableCell>
                      <TableCell>
                        <Input dir="rtl" value={item.titleAr ?? ''} onChange={(e) => update(item.key, { titleAr: e.target.value || null })} />
                      </TableCell>
                      <TableCell>
                        <Input
                          dir="ltr"
                          value={item.titleEn ?? ''}
                          data-testid="wasel-proposal-title-en"
                          onChange={(e) => update(item.key, { titleEn: e.target.value || null })}
                        />
                      </TableCell>
                      <TableCell>
                        {leaf ? (
                          <select
                            className="h-9 rounded-md border bg-card px-2 text-sm"
                            aria-label={t('proposal.parent')}
                            value={item.parentKey ?? ''}
                            onChange={(e) => update(item.key, { parentKey: e.target.value || null })}
                          >
                            <option value="">{t('proposal.noParent')}</option>
                            {parents.map((p) => (
                              <option key={p.key} value={p.key}>
                                {p.titleEn ?? p.titleAr ?? p.key}
                              </option>
                            ))}
                          </select>
                        ) : (
                          '—'
                        )}
                      </TableCell>
                      <TableCell>
                        <Input type="date" value={item.plannedStart ?? ''} onChange={(e) => update(item.key, { plannedStart: e.target.value || null })} />
                      </TableCell>
                      <TableCell>
                        <Input type="date" value={item.plannedEnd ?? ''} onChange={(e) => update(item.key, { plannedEnd: e.target.value || null })} />
                      </TableCell>
                      <TableCell>
                        {weighted ? (
                          <Input
                            inputMode="decimal"
                            className="w-24"
                            aria-label={t('panel.weight')}
                            defaultValue={toPercent(item.weight)?.toString() ?? ''}
                            onBlur={(e) => update(item.key, { weight: fromPercent(e.target.value) })}
                          />
                        ) : (
                          '—'
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
          <p className="text-sm text-muted-foreground" data-testid="wasel-proposal-total">
            {t('proposal.total', { total: toPercent(total) ?? 0 })}
          </p>
          <FormField label={t('panel.comment')}>
            <Textarea dir="auto" value={comment} onChange={(e) => setComment(e.target.value)} />
          </FormField>
          {accept.error && <FieldErrors error={accept.error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ReturnDialog({ project, proposal, onClose }: { project: ProjectFull; proposal: PmProposal; onClose: () => void }) {
  const { t } = useTranslation('vendors');
  const { t: tc } = useTranslation();
  const back = usePanelMutation(project.id, waselApi.returnProposal);
  const [comment, setComment] = useState('');
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('proposal.returnTitle')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              disabled={comment.trim() === ''}
              loading={back.isPending}
              data-testid="wasel-proposal-return-confirm"
              onClick={() =>
                back.mutate({ proposalId: proposal.id, comment: comment.trim(), version: proposal.version }, { onSuccess: onClose })
              }
            >
              {t('actions.return')}
            </Button>
          </>
        }
      >
        <FormField label={t('panel.comment')} required>
          <Textarea dir="auto" value={comment} onChange={(e) => setComment(e.target.value)} />
        </FormField>
        {back.error && <FieldErrors error={back.error} />}
      </DialogContent>
    </Dialog>
  );
}
