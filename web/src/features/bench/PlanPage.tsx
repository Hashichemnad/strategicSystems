import { getRouteApi } from '@tanstack/react-router';
import { Send } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useDate } from '@/features/cap/components';
import { Button, PageHeader, Select, Skeleton, StatusBanner, Section } from '@/ui';
import { benchApi, useBenchPlan } from './api';
import { BenchPageLayout, useBenchAction } from './components';
import { StudiesTable } from './StudiesPage';

const route = getRouteApi('/app/bench/plan');

/** Annual benchmarking plan (SPEC §8.1): approved studies by quarter; the PM Office circulates it to the units. */
export function BenchPlanPage() {
  const { t } = useTranslation('bench');
  const { t: tc } = useTranslation();
  const date = useDate();
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const year0 = new Date().getFullYear();
  const year = search.year ?? year0;
  const plan = useBenchPlan(year);
  const circulate = useBenchAction(benchApi.circulate);

  return (
    <BenchPageLayout testId="bench-plan">
      <PageHeader
        title={t('plan.title', { year })}
        description={t('plan.description')}
        actions={
          plan.data?.canCirculate && (
            <Button
              loading={circulate.busy}
              onClick={() => circulate.run(year)}
              data-testid="bench-plan-circulate"
            >
              <Send aria-hidden />
              {t('plan.circulate')}
            </Button>
          )
        }
      />
      <div className="max-w-xs">
        <Select
          aria-label={t('fields.year')}
          value={String(year)}
          onValueChange={(v) => void navigate({ search: { year: Number(v) } })}
          options={[year0 + 1, year0, year0 - 1, year0 - 2].map((y) => ({
            value: String(y),
            label: String(y),
          }))}
        />
      </div>
      {plan.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, plan.error)} />
      ) : !plan.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : (
        <>
          {[1, 2, 3, 4].map((q) => {
            const rows = plan.data.studies.filter((s) => s.targetQuarter === q);
            return (
              <Section key={q} title={t('plan.quarter', { quarter: q, year })} testId={`bench-plan-q${q}`}>
                {rows.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t('plan.noStudies')}</p>
                ) : (
                  <StudiesTable rows={rows} />
                )}
              </Section>
            );
          })}
          <Section title={t('plan.circulations')} testId="bench-plan-circulations">
            {plan.data.circulations.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t('plan.notCirculated')}</p>
            ) : (
              <ul className="flex flex-col gap-1 text-sm">
                {plan.data.circulations.map((c) => (
                  <li key={c.circulatedAt}>
                    {t('plan.circulatedOn', {
                      date: date(c.circulatedAt),
                      recipients: c.recipients,
                      studies: c.studies,
                    })}
                  </li>
                ))}
              </ul>
            )}
          </Section>
        </>
      )}
    </BenchPageLayout>
  );
}
