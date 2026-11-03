import { Link } from '@tanstack/react-router';
import { ChevronDown, Download } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { cn } from '@/lib/cn';
import { pickText } from '@/lib/format';
import {
  Button,
  FormField,
  PageHeader,
  Select,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { currentYear, exportUrl, useKpiScorecard, type KpiScoreNode } from './api';
import { KpiPage, PerformanceValue, RagChip, ScoreBar, useKpiT, useNumber, yearOptions } from './components';
import { Narrative } from './DashboardPage';

/** Weighted scorecard on capped performance (SPEC §2.7, ADR 0062): organisation → vision → perspective → objective → KPI, and departments. */
export function KpiScorecardPage() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { language } = useKpiT();
  const [year, setYear] = useState(currentYear());
  const [unitId, setUnitId] = useState<string>();
  const scorecard = useKpiScorecard(year, unitId);
  const canExport = useCan('kpi.report.export');
  return (
    <KpiPage testId="kpi-scorecard">
      <PageHeader
        title={t('scorecard.title')}
        description={t('scorecard.description')}
        actions={
          canExport && (
            <Button variant="outline" size="sm" asChild>
              <a href={exportUrl('scorecard', 'xlsx', language, { year: String(year) })}>
                <Download aria-hidden />
                {t('reports.exportScorecard')}
              </a>
            </Button>
          )
        }
      />
      <div className="grid gap-3 md:grid-cols-[10rem_1fr]">
        <FormField label={t('common.year')}>
          <Select
            value={String(year)}
            onValueChange={(v) => setYear(Number(v))}
            options={yearOptions(currentYear())}
          />
        </FormField>
        <FormField label={t('approvals.department')}>
          <UnitSelect allowEmpty value={unitId} onChange={setUnitId} />
        </FormField>
      </div>
      {scorecard.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, scorecard.error)} />
      ) : !scorecard.data ? (
        <Skeleton className="h-96" />
      ) : (
        <>
          <section className="rounded-lg border bg-card p-4" data-testid="kpi-scorecard-tree">
            <ul role="tree" aria-label={t('scorecard.title')}>
              <Node node={scorecard.data.root} depth={0} />
            </ul>
          </section>
          <section
            className="flex flex-col gap-3 rounded-lg border bg-card p-5"
            data-testid="kpi-department-scores"
          >
            <h2 className="font-semibold">{t('scorecard.departments')}</h2>
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('approvals.department')}</TableHeaderCell>
                  <TableHeaderCell>{t('scorecard.score')}</TableHeaderCell>
                  <TableHeaderCell>{t('scorecard.kpis')}</TableHeaderCell>
                  <TableHeaderCell>{t('scorecard.reported')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {scorecard.data.departments.map((d) => (
                  <TableRow key={d.unit.id}>
                    <TableCell>{pickText(language, d.unit.nameAr, d.unit.nameEn)}</TableCell>
                    <TableCell>
                      <span className="flex items-center gap-2">
                        <PerformanceValue capped={d.score} />
                        <RagChip rag={d.rag} />
                      </span>
                    </TableCell>
                    <TableCell className="tabular-nums">{d.kpiCount}</TableCell>
                    <TableCell className="tabular-nums">{d.reported}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </section>
          <Narrative year={year} unitId={unitId} />
        </>
      )}
    </KpiPage>
  );
}

function Node({ node, depth }: { node: KpiScoreNode; depth: number }) {
  const { t } = useTranslation('kpi');
  const { k, language } = useKpiT();
  const num = useNumber();
  const [open, setOpen] = useState(depth < 3);
  const name =
    node.kind === 'organisation'
      ? t('scorecard.organisation')
      : node.kind === 'unaligned'
        ? t('scorecard.unaligned')
        : pickText(language, node.nameAr, node.nameEn) || node.code;
  const hasChildren = node.children.length > 0;
  return (
    <li
      role="treeitem"
      aria-expanded={hasChildren ? open : undefined}
      aria-selected={false}
      data-testid="kpi-score-node"
      data-kind={node.kind}
    >
      <div
        className={cn(
          'grid grid-cols-[1fr_8rem_auto] items-center gap-3 rounded-md px-2 py-2 hover:bg-muted/50',
          depth === 0 && 'font-semibold',
        )}
        style={{ paddingInlineStart: `${depth * 1.25 + 0.5}rem` }}
      >
        <span className="flex min-w-0 items-center gap-2">
          {hasChildren ? (
            <button
              type="button"
              aria-label={open ? t('scorecard.collapse') : t('scorecard.expand')}
              onClick={() => setOpen((o) => !o)}
              className="rounded p-0.5 hover:bg-muted"
            >
              <ChevronDown
                className={cn('size-4 transition-transform', !open && '-rotate-90 rtl:rotate-90')}
                aria-hidden
              />
            </button>
          ) : (
            <span className="inline-block size-5" aria-hidden />
          )}
          <span className="text-xs text-muted-foreground">{k(`elementKind.${node.kind}`)}</span>
          {node.kind === 'kpi' && node.id ? (
            <Link
              to="/kpi/cards/$cardId"
              params={{ cardId: node.id }}
              className="truncate text-primary hover:underline"
            >
              <bdi>{node.code}</bdi> · {name}
            </Link>
          ) : (
            <span className="truncate">{name}</span>
          )}
          {node.weight != null && (
            <span className="text-xs text-muted-foreground">
              ({t('scorecard.weight', { weight: num(node.weight) })})
            </span>
          )}
        </span>
        <ScoreBar score={node.score} rag={node.rag} />
        <span className="flex items-center gap-2">
          <PerformanceValue capped={node.score} actual={node.kind === 'kpi' ? node.actual : undefined} />
          <RagChip rag={node.rag} />
        </span>
      </div>
      {hasChildren && open && (
        <ul role="group">
          {node.children.map((child, i) => (
            <Node key={child.id ?? `${child.kind}-${i}`} node={child} depth={depth + 1} />
          ))}
        </ul>
      )}
    </li>
  );
}
