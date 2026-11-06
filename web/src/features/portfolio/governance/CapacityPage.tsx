import { Link } from '@tanstack/react-router';
import { AlertTriangle, ChevronDown, ChevronUp, UsersRound } from 'lucide-react';
import { Fragment, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { cn } from '@/lib/cn';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  FormField,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { useCapacity, type CapacityRow } from './governanceApi';

/** Percent text of a ratio (1.0 = 100 %), one decimal. */
function usePercent() {
  const { t, i18n } = useTranslation('portfolioGov');
  return (ratio: number) =>
    t('percent', { value: formatNumber(Math.round(ratio * 1000) / 10, i18n.language) });
}

/** Portfolio → Capacity (SPEC §1.12, docs/design/portfolio.md §58): allocation per employee with over-allocation warnings. */
export function CapacityPage() {
  const { t } = useTranslation('portfolioGov');
  const { t: tc } = useTranslation();
  const percent = usePercent();
  const [unitId, setUnitId] = useState<string | undefined>(undefined);
  const [below, setBelow] = useState(true);
  const [overOnly, setOverOnly] = useState(false);
  const capacity = useCapacity({ unitId, below, overOnly });
  const data = capacity.data;

  return (
    <div className="flex flex-col gap-6" data-testid="capacity-page">
      <PageHeader title={t('capacity.title')} description={t('capacity.description')} />
      <div className="grid items-end gap-4 rounded-lg border bg-card p-4 md:grid-cols-3">
        <FormField label={t('capacity.unit')}>
          <UnitSelect value={unitId} onChange={setUnitId} />
        </FormField>
        <Switch checked={below} onCheckedChange={setBelow} label={t('capacity.below')} />
        <Switch
          checked={overOnly}
          onCheckedChange={setOverOnly}
          label={t('capacity.overOnly')}
          id="capacity-over-only"
        />
      </div>
      {capacity.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : capacity.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, capacity.error)} />
      ) : data ? (
        <>
          <p className="text-sm text-muted-foreground" data-testid="capacity-summary">
            {t('capacity.summary', { employees: data.employees, over: data.overAllocated })} ·{' '}
            {t('capacity.threshold', { value: percent(data.threshold) })}
          </p>
          {data.rows.length === 0 ? (
            <EmptyState title={overOnly ? t('capacity.emptyOver') : t('capacity.empty')} icon={UsersRound} />
          ) : (
            <div className="overflow-x-auto rounded-lg border bg-card">
              <Table>
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('capacity.employee')}</TableHeaderCell>
                    <TableHeaderCell>{t('capacity.unitColumn')}</TableHeaderCell>
                    <TableHeaderCell className="w-1/3">{t('capacity.total')}</TableHeaderCell>
                    <TableHeaderCell>
                      <span className="sr-only">{t('capacity.allocations')}</span>
                    </TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {data.rows.map((row) => (
                    <CapacityRowView key={row.employeeId} row={row} threshold={data.threshold} />
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </>
      ) : null}
    </div>
  );
}

function CapacityRowView({ row, threshold }: { row: CapacityRow; threshold: number }) {
  const { t, i18n } = useTranslation('portfolioGov');
  const language = i18n.language;
  const percent = usePercent();
  const [open, setOpen] = useState(false);
  const name = pickText(language, row.nameAr, row.nameEn) || '—';
  // The bar's scale shows at least the threshold and the total; the threshold is marked with a line.
  const scale = Math.max(threshold, row.total, 1);
  const width = `${Math.min(100, (row.total / scale) * 100)}%`;
  const thresholdAt = `${(threshold / scale) * 100}%`;
  return (
    <Fragment>
      <TableRow data-testid="capacity-row" data-over={row.overAllocated ? 'true' : 'false'}>
        <TableCell className="font-medium">{name}</TableCell>
        <TableCell>{pickText(language, row.unitNameAr, row.unitNameEn) || '—'}</TableCell>
        <TableCell>
          <div className="flex flex-col gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="tabular-nums" data-testid="capacity-total">
                {percent(row.total)}
              </span>
              {row.overAllocated && (
                <span data-testid="capacity-warning">
                  <StatusChip tone="danger" className="gap-1">
                    <AlertTriangle aria-hidden className="size-3.5" />
                    {t('capacity.overAllocated')}
                  </StatusChip>
                </span>
              )}
              {row.notSet > 0 && (
                <span className="text-xs text-muted-foreground">
                  {t('capacity.notSet', { count: row.notSet })}
                </span>
              )}
            </div>
            <div
              className="relative h-2 w-full overflow-hidden rounded-full bg-muted"
              role="presentation"
              aria-hidden
            >
              <div
                className={cn(
                  'absolute inset-y-0 start-0 rounded-full',
                  row.overAllocated ? 'bg-danger' : 'bg-primary',
                )}
                style={{ width }}
              />
              <div
                className="absolute inset-y-0 w-0.5 bg-foreground/60"
                style={{ insetInlineStart: thresholdAt }}
              />
            </div>
          </div>
        </TableCell>
        <TableCell>
          <Button
            variant="ghost"
            size="sm"
            data-testid="capacity-toggle"
            aria-expanded={open}
            onClick={() => setOpen((v) => !v)}
          >
            {formatNumber(row.allocations.length, language)}
            {/* The visible count stays first in the accessible name (WCAG 2.5.3). */}
            <span className="sr-only">
              {' — '}
              {open ? t('capacity.hideAllocations', { name }) : t('capacity.showAllocations', { name })}
            </span>
            {open ? <ChevronUp aria-hidden /> : <ChevronDown aria-hidden />}
          </Button>
        </TableCell>
      </TableRow>
      {open && (
        <TableRow data-testid="capacity-allocations">
          <TableCell colSpan={4} className="bg-muted/40">
            <ul className="flex flex-col gap-1 text-sm">
              {row.allocations.map((a, i) => {
                const label = `${a.number ? `${a.number} · ` : ''}${pickText(language, a.nameAr, a.nameEn)}`;
                return (
                  <li
                    key={`${a.teamId ?? a.projectId ?? ''}-${i}`}
                    className="flex flex-wrap items-center gap-2"
                  >
                    <StatusChip tone={a.source === 'team' ? 'info' : 'neutral'}>
                      {t(a.source === 'team' ? 'capacity.source.team' : 'capacity.source.project')}
                    </StatusChip>
                    {a.projectId ? (
                      <Link
                        to="/portfolio/projects/$projectId"
                        params={{ projectId: a.projectId }}
                        search={{ tab: 'overview' }}
                        className="underline-offset-4 hover:underline"
                      >
                        {label}
                      </Link>
                    ) : (
                      <span>{label}</span>
                    )}
                    {a.source === 'team' && a.projectNumber && (
                      <span className="text-xs text-muted-foreground">
                        {t('capacity.forProject', { number: a.projectNumber })}
                      </span>
                    )}
                    <span className="ms-auto tabular-nums">
                      {a.allocationPercent === null
                        ? t('capacity.notSetValue')
                        : percent(a.allocationPercent)}
                    </span>
                  </li>
                );
              })}
            </ul>
          </TableCell>
        </TableRow>
      )}
    </Fragment>
  );
}
