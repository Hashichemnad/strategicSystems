import { History, Minus, Plus, Pencil, ShieldAlert } from 'lucide-react';
import type { TFunction } from 'i18next';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDateTime } from '@/lib/format';
import {
  Button,
  EmptyState,
  Input,
  Label,
  Pagination,
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
  type Tone,
} from '@/ui';
import { useRecordAsOf, useRecordHistory, type AuditEntry, type AuditedRecord } from './api';

const PAGE_SIZE = 50;
const ALL = '__all';

interface HistoryTabProps {
  record: AuditedRecord;
  id: string;
  /** Readable names for technical field names (e.g. NameEn → "Name (English)"); unknown fields show their code. */
  fieldLabels?: Record<string, string>;
}

/**
 * History tab of a record (CLAUDE.md §8, §10): every change as old → new with user, time and channel, filterable by field and user,
 * and the record "as of" a chosen date. The server answers only for records the user may read.
 */
export function HistoryTab({ record, id, fieldLabels = {} }: HistoryTabProps) {
  const { t, i18n } = useTranslation('audit');
  const { t: tc } = useTranslation();
  const [page, setPage] = useState(1);
  const [field, setField] = useState<string>(ALL);
  const [user, setUser] = useState<string>(ALL);
  const [asOfInput, setAsOfInput] = useState('');
  const [asOf, setAsOf] = useState<string | undefined>();
  const history = useRecordHistory(record, id, {
    page,
    pageSize: PAGE_SIZE,
    ...(field !== ALL ? { field } : {}),
    ...(user !== ALL ? { userId: user } : {}),
  });
  const snapshot = useRecordAsOf(record, id, asOf);

  const items = history.data?.items;
  const groups = useMemo(() => groupByChangeSet(items ?? []), [items]);
  const fields = useMemo(() => unique((items ?? []).map((e) => e.field)), [items]);
  const users = useMemo(
    () =>
      uniqueBy(
        (items ?? []).filter((e) => e.userId),
        (e) => e.userId ?? '',
      ).map((e) => ({ value: e.userId ?? '', label: e.userName ?? '—' })),
    [items],
  );
  const label = (name: string) => fieldLabels[name] ?? name;

  if (history.isPending) return <Skeleton className="h-64 rounded-lg" />;
  if (history.isError) return <StatusBanner tone="danger" title={errorMessage(tc, history.error)} />;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-3 rounded-lg border bg-card p-4 md:flex-row md:items-end">
        <div className="flex flex-1 flex-col gap-1">
          <Label htmlFor="history-field">{t('history.field')}</Label>
          <Select
            id="history-field"
            value={field}
            onValueChange={(value) => {
              setField(value);
              setPage(1);
            }}
            options={[
              { value: ALL, label: t('history.allFields') },
              ...fields.map((f) => ({ value: f, label: label(f) })),
            ]}
          />
        </div>
        <div className="flex flex-1 flex-col gap-1">
          <Label htmlFor="history-user">{t('history.user')}</Label>
          <Select
            id="history-user"
            value={user}
            onValueChange={(value) => {
              setUser(value);
              setPage(1);
            }}
            options={[{ value: ALL, label: t('history.allUsers') }, ...users]}
          />
        </div>
        <form
          className="flex flex-1 items-end gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            setAsOf(asOfInput ? new Date(asOfInput).toISOString() : undefined);
          }}
        >
          <div className="flex flex-1 flex-col gap-1">
            <Label htmlFor="history-as-of">{t('history.asOf')}</Label>
            <Input
              id="history-as-of"
              type="datetime-local"
              value={asOfInput}
              onChange={(e) => setAsOfInput(e.target.value)}
            />
          </div>
          <Button type="submit" variant="outline" disabled={!asOfInput}>
            {t('history.show')}
          </Button>
        </form>
      </div>

      {asOf && snapshot.data && (
        <section aria-labelledby="as-of-title" className="rounded-lg border bg-card p-4">
          <div className="mb-3 flex items-center justify-between gap-2">
            <h3 id="as-of-title" className="font-semibold">
              {t('history.asOfTitle', { date: formatDateTime(asOf, i18n.language) })}
            </h3>
            <Button variant="ghost" size="sm" onClick={() => setAsOf(undefined)}>
              {tc('actions.close')}
            </Button>
          </div>
          {snapshot.data.existed ? (
            <Table>
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('history.field')}</TableHeaderCell>
                  <TableHeaderCell>{t('history.value')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {Object.entries(snapshot.data.fields as Record<string, string | null>).map(
                  ([name, value]) => (
                    <TableRow key={name}>
                      <TableCell className="font-medium">{label(name)}</TableCell>
                      <TableCell dir="auto" className="break-all">
                        {value ?? '—'}
                      </TableCell>
                    </TableRow>
                  ),
                )}
              </TableBody>
            </Table>
          ) : (
            <p className="text-sm text-muted-foreground">{t('history.notYet')}</p>
          )}
        </section>
      )}

      {groups.length === 0 ? (
        <EmptyState icon={History} title={t('history.empty')} />
      ) : (
        <ol className="flex flex-col gap-3" aria-label={t('history.timeline')}>
          {groups.map((group) => (
            <ChangeGroup key={group.key} entries={group.entries} label={label} />
          ))}
        </ol>
      )}

      {history.data.totalCount > PAGE_SIZE && (
        <Pagination
          page={page}
          totalPages={Math.ceil(history.data.totalCount / PAGE_SIZE)}
          totalCount={history.data.totalCount}
          onPageChange={setPage}
        />
      )}
    </div>
  );
}

