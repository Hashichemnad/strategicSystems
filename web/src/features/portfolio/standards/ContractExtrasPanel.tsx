import { ChevronDown, Pencil, Plus } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, formatDateTime, formatNumber, pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
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
  useToast,
} from '@/ui';
import { cn } from '@/lib/cn';
import {
  useMilestones,
  useProjectItems,
  useProjectMutation,
  type Contract,
  type ProjectFull,
} from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { standardsApi, useContractExtras, type ContractExtras, type Payment } from './standardsApi';
import { averageRating, parseNumber, type Tone } from './standardsLogic';

const paymentStatuses = ['planned', 'paid', 'cancelled'] as const;
const paymentTone: Record<string, Tone> = { planned: 'info', paid: 'success', cancelled: 'neutral' };
const asPaymentStatus = (s: string) =>
  (paymentStatuses as readonly string[]).includes(s) ? (s as 'planned') : 'planned';

/** Payment milestones and supplier rating per contract (docs/design/portfolio.md §35, switch `procurement`). */
export function ContractExtrasSection({ project, canWrite }: { project: ProjectFull; canWrite: boolean }) {
  const { t } = useTranslation('pmStandards');
  const contracts = useProjectItems<Contract>(project.id, 'contracts');
  const rows = (contracts.data ?? []).filter((c) => c.status === 'active');
  if (rows.length === 0) return null;
  return (
    <section className="flex flex-col gap-3" data-testid="contract-extras">
      <header>
        <h3 className="font-semibold">{t('procurement.title')}</h3>
        <p className="text-sm text-muted-foreground">{t('procurement.hint')}</p>
      </header>
      <ul className="flex flex-col gap-2">
        {rows.map((c) => (
          <ContractRow key={c.id} project={project} contract={c} canWrite={canWrite} />
        ))}
      </ul>
    </section>
  );
}

function ContractRow({
  project,
  contract,
  canWrite,
}: {
  project: ProjectFull;
  contract: Contract;
  canWrite: boolean;
}) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  return (
    <li className="rounded-lg border bg-card">
      <button
        type="button"
        className="flex w-full items-center justify-between gap-2 px-4 py-3 text-start"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}
        data-testid="contract-expand"
      >
        <span dir="auto" className="font-medium">
          {contract.name ?? '—'}
          {contract.number && <bdi className="ms-2 text-sm text-muted-foreground">{contract.number}</bdi>}
          {contract.supplier && (
            <span className="ms-2 text-sm text-muted-foreground">{contract.supplier}</span>
          )}
        </span>
        <ChevronDown aria-hidden className={cn('size-4 transition-transform', open && 'rotate-180')} />
      </button>
      {open && (
        <div id={panelId} className="border-t px-4 py-3">
          <ContractExtrasPanel project={project} contractId={contract.id} canWrite={canWrite} />
        </div>
      )}
    </li>
  );
}

