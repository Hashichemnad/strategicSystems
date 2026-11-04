import { FolderKanban, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import {
  EmptyState,
  Input,
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
import { useProjectRisks } from './api';
import {
  ProgressBar,
  RatingChip,
  RiskPageLayout,
  useBands,
  useNames,
  useNum,
  useRiskLabels,
} from './components';

const ALL = '__all';

/** Project Risks (docs/design/risk.md §8, ADR 0103): live, read-only rows from Portfolio scored by the shared framework. */
export function RiskProjectRisksPage() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const names = useNames();
  const num = useNum();
  const labels = useRiskLabels();
  const { bands } = useBands();
  const [band, setBand] = useState<string>();
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  useEffect(() => {
    const timer = window.setTimeout(() => setSearch(text.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [text]);
  const rows = useProjectRisks({ ...(band ? { band } : {}), ...(search ? { search } : {}) });
  return (
    <RiskPageLayout testId="risk-project-risks">
      <PageHeader title={t('projectRisks.title')} description={t('projectRisks.description')} />
      <div className="grid gap-3 md:grid-cols-3">
        <div className="relative md:col-span-2">
          <Search
            className="pointer-events-none absolute start-3 top-3 size-4 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            placeholder={t('projectRisks.search')}
            aria-label={t('projectRisks.search')}
            value={text}
            onChange={(e) => setText(e.target.value)}
            data-testid="risk-project-risks-search"
          />
        </div>
        <Select
          aria-label={t('register.residualBand')}
          value={band ?? ALL}
          onValueChange={(v) => setBand(v === ALL ? undefined : v)}
          options={[
            { value: ALL, label: t('register.allBands') },
            ...bands.map((b) => ({ value: b.code, label: names.text(b.labelAr, b.labelEn) || b.code })),
          ]}
        />
      </div>
      {rows.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, rows.error)} />
      ) : !rows.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : rows.data.length === 0 ? (
        <EmptyState icon={FolderKanban} title={t('projectRisks.empty')} />
      ) : (
        <Table data-testid="risk-project-risks-table">
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('projectRisks.project')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.title')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.category')}</TableHeaderCell>
              <TableHeaderCell>{t('projectRisks.pxi')}</TableHeaderCell>
              <TableHeaderCell>{t('projectRisks.controlEvaluation')}</TableHeaderCell>
              <TableHeaderCell>{t('rating.inherent')}</TableHeaderCell>
              <TableHeaderCell>{t('rating.residual')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.response')}</TableHeaderCell>
              <TableHeaderCell>{t('projectRisks.implementation')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.owner')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.data.map((r) => (
              <TableRow key={r.id} data-testid="risk-project-risk-row">
                <TableCell>
                  <a href={r.link} className="font-medium text-primary hover:underline">
                    <bdi>{r.projectNumber}</bdi>
                  </a>
                  <span className="block text-xs text-muted-foreground">
                    {names.text(r.projectNameAr, r.projectNameEn)}
                  </span>
                </TableCell>
                <TableCell>
                  <a href={r.link} className="hover:underline">
                    {r.title ?? '—'}
                  </a>
                </TableCell>
                <TableCell>
                  {r.categoryCode ? labels.lookup('portfolio.risk-category', r.categoryCode) : '—'}
                </TableCell>
                <TableCell className="tabular-nums">
                  <bdi>
                    {num(r.probability)} × {num(r.impact)}
                  </bdi>
                </TableCell>
                <TableCell className="tabular-nums">{num(r.controlEvaluation)}</TableCell>
                <TableCell>
                  <RatingChip band={r.inherentBand} score={r.inherentScore} />
                </TableCell>
                <TableCell>
                  <RatingChip band={r.residualBand} score={r.residualScore} />
                </TableCell>
                <TableCell>{r.response ?? '—'}</TableCell>
                <TableCell>
                  {r.implementationPct == null ? (
                    '—'
                  ) : (
                    <ProgressBar ratio={r.implementationPct} label={t('projectRisks.implementation')} />
                  )}
                </TableCell>
                <TableCell>{names.person(r.owner)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </RiskPageLayout>
  );
}