function ChangeGroup({ entries, label }: { entries: AuditEntry[]; label: (name: string) => string }) {
  const { t, i18n } = useTranslation('audit');
  const first = entries[0];
  if (!first) return null;
  return (
    <li className="rounded-lg border bg-card p-4">
      <div className="mb-2 flex flex-wrap items-center gap-2 text-sm">
        <ActionIcon action={first.action} success={first.success} />
        <span className="font-medium">{actionLabel(t, first)}</span>
        {first.parentType && first.entityType && (
          <span className="text-muted-foreground">· {first.entityType}</span>
        )}
        <span className="text-muted-foreground">·</span>
        <span>{first.userName ?? t('history.system')}</span>
        {first.onBehalfOfName && (
          <span className="text-muted-foreground">
            {t('history.onBehalfOf', { name: first.onBehalfOfName })}
          </span>
        )}
        <StatusChip tone="neutral">
          {t(`channels.${first.channel as 'ui'}`, { defaultValue: first.channel })}
        </StatusChip>
        <time className="ms-auto text-muted-foreground" dateTime={first.occurredAt}>
          {formatDateTime(first.occurredAt, i18n.language)}
        </time>
      </div>
      {first.reason && (
        <p className="mb-2 text-sm text-muted-foreground">{t('history.reason', { reason: first.reason })}</p>
      )}
      <ul className="flex flex-col gap-1 text-sm">
        {entries.map((entry) => (
          <EntryLine key={entry.id} entry={entry} label={label} />
        ))}
      </ul>
    </li>
  );
}

function EntryLine({ entry, label }: { entry: AuditEntry; label: (name: string) => string }) {
  const { t } = useTranslation('audit');
  if (entry.kind === 'event') {
    return <li className="text-muted-foreground">{eventDetail(entry)}</li>;
  }

  if (entry.action === 'update' && entry.field) {
    return (
      <li className="grid gap-1 md:grid-cols-[minmax(8rem,12rem)_1fr]">
        <span className="font-medium">{label(entry.field)}</span>
        <span className="flex flex-wrap items-center gap-2">
          <del dir="auto" className="rounded bg-danger-soft px-1.5 text-danger break-all">
            {entry.oldValue ?? t('history.emptyValue')}
          </del>
          <span aria-hidden className="rtl:rotate-180">
            →
          </span>
          <span className="sr-only">{t('history.changedTo')}</span>
          <ins dir="auto" className="rounded bg-success-soft px-1.5 text-success no-underline break-all">
            {entry.newValue ?? t('history.emptyValue')}
          </ins>
        </span>
      </li>
    );
  }

  const snapshot = parseSnapshot(entry.action === 'delete' ? entry.oldValue : entry.newValue);
  return (
    <li>
      <dl className="grid gap-x-4 gap-y-1 text-muted-foreground md:grid-cols-[minmax(8rem,12rem)_1fr]">
        {Object.entries(snapshot)
          .filter(([, value]) => value !== null && value !== '')
          .map(([name, value]) => (
            <div key={name} className="contents">
              <dt className="font-medium text-foreground">{label(name)}</dt>
              <dd dir="auto" className="break-all">
                {value}
              </dd>
            </div>
          ))}
      </dl>
    </li>
  );
}

function ActionIcon({ action, success }: { action: string; success: boolean | null }) {
  if (success === false) return <ShieldAlert className="size-4 text-danger" aria-hidden />;
  if (action === 'create') return <Plus className="size-4 text-success" aria-hidden />;
  if (action === 'delete') return <Minus className="size-4 text-danger" aria-hidden />;
  return <Pencil className="size-4 text-info" aria-hidden />;
}

export function actionLabel(t: TFunction<'audit'>, entry: AuditEntry): string {
  const key = entry.action.replaceAll('.', '_');
  return t(`actions.${key}` as 'actions.create', { defaultValue: entry.action });
}

export function actionTone(entry: AuditEntry): Tone {
  if (entry.success === false) return 'danger';
  if (entry.action === 'create') return 'success';
  if (entry.action === 'delete') return 'danger';
  return entry.kind === 'event' ? 'info' : 'neutral';
}

function eventDetail(entry: AuditEntry): string {
  if (!entry.data) return entry.action;
  try {
    const data = JSON.parse(entry.data) as Record<string, unknown>;
    return Object.entries(data)
      .filter(([, value]) => value !== null && typeof value !== 'object')
      .map(([key, value]) => `${key}: ${String(value)}`)
      .join(' · ');
  } catch {
    return entry.data;
  }
}

function parseSnapshot(value: string | null): Record<string, string | null> {
  if (!value) return {};
  try {
    return JSON.parse(value) as Record<string, string | null>;
  } catch {
    return { value };
  }
}

function groupByChangeSet(entries: AuditEntry[]): { key: string; entries: AuditEntry[] }[] {
  const groups: { key: string; entries: AuditEntry[] }[] = [];
  for (const entry of entries) {
    const key = `${entry.changeSetId ?? entry.id}:${entry.entityType ?? ''}:${entry.recordId ?? ''}`;
    const last = groups.at(-1);
    if (last && last.key === key) last.entries.push(entry);
    else groups.push({ key, entries: [entry] });
  }
  return groups;
}

function unique(values: (string | null)[]): string[] {
  return [...new Set(values.filter((v): v is string => Boolean(v)))].sort();
}

function uniqueBy<T>(items: T[], key: (item: T) => string): T[] {
  const seen = new Set<string>();
  return items.filter((item) => {
    const k = key(item);
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
}
