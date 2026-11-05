import { Link } from '@tanstack/react-router';
import { Save, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { Button, StatusBanner, StatusChip, Textarea, useToast } from '@/ui';
import { useGenerateBrief, useSaveBrief, type Brief } from './api';

type Point = Brief['improved'][number];

/** AI executive brief (ADR 0147): an AI-generated draft from the viewer's own executive dashboard, editable, saved by a person. */
export function BriefPanel({
  period,
  unitId,
}: {
  period: { kind: string; from: string; to: string };
  unitId?: string;
}) {
  const { t, i18n } = useTranslation('reporting');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const canBrief = useCan('reporting.brief.generate');
  const generate = useGenerateBrief();
  const save = useSaveBrief();
  const [brief, setBrief] = useState<Brief>();
  const [edited, setEdited] = useState(false);
  if (!canBrief) return null;
  const ar = i18n.language === 'ar';
  const section = (key: 'improved' | 'attention' | 'decisions', tone: 'success' | 'warning' | 'info') => {
    const points = brief?.[key] ?? [];
    return (
      <div className="flex flex-col gap-2" data-testid={`brief-${key}`}>
        <h4 className="text-sm font-semibold">
          <StatusChip tone={tone}>{t(`brief.${key}`)}</StatusChip>
        </h4>
        {points.length === 0 && <p className="text-sm text-muted-foreground">—</p>}
        {points.map((point: Point, index: number) => (
          <div key={index} className="flex flex-col gap-1">
            <Textarea
              aria-label={`${t(`brief.${key}`)} ${index + 1}`}
              value={ar ? point.textAr : point.textEn}
              rows={2}
              onChange={(e) => {
                setEdited(true);
                setBrief((b) =>
                  b
                    ? {
                        ...b,
                        [key]: b[key].map((p, i) =>
                          i === index ? { ...p, [ar ? 'textAr' : 'textEn']: e.target.value } : p,
                        ),
                      }
                    : b,
                );
              }}
            />
            {point.evidence.length > 0 && (
              <p className="flex flex-wrap gap-2 text-xs">
                <span className="text-muted-foreground">{t('brief.evidence')}:</span>
                {point.evidence.map((e) => (
                  <Link key={e.ref} to={e.link as '/'} className="text-primary hover:underline">
                    {e.number ?? (ar ? e.titleAr : e.titleEn)}
                  </Link>
                ))}
              </p>
            )}
          </div>
        ))}
      </div>
    );
  };

  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-4" data-testid="ai-brief">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="flex items-center gap-2 font-semibold">
            <Sparkles className="size-4 text-primary" aria-hidden />
            {t('brief.title')}
          </h3>
          <p className="text-sm text-muted-foreground">{t('brief.description')}</p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            disabled={generate.isPending}
            data-testid="brief-generate"
            onClick={() =>
              generate.mutate(
                { kind: period.kind, from: period.from, to: period.to, unitId },
                {
                  onSuccess: (b) => {
                    setBrief(b);
                    setEdited(false);
                  },
                },
              )
            }
          >
            <Sparkles className="size-4" aria-hidden />
            {generate.isPending ? t('brief.generating') : t('brief.generate')}
          </Button>
          {brief && (
            <Button
              disabled={save.isPending}
              data-testid="brief-save"
              onClick={() =>
                save.mutate(
                  {
                    periodKey: brief.periodKey,
                    improved: brief.improved,
                    attention: brief.attention,
                    decisions: brief.decisions,
                    aiGenerated: !edited,
                  },
                  { onSuccess: () => toast({ title: t('brief.saved') }) },
                )
              }
            >
              <Save className="size-4" aria-hidden />
              {t('brief.save')}
            </Button>
          )}
        </div>
      </header>
      {generate.isError && <StatusBanner tone="danger" title={errorMessage(tc, generate.error)} />}
      {brief && (
        <>
          {!edited && <StatusBanner tone="info" title={t('brief.aiDraft')} />}
          <div className="grid gap-4 lg:grid-cols-3">
            {section('improved', 'success')}
            {section('attention', 'warning')}
            {section('decisions', 'info')}
          </div>
        </>
      )}
    </section>
  );
}
