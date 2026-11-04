import { Download, Printer } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, pickText } from '@/lib/format';
import {
  Button,
  PageHeader,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { mechanismPdfUrl, useMechanism } from './api';
import { RatingChip, RiskMatrix, RiskPageLayout, Section, useNum, useRiskT } from './components';

/** Risk Assessment Mechanism (docs/design/risk.md §12): generated from the current framework, bilingual, printable / PDF. */
export function RiskMechanismPage() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k, language } = useRiskT();
  const num = useNum();
  const mechanism = useMechanism();
  const text = (ar: string | null | undefined, en: string | null | undefined) =>
    pickText(language, ar, en) || '—';
  if (mechanism.isError) return <StatusBanner tone="danger" title={errorMessage(tc, mechanism.error)} />;
  if (!mechanism.data) return <Skeleton className="h-96 rounded-lg" />;
  const { framework, matrix, categories } = mechanism.data;
  const c = framework.content;
  return (
    <RiskPageLayout testId="risk-mechanism">
      <PageHeader
        title={t('mechanism.title')}
        description={t('mechanism.description', {
          version: framework.versionNo,
          date: formatDate(framework.createdAt, language),
        })}
        actions={
          <div className="flex flex-wrap gap-2 print:hidden">
            <Button variant="outline" onClick={() => window.print()} data-testid="risk-mechanism-print">
              <Printer aria-hidden />
              {t('mechanism.print')}
            </Button>
            <Button variant="outline" asChild>
              <a href={mechanismPdfUrl(language)} data-testid="risk-mechanism-pdf">
                <Download aria-hidden />
                {t('mechanism.pdf')}
              </a>
            </Button>
          </div>
        }
      />
      <Section title={t('mechanism.scoring')} testId="risk-mechanism-scoring">
        <p>
          {k(`scoringMethod.${c.scoringMethod}`)}
          {c.scoringMethod === 'custom' && c.formula && (
            <code className="ms-2 rounded bg-muted px-2 py-0.5" dir="ltr">
              {c.formula}
            </code>
          )}
        </p>
        <p className="text-sm text-muted-foreground">{k(`scoringMethodHint.${c.scoringMethod}`)}</p>
      </Section>
      <div className="grid gap-4 xl:grid-cols-2">
        <Section title={t('mechanism.likelihood')} testId="risk-mechanism-likelihood">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('mechanism.level')}</TableHeaderCell>
                <TableHeaderCell>{t('mechanism.label')}</TableHeaderCell>
                <TableHeaderCell>{t('mechanism.descriptionCol')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {[...c.likelihood]
                .sort((a, b) => b.value - a.value)
                .map((l) => (
                  <TableRow key={l.value}>
                    <TableCell className="tabular-nums">{l.value}</TableCell>
                    <TableCell className="font-medium">{text(l.labelAr, l.labelEn)}</TableCell>
                    <TableCell>{pickText(language, l.descriptionAr, l.descriptionEn) || '—'}</TableCell>
                  </TableRow>
                ))}
            </TableBody>
          </Table>
        </Section>
        <Section title={t('mechanism.matrix')} testId="risk-mechanism-matrix">
          <RiskMatrix
            cells={matrix}
            bands={c.bands}
            prefix="mechanism"
            label={t('mechanism.matrix')}
            axis={{
              likelihood: (v) =>
                text(
                  c.likelihood.find((x) => x.value === v)?.labelAr,
                  c.likelihood.find((x) => x.value === v)?.labelEn,
                ),
              impact: (v) =>
                text(
                  c.impact.find((x) => x.value === v)?.labelAr,
                  c.impact.find((x) => x.value === v)?.labelEn,
                ),
            }}
          />
        </Section>
      </div>
      <Section title={t('mechanism.impact')} testId="risk-mechanism-impact">
        <div className="overflow-x-auto">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('mechanism.level')}</TableHeaderCell>
                <TableHeaderCell>{t('mechanism.label')}</TableHeaderCell>
                {c.impactAreas.map((a) => (
                  <TableHeaderCell key={a.code}>{text(a.labelAr, a.labelEn)}</TableHeaderCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {[...c.impact]
                .sort((a, b) => b.value - a.value)
                .map((l) => (
                  <TableRow key={l.value}>
                    <TableCell className="tabular-nums">{l.value}</TableCell>
                    <TableCell className="font-medium">{text(l.labelAr, l.labelEn)}</TableCell>
                    {c.impactAreas.map((a) => (
                      <TableCell key={a.code} className="min-w-40 text-sm">
                        {text(l.criteria?.[a.code]?.ar, l.criteria?.[a.code]?.en)}
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
            </TableBody>
          </Table>
        </div>
      </Section>
      <div className="grid gap-4 xl:grid-cols-2">
        <Section title={t('mechanism.bands')} testId="risk-mechanism-bands">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('mechanism.band')}</TableHeaderCell>
                <TableHeaderCell>{t('mechanism.range')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.residualStatus')}</TableHeaderCell>
                <TableHeaderCell>{t('mechanism.reviewEvery')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {c.bands.map((b) => (
                <TableRow key={b.code}>
                  <TableCell>
                    <RatingChip band={b.code} score={null} bands={c.bands} />
                  </TableCell>
                  <TableCell className="tabular-nums">
                    <bdi>
                      {num(b.min)} – {num(b.max)}
                    </bdi>
                  </TableCell>
                  <TableCell>{k(`residualStatus.${b.residualStatus}`)}</TableCell>
                  <TableCell>
                    {c.reviewFrequencyMonths[b.code] != null
                      ? t('mechanism.months', { count: c.reviewFrequencyMonths[b.code] })
                      : '—'}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <p className="text-xs text-muted-foreground">{t('mechanism.gapRule')}</p>
        </Section>
        <Section title={t('mechanism.effectiveness')} testId="risk-mechanism-effectiveness">
          <Table>
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('mechanism.label')}</TableHeaderCell>
                <TableHeaderCell>{t('mechanism.factor')}</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {c.controlEffectiveness.map((e) => (
                <TableRow key={e.code}>
                  <TableCell>{text(e.labelAr, e.labelEn)}</TableCell>
                  <TableCell className="tabular-nums">{num(e.factor)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <p className="text-xs text-muted-foreground">
            {k(`projectResidualRuleHint.${c.projectResidualRule}`)}
          </p>
        </Section>
      </div>
      <Section title={t('mechanism.categories')} testId="risk-mechanism-categories">
        <Table>
          <TableHead>
            <TableRow>
              <TableHeaderCell>{t('settings.categories.code')}</TableHeaderCell>
              <TableHeaderCell>{t('mechanism.label')}</TableHeaderCell>
              <TableHeaderCell>{t('fields.appetite')}</TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {categories
              .filter((cat) => cat.active)
              .map((cat) => (
                <TableRow key={cat.id}>
                  <TableCell>
                    <bdi>{cat.code}</bdi>
                  </TableCell>
                  <TableCell>{text(cat.nameAr, cat.nameEn)}</TableCell>
                  <TableCell className="tabular-nums">
                    {cat.appetite == null ? t('common.notSet') : num(cat.appetite)}
                  </TableCell>
                </TableRow>
              ))}
          </TableBody>
        </Table>
      </Section>
    </RiskPageLayout>
  );
}
