import { Plus, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature } from '@/features/ai/api';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import { formatDate } from '@/lib/format';
import {
  Button,
  ConfirmDialog,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  Select,
  Skeleton,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
  type Tone,
} from '@/ui';
import { ActionDialog } from './ActionsTab';
import {
  riskApi,
  useControls,
  useRiskMutation,
  useRiskOptions,
  type ActionValues,
  type Control,
  type ControlValues,
  type Risk,
  type TreatmentSuggestion,
} from './api';
import { Section, useNames, useRiskLabels, useRiskT } from './components';

const NONE = '-';

/** Tone of an effectiveness level from its factor (0–10). */
export function useEffectivenessTone() {
  const options = useRiskOptions();
  return (code: string | null | undefined): Tone => {
    const levels = options.data?.framework.content.controlEffectiveness ?? [];
    const level = levels.find((l) => l.code === code);
    if (!level) return 'neutral';
    const max = Math.max(1, ...levels.map((l) => l.factor));
    const ratio = level.factor / max;
    return ratio >= 0.75 ? 'success' : ratio >= 0.4 ? 'warning' : 'danger';
  };
}

export function ControlsTab({ risk }: { risk: Risk }) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { k, language } = useRiskT();
  const controls = useControls(risk.id);
  const labels = useRiskLabels();
  const names = useNames();
  const tone = useEffectivenessTone();
  const options = useRiskOptions();
  const toast = useToast();
  const aiSuggest = useAiFeature('risk-suggest');
  const canSuggest = useCan('risk.ai.suggest') && aiSuggest;
  const rate = useRiskMutation(riskApi.rateControl);
  const retire = useRiskMutation(riskApi.retireControl);
  const [editing, setEditing] = useState<{ control?: Control; values?: Partial<ControlValues> }>();
  const [retiring, setRetiring] = useState<Control>();
  const [suggestions, setSuggestions] = useState<TreatmentSuggestion>();
  const [suggesting, setSuggesting] = useState(false);
  const [newAction, setNewAction] = useState<Partial<ActionValues>>();
  const onError = (e: unknown) => toast({ tone: 'error', title: errorMessage(tc, e) });

  async function suggest() {
    setSuggesting(true);
    try {
      setSuggestions(await riskApi.suggestions(risk.id));
    } catch (e) {
      onError(e);
    } finally {
      setSuggesting(false);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Section
        title={t('controls.title')}
        testId="risk-controls"
        actions={
          <>
            {canSuggest && risk.can.manageChildren && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => void suggest()}
                loading={suggesting}
                data-testid="risk-ai-suggest"
              >
                <Sparkles aria-hidden />
                {t('ai.suggest.action')}
              </Button>
            )}
            {risk.can.manageChildren && (
              <Button size="sm" onClick={() => setEditing({})} data-testid="risk-control-add">
                <Plus aria-hidden />
                {t('controls.add')}
              </Button>
            )}
          </>
        }
      >
        {!controls.data ? (
          <Skeleton className="h-24" />
        ) : controls.data.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('controls.empty')}</p>
        ) : (
          <Table data-testid="risk-controls-table">
            <TableHead>
              <TableRow>
                <TableHeaderCell>{t('controls.name')}</TableHeaderCell>
                <TableHeaderCell>{t('controls.type')}</TableHeaderCell>
                <TableHeaderCell>{t('controls.nature')}</TableHeaderCell>
                <TableHeaderCell>{t('controls.frequency')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.owner')}</TableHeaderCell>
                <TableHeaderCell>{t('controls.effectiveness')}</TableHeaderCell>
                <TableHeaderCell>{t('controls.lastAssessedOn')}</TableHeaderCell>
                <TableHeaderCell>{t('fields.status')}</TableHeaderCell>
                <TableHeaderCell>
                  <span className="sr-only">{tc('actions.more')}</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {controls.data.map((c) => (
                <TableRow key={c.id} data-testid="risk-control-row">
                  <TableCell>
                    <span className="flex flex-col">
                      <span className="font-medium">{names.text(c.values.nameAr, c.values.nameEn)}</span>
                      {c.values.description && (
                        <span className="text-xs text-muted-foreground">{c.values.description}</span>
                      )}
                      {c.sourceActionId && (
                        <span className="text-xs text-muted-foreground">{t('controls.fromAction')}</span>
                      )}
                    </span>
                  </TableCell>
                  <TableCell>{labels.lookup('risk.control-type', c.values.typeCode)}</TableCell>
                  <TableCell>{labels.lookup('risk.control-nature', c.values.natureCode)}</TableCell>
                  <TableCell>{labels.lookup('risk.control-frequency', c.values.frequencyCode)}</TableCell>
                  <TableCell>{names.person(c.owner)}</TableCell>
                  <TableCell>
                    {c.canRate && c.status === 'active' ? (
                      <Select
                        aria-label={t('controls.rate')}
                        value={c.values.effectivenessCode ?? NONE}
                        onValueChange={(value) =>
                          rate.mutate(
                            {
                              id: c.id,
                              effectivenessCode: value === NONE ? null : value,
                              version: c.version,
                            },
                            { onError },
                          )
                        }
                        options={[
                          { value: NONE, label: t('controls.notRated') },
                          ...(options.data?.framework.content.controlEffectiveness ?? []).map((e) => ({
                            value: e.code,
                            label: labels.effectiveness(e.code),
                          })),
                        ]}
                        className="min-w-36"
                      />
                    ) : c.values.effectivenessCode ? (
                      <StatusChip tone={tone(c.values.effectivenessCode)}>
                        {labels.effectiveness(c.values.effectivenessCode)}
                      </StatusChip>
                    ) : (
                      <span className="text-muted-foreground">{t('controls.notRated')}</span>
                    )}
                  </TableCell>
                  <TableCell>
                    {c.values.lastAssessedOn ? formatDate(c.values.lastAssessedOn, language) : '—'}
                  </TableCell>
                  <TableCell>
                    <StatusChip tone={c.status === 'active' ? 'success' : 'neutral'}>
                      {k(`controlStatus.${c.status}`)}
                    </StatusChip>
                  </TableCell>
                  <TableCell>
                    {c.canEdit && c.status === 'active' && (
                      <span className="flex gap-1">
                        <Button variant="ghost" size="sm" onClick={() => setEditing({ control: c })}>
                          {tc('actions.edit')}
                        </Button>
                        <Button variant="ghost" size="sm" onClick={() => setRetiring(c)}>
                          {t('controls.retire')}
                        </Button>
                      </span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Section>
      {suggestions && (
        <Section title={t('ai.suggest.title')} testId="risk-ai-suggestions">
          <AiBadge label={t('ai.generatedDraft')} className="self-start" />
          <div className="grid gap-4 md:grid-cols-2">
            <div className="flex flex-col gap-2">
              <h3 className="text-sm font-semibold">{t('controls.title')}</h3>
              {suggestions.controls.length === 0 && (
                <p className="text-sm text-muted-foreground">{t('ai.none')}</p>
              )}
              {suggestions.controls.map((s, index) => (
                <div
                  key={index}
                  className="flex items-center justify-between gap-2 rounded border p-2 text-sm"
                >
                  <span>
                    {names.text(s.nameAr, s.nameEn)}
                    {s.typeCode && (
                      <span className="ms-1 text-muted-foreground">
                        · {labels.lookup('risk.control-type', s.typeCode)}
                      </span>
                    )}
                  </span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      setEditing({ values: { nameAr: s.nameAr, nameEn: s.nameEn, typeCode: s.typeCode } })
                    }
                  >
                    {t('ai.add')}
                  </Button>
                </div>
              ))}
            </div>
            <div className="flex flex-col gap-2">
              <h3 className="text-sm font-semibold">{t('treatment.title')}</h3>
              {suggestions.actions.length === 0 && (
                <p className="text-sm text-muted-foreground">{t('ai.none')}</p>
              )}
              {suggestions.actions.map((s, index) => (
                <div
                  key={index}
                  className="flex items-center justify-between gap-2 rounded border p-2 text-sm"
                >
                  <span>{names.text(s.titleAr, s.titleEn)}</span>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => setNewAction({ titleAr: s.titleAr, titleEn: s.titleEn })}
                  >
                    {t('ai.add')}
                  </Button>
                </div>
              ))}
            </div>
          </div>
          {suggestions.similarRisks.length > 0 && (
            <p className="text-xs text-muted-foreground">
              {t('ai.suggest.basedOn')}: {suggestions.similarRisks.map((r) => r.number).join('، ')}
            </p>
          )}
        </Section>
      )}
      {editing && (
        <ControlDialog
          riskId={risk.id}
          control={editing.control}
          prefill={editing.values}
          onClose={() => setEditing(undefined)}
        />
      )}
      {newAction && (
        <ActionDialog riskId={risk.id} prefill={newAction} onClose={() => setNewAction(undefined)} />
      )}
      <ConfirmDialog
        open={!!retiring}
        onOpenChange={(open) => !open && setRetiring(undefined)}
        title={t('controls.retire')}
        description={t('controls.retireHint')}
        confirmLabel={t('controls.retire')}
        destructive
        loading={retire.isPending}
        onConfirm={() =>
          retiring && retire.mutate(retiring.id, { onSuccess: () => setRetiring(undefined), onError })
        }
      />
    </div>
  );
}

function ControlDialog({
  riskId,
  control,
  prefill,
  onClose,
}: {
  riskId: string;
  control?: Control;
  prefill?: Partial<ControlValues>;
  onClose: () => void;
}) {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const labels = useRiskLabels();
  const options = useRiskOptions();
  const save = useRiskMutation(riskApi.saveControl);
  const [v, setV] = useState<ControlValues>({
    nameAr: null,
    nameEn: null,
    description: null,
    typeCode: null,
    natureCode: null,
    ownerEmployeeId: null,
    frequencyCode: null,
    effectivenessCode: null,
    lastAssessedOn: null,
    evidence: null,
    ...control?.values,
    ...prefill,
  });
  const set = <K extends keyof ControlValues>(key: K, value: ControlValues[K]) =>
    setV((p) => ({ ...p, [key]: value }));
  const lookup = (key: 'typeCode' | 'natureCode' | 'frequencyCode', type: string) => (
    <Select
      value={v[key] ?? NONE}
      onValueChange={(value) => set(key, value === NONE ? null : value)}
      options={[{ value: NONE, label: t('common.none') }, ...labels.lookupOptions(type)]}
    />
  );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={control ? t('controls.edit') : t('controls.add')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              loading={save.isPending}
              disabled={!v.nameAr?.trim() && !v.nameEn?.trim()}
              data-testid="risk-control-save"
              onClick={() =>
                save.mutate(
                  { riskId, controlId: control?.id, values: v, version: control?.version ?? null },
                  {
                    onSuccess: onClose,
                    onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }),
                  },
                )
              }
            >
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2">
          <FormField label={t('controls.nameAr')}>
            <Input
              dir="rtl"
              value={v.nameAr ?? ''}
              onChange={(e) => set('nameAr', e.target.value || null)}
              data-testid="risk-control-name-ar"
            />
          </FormField>
          <FormField label={t('controls.nameEn')}>
            <Input
              dir="ltr"
              value={v.nameEn ?? ''}
              onChange={(e) => set('nameEn', e.target.value || null)}
              data-testid="risk-control-name-en"
            />
          </FormField>
          <FormField label={t('controls.description')} className="md:col-span-2">
            <Textarea
              value={v.description ?? ''}
              onChange={(e) => set('description', e.target.value || null)}
            />
          </FormField>
          <FormField label={t('controls.type')}>{lookup('typeCode', 'risk.control-type')}</FormField>
          <FormField label={t('controls.nature')}>{lookup('natureCode', 'risk.control-nature')}</FormField>
          <FormField label={t('controls.frequency')}>
            {lookup('frequencyCode', 'risk.control-frequency')}
          </FormField>
          <FormField label={t('fields.owner')}>
            <EmployeePicker
              value={v.ownerEmployeeId ?? undefined}
              onChange={(x) => set('ownerEmployeeId', x ?? null)}
            />
          </FormField>
          <FormField label={t('controls.effectiveness')}>
            <Select
              value={v.effectivenessCode ?? NONE}
              onValueChange={(value) => set('effectivenessCode', value === NONE ? null : value)}
              options={[
                { value: NONE, label: t('controls.notRated') },
                ...(options.data?.framework.content.controlEffectiveness ?? []).map((e) => ({
                  value: e.code,
                  label: labels.effectiveness(e.code),
                })),
              ]}
            />
          </FormField>
          <FormField label={t('controls.lastAssessedOn')}>
            <Input
              type="date"
              value={v.lastAssessedOn ?? ''}
              onChange={(e) => set('lastAssessedOn', e.target.value || null)}
            />
          </FormField>
          <FormField label={t('controls.evidence')} className="md:col-span-2">
            <Textarea value={v.evidence ?? ''} onChange={(e) => set('evidence', e.target.value || null)} />
          </FormField>
        </div>
      </DialogContent>
    </Dialog>
  );
}
