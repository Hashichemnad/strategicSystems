import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime, pickText } from '@/lib/format';
import { Checkbox, Input, Skeleton, StatusBanner, StatusChip } from '@/ui';
import { useFeature } from '../featuresApi';
import { useProjectMutation, type ProjectFull } from '../projectApi';
import { FieldErrors } from '../project/FieldErrors';
import { standardsApi, useGateChecklist, type Gate, type GateChecklistItem } from './standardsApi';

/** Configured stage-gate checklist items of one gate (docs/design/portfolio.md §35); required ones join the gate's minimums. */
export function GateChecklistPanel({ project, gate }: { project: ProjectFull; gate: Gate }) {
  const on = useFeature('gateChecklists');
  if (!on) return null;
  return <ChecklistBody project={project} gate={gate} />;
}

function ChecklistBody({ project, gate }: { project: ProjectFull; gate: Gate }) {
  const { t, i18n } = useTranslation('pmStandards');
  const { t: tc } = useTranslation();
  const checklist = useGateChecklist(project.id);
  const tick = useProjectMutation(project.id, standardsApi.tickChecklist);
  if (checklist.isPending) return <Skeleton className="h-16" />;
  if (checklist.isError) return <StatusBanner tone="danger" title={errorMessage(tc, checklist.error)} />;
  const items = checklist.data.items.filter((i) => i.gate === gate);
  if (items.length === 0) return null;
  const done = items.filter((i) => i.checked).length;
  const send = (item: GateChecklistItem, checked: boolean, note: string | null) =>
    tick.mutate({
      id: project.id,
      gate,
      code: item.code,
      checked,
      note,
      version: checklist.data.version,
    });
  return (
    <section
      className="flex flex-col gap-2 rounded-lg border bg-card p-4"
      data-testid={`gate-checklist-${gate}`}
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">
          {t('checklist.title', { gate: t(`checklist.gates.${gate}`) })}
        </h3>
        <StatusChip tone={done === items.length ? 'success' : 'warning'}>
          {t('checklist.progress', { done, total: items.length })}
        </StatusChip>
      </header>
      <ul className="flex flex-col gap-2">
        {items.map((item) => (
          <ChecklistRow
            key={item.code}
            item={item}
            canTick={checklist.data.canTick}
            busy={tick.isPending}
            label={pickText(i18n.language, item.labelAr, item.labelEn) || item.code}
            onSend={(checked, note) => send(item, checked, note)}
          />
        ))}
      </ul>
      {tick.isError && <FieldErrors error={tick.error} />}
    </section>
  );
}

function ChecklistRow({
  item,
  canTick,
  busy,
  label,
  onSend,
}: {
  item: GateChecklistItem;
  canTick: boolean;
  busy: boolean;
  label: string;
  onSend: (checked: boolean, note: string | null) => void;
}) {
  const { t, i18n } = useTranslation('pmStandards');
  const [note, setNote] = useState(item.note ?? '');
  return (
    <li className="flex flex-col gap-1" data-testid="gate-checklist-item">
      <div className="flex flex-wrap items-center gap-2">
        <Checkbox
          label={label}
          checked={item.checked}
          disabled={!canTick || busy}
          onChange={(e) => onSend(e.target.checked, note.trim() || null)}
        />
        {item.required ? (
          <StatusChip tone="warning">{t('checklist.required')}</StatusChip>
        ) : (
          <StatusChip tone="neutral">{t('checklist.optional')}</StatusChip>
        )}
        {item.checked && item.at && (
          <span className="text-xs text-muted-foreground">{formatDateTime(item.at, i18n.language)}</span>
        )}
      </div>
      {canTick ? (
        <Input
          dir="auto"
          className="ms-7 h-8 max-w-md text-sm"
          aria-label={t('checklist.noteFor', { item: label })}
          placeholder={t('checklist.note')}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          onBlur={() => {
            if ((item.note ?? '') !== note.trim()) onSend(item.checked, note.trim() || null);
          }}
        />
      ) : (
        item.note && (
          <p className="ms-7 text-xs text-muted-foreground" dir="auto">
            {item.note}
          </p>
        )
      )}
    </li>
  );
}
