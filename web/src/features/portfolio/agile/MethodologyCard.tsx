import { Lock, Settings2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '@/lib/format';
import { cn } from '@/lib/cn';
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  FormField,
  Input,
  Select,
  StatusChip,
  Textarea,
  useToast,
} from '@/ui';
import type { ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { agileApi, methodologies, useAgileMutation, type ApprovalMode, type Methodology } from './agileApi';

/** Delivery methodology of a project (SPEC §1.13, design §29): what it is and, while it may still be chosen, the choice dialog. */
export function MethodologyCard({ project }: { project: ProjectFull }) {
  const { t, i18n } = useTranslation('agile');
  const [open, setOpen] = useState(false);
  const delivery = project.delivery;
  const method = delivery.methodology as Methodology;
  const sprinting = method !== 'waterfall';
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-4" data-testid="methodology-card">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold">{t('methodology.title')}</h2>
        {delivery.canSetMethodology && (
          <Button size="sm" variant="outline" onClick={() => setOpen(true)} data-testid="methodology-choose">
            <Settings2 aria-hidden />
            {t('methodology.choose')}
          </Button>
        )}
      </header>
      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-muted-foreground">{t('methodology.methodology')}</dt>
          <dd className="font-medium" data-testid="methodology-value">
            <StatusChip tone="info">{t(`methodology.methods.${method}`)}</StatusChip>
            <span className="block pt-1 text-xs text-muted-foreground">
              {t(`methodology.methodHints.${method}`)}
            </span>
          </dd>
        </div>
        {sprinting && (
          <div>
            <dt className="text-muted-foreground">{t('methodology.approvalMode')}</dt>
            <dd className="font-medium">
              {t(`methodology.modes.${delivery.approvalMode as ApprovalMode}`)}
              <span className="block text-xs text-muted-foreground">
                {t(`methodology.modeHints.${delivery.approvalMode as ApprovalMode}`)}
              </span>
            </dd>
          </div>
        )}
        {sprinting && (
          <div>
            <dt className="text-muted-foreground">{t('methodology.sprintLength')}</dt>
            <dd className="font-medium tabular-nums">
              {delivery.sprintLengthDays !== null
                ? formatNumber(delivery.sprintLengthDays, i18n.language)
                : t('methodology.notSet')}
            </dd>
          </div>
        )}
        {sprinting && (
          <div className="sm:col-span-2">
            <dt className="text-muted-foreground">{t('methodology.definitionOfDone')}</dt>
            <dd className="whitespace-pre-line" dir="auto">
              {delivery.definitionOfDone?.trim() || t('methodology.notSet')}
            </dd>
          </div>
        )}
      </dl>
      {!delivery.canSetMethodology && (
        <p className="flex items-center gap-2 text-xs text-muted-foreground" data-testid="methodology-locked">
          <Lock className="size-3.5" aria-hidden />
          {t('methodology.lockedNote')}
        </p>
      )}
      {open && <MethodologyDialog project={project} onClose={() => setOpen(false)} />}
    </section>
  );
}

function MethodologyDialog({ project, onClose }: { project: ProjectFull; onClose: () => void }) {
  const { t } = useTranslation('agile');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const save = useAgileMutation(project.id, agileApi.setMethodology);
  const delivery = project.delivery;
  const [method, setMethod] = useState<Methodology>(delivery.methodology as Methodology);
  const [length, setLength] = useState(
    delivery.sprintLengthDays !== null ? String(delivery.sprintLengthDays) : '14',
  );
  const [dod, setDod] = useState(delivery.definitionOfDone ?? '');
  // The effective mode is shown as the choice; "installation default" clears the project's own choice.
  const [mode, setMode] = useState<string>(
    delivery.methodology === 'agile' ? delivery.approvalMode : 'default',
  );
  const sprinting = method !== 'waterfall';
  const submit = () =>
    save.mutate(
      {
        id: project.id,
        methodology: method,
        sprintLengthDays: sprinting && length.trim() !== '' ? Number(length) : null,
        definitionOfDone: sprinting ? dod.trim() || null : null,
        agileApprovalMode: method === 'agile' && mode !== 'default' ? mode : null,
        version: project.version,
      },
      {
        onSuccess: () => {
          toast({ title: t('methodology.saved') });
          onClose();
        },
      },
    );
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent
        title={t('methodology.dialogTitle')}
        description={t('methodology.dialogHint')}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button loading={save.isPending} onClick={submit} data-testid="methodology-save">
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4" data-testid="methodology-dialog">
          {save.error && <FieldErrors error={save.error} />}
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-2 text-sm font-medium">{t('methodology.methodology')}</legend>
            <div className="grid gap-2 sm:grid-cols-3">
              {methodologies.map((m) => (
                <label
                  key={m}
                  className={cn(
                    'flex cursor-pointer flex-col gap-1 rounded-lg border p-3 text-sm focus-within:ring-2 focus-within:ring-primary',
                    method === m ? 'border-primary bg-primary/5' : 'hover:bg-muted',
                  )}
                  data-testid={`methodology-option-${m}`}
                >
                  <span className="flex items-center gap-2 font-medium">
                    <input
                      type="radio"
                      name="methodology"
                      value={m}
                      checked={method === m}
                      onChange={() => setMethod(m)}
                      className="accent-primary"
                    />
                    {t(`methodology.methods.${m}`)}
                  </span>
                  <span className="text-xs text-muted-foreground">{t(`methodology.methodHints.${m}`)}</span>
                </label>
              ))}
            </div>
          </fieldset>
          {sprinting && (
            <div className="grid gap-3 sm:grid-cols-2">
              <FormField label={t('methodology.sprintLength')} required>
                <Input
                  dir="ltr"
                  type="number"
                  min={1}
                  max={28}
                  value={length}
                  onChange={(e) => setLength(e.target.value)}
                  data-testid="methodology-sprint-length"
                />
              </FormField>
              {method === 'agile' ? (
                <FormField label={t('methodology.approvalModeOverride')}>
                  <Select
                    value={mode}
                    onValueChange={setMode}
                    options={[
                      { value: 'default', label: t('methodology.approvalModeDefault') },
                      { value: 'sprint', label: t('methodology.modes.sprint') },
                      { value: 'monthly', label: t('methodology.modes.monthly') },
                    ]}
                  />
                </FormField>
              ) : (
                <p className="self-end text-xs text-muted-foreground">{t('methodology.hybridMonthly')}</p>
              )}
              <div className="sm:col-span-2">
                <FormField label={t('methodology.definitionOfDone')} required>
                  <Textarea
                    value={dod}
                    onChange={(e) => setDod(e.target.value)}
                    rows={4}
                    data-testid="methodology-dod"
                  />
                </FormField>
              </div>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
