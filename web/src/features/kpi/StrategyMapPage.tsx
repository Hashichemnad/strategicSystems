import { Link } from '@tanstack/react-router';
import { Map as MapIcon } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { pickText } from '@/lib/format';
import { EmptyState, FormField, PageHeader, Select, Skeleton, StatusBanner } from '@/ui';
import { currentYear, ragSoft, useKpiStrategyMap } from './api';
import { KpiPage, PerformanceValue, RagChip, useKpiT, yearOptions } from './components';

/** Strategy map (SPEC §2.7): objectives by perspective, coloured by capped performance. */
export function KpiStrategyMapPage() {
  const { t } = useTranslation('kpi');
  const { t: tc } = useTranslation();
  const { language } = useKpiT();
  const [year, setYear] = useState(currentYear());
  const map = useKpiStrategyMap(year);
  return (
    <KpiPage testId="kpi-strategy-map">
      <PageHeader title={t('map.title')} description={t('map.description')} />
      <FormField label={t('common.year')} className="max-w-40">
        <Select
          value={String(year)}
          onValueChange={(v) => setYear(Number(v))}
          options={yearOptions(currentYear())}
        />
      </FormField>
      {map.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, map.error)} />
      ) : !map.data ? (
        <Skeleton className="h-96" />
      ) : map.data.perspectives.length === 0 ? (
        <EmptyState icon={MapIcon} title={t('map.empty')} />
      ) : (
        <div className="flex flex-col gap-4" data-testid="kpi-map">
          <section
            className={cn(
              'flex flex-wrap items-center justify-between gap-3 rounded-lg border-2 p-5',
              ragSoft[map.data.rag],
            )}
          >
            <div>
              <p className="text-xs text-muted-foreground">{t('map.vision')}</p>
              <h2 className="text-lg font-semibold">
                {pickText(language, map.data.visionNameAr, map.data.visionNameEn) ||
                  t('scorecard.organisation')}
              </h2>
            </div>
            <span className="flex items-center gap-2 text-lg">
              <PerformanceValue capped={map.data.score} />
              <RagChip rag={map.data.rag} />
            </span>
          </section>
          {map.data.perspectives.map((p) => (
            <section
              key={p.id ?? p.code ?? ''}
              className="grid gap-3 rounded-lg border bg-card p-4 md:grid-cols-[14rem_1fr]"
              data-testid="kpi-map-perspective"
            >
              <div className="flex flex-col gap-1 border-b pb-3 md:border-b-0 md:border-e md:pe-3 md:pb-0">
                <p className="text-xs text-muted-foreground">{t('elementKind.perspective')}</p>
                <h3 className="font-semibold">{pickText(language, p.nameAr, p.nameEn) || p.code}</h3>
                <span className="flex items-center gap-2">
                  <PerformanceValue capped={p.score} />
                  <RagChip rag={p.rag} />
                </span>
              </div>
              <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {p.objectives.map((o) => (
                  <li
                    key={o.id}
                    className={cn('flex flex-col gap-1 rounded-lg border-2 p-3', ragSoft[o.rag])}
                    data-testid="kpi-map-objective"
                    data-rag={o.rag}
                  >
                    <Link
                      to="/kpi/cards"
                      search={{ objective: o.id }}
                      className="font-medium hover:underline"
                    >
                      <bdi className="text-xs text-muted-foreground">{o.code}</bdi>{' '}
                      {pickText(language, o.nameAr, o.nameEn)}
                    </Link>
                    <span className="flex items-center justify-between gap-2 text-sm">
                      <PerformanceValue capped={o.score} />
                      <span className="text-xs text-muted-foreground">
                        {t('common.kpiCount', { count: o.kpiCount, formatted: String(o.kpiCount) })}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          ))}
          <p className="flex flex-wrap gap-3 text-xs text-muted-foreground">
            {(['green', 'amber', 'red', 'none'] as const).map((r) => (
              <RagChip key={r} rag={r} />
            ))}
          </p>
        </div>
      )}
    </KpiPage>
  );
}
