import { Link } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  Dialog,
  DialogClose,
  DialogContent,
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
  useToast,
} from '@/ui';
import {
  kriBases,
  kriDirections,
  kriTone,
  riskApi,
  useKpiCardSearch,
  useKris,
  useRiskMutation,
  type Kri,
  type KriValues,
  type Risk,
} from './api';
import { Section, useNames, useNum, usePct, useRiskT } from './components';

export function KrisTab({ risk }: { risk: Risk }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k, language } = useRiskT();
  const kris = useKris(risk.id);
  const names = useNames();
  const num = useNum();
  const pct = usePct();
  const toast = useToast();
  const remove = useRiskMutation(riskApi.removeKri);
  const [editing, setEditing] = useState<{ kri?: Kri }>();
  const [removing, setRemoving] = useState<Kri>();
  const value = (kri: Kri, v: number | null | undefined) => (kri.basis === 'performance' ? pct(v) : num(v));
  return (
    <Section
      title={t('kris.title')}
      testId="risk-kris"
      actions={
        risk.can.manageChildren && (
          <Button size="sm" onClick={() => setEditing({})} data-testid="risk-kri-add">
            <Plus aria-hidden />
            {t('kris.add')}
          </Button>
        )
      }
    >
      <p className="text-sm text-muted-foreground">{t('kris.hint')}</p>
      {!kris.data ? (
        <Skeleton className="h-24" />
      ) : kris.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('kris.empty')}</p>
      ) : (
        <Table data-testid="risk-kris-table">
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('kris.card')}</TableHeaderCell>
              <TableHeaderCell>{t('kris.basis')}</TableHeaderCell>
              <TableHeaderCell>{t('kris.direction')}</TableHeaderCell>
              <TableHeaderCell>{t('kris.warningThreshold')}</TableHeaderCell>
              <TableHeaderCell>{t('kris.breachThreshold')}</TableHeaderCell>
              <TableHeaderCell>{t('kris.lastValue')}</TableHeaderCell>
              <TableHeaderCell>{t('kris.performance')}</TableHeaderCell>
              <TableHeaderCell>
                <span className="sr-only">{tc('actions.more')}</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {kris.data.map((kri) => (
              <TableRow key={kri.id} data-testid="risk-kri-row">
                <TableCell>
                  {kri.cardVisible ? (
                    <Link
                      to="/kpi/cards/$cardId"
                      params={{ cardId: kri.kpiCardId }}
                      className="text-primary hover:underline"
                    >
                      <bdi>{kri.cardCode}</bdi> · {names.text(kri.cardNameAr, kri.cardNameEn)}
                    </Link>
                  ) : (
                    <span>
                      <bdi>{kri.cardCode}</bdi> · {names.text(kri.cardNameAr, kri.cardNameEn)}
                    </span>
                  )}
                  {kri.status !== 'active' && (
                    <StatusChip tone="neutral" className="ms-1">
                      {k(`controlStatus.${kri.status}`)}
                    </StatusChip>
                  )}
                </TableCell>
                <TableCell>{k(`kriBasis.${kri.basis}`)}</TableCell>
                <TableCell>{k(`kriDirection.${kri.direction}`)}</TableCell>
                <TableCell className="tabular-nums">{value(kri, kri.warningThreshold)}</TableCell>
                <TableCell className="tabular-nums">{value(kri, kri.breachThreshold)}</TableCell>
                <TableCell>
                  <span className="flex flex-wrap items-center gap-2">
                    <span className="tabular-nums">{value(kri, kri.lastValue)}</span>
                    <StatusChip tone={kriTone[kri.lastLevel] ?? 'neutral'}>
                      <span data-testid="risk-kri-level">{k(`kriLevel.${kri.lastLevel}`)}</span>
                    </StatusChip>
                    {kri.lastPeriod && (
                      <span className="text-xs text-muted-foreground">{kri.lastPeriod}</span>
                    )}
                    {kri.lastAt && (
                      <span className="text-xs text-muted-foreground">
                        {formatDateTime(kri.lastAt, language)}
                      </span>
                    )}
                  </span>
                </TableCell>
                <TableCell className="tabular-nums">
                  {kri.performance ? (
                    <span>
                      {pct(kri.performance.capped)}
                      {kri.performance.actual != null &&
                        kri.performance.actual !== kri.performance.capped && (
                          <span className="ms-1 text-xs text-muted-foreground">
                            ({pct(kri.performance.actual)})
                          </span>
                        )}
                      <span className="ms-1 text-xs text-muted-foreground">{kri.performance.year}</span>
                    </span>
                  ) : (
                    '—'
                  )}
                </TableCell>
                <TableCell>
                  {risk.can.manageChildren && kri.status === 'active' && (
                    <span className="flex gap-1">
                      <Button size="sm" variant="ghost" onClick={() => setEditing({ kri })}>
                        {tc('actions.edit')}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setRemoving(kri)}>
                        {t('kris.remove')}
                      </Button>
                    </span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {editing && <KriDialog riskId={risk.id} kri={editing.kri} onClose={() => setEditing(undefined)} />}
      <ConfirmDialog
        open={!!removing}
        onOpenChange={(open) => !open && setRemoving(undefined)}
        title={t('kris.remove')}
        description={t('kris.removeHint')}
        confirmLabel={t('kris.remove')}
        destructive
        loading={remove.isPending}
        onConfirm={() =>
          removing &&
          remove.mutate(removing.id, {
            onSuccess: () => setRemoving(undefined),
            onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
          })
        }
      />
    </Section>
  );
}

function KriDialog({ riskId, kri, onClose }: { riskId: string; kri?: Kri; onClose: () => void }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k } = useRiskT();
  const names = useNames();
  const toast = useToast();
  const save = useRiskMutation(riskApi.saveKri);
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(search.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [search]);
  const cards = useKpiCardSearch(debounced);
  const [v, setV] = useState<KriValues>({
    kpiCardId: kri?.kpiCardId ?? '',
    basis: kri?.basis ?? 'value',
    direction: kri?.direction ?? 'above',
    warningThreshold: kri?.warningThreshold ?? null,
    breachThreshold: kri?.breachThreshold ?? 0,
  });
  const [breach, setBreach] = useState(kri ? String(kri.breachThreshold) : '');
  const cardOptions = [
    ...(kri
      ? [{ value: kri.kpiCardId, label: `${kri.cardCode} · ${names.text(kri.cardNameAr, kri.cardNameEn)}` }]
      : []),
    ...(cards.data?.items ?? [])
      .filter((c) => c.id !== kri?.kpiCardId)
      .map((c) => ({ value: c.id, label: `${c.code} · ${names.text(c.nameAr, c.nameEn)}` })),
  ];
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={kri ? t('kris.edit') : t('kris.add')}
        description={t('kris.dialogHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!v.kpiCardId || breach === ''}
              data-testid="risk-kri-save"
              onClick={() =>
                save.mutate(
                  {
                    riskId,
                    kriId: kri?.id,
                    values: { ...v, breachThreshold: Number(breach) },
                    version: kri?.version ?? null,
                  },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('kris.searchCard')} className="md:col-span-2">
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              data-testid="risk-kri-card-search"
            />
          </FormField>
          <FormField label={t('kris.card')} required className="md:col-span-2">
            <Select
              value={v.kpiCardId || undefined}
              onValueChange={(value) => setV({ ...v, kpiCardId: value })}
              options={cardOptions}
              placeholder={t('kris.chooseCard')}
            />
          </FormField>
          <FormField label={t('kris.basis')}>
            <Select
              value={v.basis}
              onValueChange={(value) => setV({ ...v, basis: value })}
              options={kriBases.map((b) => ({ value: b, label: k(`kriBasis.${b}`) }))}
            />
          </FormField>
          <FormField label={t('kris.direction')}>
            <Select
              value={v.direction}
              onValueChange={(value) => setV({ ...v, direction: value })}
              options={kriDirections.map((d) => ({ value: d, label: k(`kriDirection.${d}`) }))}
            />
          </FormField>
          <FormField
            label={t('kris.warningThreshold')}
            hint={v.basis === 'performance' ? t('kris.ratioHint') : undefined}
          >
            <Input
              type="number"
              step="any"
              value={v.warningThreshold ?? ''}
              onChange={(e) =>
                setV({ ...v, warningThreshold: e.target.value === '' ? null : Number(e.target.value) })
              }
            />
          </FormField>
          <FormField
            label={t('kris.breachThreshold')}
            required
            hint={v.basis === 'performance' ? t('kris.ratioHint') : undefined}
          >
            <Input
              type="number"
              step="any"
              value={breach}
              onChange={(e) => setBreach(e.target.value)}
              data-testid="risk-kri-breach"
            />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}
