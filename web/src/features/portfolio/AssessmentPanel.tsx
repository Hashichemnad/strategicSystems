import { Link } from '@tanstack/react-router';
import { RefreshCw, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { formatDateTime, formatNumber, pickText } from '@/lib/format';
import { Button, EmptyState, Select, Skeleton, StatusBanner, StatusChip, useToast } from '@/ui';
import {
  bandTone,
  requestApi,
  useAssessments,
  useRequestMutation,
  type Assessment,
  type PortfolioOptions,
  type ProgramRequest,
} from './api';

/** The versioned Proposal Assessment (SPEC §1.15 A5–A6): AI-generated, read by everyone who may read the request. */
export function AssessmentPanel({
  request,
  options,
}: {
  request: ProgramRequest;
  options: PortfolioOptions;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const enabled = useAiFeature('portfolio-assessment');
  const assessments = useAssessments(request.id);
  const generate = useRequestMutation(requestApi.assess);
  const [selected, setSelected] = useState<number>();
  if (assessments.isPending) return <Skeleton className="h-64 rounded-lg" />;
  if (assessments.isError) return <StatusBanner tone="danger" title={errorMessage(tc, assessments.error)} />;
  const list = assessments.data;
  const shown = list.find((a) => a.versionNo === selected) ?? list[0];
  const run = () =>
    generate.mutate(
      { id: request.id },
      {
        onSuccess: (result) => {
          toast({ title: t('toast.assessed', { version: result.versionNo }) });
          setSelected(result.versionNo);
          void assessments.refetch();
        },
        onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
      },
    );
  const action = enabled && request.can.assess && (
    <Button variant={list.length ? 'outline' : 'primary'} loading={generate.isPending} onClick={run}>
      {list.length ? <RefreshCw aria-hidden /> : <Sparkles aria-hidden />}
      {list.length ? t('actions.regenerate') : t('actions.assess')}
    </Button>
  );
  if (!shown) {
    return (
      <EmptyState
        icon={Sparkles}
        title={t('assessment.empty')}
        description={t('assessment.description')}
        action={action || undefined}
      />
    );
  }
  return (
    <div className="flex flex-col gap-4" data-testid="proposal-assessment">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <h2 className="text-lg font-semibold">{t('assessment.title')}</h2>
          <AiBadge />
        </div>
        <div className="flex items-center gap-2">
          {list.length > 1 && (
            <Select
              aria-label={t('assessment.version', { version: shown.versionNo })}
              value={String(shown.versionNo)}
              onValueChange={(value) => setSelected(Number(value))}
              options={list.map((a) => ({
                value: String(a.versionNo),
                label: t('assessment.version', { version: a.versionNo }),
              }))}
            />
          )}
          {action}
        </div>
      </div>
      <p className="text-sm text-muted-foreground">
        {t('assessment.description')} · {t('assessment.version', { version: shown.versionNo })} ·{' '}
        {t('assessment.generatedBy', {
          name: shown.generatedBy
            ? pickText(i18n.language, shown.generatedBy.nameAr, shown.generatedBy.nameEn)
            : '—',
          date: formatDateTime(shown.generatedAt, i18n.language),
        })}
      </p>
      <AssessmentBody assessment={shown} options={options} />
    </div>
  );
}

function AssessmentBody({ assessment, options }: { assessment: Assessment; options: PortfolioOptions }) {
  const { t, i18n } = useTranslation('portfolio');
  const lang = i18n.language;
  const c = assessment.content;
  const list = (title: string, items: readonly string[], testId: string) => (
    <section className="rounded-lg border bg-card p-4" data-testid={testId}>
      <h3 className="mb-2 font-medium">{title}</h3>
      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">—</p>
      ) : (
        <ul className="list-disc ps-5 text-sm" dir="auto">
          {items.map((item, i) => (
            <li key={i}>{item}</li>
          ))}
        </ul>
      )}
    </section>
  );
  const band = options.bands.find((b) => b.code === c.suggestedPriority.band);
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <section className="rounded-lg border bg-card p-4 lg:col-span-2">
        <h3 className="mb-2 font-medium">{t('assessment.summary')}</h3>
        <p className="whitespace-pre-line" dir="auto">
          {c.summary ?? '—'}
        </p>
      </section>
      {list(t('assessment.gaps'), c.gaps, 'assessment-gaps')}
      {list(t('assessment.strengths'), c.strengths, 'assessment-strengths')}
      {list(t('assessment.risks'), c.risks, 'assessment-risks')}
      {list(t('assessment.recommendations'), c.recommendations, 'assessment-recommendations')}
      <section className="rounded-lg border bg-card p-4">
        <h3 className="mb-2 font-medium">{t('assessment.alignment')}</h3>
        {c.alignment.summary && (
          <p className="mb-2 text-sm" dir="auto">
            {c.alignment.summary}
          </p>
        )}
        <ul className="flex flex-col gap-2 text-sm">
          {c.alignment.objectives.map((o) => (
            <li key={o.code} className="flex flex-col gap-1">
              <span className="flex items-center gap-2">
                <StatusChip
                  tone={o.strength === 'strong' ? 'success' : o.strength === 'moderate' ? 'info' : 'neutral'}
                >
                  {t(`assessment.strength.${o.strength as 'strong'}`)}
                </StatusChip>
                {pickText(lang, o.labelAr, o.labelEn)}
              </span>
              {o.reason && (
                <span className="text-muted-foreground" dir="auto">
                  {o.reason}
                </span>
              )}
            </li>
          ))}
        </ul>
      </section>
      <section className="flex flex-col gap-3 rounded-lg border bg-card p-4">
        <div>
          <h3 className="font-medium">{t('assessment.suggestedClassification')}</h3>
          <p className="text-sm">
            {c.suggestedClassification.value
              ? t(`classification.${c.suggestedClassification.value as 'project'}`)
              : '—'}
            <span className="ms-2 text-xs text-muted-foreground">
              {t('assessment.computedNow', {
                value: t(`classification.${c.suggestedClassification.computed as 'project'}`),
              })}
            </span>
          </p>
          {c.suggestedClassification.reasoning && (
            <p className="text-sm text-muted-foreground" dir="auto">
              {c.suggestedClassification.reasoning}
            </p>
          )}
        </div>
        <div>
          <h3 className="font-medium">{t('assessment.suggestedPriority')}</h3>
          <ul className="text-sm">
            {options.criteria.map((criterion) => (
              <li key={criterion.code} className="flex justify-between gap-2">
                <span className="text-muted-foreground">
                  {pickText(lang, criterion.labelAr, criterion.labelEn)}
                </span>
                <span className="tabular-nums">
                  {c.suggestedPriority.scores[criterion.code] !== undefined
                    ? formatNumber(c.suggestedPriority.scores[criterion.code] ?? 0, lang)
                    : '—'}
                </span>
              </li>
            ))}
          </ul>
          {c.suggestedPriority.score !== null && (
            <StatusChip className="mt-2" tone={bandTone[c.suggestedPriority.band ?? ''] ?? 'neutral'}>
              {formatNumber(Math.round(c.suggestedPriority.score * 10000) / 100, lang)} %
              {band ? ` · ${pickText(lang, band.labelAr, band.labelEn)}` : ''}
            </StatusChip>
          )}
          {c.suggestedPriority.reasoning && (
            <p className="text-sm text-muted-foreground" dir="auto">
              {c.suggestedPriority.reasoning}
            </p>
          )}
        </div>
      </section>
      <section className="rounded-lg border bg-card p-4 lg:col-span-2" data-testid="assessment-similar">
        <h3 className="mb-2 font-medium">{t('assessment.similar')}</h3>
        {c.similar.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('assessment.noSimilar')}</p>
        ) : (
          <ul className="flex flex-col gap-1 text-sm">
            {c.similar.map((s) => (
              <li key={s.id}>
                <Link to={s.link} className="text-primary hover:underline">
                  {s.number && <bdi>{s.number}</bdi>} · {pickText(lang, s.titleAr, s.titleEn)}
                </Link>
                <span className="ms-2 text-xs text-muted-foreground">{Math.round(s.score * 100)} %</span>
              </li>
            ))}
          </ul>
        )}
      </section>
      {assessment.documents.length > 0 && (
        <p className="text-xs text-muted-foreground lg:col-span-2">
          {t('assessment.documents', { names: assessment.documents.join('، ') })}
        </p>
      )}
    </div>
  );
}
