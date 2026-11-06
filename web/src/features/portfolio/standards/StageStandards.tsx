import { useMutation } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { FileDown, Sparkles, Wand2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { AiBadge } from '@/features/ai/AiDraft';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { Button, FormField, StatusBanner, Textarea, useToast } from '@/ui';
import { useFeature } from '../featuresApi';
import { useProjectMutation, type ChangeRequest, type MonthlyUpdate, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import {
  aiDrafts,
  downloadStatusReport,
  standardsApi,
  useAiAssist,
  type ChangeImpact,
  type UpdateDraft,
} from './standardsApi';
import { parseSprints } from './standardsLogic';

/**
 * The month's narrative on the open window (docs/design/portfolio.md §37): typed by the PM or drafted by the status report writer /
 * update assistant (§40). AI fills the fields only; the PM saves.
 */
export function MonthlyNarrativePanel({
  project,
  current,
}: {
  project: ProjectFull;
  current: MonthlyUpdate;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useProjectMutation(project.id, standardsApi.saveNarrative);
  const writerOn = useAiAssist('portfolio-status-report');
  const assistantOn = useAiAssist('portfolio-update-assistant');
  const writer = useMutation({ mutationFn: () => aiDrafts.statusNarrative(project.id) });
  const assistant = useMutation({ mutationFn: () => aiDrafts.update(project.id) });
  const [ar, setAr] = useState(current.narrativeAr ?? '');
  const [en, setEn] = useState(current.narrativeEn ?? '');
  const [fromAi, setFromAi] = useState(false);
  const [draft, setDraft] = useState<UpdateDraft | null>(null);
  const editable =
    project.permissions.canEdit && (current.status === 'open' || current.status === 'returned');
  const lang = i18n.language;
  if (!editable)
    return current.narrativeAr || current.narrativeEn ? (
      <div className="rounded-md border p-3 text-sm" data-testid="monthly-narrative">
        <h3 className="font-medium">{t('monthly.narrative')}</h3>
        <p className="whitespace-pre-line" dir="auto">
          {pickText(lang, current.narrativeAr, current.narrativeEn)}
        </p>
      </div>
    ) : null;
  const aiError = writer.error ?? assistant.error;
  return (
    <section className="flex flex-col gap-3 rounded-md border p-3" data-testid="monthly-narrative">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-medium">{t('monthly.narrative')}</h3>
          <p className="text-xs text-muted-foreground">{t('monthly.narrativeHint')}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          {writerOn && (
            <Button
              size="sm"
              variant="outline"
              loading={writer.isPending}
              data-testid="narrative-ai"
              onClick={() =>
                writer.mutate(undefined, {
                  onSuccess: (n) => {
                    setAr(n.narrativeAr ?? '');
                    setEn(n.narrativeEn ?? '');
                    setFromAi(true);
                  },
                })
              }
            >
              <Sparkles aria-hidden />
              {t('monthly.draftWithAi')}
            </Button>
          )}
          {assistantOn && (
            <Button
              size="sm"
              variant="outline"
              loading={assistant.isPending}
              data-testid="update-assistant"
              onClick={() =>
                assistant.mutate(undefined, {
                  onSuccess: (d) => {
                    if (d.narrative) {
                      if (lang === 'ar') setAr(d.narrative);
                      else setEn(d.narrative);
                      setFromAi(true);
                    }
                    setDraft(d);
                  },
                })
              }
            >
              <Wand2 aria-hidden />
              {t('monthly.updateAssistant')}
            </Button>
          )}
        </div>
      </header>
      {fromAi && <AiBadge label={t('ai.draft')} />}
      {aiError && <StatusBanner tone="danger" title={errorMessage(tc, aiError)} />}
      <div className="grid gap-3 md:grid-cols-2">
        <FormField label={t('monthly.narrativeAr')}>
          <Textarea dir="rtl" rows={5} value={ar} onChange={(e) => setAr(e.target.value)} />
        </FormField>
        <FormField label={t('monthly.narrativeEn')}>
          <Textarea dir="ltr" rows={5} value={en} onChange={(e) => setEn(e.target.value)} />
        </FormField>
      </div>
      {draft && (draft.atRisk.length > 0 || draft.correctiveActions.length > 0) && (
        <div
          className="flex flex-col gap-2 rounded-md border border-info/30 bg-info-soft/40 p-3 text-sm"
          data-testid="update-draft"
        >
          <AiBadge label={t('ai.draft')} />
          {draft.atRisk.length > 0 && (
            <div>
              <h4 className="font-medium">{t('monthly.atRisk')}</h4>
              <ul className="list-disc ps-5">
                {draft.atRisk.map((m) => (
                  <li key={m.id}>
                    <bdi>{m.code}</bdi> · {pickText(lang, m.titleAr, m.titleEn)} —{' '}
                    {t(`monthly.atRiskReasons.${m.reason === 'past-end' ? 'past-end' : 'behind-plan'}`, {
                      defaultValue: m.reason,
                    })}
                  </li>
                ))}
              </ul>
            </div>
          )}
          {draft.correctiveActions.length > 0 && (
            <div>
              <h4 className="font-medium">{t('monthly.correctiveActions')}</h4>
              <ul className="list-disc ps-5" dir="auto">
                {draft.correctiveActions.map((a, i) => (
                  <li key={i}>{a}</li>
                ))}
              </ul>
            </div>
          )}
          <div>
            <Button size="sm" variant="ghost" onClick={() => setDraft(null)}>
              {t('ai.dismiss')}
            </Button>
          </div>
        </div>
      )}
      <div>
        <Button
          size="sm"
          loading={save.isPending}
          data-testid="narrative-save"
          onClick={() =>
            save.mutate(
              { id: project.id, narrativeAr: ar.trim() || null, narrativeEn: en.trim() || null },
              {
                onSuccess: () => {
                  setFromAi(false);
                  toast({ title: t('saved') });
                },
              },
            )
          }
        >
          {t('monthly.saveNarrative')}
        </Button>
      </div>
      {save.isError && <FieldErrors error={save.error} />}
    </section>
  );
}

/** History extras of a monthly update: narrative, sprints of an agile roll-up, status report PDF downloads (approved only). */
export function MonthlyHistoryExtras({ project, update }: { project: ProjectFull; update: MonthlyUpdate }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const reportOn = useFeature('statusReport');
  const [busy, setBusy] = useState<'ar' | 'en' | null>(null);
  const lang = i18n.language;
  const sprints = parseSprints(update.sprints);
  const narrative = pickText(lang, update.narrativeAr, update.narrativeEn);
  const download = async (language: 'ar' | 'en') => {
    setBusy(language);
    try {
      await downloadStatusReport(project.id, update.period, language, project.number);
    } catch (failure) {
      toast({ tone: 'error', title: errorMessage(tc, failure) });
    } finally {
      setBusy(null);
    }
  };
  return (
    <div className="mt-2 flex flex-col gap-2 text-sm">
      {narrative && (
        <p className="whitespace-pre-line" dir="auto">
          {narrative}
        </p>
      )}
      {sprints.length > 0 && (
        <div data-testid="monthly-sprints">
          <h4 className="font-medium">{t('monthly.sprints')}</h4>
          <ul className="flex flex-col gap-1">
            {sprints.map((s) => (
              <li key={s.number}>
                {t('monthly.sprint', { number: formatNumber(s.number, lang) })}
                {s.name && (
                  <span dir="auto" className="ms-1">
                    · {s.name}
                  </span>
                )}
                {s.startDate && s.endDate && (
                  <span className="ms-1 text-muted-foreground">
                    ({formatDate(s.startDate, lang)} – {formatDate(s.endDate, lang)})
                  </span>
                )}
                <span className="ms-1">
                  ·{' '}
                  {t('monthly.points', {
                    completed: formatNumber(s.completedPoints, lang),
                    committed: formatNumber(s.committedPoints, lang),
                  })}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
      {reportOn && update.status === 'approved' && (
        <div className="flex flex-wrap items-center gap-2" data-testid="status-report-download">
          <span className="text-muted-foreground">{t('monthly.statusReport')}</span>
          <Button size="sm" variant="outline" loading={busy === 'ar'} onClick={() => void download('ar')}>
            <FileDown aria-hidden />
            <span lang="ar">العربية</span>
            {/* Visible language first in the name (WCAG 2.5.3). */}
            <span className="sr-only"> — {t('monthly.downloadAr')}</span>
          </Button>
          <Button size="sm" variant="outline" loading={busy === 'en'} onClick={() => void download('en')}>
            <FileDown aria-hidden />
            <span lang="en">English</span>
            {/* Visible language first in the name (WCAG 2.5.3). */}
            <span className="sr-only"> — {t('monthly.downloadEn')}</span>
          </Button>
        </div>
      )}
    </div>
  );
}

/** "Analyse impact (AI)" on a change request: a draft of the effect on dates, cost, weights and dependent projects. */
export function ChangeImpactPanel({ change }: { change: ChangeRequest }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const on = useAiAssist('portfolio-change-impact');
  const analyse = useMutation({ mutationFn: () => aiDrafts.changeImpact(change.id) });
  const [shown, setShown] = useState<ChangeImpact | null>(null);
  if (!on || change.status === 'cancelled' || change.status === 'rejected') return null;
  const lang = i18n.language;
  const rows: [string, string | null][] = shown
    ? [
        [t('impact.summary'), shown.summary],
        [t('impact.dates'), shown.dates],
        [t('impact.cost'), shown.cost],
        [t('impact.weights'), shown.weights],
        [t('impact.dependents'), shown.dependents],
      ]
    : [];
  return (
    <div className="flex flex-col gap-2">
      <div>
        <Button
          size="sm"
          variant="outline"
          loading={analyse.isPending}
          data-testid="change-impact"
          onClick={() => analyse.mutate(undefined, { onSuccess: setShown })}
        >
          <Sparkles aria-hidden />
          {t('impact.analyse')}
        </Button>
      </div>
      {analyse.isError && <StatusBanner tone="danger" title={errorMessage(tc, analyse.error)} />}
      {shown && (
        <div
          className="flex flex-col gap-2 rounded-md border border-info/30 bg-info-soft/40 p-3 text-sm"
          data-testid="change-impact-draft"
        >
          <div className="flex items-center justify-between gap-2">
            <AiBadge label={t('ai.draft')} />
            <Button size="sm" variant="ghost" onClick={() => setShown(null)}>
              {t('ai.dismiss')}
            </Button>
          </div>
          <dl className="flex flex-col gap-1">
            {rows
              .filter(([, v]) => v)
              .map(([k, v]) => (
                <div key={k}>
                  <dt className="font-medium">{k}</dt>
                  <dd className="whitespace-pre-line" dir="auto">
                    {v}
                  </dd>
                </div>
              ))}
          </dl>
          {shown.recommendation.length > 0 && (
            <div>
              <h4 className="font-medium">{t('impact.recommendation')}</h4>
              <ul className="list-disc ps-5" dir="auto">
                {shown.recommendation.map((r, i) => (
                  <li key={i}>{r}</li>
                ))}
              </ul>
            </div>
          )}
          <div>
            <h4 className="font-medium">{t('impact.dependentProjects')}</h4>
            {shown.dependentProjects.length === 0 ? (
              <p className="text-muted-foreground">{t('impact.noDependents')}</p>
            ) : (
              <ul className="list-disc ps-5">
                {shown.dependentProjects.map((p) => (
                  <li key={p.id}>
                    <Link
                      to="/portfolio/projects/$projectId"
                      params={{ projectId: p.id }}
                      className="text-primary hover:underline"
                    >
                      <bdi>{p.number}</bdi> · {pickText(lang, p.nameAr, p.nameEn)}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