function ContractExtrasPanel({
  project,
  contractId,
  canWrite,
}: {
  project: ProjectFull;
  contractId: string;
  canWrite: boolean;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const extras = useContractExtras(project.id, contractId);
  const milestones = useMilestones(project.id);
  const [editing, setEditing] = useState<{ row?: Payment } | null>(null);
  const lang = i18n.language;
  if (extras.isPending) return <Skeleton className="h-24" />;
  if (extras.isError) return <StatusBanner tone="danger" title={errorMessage(tc, extras.error)} />;
  const data = extras.data;
  const hidden = data.costsHidden;
  const money = (n: number | null) => (n === null ? '—' : formatNumber(n, lang));
  const milestoneLabel = (id: string | null) => {
    const m = (milestones.data?.milestones ?? []).find((x) => x.id === id);
    return m ? `${m.code} · ${pickText(lang, m.titleAr, m.titleEn)}` : '—';
  };
  return (
    <div className="flex flex-col gap-4">
      <section className="flex flex-col gap-2" data-testid="contract-payments">
        <header className="flex flex-wrap items-center justify-between gap-2">
          <h4 className="text-sm font-semibold">{t('procurement.payments')}</h4>
          <div className="flex flex-wrap items-center gap-2">
            {!hidden && (
              <span className="text-xs text-muted-foreground">
                {t('procurement.totals', { planned: money(data.plannedTotal), paid: money(data.paidTotal) })}
              </span>
            )}
            {canWrite && (
              <Button size="sm" onClick={() => setEditing({})} data-testid="payment-add">
                <Plus aria-hidden />
                {t('procurement.addPayment')}
              </Button>
            )}
          </div>
        </header>
        {data.payments.length === 0 ? (
          <EmptyState title={t('procurement.noPayments')} />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('procurement.paymentTitle')}</TableHeaderCell>
                  <TableHeaderCell>{t('procurement.dueDate')}</TableHeaderCell>
                  {!hidden && <TableHeaderCell>{t('procurement.amount')}</TableHeaderCell>}
                  <TableHeaderCell>{t('procurement.milestone')}</TableHeaderCell>
                  <TableHeaderCell>{t('procurement.status')}</TableHeaderCell>
                  <TableHeaderCell>{t('procurement.paidOn')}</TableHeaderCell>
                  {canWrite && <TableHeaderCell>{t('actionsColumn')}</TableHeaderCell>}
                </TableRow>
              </TableHead>
              <TableBody>
                {data.payments.map((p) => (
                  <TableRow key={p.id} data-testid="payment-row">
                    <TableCell dir="auto">{p.title ?? '—'}</TableCell>
                    <TableCell>{p.dueDate ? formatDate(p.dueDate, lang) : '—'}</TableCell>
                    {!hidden && <TableCell className="tabular-nums">{money(p.amount)}</TableCell>}
                    <TableCell>
                      <bdi>{milestoneLabel(p.milestoneId)}</bdi>
                    </TableCell>
                    <TableCell>
                      <StatusChip tone={paymentTone[p.status] ?? 'neutral'}>
                        {t(`procurement.statuses.${asPaymentStatus(p.status)}`)}
                      </StatusChip>
                    </TableCell>
                    <TableCell>{p.paidOn ? formatDate(p.paidOn, lang) : '—'}</TableCell>
                    {canWrite && (
                      <TableCell>
                        <Button
                          size="sm"
                          variant="ghost"
                          aria-label={tc('actions.edit')}
                          onClick={() => setEditing({ row: p })}
                        >
                          <Pencil aria-hidden />
                        </Button>
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>
      <RatingForm project={project} contractId={contractId} extras={data} canWrite={canWrite} />
      {editing && (
        <PaymentDialog
          project={project}
          contractId={contractId}
          row={editing.row}
          costsHidden={hidden}
          onClose={() => setEditing(null)}
        />
      )}
    </div>
  );
}

function PaymentDialog({
  project,
  contractId,
  row,
  costsHidden,
  onClose,
}: {
  project: ProjectFull;
  contractId: string;
  row: Payment | undefined;
  costsHidden: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(project.id, standardsApi.savePayment);
  const milestones = useMilestones(project.id);
  const [v, setV] = useState({
    title: row?.title ?? '',
    dueDate: row?.dueDate ?? undefined,
    amount: row?.amount === null || row?.amount === undefined ? '' : String(row.amount),
    milestoneId: row?.milestoneId ?? '',
    status: row?.status ?? 'planned',
    paidOn: row?.paidOn ?? undefined,
  });
  const set = (patch: Partial<typeof v>) => setV((c) => ({ ...c, ...patch }));
  const amount = parseNumber(v.amount);
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={row ? t('procurement.editPayment') : t('procurement.addPayment')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={v.title.trim() === '' || (!costsHidden && amount === null)}
              loading={save.isPending}
              onClick={() =>
                save.mutate(
                  {
                    id: project.id,
                    cid: contractId,
                    pid: row?.id,
                    version: row?.version,
                    values: {
                      title: v.title.trim() || null,
                      dueDate: v.dueDate ?? null,
                      amount: amount ?? row?.amount ?? 0,
                      milestoneId: v.milestoneId || null,
                      status: v.status,
                      paidOn: v.status === 'paid' ? (v.paidOn ?? null) : null,
                    },
                  },
                  {
                    onSuccess: () => {
                      toast({ title: t('saved') });
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
        <div className="grid gap-3 md:grid-cols-2" data-testid="payment-dialog">
          <FormField label={t('procurement.paymentTitle')} required className="md:col-span-2">
            <Input dir="auto" value={v.title} onChange={(e) => set({ title: e.target.value })} />
          </FormField>
          <FormField label={t('procurement.dueDate')}>
            <DatePicker value={v.dueDate} onChange={(d) => set({ dueDate: d })} />
          </FormField>
          {!costsHidden && (
            <FormField label={t('procurement.amount')} required>
              <Input
                dir="ltr"
                inputMode="decimal"
                value={v.amount}
                onChange={(e) => set({ amount: e.target.value })}
              />
            </FormField>
          )}
          <FormField label={t('procurement.milestone')}>
            <Select
              value={v.milestoneId || '*'}
              onValueChange={(id) => set({ milestoneId: id === '*' ? '' : id })}
              options={[
                { value: '*', label: t('raid.none') },
                ...(milestones.data?.milestones ?? [])
                  .filter((m) => !m.isHeader && m.status !== 'cancelled')
                  .map((m) => ({
                    value: m.id,
                    label: `${m.code} · ${pickText(i18n.language, m.titleAr, m.titleEn)}`,
                  })),
              ]}
            />
          </FormField>
          <FormField label={t('procurement.status')}>
            <Select
              value={v.status}
              onValueChange={(status) => set({ status })}
              options={paymentStatuses.map((s) => ({ value: s, label: t(`procurement.statuses.${s}`) }))}
            />
          </FormField>
          {v.status === 'paid' && (
            <FormField label={t('procurement.paidOn')} required>
              <DatePicker value={v.paidOn} onChange={(d) => set({ paidOn: d })} />
            </FormField>
          )}
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

const ratingKeys = ['quality', 'timeliness', 'communication'] as const;

function RatingForm({
  project,
  contractId,
  extras,
  canWrite,
}: {
  project: ProjectFull;
  contractId: string;
  extras: ContractExtras;
  canWrite: boolean;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const toast = useToast();
  const rate = useProjectMutation(project.id, standardsApi.rateSupplier);
  const r = extras.rating;
  const [v, setV] = useState({
    quality: r.quality === null ? '' : String(r.quality),
    timeliness: r.timeliness === null ? '' : String(r.timeliness),
    communication: r.communication === null ? '' : String(r.communication),
    comment: r.comment ?? '',
  });
  const scores = ratingKeys.map((k) => parseNumber(v[k]));
  const overall = averageRating(scores);
  const lang = i18n.language;
  const scale = [1, 2, 3, 4, 5].map((n) => ({ value: String(n), label: formatNumber(n, lang) }));
  return (
    <section className="flex flex-col gap-2" data-testid="supplier-rating">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-sm font-semibold">{t('procurement.rating')}</h4>
        <StatusChip
          tone={overall === null ? 'neutral' : overall >= 4 ? 'success' : overall >= 3 ? 'info' : 'warning'}
        >
          {t('procurement.overall', { value: overall === null ? '—' : formatNumber(overall, lang) })}
        </StatusChip>
      </header>
      {r.ratedAt && (
        <p className="text-xs text-muted-foreground">
          {t('procurement.ratedAt', { date: formatDateTime(r.ratedAt, lang) })}
        </p>
      )}
      <div className="grid gap-3 md:grid-cols-3">
        {ratingKeys.map((k) => (
          <FormField key={k} label={t(`procurement.criteria.${k}`)} required>
            <Select
              disabled={!canWrite}
              value={v[k] || undefined}
              onValueChange={(value) => setV((c) => ({ ...c, [k]: value }))}
              options={scale}
            />
          </FormField>
        ))}
        <FormField label={t('procurement.comment')} className="md:col-span-3">
          <Textarea
            disabled={!canWrite}
            value={v.comment}
            onChange={(e) => setV((c) => ({ ...c, comment: e.target.value }))}
          />
        </FormField>
      </div>
      {canWrite && (
        <div>
          <Button
            size="sm"
            variant="outline"
            disabled={scores.some((s) => s === null)}
            loading={rate.isPending}
            onClick={() =>
              rate.mutate(
                {
                  id: project.id,
                  cid: contractId,
                  quality: scores[0] ?? 1,
                  timeliness: scores[1] ?? 1,
                  communication: scores[2] ?? 1,
                  comment: v.comment.trim() || null,
                },
                { onSuccess: () => toast({ title: t('saved') }) },
              )
            }
            data-testid="rating-save"
          >
            {t('procurement.saveRating')}
          </Button>
        </div>
      )}
      {rate.isError && <FieldErrors error={rate.error} />}
    </section>
  );
}
