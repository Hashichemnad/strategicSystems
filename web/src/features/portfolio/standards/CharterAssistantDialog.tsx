import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Sparkles } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  Checkbox,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  Skeleton,
  StatusBanner,
  StatusChip,
  useToast,
} from '@/ui';
import type { PortfolioOptions } from '../api';
import { saveItem, type ItemKind, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { aiDrafts, useLessonSearch, type CharterSuggestions, type LessonHit } from './standardsApi';

type Group = 'kpis' | 'risks' | 'stakeholders' | 'planningItems';
const groups: Group[] = ['kpis', 'risks', 'stakeholders', 'planningItems'];
const kindOf: Record<Group, ItemKind> = {
  kpis: 'kpis',
  risks: 'risks',
  stakeholders: 'stakeholders',
  planningItems: 'planning-items',
};
const levels = ['low', 'medium', 'high'] as const;
const level = (v: string) => ((levels as readonly string[]).includes(v) ? v : null);

/** The payload each suggestion is created with, the same shape the KPIs / Risks / Stakeholders / Planning tabs send. */
export function suggestionPayload(
  group: Group,
  row: CharterSuggestions[Group][number],
  options: Pick<PortfolioOptions, 'stakeholderClasses'>,
): unknown {
  switch (group) {
    case 'kpis': {
      const k = row as CharterSuggestions['kpis'][number];
      return {
        nature: 'new',
        kpiCardId: null,
        projectGoal: null,
        nameAr: k.nameAr,
        nameEn: k.nameEn,
        equation: k.equation,
        direction: 'increase',
        indexClassCode: null,
        target: k.target,
        targetYear: null,
        whenMeasured: 'during',
        currentResult: null,
      };
    }
    case 'risks': {
      const r = row as CharterSuggestions['risks'][number];
      return {
        title: r.title,
        categoryCode: null,
        probability: Math.min(5, Math.max(1, Math.round(r.probability))),
        impact: Math.min(5, Math.max(1, Math.round(r.impact))),
        riskFactor: null,
        controlEvaluation: null,
        response: r.mitigation ? 'mitigate' : null,
        mitigationActions: r.mitigation,
        implementationPct: null,
        implementationStart: null,
        implementationEnd: null,
        ownerEmployeeId: null,
        rmsStatus: null,
      };
    }
    case 'stakeholders': {
      const s = row as CharterSuggestions['stakeholders'][number];
      return {
        name: s.name,
        classificationCode: options.stakeholderClasses.some((c) => c.code === s.classification)
          ? s.classification
          : null,
        interestLevel: level(s.interest),
        impactLevel: level(s.impact),
        communicationMethod: null,
        communicationFrequency: null,
        email: null,
        phone: null,
      };
    }
    default: {
      const p = row as CharterSuggestions['planningItems'][number];
      return { category: p.category, description: p.description };
    }
  }
}

/** Charter assistant (docs/design/portfolio.md §40): AI suggestions the PM ticks; nothing is saved until "Add selected". */
export function CharterAssistantDialog({
  project,
  open,
  onOpenChange,
  options,
}: {
  project: ProjectFull;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  options: PortfolioOptions;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const { t: tp } = useTranslation('portfolio');
  const toast = useToast();
  const queryClient = useQueryClient();
  const suggest = useMutation({ mutationFn: () => aiDrafts.charter(project.id) });
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const lang = i18n.language;
  const { mutate, reset } = suggest;
  useEffect(() => {
    if (open) {
      setSelected(new Set());
      setError(undefined);
      mutate();
    } else reset();
  }, [open, mutate, reset]);
  const data = suggest.data;
  const toggle = (key: string, on: boolean) =>
    setSelected((s) => {
      const next = new Set(s);
      if (on) next.add(key);
      else next.delete(key);
      return next;
    });
  const label = (group: Group, row: CharterSuggestions[Group][number]): string => {
    switch (group) {
      case 'kpis': {
        const k = row as CharterSuggestions['kpis'][number];
        const name = pickText(lang, k.nameAr, k.nameEn);
        return k.target !== null
          ? `${name} · ${t('charterAi.target')}: ${formatNumber(k.target, lang)}`
          : name;
      }
      case 'risks': {
        const r = row as CharterSuggestions['risks'][number];
        return `${r.title} · ${formatNumber(r.probability, lang)} × ${formatNumber(r.impact, lang)}`;
      }
      case 'stakeholders': {
        const s = row as CharterSuggestions['stakeholders'][number];
        const cls = options.stakeholderClasses.find((c) => c.code === s.classification);
        return `${s.name} · ${cls ? pickText(lang, cls.labelAr, cls.labelEn) : s.classification}`;
      }
      default: {
        const p = row as CharterSuggestions['planningItems'][number];
        return `${tp(`project.planning.categories.${p.category as 'in-scope'}`, { defaultValue: p.category })}: ${p.description}`;
      }
    }
  };
  const detail = (group: Group, row: CharterSuggestions[Group][number]): string | null => {
    if (group === 'kpis') return (row as CharterSuggestions['kpis'][number]).equation;
    if (group === 'risks') return (row as CharterSuggestions['risks'][number]).mitigation;
    if (group === 'stakeholders') {
      const s = row as CharterSuggestions['stakeholders'][number];
      return t('charterAi.interestImpact', {
        interest: tp(`project.levels.${s.interest as 'low'}`, { defaultValue: s.interest }),
        impact: tp(`project.levels.${s.impact as 'low'}`, { defaultValue: s.impact }),
      });
    }
    return null;
  };
  const addSelected = async () => {
    if (!data) return;
    setBusy(true);
    setError(undefined);
    let created = 0;
    try {
      for (const group of groups) {
        const rows = data[group] as CharterSuggestions[Group][number][];
        for (const [index, row] of rows.entries()) {
          if (!selected.has(`${group}:${index}`)) continue;
          await saveItem(project.id, kindOf[group], undefined, {
            values: suggestionPayload(group, row, options),
            version: null,
          });
          created++;
        }
      }
      toast({ title: t('charterAi.added', { count: created }) });
      onOpenChange(false);
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
      if (created > 0)
        await queryClient.invalidateQueries({ queryKey: ['portfolio', 'project', project.id] });
    }
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t('charterAi.title')}
        description={t('charterAi.hint')}
        className="max-w-3xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              disabled={selected.size === 0 || !project.permissions.canEditContent}
              loading={busy}
              onClick={() => void addSelected()}
              data-testid="charter-ai-add"
            >
              {t('charterAi.addSelected', { count: selected.size })}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4" data-testid="charter-assistant">
          <StatusChip tone="info">
            <Sparkles aria-hidden className="me-1 inline size-3" />
            {t('ai.draft')}
          </StatusChip>
          {suggest.isPending ? (
            <Skeleton className="h-48" />
          ) : suggest.isError ? (
            <StatusBanner
              tone="danger"
              title={errorMessage(tc, suggest.error)}
              actions={
                <Button size="sm" variant="outline" onClick={() => mutate()}>
                  {t('ai.retry')}
                </Button>
              }
            />
          ) : (
            data && (
              <>
                {groups.map((group) => {
                  const rows = data[group] as CharterSuggestions[Group][number][];
                  return (
                    <fieldset key={group} className="flex flex-col gap-2 rounded-lg border p-3">
                      <legend className="px-1 text-sm font-semibold">{t(`charterAi.groups.${group}`)}</legend>
                      {rows.length === 0 ? (
                        <p className="text-sm text-muted-foreground">{t('charterAi.none')}</p>
                      ) : (
                        rows.map((row, index) => {
                          const key = `${group}:${index}`;
                          return (
                            <Checkbox
                              key={key}
                              label={<span dir="auto">{label(group, row)}</span>}
                              description={detail(group, row) ?? undefined}
                              checked={selected.has(key)}
                              onChange={(e) => toggle(key, e.target.checked)}
                              data-testid={`charter-ai-${group}`}
                            />
                          );
                        })
                      )}
                    </fieldset>
                  );
                })}
                <LessonsPanel projectId={project.id} lessons={data.lessons} />
              </>
            )
          )}
          {error !== undefined && <FieldErrors error={error} />}
        </div>
      </DialogContent>
    </Dialog>
  );
}

/** Relevant lessons from past projects, found by the server (not the model), plus a search over readable projects. */
function LessonsPanel({ projectId, lessons }: { projectId: string; lessons: LessonHit[] }) {
  const { t } = useTranslation('pmStandards');
  const [text, setText] = useState('');
  const [q, setQ] = useState('');
  useEffect(() => {
    const timer = window.setTimeout(() => setQ(text.trim()), 300);
    return () => window.clearTimeout(timer);
  }, [text]);
  const search = useLessonSearch(q, projectId, q.length >= 2);
  const shown = q.length >= 2 ? (search.data ?? []) : lessons;
  return (
    <section className="flex flex-col gap-2 rounded-lg border p-3" data-testid="charter-lessons">
      <h3 className="text-sm font-semibold">{t('lessons.title')}</h3>
      <FormField label={t('lessons.search')}>
        <Input dir="auto" type="search" value={text} onChange={(e) => setText(e.target.value)} />
      </FormField>
      <LessonList lessons={shown} />
    </section>
  );
}

export function LessonList({ lessons }: { lessons: LessonHit[] }) {
  const { t, i18n } = useTranslation('pmStandards');
  if (lessons.length === 0) return <p className="text-sm text-muted-foreground">{t('lessons.none')}</p>;
  return (
    <ul className="flex flex-col gap-2 text-sm">
      {lessons.map((l) => (
        <li key={l.id} className="rounded-md border bg-card p-2">
          <p className="text-xs text-muted-foreground">
            <bdi>{l.projectNumber}</bdi> · {pickText(i18n.language, l.projectNameAr, l.projectNameEn)}
          </p>
          <p dir="auto" className="whitespace-pre-line">
            {l.lesson}
          </p>
          {l.recommendation && (
            <p dir="auto" className="text-muted-foreground">
              {t('lessons.recommendation')}: {l.recommendation}
            </p>
          )}
        </li>
      ))}
    </ul>
  );
}
