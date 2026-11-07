import { useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { KpiCardPicker } from '@/features/kpi/KpiCardPicker';
import { EmployeePicker } from '@/features/org/EmployeePicker';
import {
  Button,
  Checkbox,
  DatePicker,
  Dialog,
  DialogClose,
  DialogContent,
  EmptyState,
  FormField,
  Input,
  RichTextEditor,
  Select,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  Textarea,
  useToast,
  type SelectOption,
} from '@/ui';
import { deactivateItem, saveItem, useProjectItems, type ItemKind } from '../projectApi';
import { FieldErrors } from './FieldErrors';

export type FieldValue = string | boolean | null;
export type FormValues = Record<string, FieldValue>;

export interface FieldSpec {
  name: string;
  label: string;
  kind: 'text' | 'textarea' | 'richtext' | 'number' | 'date' | 'datetime' | 'select' | 'employee' | 'bool' | 'kpiCard';
  options?: readonly SelectOption[];
  required?: boolean;
  wide?: boolean;
  /** Only shown when this returns true for the current values. */
  when?: (values: FormValues) => boolean;
}

export interface Column<T> {
  label: string;
  render: (row: T) => ReactNode;
}

interface ItemsTabProps<T extends { id: string; status: string; version: number }> {
  projectId: string;
  kind: ItemKind;
  title: string;
  description?: string;
  testId: string;
  canWrite: boolean;
  columns: Column<T>[];
  fields: FieldSpec[];
  empty: FormValues;
  fromRow: (row: T) => FormValues;
  toPayload: (values: FormValues) => unknown;
  filter?: (row: T) => boolean;
  addLabel?: string;
  extra?: ReactNode;
}

/** A project row list with add / edit / deactivate dialogs driven by a field spec (docs/design/portfolio.md §27). */
export function ItemsTab<T extends { id: string; status: string; version: number }>(props: ItemsTabProps<T>) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const items = useProjectItems<T>(props.projectId, props.kind);
  const [editing, setEditing] = useState<{ row?: T } | null>(null);
  const [removing, setRemoving] = useState<T | null>(null);
  const rows = (items.data ?? []).filter((r) => r.status === 'active' && (props.filter?.(r) ?? true));
  return (
    <section className="flex flex-col gap-4" data-testid={props.testId}>
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{props.title}</h2>
          {props.description && <p className="text-sm text-muted-foreground">{props.description}</p>}
        </div>
        {props.canWrite && (
          <Button onClick={() => setEditing({})}>
            <Plus aria-hidden />
            {props.addLabel ?? t('project.add')}
          </Button>
        )}
      </header>
      {props.extra}
      {items.isPending ? (
        <Skeleton className="h-32" />
      ) : items.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, items.error)} />
      ) : rows.length === 0 ? (
        <EmptyState title={t('project.emptyRows')} />
      ) : (
        <div className="overflow-x-auto rounded-lg border bg-card">
          <Table>
            <TableHead>
              <TableRow>
                {props.columns.map((c) => (
                  <TableHeaderCell key={c.label}>{c.label}</TableHeaderCell>
                ))}
                {props.canWrite && <TableHeaderCell>{tc('actions.edit')}</TableHeaderCell>}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={row.id}>
                  {props.columns.map((c) => (
                    <TableCell key={c.label} dir="auto">
                      {c.render(row)}
                    </TableCell>
                  ))}
                  {props.canWrite && (
                    <TableCell>
                      <div className="flex gap-1">
                        <Button
                          size="sm"
                          variant="ghost"
                          aria-label={tc('actions.edit')}
                          onClick={() => setEditing({ row })}
                        >
                          <Pencil aria-hidden />
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          aria-label={t('project.remove')}
                          onClick={() => setRemoving(row)}
                        >
                          <Trash2 aria-hidden />
                        </Button>
                      </div>
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      {editing && <ItemDialog {...props} row={editing.row} onClose={() => setEditing(null)} />}
      {removing && (
        <RemoveDialog
          projectId={props.projectId}
          kind={props.kind}
          row={removing}
          onClose={() => setRemoving(null)}
        />
      )}
    </section>
  );
}

function ItemDialog<T extends { id: string; version: number }>({
  projectId,
  kind,
  title,
  fields,
  empty,
  fromRow,
  toPayload,
  row,
  onClose,
}: ItemsTabProps<T & { status: string }> & { row: T | undefined; onClose: () => void }) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [values, setValues] = useState<FormValues>(row ? fromRow(row as T & { status: string }) : empty);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const set = (name: string, value: FieldValue) => setValues((v) => ({ ...v, [name]: value }));
  const save = async () => {
    setBusy(true);
    setError(undefined);
    try {
      await saveItem(projectId, kind, row?.id, { values: toPayload(values), version: row?.version ?? null });
      await queryClient.invalidateQueries({ queryKey: ['portfolio', 'project', projectId] });
      toast({ title: t('project.saved') });
      onClose();
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  };
  const missing = fields.some(
    (f) => f.required && (f.when?.(values) ?? true) && (values[f.name] === null || values[f.name] === ''),
  );
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={title}
        className="max-w-2xl"
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button disabled={missing} loading={busy} onClick={() => void save()}>
              {tc('actions.save')}
            </Button>
          </>
        }
      >
        <div className="grid gap-3 md:grid-cols-2" data-testid={`item-dialog-${kind}`}>
          {fields
            .filter((f) => f.when?.(values) ?? true)
            .map((f) => (
              <FieldInput
                key={f.name}
                spec={f}
                value={values[f.name] ?? null}
                onChange={(v) => set(f.name, v)}
              />
            ))}
          {error !== undefined && (
            <div className="md:col-span-2">
              <FieldErrors error={error} />
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

export function FieldInput({
  spec,
  value,
  onChange,
}: {
  spec: FieldSpec;
  value: FieldValue;
  onChange: (value: FieldValue) => void;
}) {
  const text = typeof value === 'string' ? value : '';
  const wide =
    spec.wide || spec.kind === 'textarea' || spec.kind === 'richtext' ? 'md:col-span-2' : undefined;
  if (spec.kind === 'bool')
    return (
      <div className={wide}>
        <Checkbox label={spec.label} checked={value === true} onChange={(e) => onChange(e.target.checked)} />
      </div>
    );
  const control = (() => {
    switch (spec.kind) {
      case 'textarea':
        return <Textarea value={text} onChange={(e) => onChange(e.target.value)} />;
      case 'richtext':
        return <RichTextEditor aria-label={spec.label} value={text} onChange={(html) => onChange(html)} />;
      case 'number':
        return (
          <Input dir="ltr" inputMode="decimal" value={text} onChange={(e) => onChange(e.target.value)} />
        );
      case 'date':
        return <DatePicker value={text || undefined} onChange={(v) => onChange(v ?? null)} />;
      case 'datetime':
        return (
          <Input type="datetime-local" dir="ltr" value={text} onChange={(e) => onChange(e.target.value)} />
        );
      case 'select':
        return <Select value={text || undefined} onValueChange={onChange} options={spec.options ?? []} />;
      case 'employee':
        return <EmployeePicker value={text || undefined} onChange={(id) => onChange(id ?? null)} />;
      case 'kpiCard':
        return (
          <KpiCardPicker
            value={text || null}
            onChange={(id) => onChange(id)}
            aria-label={spec.label}
            testId={`field-${spec.name}`}
          />
        );
      default:
        return <Input dir="auto" value={text} onChange={(e) => onChange(e.target.value)} />;
    }
  })();
  return (
    <FormField label={spec.label} required={spec.required} className={wide}>
      {control}
    </FormField>
  );
}

function RemoveDialog({
  projectId,
  kind,
  row,
  onClose,
}: {
  projectId: string;
  kind: ItemKind;
  row: { id: string; version: number };
  onClose: () => void;
}) {
  const { t } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        title={t('project.removeTitle')}
        description={t('project.removeHint')}
        footer={
          <>
            <DialogClose asChild>
              <Button variant="outline">{tc('actions.cancel')}</Button>
            </DialogClose>
            <Button
              variant="danger"
              loading={busy}
              onClick={async () => {
                setBusy(true);
                try {
                  await deactivateItem(projectId, kind, row.id, row.version, reason);
                  await queryClient.invalidateQueries({ queryKey: ['portfolio', 'project', projectId] });
                  onClose();
                } catch (failure) {
                  setError(failure);
                } finally {
                  setBusy(false);
                }
              }}
            >
              {t('project.remove')}
            </Button>
          </>
        }
      >
        <FormField label={t('project.reason')}>
          <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {error !== undefined && <StatusBanner tone="danger" title={errorMessage(tc, error)} />}
      </DialogContent>
    </Dialog>
  );
}

/** Small display helpers shared by the tabs. */
export const str = (v: FieldValue | undefined) =>
  typeof v === 'string' && v.trim() !== '' ? v.trim() : null;
export const num = (v: FieldValue | undefined) => {
  const s = str(v);
  return s === null || Number.isNaN(Number(s)) ? null : Number(s);
};
export const asText = (v: number | string | null | undefined) =>
  v === null || v === undefined ? '' : String(v);

export function Chip({
  tone,
  children,
}: {
  tone: 'neutral' | 'info' | 'success' | 'warning' | 'danger';
  children: ReactNode;
}) {
  return <StatusChip tone={tone}>{children}</StatusChip>;
}
