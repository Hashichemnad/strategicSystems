import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { pickText } from '@/lib/format';
import { FormField, Select } from '@/ui';
import { useFeature } from '../featuresApi';
import { usePortfolioDashboard, usePortfolioGroups, type PortfolioFilter } from './viewsApi';

const ALL = 'all';

/** Portfolio / programme / department filter of the dashboard and the portfolio Gantt. */
export function PortfolioFilters({
  value,
  onChange,
}: {
  value: PortfolioFilter;
  onChange: (value: PortfolioFilter) => void;
}) {
  const { t, i18n } = useTranslation('portfolioViews');
  const language = i18n.language;
  const portfoliosOn = useFeature('portfolios');
  const groups = usePortfolioGroups(portfoliosOn);
  // Departments come from the unfiltered dashboard (projects the viewer may read), cached and shared with the dashboard.
  const all = usePortfolioDashboard({});
  const units = useMemo(() => {
    const map = new Map<string, string>();
    for (const p of all.data?.projects ?? [])
      if (p.unit) map.set(p.unit.id, pickText(language, p.unit.nameAr, p.unit.nameEn) || p.unit.code);
    return [...map.entries()]
      .map(([id, label]) => ({ value: id, label }))
      .sort((a, b) => a.label.localeCompare(b.label, language));
  }, [all.data, language]);
  const portfolios = (groups.data ?? []).filter((g) => g.kind === 'portfolio');
  const programmes = (groups.data ?? []).filter(
    (g) => g.kind === 'programme' && (!value.portfolioGroupId || g.parentId === value.portfolioGroupId),
  );
  const name = (g: { code: string; nameAr: string | null; nameEn: string | null }) =>
    `${g.code} · ${pickText(language, g.nameAr, g.nameEn)}`;
  const allOption = { value: ALL, label: t('filters.all') };

  return (
    <div className="grid gap-3 sm:grid-cols-3" data-testid="portfolio-filters">
      {portfoliosOn && (
        <>
          <FormField label={t('filters.portfolio')}>
            <Select
              value={value.portfolioGroupId ?? ALL}
              onValueChange={(v) =>
                onChange({ ...value, portfolioGroupId: v === ALL ? undefined : v, programmeId: undefined })
              }
              options={[allOption, ...portfolios.map((g) => ({ value: g.id, label: name(g) }))]}
            />
          </FormField>
          <FormField label={t('filters.programme')}>
            <Select
              value={value.programmeId ?? ALL}
              onValueChange={(v) => onChange({ ...value, programmeId: v === ALL ? undefined : v })}
              options={[allOption, ...programmes.map((g) => ({ value: g.id, label: name(g) }))]}
            />
          </FormField>
        </>
      )}
      <FormField label={t('filters.unit')}>
        <Select
          value={value.unitId ?? ALL}
          onValueChange={(v) => onChange({ ...value, unitId: v === ALL ? undefined : v })}
          options={[allOption, ...units]}
        />
      </FormField>
    </div>
  );
}
