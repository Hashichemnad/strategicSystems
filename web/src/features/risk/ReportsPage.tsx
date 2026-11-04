import { Download } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { UnitSelect } from '@/features/access/pickers';
import { Button, FormField, PageHeader, StatusBanner } from '@/ui';
import { reportKinds, reportUrl, useRiskOptions } from './api';
import { RiskPageLayout, Section, useRiskT } from './components';

/** Reports (docs/design/risk.md §12): register, Top-10, unit profile, action tracker — PDF and Excel in the UI language (audited). */
export function RiskReportsPage() {
  const { t } = useTranslation('risk');
  const { k, language } = useRiskT();
  const options = useRiskOptions();
  const [unitId, setUnitId] = useState<string>();
  return (
    <RiskPageLayout testId="risk-reports">
      <PageHeader title={t('reports.title')} description={t('reports.description')} />
      {options.data && !options.data.canExport && <StatusBanner tone="info" title={t('reports.noAccess')} />}
      <FormField label={t('reports.unit')} className="max-w-md">
        <UnitSelect allowEmpty value={unitId} onChange={setUnitId} />
      </FormField>
      <Section title={t('reports.exports')}>
        {reportKinds.map((kind) => (
          <div
            key={kind}
            className="flex flex-wrap items-center justify-between gap-2 border-b py-2 last:border-b-0"
          >
            <span className="flex flex-col">
              <span className="font-medium">{k(`reports.kinds.${kind}`)}</span>
              <span className="text-sm text-muted-foreground">{k(`reports.hints.${kind}`)}</span>
            </span>
            {options.data?.canExport && (
              <span className="flex gap-2">
                {(['pdf', 'xlsx'] as const).map((format) => (
                  <Button key={format} variant="outline" size="sm" asChild>
                    <a
                      href={reportUrl(kind, format, language, unitId)}
                      data-testid={`risk-report-${kind}-${format}`}
                    >
                      <Download aria-hidden />
                      {t(`reports.formats.${format}`)}
                    </a>
                  </Button>
                ))}
              </span>
            )}
          </div>
        ))}
      </Section>
    </RiskPageLayout>
  );
}
