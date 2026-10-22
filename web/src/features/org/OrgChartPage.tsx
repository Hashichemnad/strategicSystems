import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { chartRoute } from '@/app/router';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  DatePicker,
  EmptyState,
  FormField,
  OrgChart,
  PageHeader,
  Skeleton,
  StatusBanner,
} from '@/ui';
import { useLevels, useUnitTree } from './api';

/** The structure as it stood on any date (history is kept on every re-organisation). */
export function OrgChartPage() {
  const { t } = useTranslation('org');
  const { t: tc, i18n } = useTranslation();
  const { asOf } = chartRoute.useSearch();
  const navigate = useNavigate();
  const tree = useUnitTree(asOf);
  const levels = useLevels();
  const levelName = (code: string) => {
    const level = levels.data?.find((l) => l.code === code);
    return level ? pickText(i18n.language, level.nameAr, level.nameEn) : code;
  };

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('chart.title')}
        description={
          asOf
            ? t('chart.asOfDescription', { date: formatDate(asOf, i18n.language) })
            : t('chart.description')
        }
      />
      <div className="flex flex-wrap items-end gap-3">
        <FormField label={t('chart.asOf')} className="w-56">
          <DatePicker
            value={asOf}
            onChange={(value) => void navigate({ to: '/org/chart', search: { asOf: value } })}
          />
        </FormField>
        {asOf && (
          <Button variant="outline" onClick={() => void navigate({ to: '/org/chart', search: {} })}>
            {t('chart.today')}
          </Button>
        )}
      </div>
      {tree.isPending ? (
        <Skeleton className="h-80 rounded-lg" />
      ) : tree.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, tree.error)} />
      ) : tree.data.length === 0 ? (
        <EmptyState title={t('chart.empty')} />
      ) : (
        <OrgChart
          items={tree.data}
          label={t('chart.title')}
          renderNode={(node) => (
            <>
              <span className="text-xs text-muted-foreground">{levelName(node.levelCode)}</span>
              <span className="font-semibold">{pickText(i18n.language, node.nameAr, node.nameEn)}</span>
              <span className="text-xs text-muted-foreground" dir="ltr">
                {node.code}
              </span>
              {node.head && (
                <span className="mt-1 text-xs">
                  {pickText(i18n.language, node.head.nameAr, node.head.nameEn)}
                </span>
              )}
              {!asOf && node.employeeCount > 0 && (
                <span className="text-xs text-muted-foreground">
                  {t('chart.employees', { count: node.employeeCount })}
                </span>
              )}
            </>
          )}
        />
      )}
    </div>
  );
}
