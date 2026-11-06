import { Plus, Sparkles, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { Button, FormField, Input, Select, StatusChip, Textarea, Section } from '@/ui';
import { capApi, fishboneCategories, rootCauseMethods, type CapRemark, type CapRootCauseValues } from './api';
import { useCapT, useToastMutation } from './components';

const MAX_WHYS = 7;

function initial(r: CapRemark): CapRootCauseValues {
  const rc = r.rootCause;
  return {
    method: rc.method ?? 'five-whys',
    whys: rc.whys.length ? [...rc.whys] : ['', '', ''],
    fishbone: Object.fromEntries(fishboneCategories.map((c) => [c, [...(rc.fishbone[c] ?? [])]])),
    text: rc.text ?? '',
  };
}

/** Root cause analysis (SPEC §9.3): 5 Whys, fishbone or free text; required by the configurable rule; AI assistant drafts. */
export function RootCauseTab({ remark: r }: { remark: CapRemark }) {
  const { t } = useTranslation('cap');
  const { k } = useCapT();
  const [v, setV] = useState<CapRootCauseValues>(() => initial(r));
  const [aiUsed, setAiUsed] = useState(false);
  const save = useToastMutation(capApi.saveRootCause);
  const draft = useToastMutation(capApi.rootCauseDraft);
  const aiFeature = useAiFeature('cap-root-cause');
  const aiAllowed = useCan('cap.ai.root-cause');
  const editable = r.can.rootCause;
  const whys = v.whys ?? [];
  const fishbone = v.fishbone ?? {};
  const set = (patch: Partial<CapRootCauseValues>) => setV((x) => ({ ...x, ...patch }));

  return (
    <Section
      title={
        <span className="flex items-center gap-2">
          {t('rootCause.title')}
          {r.rootCause.required && <StatusChip tone="warning">{t('rootCause.required')}</StatusChip>}
          {aiUsed && <AiBadge />}
        </span>
      }
      testId="cap-root-cause"
      actions={
        editable && (
          <>
            {aiFeature && aiAllowed && (
              <Button
                variant="outline"
                size="sm"
                loading={draft.busy}
                onClick={() =>
                  draft.run(r.id, (d) => {
                    setAiUsed(true);
                    const fb: Record<string, string[]> = Object.fromEntries(
                      fishboneCategories.map((c) => [c, [] as string[]]),
                    );
                    for (const c of d.causes) (fb[c.categoryCode] ??= []).push(c.text);
                    setV((x) => ({
                      ...x,
                      whys: d.whys.length ? d.whys : x.whys,
                      fishbone: d.causes.length ? fb : x.fishbone,
                      text: d.analysis ?? x.text,
                    }));
                  })
                }
                data-testid="cap-ai-root-cause"
              >
                <Sparkles aria-hidden />
                {t('ai.rootCause')}
              </Button>
            )}
            <Button
              size="sm"
              loading={save.busy}
              onClick={() =>
                save.run({
                  id: r.id,
                  version: r.version,
                  values: {
                    method: v.method,
                    whys: whys.map((w) => w.trim()).filter(Boolean),
                    fishbone: Object.fromEntries(
                      Object.entries(fishbone).map(([c, list]) => [
                        c,
                        list.map((x) => x.trim()).filter(Boolean),
                      ]),
                    ),
                    text: v.text?.trim() || null,
                  },
                })
              }
              data-testid="cap-root-cause-save"
            >
              {t('common.save')}
            </Button>
          </>
        )
      }
    >
      <p className="text-sm text-muted-foreground">{t('rootCause.hint')}</p>
      <FormField label={t('rootCause.method')}>
        <Select
          value={v.method}
          onValueChange={(method) => set({ method })}
          disabled={!editable}
          options={rootCauseMethods.map((m) => ({ value: m, label: k(`rootCauseMethod.${m}`) }))}
        />
      </FormField>
      {v.method === 'five-whys' && (
        <ol className="flex flex-col gap-2" data-testid="cap-five-whys">
          {whys.map((w, i) => (
            <li key={i} className="flex items-center gap-2">
              <span className="w-20 shrink-0 text-sm text-muted-foreground">
                {t('rootCause.why', { n: i + 1 })}
              </span>
              <Input
                value={w}
                disabled={!editable}
                aria-label={t('rootCause.why', { n: i + 1 })}
                onChange={(e) => set({ whys: whys.map((x, j) => (j === i ? e.target.value : x)) })}
                data-testid={`cap-why-${i}`}
              />
              {editable && (
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={t('common.remove')}
                  onClick={() => set({ whys: whys.filter((_, j) => j !== i) })}
                >
                  <Trash2 aria-hidden />
                </Button>
              )}
            </li>
          ))}
          {editable && whys.length < MAX_WHYS && (
            <li>
              <Button variant="outline" size="sm" onClick={() => set({ whys: [...whys, ''] })}>
                <Plus aria-hidden />
                {t('rootCause.addWhy')}
              </Button>
            </li>
          )}
        </ol>
      )}
      {v.method === 'fishbone' && (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" data-testid="cap-fishbone">
          {fishboneCategories.map((c) => (
            <FormField key={c} label={k(`fishbone.${c}`)} hint={t('rootCause.oneCausePerLine')}>
              <Textarea
                rows={3}
                disabled={!editable}
                value={(fishbone[c] ?? []).join('\n')}
                onChange={(e) => set({ fishbone: { ...fishbone, [c]: e.target.value.split('\n') } })}
              />
            </FormField>
          ))}
        </div>
      )}
      <FormField label={v.method === 'text' ? t('rootCause.text') : t('rootCause.summary')}>
        <Textarea
          rows={4}
          disabled={!editable}
          value={v.text ?? ''}
          onChange={(e) => set({ text: e.target.value })}
          data-testid="cap-root-cause-text"
        />
      </FormField>
    </Section>
  );
}
